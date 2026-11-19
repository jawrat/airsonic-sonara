using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Sonara.Api;
using Sonara.Api.Backups;
using Sonara.Api.Media;
using Sonara.Api.Options;
using Sonara.Api.Podcasts;
using Sonara.Api.Scanning;
using Sonara.Api.Security;
using Sonara.Api.Subsonic;
using Sonara.Data;
using Sonara.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Settings come from appsettings.json and environment variables and are read once at startup, so there is
// nothing to gain from watching the files for changes (and file watchers misbehave on network drives).
var commandMode = BackupCommand.IsCommand(args);
var builder = WebApplication.CreateBuilder((commandMode ? Array.Empty<string>() : args).Append("--hostBuilder:reloadConfigOnChange=false").ToArray());

builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 16 * 1024 * 1024);
var section = builder.Configuration.GetSection("Sonara");
builder.Services.Configure<SonaraOptions>(section);
var options = section.Get<SonaraOptions>() ?? new SonaraOptions();

if (BackupCommand.IsCommand(args))
{
    Environment.ExitCode = await BackupCommand.RunAsync(args, options);
    return;
}
var dataDir = Path.GetFullPath(options.DataDir);
Directory.CreateDirectory(dataDir);
using var instanceLock = InstanceLock.Acquire(dataDir);

var connectionString = options.Database.ConnectionString;
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Sonara:Database:ConnectionString is not set. Sonara needs a PostgreSQL database, e.g. " +
        "Sonara__Database__ConnectionString=\"Host=db;Database=sonara;Username=sonara;Password=...\". " +
        "The docker compose file in install/compose sets this up for you.");
}

builder.Services.AddSonaraData(connectionString);
builder.Services.AddSingleton(new CredentialProtector(
    CredentialProtector.LoadOrCreateKey(Path.Combine(dataDir, "secret.key"))));
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<SubsonicAuthFilter>();
builder.Services.AddSingleton<AuthenticationThrottle>();
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    forwarded.ForwardLimit = 1;
    forwarded.RequireHeaderSymmetry = true;
    foreach (var address in options.Security.TrustedProxyIPs)
    {
        if (!IPAddress.TryParse(address, out var ip)) throw new InvalidOperationException("Security.TrustedProxyIPs must contain numeric IP addresses.");
        forwarded.KnownProxies.Add(ip);
    }
});
builder.Services.AddSingleton<IMediaScanner, MediaScanner>();
builder.Services.AddSingleton<FfmpegTranscoder>();
builder.Services.AddSingleton<CoverArtResizer>();
builder.Services.AddSingleton<AvatarImages>();
builder.Services.AddSingleton<HlsCache>();
builder.Services.AddHostedService<ScannerService>();
builder.Services.AddSingleton(sp =>
    new PodcastHttp(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SonaraOptions>>().Value.Podcasts.AllowPrivateAddresses));
builder.Services.AddSingleton<PodcastIndexClient>();
builder.Services.AddSingleton<PodcastWorker>();
builder.Services.AddSingleton<IPodcastQueue>(sp => sp.GetRequiredService<PodcastWorker>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PodcastWorker>());
builder.Services.AddSingleton<PodcastFileGate>();
builder.Services.AddScoped<PodcastRetention>();
builder.Services.AddScoped<PodcastService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupWorker>();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);
// Non-secret database overrides are applied before hosted workers start. Changes take effect on restart.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
    var effective = app.Services.GetRequiredService<IOptions<SonaraOptions>>().Value;
    foreach (var setting in await db.ServerSettings.AsNoTracking().ToListAsync())
        SettingCatalog.Apply(effective, setting.Key, setting.Value);
}

// The Angular build is copied to wwwroot in the Docker image.
app.UseForwardedHeaders();
var indexFile = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
var indexHtml = indexFile.Exists ? await File.ReadAllTextAsync(indexFile.PhysicalPath!) : "";
var contentPolicy = SecurityHeaders.Policy(indexHtml);
var publicHttps = Uri.TryCreate(app.Services.GetRequiredService<IOptions<SonaraOptions>>().Value.PublicBaseUrl, UriKind.Absolute, out var publicOrigin) && publicOrigin.Scheme == "https";
app.Use((http, next) => { SecurityHeaders.Apply(http, contentPolicy, publicHttps); return next(http); });
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.Use(RequestParameters.Normalize);

app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapSubsonic();
app.MapPublicShares();

// SPA fallback for client-side routes. The single-argument overload only matches paths
// without a file extension ("{*path:nonfile}"), so real static files (main-*.js, css,
// favicon) are still served by UseStaticFiles. Mapped endpoints (/rest/*, /healthz) win
// over the fallback automatically.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
