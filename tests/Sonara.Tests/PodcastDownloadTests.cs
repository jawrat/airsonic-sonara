using System.Net;
using System.Security.Authentication;
using Sonara.Api.Podcasts;

namespace Sonara.Tests;

public class PodcastDownloadTests
{
    private sealed class Handler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(++Attempts));
    }
    private sealed class BrokenStream : Stream
    {
        private int _reads;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_reads++ > 0) throw new IOException("The decryption operation failed", new IOException("bad record mac"));
            buffer.Span[0] = 99; return ValueTask.FromResult(1);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    [Fact]
    public async Task Interrupted_transfer_restarts_from_zero_without_corrupt_bytes()
    {
        using var handler = new Handler(attempt => new(HttpStatusCode.OK) { Content = attempt == 1 ? new StreamContent(new BrokenStream()) : new ByteArrayContent([1, 2, 3]) });
        using var client = new HttpClient(handler);
        var path = Path.GetTempFileName();
        try
        {
            await PodcastDownload.DownloadAsync(client, new Uri("https://example.com/audio"), path, 100, CancellationToken.None, delay: (_, _) => Task.CompletedTask);
            Assert.Equal(2, handler.Attempts); Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task Repeated_transport_failure_is_bounded_and_removes_partial_file()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });
        using var client = new HttpClient(handler); var path = Path.GetTempFileName();
        await Assert.ThrowsAnyAsync<IOException>(() => PodcastDownload.DownloadAsync(client, new Uri("https://example.com/audio"), path, 100, CancellationToken.None, delay: (_, _) => Task.CompletedTask));
        Assert.Equal(3, handler.Attempts); Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task Permanent_http_errors_and_size_limits_do_not_retry()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.NotFound)); using var client = new HttpClient(handler); var path = Path.GetTempFileName();
        await Assert.ThrowsAsync<HttpRequestException>(() => PodcastDownload.DownloadAsync(client, new Uri("https://example.com/audio"), path, 100, CancellationToken.None));
        Assert.Equal(1, handler.Attempts); Assert.False(File.Exists(path));
        using var large = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[20]) }); using var other = new HttpClient(large);
        await Assert.ThrowsAsync<PodcastException>(() => PodcastDownload.DownloadAsync(other, new Uri("https://example.com/audio"), path, 10, CancellationToken.None)); Assert.Equal(1, large.Attempts); Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task Long_retry_after_is_not_ignored_or_retried_early()
    {
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return response;
        });
        using var client = new HttpClient(handler); var path = Path.GetTempFileName();
        await Assert.ThrowsAsync<HttpRequestException>(() => PodcastDownload.DownloadAsync(client, new Uri("https://example.com/audio"), path, 100, CancellationToken.None));
        Assert.Equal(1, handler.Attempts); Assert.False(File.Exists(path));
    }

    [Fact]
    public void Diagnostics_include_inner_error_and_redact_remote_urls()
    {
        var message = PodcastDownload.Describe(new IOException("The decryption operation failed", new IOException("bad record mac at https://example.com/audio?token=SECRET")));
        Assert.Contains("bad record mac", message); Assert.DoesNotContain("SECRET", message);
        Assert.False(PodcastDownload.IsTransient(new HttpRequestException("TLS authentication failed", new AuthenticationException("Invalid certificate"))));
    }
    [Fact]
    public async Task Covers_larger_than_old_limit_fit_new_default()
    {
        var limit = new Sonara.Api.Options.PodcastOptions().MaxCoverMegabytes;
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[6 * 1024 * 1024]) };
        Assert.Equal(6 * 1024 * 1024, (await PodcastHttp.ReadLimitedAsync(response, limit * 1024L * 1024, CancellationToken.None)).Length);
    }
}
