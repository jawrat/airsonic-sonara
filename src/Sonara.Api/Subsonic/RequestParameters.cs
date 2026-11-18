using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;

namespace Sonara.Api.Subsonic;

/// <summary>Merge form parameters before Minimal API binding, not in an endpoint filter (which runs too late).</summary>
public static class RequestParameters
{
    public static async Task Normalize(HttpContext http, RequestDelegate next)
    {
        if (http.Request.Path.StartsWithSegments("/rest") && http.Request.HasFormContentType)
        {
            var operation = http.Request.Path.Value?.Split('/').Last().Replace(".view", "");
            IFormCollection form;
            if (operation is "uploadMusic" or "setAvatar")
            {
                var megabytes = operation == "setAvatar" ? 2 : Math.Clamp(http.RequestServices.GetRequiredService<IOptions<SonaraOptions>>().Value.Uploads.MaxFileMegabytes, 1, 1024);
                var limit = (long)megabytes * 1024 * 1024;
                var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limit + 65536;
                try { form = await http.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = limit, ValueLengthLimit = 8192, ValueCountLimit = 32, MultipartHeadersLengthLimit = 8192 }, http.RequestAborted); }
                catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
                { await SubsonicResult.Error(0, "The upload is too large or the multipart request is invalid.").ExecuteAsync(http); return; }
            }
            else form = await http.Request.ReadFormAsync(http.RequestAborted);
            var values = form.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in http.Request.Query) values[pair.Key] = pair.Value; // query takes precedence
            http.Request.Query = new QueryCollection(values);
        }
        await next(http);
    }
}
