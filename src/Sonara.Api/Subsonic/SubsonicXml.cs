using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace Sonara.Api.Subsonic;

/// <summary>
/// Converts a Subsonic JSON tree to the XML form. Conventions (identical in the two
/// formats apart from this mapping): scalar properties become attributes, objects become
/// child elements, and arrays become repeated child elements named after the property.
/// </summary>
public static class SubsonicXml
{
    public static readonly XNamespace Ns = "http://subsonic.org/restapi";

    public static string ToXml(JsonObject envelope)
    {
        var root = new XElement(Ns + "subsonic-response");
        Populate(root, envelope);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + root.ToString(SaveOptions.DisableFormatting);
    }

    private static void Populate(XElement element, JsonObject obj)
    {
        foreach (var (name, value) in obj)
        {
            switch (value)
            {
                case null:
                    break;

                case JsonObject child:
                {
                    var childElement = new XElement(Ns + name);
                    Populate(childElement, child);
                    element.Add(childElement);
                    break;
                }

                case JsonArray array:
                    foreach (var item in array)
                    {
                        if (item is JsonObject itemObj)
                        {
                            var itemElement = new XElement(Ns + name);
                            Populate(itemElement, itemObj);
                            element.Add(itemElement);
                        }
                        else if (item is JsonValue itemValue)
                        {
                            element.Add(new XElement(Ns + name, Scalar(itemValue)));
                        }
                    }
                    break;

                case JsonValue scalar:
                    if ((element.Name.LocalName is "genre" or "lyrics" && name == "value") ||
                        (element.Name.LocalName == "chatMessage" && name == "message"))
                        element.Add(new XText(Scalar(scalar)));
                    else element.SetAttributeValue(name, Scalar(scalar));
                    break;
            }
        }
    }

    private static string Scalar(JsonValue value) => value.GetValueKind() switch
    {
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.ToJsonString(),
        _ => StripInvalidXmlChars(value.GetValue<string>()),
    };

    /// <summary>Tag data can contain control characters that are illegal in XML 1.0.</summary>
    public static string StripInvalidXmlChars(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                sb.Append(c).Append(s[i + 1]);
                i++;
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
