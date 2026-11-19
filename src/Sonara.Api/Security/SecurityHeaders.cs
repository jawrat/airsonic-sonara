using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sonara.Api.Security;

public static partial class SecurityHeaders
{
    // Hash only trusted inline bootstrap scripts from the deployed build, never user content.
    public static string Policy(string html)
    {
        var hashes = InlineScript().Matches(html).Select(m => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups[1].Value))) + "'");
        return "default-src 'self'; script-src 'self' " + string.Join(' ', hashes) +
            "; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https:; font-src 'self' data:; " +
            "connect-src 'self'; media-src 'self' blob:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    }
    public static void Apply(HttpContext context, string policy, bool publicHttps)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            if (!h.ContainsKey("Content-Security-Policy")) h["Content-Security-Policy"] = policy;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            // PublicBaseUrl permits HTTPS termination at the explicitly configured edge.
            // Browsers ignore HSTS received over plain HTTP.
            if (context.Request.IsHttps || publicHttps) h["Strict-Transport-Security"] = "max-age=31536000";
            var type = context.Response.ContentType ?? "";
            if (context.Request.Path.StartsWithSegments("/rest") &&
                !type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) &&
                !type.StartsWith("video/", StringComparison.OrdinalIgnoreCase) &&
                !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) h.CacheControl = "no-store";
            return Task.CompletedTask;
        });
    }
    [GeneratedRegex(@"<script\b(?![^>]*\bsrc\s*=)[^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex InlineScript();
}
