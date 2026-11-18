using System.Text.Json.Nodes;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

/// <summary>An IResult that wraps a payload in the standard "subsonic-response" envelope (XML or JSON, per ?f=).</summary>
public sealed class SubsonicResult : IResult
{
    public const string ApiVersion = "1.16.1";
    public const string ServerVersion = "0.1.0";

    private readonly JsonObject? _payload;
    private readonly int? _errorCode;
    private readonly string? _errorMessage;

    private SubsonicResult(JsonObject? payload, int? errorCode = null, string? errorMessage = null)
    {
        _payload = payload;
        _errorCode = errorCode;
        _errorMessage = errorMessage;
    }

    public static SubsonicResult Ok(JsonObject? payload = null) => new(payload);

    /// <summary>Subsonic errors are returned with HTTP 200 and status="failed".</summary>
    public static SubsonicResult Error(int code, string message) => new(null, code, message);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var envelope = Obj(
            ("status", _errorCode is null ? "ok" : "failed"),
            ("version", ApiVersion),
            ("type", "sonara"),
            ("serverVersion", ServerVersion),
            ("openSubsonic", true));

        if (_payload is not null)
        {
            foreach (var (key, value) in _payload)
                envelope[key] = value?.DeepClone();
        }

        if (_errorCode is not null)
            envelope["error"] = Obj(("code", _errorCode.Value), ("message", _errorMessage));

        var format = httpContext.Request.Query["f"].ToString();
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            var wrapped = new JsonObject { ["subsonic-response"] = envelope };
            await httpContext.Response.WriteAsync(wrapped.ToJsonString());
        }
        else
        {
            httpContext.Response.ContentType = "text/xml; charset=utf-8";
            await httpContext.Response.WriteAsync(SubsonicXml.ToXml(envelope));
        }
    }
}
