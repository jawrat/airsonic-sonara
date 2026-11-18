using Sonara.Api.Media;
using Sonara.Core.Entities;

namespace Sonara.Tests;

public class TranscodingPreferenceRulesTests
{
    [Fact]
    public void Defaults_preserve_original_and_explicit_client_options_win()
    {
        var user = new User();
        Assert.Equal((null, (int?)null), TranscodePlanner.Preferences(user, null, null));
        user.PreferredTranscodeFormat = "opus"; user.PreferredTranscodeBitRate = 96;
        Assert.Equal(("opus", (int?)96), TranscodePlanner.Preferences(user, null, null));
        Assert.Equal(("mp3", (int?)192), TranscodePlanner.Preferences(user, "mp3", 192));
        Assert.Equal(("raw", (int?)0), TranscodePlanner.Preferences(user, "raw", 0));
    }
    [Fact]
    public void Preferred_format_and_rate_produce_expected_plan_without_upscaling_lossy_audio()
    {
        var user = new User { PreferredTranscodeFormat = "opus", PreferredTranscodeBitRate = 192 };
        var (format, rate) = TranscodePlanner.Preferences(user, null, null);
        var plan = TranscodePlanner.Plan("flac", 900, format, rate, new() { Enabled = true });
        Assert.Equal("opus", plan!.Format); Assert.Equal(192, plan.BitRateKbps);
        Assert.Equal(96, TranscodePlanner.Plan("mp3", 96, format, rate, new() { Enabled = true })!.BitRateKbps);
    }
}

[Collection("PostgreSQL integration")]
public class TranscodingPreferenceIntegrationTests(ParityHost host) : IClassFixture<ParityHost>
{
    [DatabaseFact]
    public async Task Stored_preferences_transcode_streams_client_raw_overrides_and_admin_cap_still_wins()
    {
        using var client = host.CreateClient();
        try
        {
            var saved = await host.Call("setTranscodingPreferences", extra: "&preferredTranscodeFormat=mp3&preferredTranscodeBitRate=64");
            Assert.Equal("ok", saved["status"]!.ToString());
            var stream = await client.GetAsync(host.Url("stream", extra: "&id=" + host.PublicTrack));
            Assert.Equal("audio/mpeg", stream.Content.Headers.ContentType!.MediaType); Assert.NotEmpty(await stream.Content.ReadAsByteArrayAsync());
            stream = await client.GetAsync(host.Url("stream", extra: "&id=" + host.PublicTrack + "&format=raw"));
            Assert.Equal("audio/wav", stream.Content.Headers.ContentType!.MediaType);
            await host.Call("updateUser", "admin", "&username=listener&maxBitRate=64");
            stream = await client.GetAsync(host.Url("stream", extra: "&id=" + host.PublicTrack + "&format=raw"));
            Assert.Equal("audio/mpeg", stream.Content.Headers.ContentType!.MediaType);
        }
        finally
        {
            await host.Call("setTranscodingPreferences", extra: "&preferredTranscodeFormat=&preferredTranscodeBitRate=0");
            await host.Call("updateUser", "admin", "&username=listener&maxBitRate=0");
        }
    }

    [DatabaseFact]
    public async Task Users_can_save_own_preferences_admins_can_save_any_and_validation_is_atomic()
    {
        using var client = host.CreateClient();
        async Task<System.Text.Json.Nodes.JsonNode> Save(string actor, string target, string format, string rate)
        {
            var response = await client.PostAsync("/rest/setTranscodingPreferences", new FormUrlEncodedContent(new Dictionary<string,string> {
                ["u"] = actor, ["p"] = "Test-Password-123!", ["f"] = "json", ["username"] = target,
                ["preferredTranscodeFormat"] = format, ["preferredTranscodeBitRate"] = rate,
                ["maxBitRate"] = "0", ["adminRole"] = "true" }));
            return System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!["subsonic-response"]!;
        }
        try
        {
            var before = (await host.Call("getUser"))["user"]!;
            Assert.Equal("ok", (await Save("listener", "listener", "opus", "96"))["status"]!.ToString());
            var user = (await host.Call("getUser"))["user"]!;
            Assert.Equal("opus", user["preferredTranscodeFormat"]!.ToString()); Assert.Equal(96, user["preferredTranscodeBitRate"]!.GetValue<int>());
            Assert.Equal(before["maxBitRate"]!.ToString(), user["maxBitRate"]!.ToString()); Assert.False(user["adminRole"]!.GetValue<bool>());
            Assert.Equal(50, (await Save("listener", "admin", "aac", "128"))["error"]!["code"]!.GetValue<int>());
            Assert.Equal("ok", (await Save("admin", "listener", "aac", "128"))["status"]!.ToString());
            foreach (var (format, rate) in new[] { ("flac", "96"), ("mp3", "31"), ("mp3", "321"), ("mp3", "-1") })
                Assert.Equal("failed", (await Save("listener", "listener", format, rate))["status"]!.ToString());
            user = (await host.Call("getUser"))["user"]!; Assert.Equal("aac", user["preferredTranscodeFormat"]!.ToString()); Assert.Equal(128, user["preferredTranscodeBitRate"]!.GetValue<int>());
        }
        finally { await Save("admin", "listener", "", "0"); }
    }
}
