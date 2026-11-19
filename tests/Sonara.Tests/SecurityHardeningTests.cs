using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonara.Data;

namespace Sonara.Tests;

[Collection("PostgreSQL integration")]
public class SecurityHardeningIntegrationTests(ParityHost host) : IClassFixture<ParityHost>
{
    [DatabaseFact]
    public void Every_mapped_operation_has_an_explicit_permission_policy()
    {
        var source = host.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>();
        var operations = source.Endpoints.Select(e => e.Metadata.GetMetadata<Sonara.Api.Security.SubsonicOperation>()?.Name).OfType<string>().Distinct().ToList();
        Assert.True(operations.Count >= 70);
        var adminOnly = new HashSet<string> { "createUser", "updateUser", "deleteUser", "getUsers", "getSettings", "updateSettings", "getMusicFolderSettings", "saveMusicFolder", "deleteMusicFolder", "getBackups", "createBackup", "downloadBackup", "getPodcastRetentionPreview", "runPodcastRetention", "startScan", "createInternetRadioStation", "updateInternetRadioStation", "deleteInternetRadioStation" };
        var user = new Sonara.Core.Entities.User { StreamRole = true, DownloadRole = true, UploadRole = true, SettingsRole = true, PlaylistRole = true, CoverArtRole = true, PodcastRole = true, ShareRole = true, CommentRole = true };
        foreach (var operation in operations) Assert.True(!adminOnly.Contains(operation) == Sonara.Api.Subsonic.SubsonicAuthFilter.OperationAllowed(user, operation), "Missing or incorrect explicit policy: " + operation);
    }
    [DatabaseFact]
    public async Task Disabled_roles_are_enforced_for_mixed_case_routes()
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var user = await db.Users.SingleAsync(u => u.Username == "denied");
        user.StreamRole = false; user.DownloadRole = false; user.UploadRole = false;
        await db.SaveChangesAsync();
        using var client = host.CreateClient();
        foreach (var route in new[] { "stream", "STREAM", "stream.VIEW", "Download.ViEw", "GETUPLOADFOLDERS" })
        {
            using var response = await client.GetAsync(host.Url(route, "denied", "&id=" + host.PublicTrack));
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["subsonic-response"]!;
            Assert.Equal("failed", body["status"]!.ToString());
            Assert.Equal(50, body["error"]!["code"]!.GetValue<int>());
        }
    }
    [DatabaseFact]
    public async Task Responses_have_browser_headers_and_api_metadata_is_not_cached()
    {
        using var client = host.CreateClient();
        using var response = await client.GetAsync(host.Url("getUser"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
    [DatabaseFact]
    public async Task Repeated_invalid_credentials_receive_a_bounded_retry_response()
    {
        using var factory = host.WithWebHostBuilder(_ => { });
        using var scope = host.Services.CreateScope();
        var cs = scope.ServiceProvider.GetRequiredService<SonaraDbContext>().Database.GetConnectionString();
        var values = new Dictionary<string, string?> { ["Sonara__Database__ConnectionString"] = cs, ["Sonara__DataDir"] = Path.Combine(host.Root, "throttle-fixture") };
        var previous = values.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        HttpClient createdClient;
        try { foreach (var (key, value) in values) Environment.SetEnvironmentVariable(key, value); createdClient = factory.CreateClient(); }
        finally { foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value); }
        using var client = createdClient;
        for (var i = 0; i < Sonara.Api.Security.AuthenticationThrottle.FailuresPerMinute; i++)
        {
            using var failed = await client.GetAsync("/rest/ping?u=invalid-audit-user&p=invalid-test-value&f=json");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }
        using var blocked = await client.GetAsync("/rest/ping?u=invalid-audit-user&p=invalid-test-value&f=json");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), blocked.Headers.RetryAfter?.Delta);
    }

}

public class SecurityHardeningRulesTests
{
    [Fact]
    public void Unrecognized_operations_do_not_gain_permissions_by_default()
    {
        Assert.False(Sonara.Api.Subsonic.SubsonicAuthFilter.OperationAllowed(new Sonara.Core.Entities.User(), "newUnclassifiedOperation"));
    }

    [Fact]
    public void Csp_hashes_only_trusted_inline_scripts_and_does_not_allow_inline_javascript()
    {
        const string script = "document.documentElement.dataset.theme='lcars';";
        var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(script)));
        var policy = Sonara.Api.Security.SecurityHeaders.Policy("<script>" + script + "</script><script src='main.js'></script>");
        Assert.Contains("'sha256-" + hash + "'", policy);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", policy);
        Assert.Contains("frame-ancestors 'none'", policy); Assert.Contains("object-src 'none'", policy);
    }
    [Fact]
    public void Failed_authentication_is_bounded_without_limiting_normal_successful_requests()
    {
        using var throttle = new Sonara.Api.Security.AuthenticationThrottle();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        for (var i = 0; i < 100; i++) Assert.False(throttle.IsBlocked(context));
        for (var i = 0; i < Sonara.Api.Security.AuthenticationThrottle.FailuresPerMinute; i++) throttle.Failed(context);
        Assert.True(throttle.IsBlocked(context));
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.11"); Assert.False(throttle.IsBlocked(context));
    }
    [Fact]
    public async Task Unauthenticated_file_uploads_are_rejected_before_reading_the_body()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.Configure<Sonara.Api.Options.SonaraOptions>(_ => { });
        services.AddDbContext<SonaraDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused"));
        services.AddSingleton(new Sonara.Api.Security.CredentialProtector(new byte[32]));
        services.AddSingleton<Sonara.Api.Security.AuthenticationThrottle>();
        services.AddScoped<Sonara.Api.Subsonic.SubsonicAuthFilter>();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var h = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        Microsoft.AspNetCore.Http.EndpointHttpContextExtensions.SetEndpoint(h,
            new Microsoft.AspNetCore.Http.Endpoint(null, new Microsoft.AspNetCore.Http.EndpointMetadataCollection(new Sonara.Api.Security.SubsonicOperation("uploadMusic")), "test"));
        h.Request.Path = "/rest/UPLOADMUSIC.VIEW"; h.Request.ContentType = "multipart/form-data; boundary=security-test";
        h.Request.Body = new UnreadableBody(); h.Response.Body = new MemoryStream(); var next = false;
        await Sonara.Api.Subsonic.RequestParameters.Normalize(h, _ => { next = true; return Task.CompletedTask; });
        Assert.False(next); Assert.Equal(0, h.Request.Body.Position);
        h.Response.Body.Position = 0; Assert.Contains("Required parameter is missing", await new StreamReader(h.Response.Body).ReadToEndAsync());
    }
    private sealed class UnreadableBody : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => 1000000; public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body must not be read before authentication.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => throw new InvalidOperationException("The body must not be read before authentication.");
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
