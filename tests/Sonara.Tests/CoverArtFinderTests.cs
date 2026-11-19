using Sonara.Api.Media;

namespace Sonara.Tests;

public sealed class CoverArtFinderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sonara-cover-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Skips_fake_cover_files_and_uses_the_next_image_with_its_actual_content_type()
    {
        Write("cover.jpg", "<html>download failed</html>"u8.ToArray());
        Write("folder.jpg", CoverImageFixture.Png); // misleading extension
        var image = CoverArtFinder.FromFolder(_dir);
        Assert.NotNull(image);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal<byte>(CoverImageFixture.Png, image.Data);
    }

    [Fact]
    public void Representative_art_skips_empty_albums_and_finds_a_descendant_cover()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;
        var covered = Directory.CreateDirectory(Path.Combine(_dir, "covered")).FullName;
        File.WriteAllBytes(Path.Combine(covered, "cover.png"), CoverImageFixture.Png);
        var image = CoverArtFinder.FindRepresentative([Path.Combine(empty, "a.flac"), Path.Combine(covered, "b.flac")]);
        Assert.NotNull(image);
        Assert.Equal<byte>(CoverImageFixture.Png, image.Data);
        Assert.Null(CoverArtFinder.FindRepresentative([]));
    }

    [Fact]
    public void Finds_a_conventional_folder_image_case_insensitively()
    {
        Write("Folder.JPG", CoverImageFixture.Jpeg);
        var track = Write("01 song.mp3", [0]);

        var image = CoverArtFinder.Find([track]);

        Assert.NotNull(image);
        Assert.Equal("image/jpeg", image!.ContentType);
        Assert.Equal<byte>(CoverImageFixture.Jpeg, image.Data);
    }

    [Fact]
    public void Prefers_cover_over_folder()
    {
        Write("folder.jpg", CoverImageFixture.Jpeg);
        Write("cover.png", CoverImageFixture.Png);
        var track = Write("a.mp3", [0]);

        var image = CoverArtFinder.Find([track]);

        Assert.Equal("image/png", image!.ContentType);
        Assert.Equal<byte>(CoverImageFixture.Png, image.Data);
    }

    [Fact]
    public void Ignores_unrelated_images()
    {
        Write("booklet-page-3.jpg", [1]);
        var track = Write("a.mp3", [0]);

        Assert.Null(CoverArtFinder.Find([track]));
    }

    [Fact]
    public void Unreadable_audio_files_just_mean_no_art()
    {
        var notAudio = Write("broken.mp3", "definitely not audio"u8.ToArray());

        Assert.Null(CoverArtFinder.Find([notAudio]));
        Assert.Null(CoverArtFinder.Find([Path.Combine(_dir, "missing.flac")]));
    }
}
