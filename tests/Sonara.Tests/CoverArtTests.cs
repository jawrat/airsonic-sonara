using Sonara.Api.Media;

namespace Sonara.Tests;

public sealed class CoverArtTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sonara-cover").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [1]);
        return path;
    }

    [Fact]
    public void No_image_returns_null()
    {
        Touch("01 - song.mp3");
        Assert.Null(CoverArt.FindFolderImage(_dir));
    }

    [Fact]
    public void Unrelated_images_are_ignored()
    {
        Touch("booklet.jpg");
        Touch("back.png");
        Assert.Null(CoverArt.FindFolderImage(_dir));
    }

    [Fact]
    public void Cover_is_preferred_over_folder_and_matching_is_case_insensitive()
    {
        Touch("Folder.jpg");
        var cover = Touch("COVER.PNG");
        Assert.Equal(cover, CoverArt.FindFolderImage(_dir));
    }

    [Fact]
    public void Missing_directory_returns_null()
    {
        Assert.Null(CoverArt.FindFolderImage(Path.Combine(_dir, "nope")));
    }

    [Theory]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.JPEG", "image/jpeg")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.webp", "image/webp")]
    public void Content_types(string file, string expected) =>
        Assert.Equal(expected, CoverArt.ContentTypeFor(file));
}
