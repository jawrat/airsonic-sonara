using System.Threading.RateLimiting;

namespace Sonara.Api.Security;

/// <summary>Counts failures, not normal media/API requests. Idle partitions are reclaimed by the framework.</summary>
public sealed class AuthenticationThrottle : IDisposable
{
    public const int FailuresPerMinute = 20;
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = FailuresPerMinute, Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0, AutoReplenishment = true,
        }));
    private static string Key(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    public bool IsBlocked(HttpContext context) => limiter.GetStatistics(Key(context))?.CurrentAvailablePermits == 0;
    public void Failed(HttpContext context) { using var lease = limiter.AttemptAcquire(Key(context)); }
    public void Dispose() => limiter.Dispose();
}
