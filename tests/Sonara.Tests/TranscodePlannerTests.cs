using Sonara.Api.Media;
using Sonara.Api.Options;

namespace Sonara.Tests;

public class TranscodePlannerTests
{
    private static readonly TranscodingOptions Defaults = new();

    private static TranscodePlan? Plan(string suffix, int trackKbps, string? format = null, int? max = null,
        TranscodingOptions? options = null) =>
        TranscodePlanner.Plan(suffix, trackKbps, format, max, options ?? Defaults);

    [Fact]
    public void Plain_request_serves_the_original_so_flac_stays_bit_perfect()
    {
        Assert.Null(Plan("flac", 900));
        Assert.Null(Plan("flac", 900, max: 0));
    }

    [Fact]
    public void Format_raw_always_means_the_original_even_with_a_bitrate_cap()
    {
        Assert.Null(Plan("flac", 900, format: "raw", max: 128));
        Assert.Null(Plan("flac", 900, format: "RAW"));
    }

    [Fact]
    public void Disabled_transcoding_always_serves_the_original()
    {
        Assert.Null(Plan("flac", 900, format: "mp3", max: 128, options: new TranscodingOptions { Enabled = false }));
    }

    [Fact]
    public void Bitrate_cap_alone_transcodes_lossless_to_the_default_format()
    {
        var plan = Plan("flac", 900, max: 128);

        Assert.NotNull(plan);
        Assert.Equal("mp3", plan!.Format);
        Assert.Equal(128, plan.BitRateKbps);
        Assert.Equal("audio/mpeg", plan.ContentType);
    }

    [Fact]
    public void Source_already_under_the_cap_is_served_as_is()
    {
        Assert.Null(Plan("mp3", 256, max: 320));
    }

    [Fact]
    public void Source_over_the_cap_is_re_encoded()
    {
        var plan = Plan("mp3", 256, max: 128);
        Assert.Equal(128, plan!.BitRateKbps);
    }

    [Fact]
    public void Same_format_with_no_cap_is_not_re_encoded()
    {
        Assert.Null(Plan("mp3", 256, format: "mp3"));
    }

    [Fact]
    public void Explicit_format_on_lossless_uses_the_default_bitrate()
    {
        var plan = Plan("flac", 900, format: "opus");

        Assert.Equal("opus", plan!.Format);
        Assert.Equal(192, plan.BitRateKbps);
        Assert.Equal("libopus", plan.Codec);
        Assert.Equal("ogg", plan.Container);
    }

    [Fact]
    public void Unknown_format_falls_back_to_the_original()
    {
        Assert.Null(Plan("flac", 900, format: "wma", max: 128));
    }

    [Theory]
    [InlineData(1000, 320)]
    [InlineData(8, 32)]
    public void Bitrate_is_clamped(int requested, int expected)
    {
        // The source (2000 kbps hi-res FLAC) is above both caps, so it has to be transcoded,
        // and the target bitrate is then clamped to what the encoders support.
        Assert.Equal(expected, Plan("flac", 2000, max: requested)!.BitRateKbps);
    }

    [Fact]
    public void A_cap_above_the_source_bitrate_serves_the_original()
    {
        // A client allowing up to 1000 kbps can take a 900 kbps FLAC as-is.
        Assert.Null(Plan("flac", 900, max: 1000));
    }

    [Fact]
    public void A_lossy_source_is_not_inflated_beyond_its_own_bitrate()
    {
        var plan = Plan("mp3", 96, format: "opus");
        Assert.Equal(96, plan!.BitRateKbps);
    }

    [Fact]
    public void Arguments_have_input_codec_bitrate_and_stdout_output()
    {
        var plan = Plan("flac", 900, max: 128)!;
        var args = TranscodePlanner.BuildArguments("/music/a b/01.flac", plan, startSeconds: 0);

        Assert.Equal("/music/a b/01.flac", args[args.ToList().IndexOf("-i") + 1]);
        Assert.Contains("-vn", args);
        Assert.Equal("libmp3lame", args[args.ToList().IndexOf("-c:a") + 1]);
        Assert.Equal("128k", args[args.ToList().IndexOf("-b:a") + 1]);
        Assert.Equal("pipe:1", args[^1]);
        Assert.DoesNotContain("-ss", args);
    }

    [Fact]
    public void Start_offset_seeks_before_the_input()
    {
        var plan = Plan("flac", 900, max: 128)!;
        var args = TranscodePlanner.BuildArguments("/m/a.flac", plan, startSeconds: 90).ToList();

        Assert.Equal("90", args[args.IndexOf("-ss") + 1]);
        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
    }
}