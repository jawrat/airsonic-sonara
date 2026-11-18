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
    public void Finds_a_conventional_folder_image_case_insensitively()
    {
        Write("Folder.JPG", [1, 2, 3]);
        var track = Write("01 song.mp3", [0]);

        var image = CoverArtFinder.Find([track]);

        Assert.NotNull(image);
        Assert.Equal("image/jpeg", image!.ContentType);
        Assert.Equal<byte>([1, 2, 3], image.Data);
    }

    [Fact]
    public void Prefers_cover_over_folder()
    {
        Write("folder.jpg", [1]);
        Write("cover.png", [2]);
        var track = Write("a.mp3", [0]);

        var image = CoverArtFinder.Find([track]);

        Assert.Equal("image/png", image!.ContentType);
        Assert.Equal<byte>([2], image.Data);
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
