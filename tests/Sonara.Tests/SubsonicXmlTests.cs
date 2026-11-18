using System.Text.Json.Nodes;
using System.Xml.Linq;
using Sonara.Api.Subsonic;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Tests;

public class SubsonicXmlTests
{
    private static readonly XNamespace Ns = "http://subsonic.org/restapi";

    [Fact]
    public void Scalars_become_attributes_and_objects_become_children()
    {
        var envelope = Obj(
            ("status", "ok"),
            ("openSubsonic", true),
            ("musicFolders", Obj(("musicFolder", Arr([Obj(("id", 1), ("name", "Music"))])))));

        var doc = XDocument.Parse(SubsonicXml.ToXml(envelope));

        Assert.Equal(Ns + "subsonic-response", doc.Root!.Name);
        Assert.Equal("ok", (string?)doc.Root.Attribute("status"));
        Assert.Equal("true", (string?)doc.Root.Attribute("openSubsonic"));

        var folder = doc.Root.Element(Ns + "musicFolders")!.Element(Ns + "musicFolder")!;
        Assert.Equal("1", (string?)folder.Attribute("id"));
        Assert.Equal("Music", (string?)folder.Attribute("name"));
    }

    [Fact]
    public void Arrays_repeat_the_element_per_item()
    {
        var envelope = Obj(("album", Arr([Obj(("id", "1")), Obj(("id", "2")), Obj(("id", "3"))])));

        var doc = XDocument.Parse(SubsonicXml.ToXml(envelope));

        Assert.Equal(3, doc.Root!.Elements(Ns + "album").Count());
    }

    [Fact]
    public void Null_values_are_skipped_by_Obj()
    {
        var o = Obj(("a", "x"), ("b", null));
        Assert.False(o.ContainsKey("b"));
    }

    [Fact]
    public void Illegal_xml_control_characters_are_stripped_but_emoji_survive()
    {
        Assert.Equal("ab", SubsonicXml.StripInvalidXmlChars("a\u0001b"));
        Assert.Equal("hi 🎵", SubsonicXml.StripInvalidXmlChars("hi 🎵"));
    }

    [Fact]
    public void Output_parses_even_when_tags_contain_control_characters()
    {
        var envelope = Obj(("title", "bad\u0000name"));
        var doc = XDocument.Parse(SubsonicXml.ToXml(envelope));
        Assert.Equal("badname", (string?)doc.Root!.Attribute("title"));
    }
}
