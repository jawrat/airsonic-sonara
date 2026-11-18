using Sonara.Api.Media;

namespace Sonara.Tests;

public class CoverArtPlannerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(300, 300)]
    [InlineData(4, 16)]
    [InlineData(99999, 1200)]
    public void Size_is_clamped_or_ignored(int? requested, int? expected) =>
        Assert.Equal(expected, CoverArtPlanner.ClampSize(requested));

    [Fact]
    public void Cache_name_depends_on_image_content_and_size()
    {
        byte[] a = [1, 2, 3];
        byte[] b = [1, 2, 4];
        Assert.Equal(CoverArtPlanner.CacheFileName(a, 200), CoverArtPlanner.CacheFileName(a, 200));
        Assert.NotEqual(CoverArtPlanner.CacheFileName(a, 200), CoverArtPlanner.CacheFileName(b, 200));
        Assert.NotEqual(CoverArtPlanner.CacheFileName(a, 200), CoverArtPlanner.CacheFileName(a, 400));
        Assert.EndsWith("-200.jpg", CoverArtPlanner.CacheFileName(a, 200));
    }

    [Fact]
    public void Arguments_read_stdin_write_stdout_and_never_upscale()
    {
        var args = CoverArtPlanner.BuildArguments(250).ToList();
        Assert.Equal("pipe:0", args[args.IndexOf("-i") + 1]);
        Assert.Equal("pipe:1", args[^1]);
        Assert.Contains("scale=w=min(250\\,iw):h=-2", args);
        Assert.Equal("mjpeg", args[args.IndexOf("-f") + 1]);
    }
}
