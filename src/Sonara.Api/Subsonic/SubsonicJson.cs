using System.Text.Json.Nodes;

namespace Sonara.Api.Subsonic;

/// <summary>
/// Tiny helpers for building Subsonic payloads as JsonNode trees. The same tree is
/// serialised as JSON or converted to XML (see <see cref="SubsonicXml"/>), so both
/// response formats stay in sync.
/// </summary>
public static class SubsonicJson
{
    /// <summary>Builds an object, skipping null values (Subsonic clients dislike explicit nulls).</summary>
    public static JsonObject Obj(params (string Key, object? Value)[] items)
    {
        var o = new JsonObject();
        foreach (var (key, value) in items)
        {
            if (value is null) continue;
            o[key] = ToNode(value);
        }
        return o;
    }

    public static JsonArray Arr(IEnumerable<JsonNode> items) =>
        new(items.Cast<JsonNode?>().ToArray());

    public static JsonNode? ToNode(object value) => value switch
    {
        JsonNode n => n,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        DateTime dt => JsonValue.Create(DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")),
        _ => JsonValue.Create(value.ToString()),
    };
}
