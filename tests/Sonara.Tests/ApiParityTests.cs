using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sonara.Api.Security;
using Sonara.Api.Settings;
using Sonara.Api.Subsonic;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Tests;

public class ParityRulesTests
{
    [Theory]
    [InlineData("Scanner.Parallelism", "0", true)]
    [InlineData("Scanner.Parallelism", "-1", false)]
    [InlineData("Transcoding.DefaultFormat", "opus", true)]
    [InlineData("Transcoding.DefaultFormat", "raw", false)]
    [InlineData("Database.ConnectionString", "secret", false)]
    [InlineData("PublicBaseUrl", "https://music.example.com", true)]
    [InlineData("PublicBaseUrl", "https://user:secret@example.com", false)]
    [InlineData("PublicBaseUrl", "javascript:alert(1)", false)]
    [InlineData("PublicBaseUrl", "https://music.example.com/path", false)]
    public void Settings_validate_allowlisted_values(string key, string value, bool expected) => Assert.Equal(expected, SettingCatalog.Validate(key, value));
    [Fact]
    public void Directory_ids_round_trip_unicode()
    {
        Assert.Equal((7, "Björk/First album"), SubsonicEndpoints.ParseDirectoryId(SubsonicEndpoints.DirectoryId(7, "Björk/First album")));
        Assert.Null(SubsonicEndpoints.ParseDirectoryId("dir-7-%%%"));
    }
    [Fact]
    public void Roles_restrict_operations_and_admin_overrides()
    {
        var user = new User { StreamRole = false, DownloadRole = false, ShareRole = false, PodcastRole = true };
        Assert.False(SubsonicAuthFilter.OperationAllowed(user, "stream"));
        Assert.False(SubsonicAuthFilter.OperationAllowed(user, "download"));
        Assert.False(SubsonicAuthFilter.OperationAllowed(user, "createShare"));
        Assert.True(SubsonicAuthFilter.OperationAllowed(user, "createPodcastChannel"));
        user.IsAdmin = true; Assert.True(SubsonicAuthFilter.OperationAllowed(user, "stream"));
    }
    [Fact]
    public void Xml_genre_and_lyrics_use_text_content()
    {
        var payload = SubsonicJson.Obj(("genre", SubsonicJson.Obj(("songCount", 1), ("value", "Rock & roll"))), ("lyrics", SubsonicJson.Obj(("artist", "A"), ("value", "line one\nline two"))));
        var xml = XDocument.Parse(SubsonicXml.ToXml(payload));
        Assert.Equal("Rock & roll", xml.Root!.Element(SubsonicXml.Ns + "genre")!.Value);
        Assert.Equal("line one\nline two", xml.Root.Element(SubsonicXml.Ns + "lyrics")!.Value);
    }
}

public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SONARA_TEST_DATABASE")))
            Skip = "Set SONARA_TEST_DATABASE to PostgreSQL; each fixture creates and drops its own uniquely named test database.";
    }
}
public sealed class ParityHost : WebApplicationFactory<Program>, IAsyncLifetime
{
    public readonly string Root = Path.Combine(Path.GetTempPath(), "sonara-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _database = "sonara_test_" + Guid.NewGuid().ToString("N");
    public int PublicTrack, HiddenTrack, PublicAlbum, HiddenAlbum, Folder, HiddenFolder;
    private bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SONARA_TEST_DATABASE"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SONARA_TEST_DATABASE")) { Database = _database };
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sonara:Database:ConnectionString"] = cs.ConnectionString, ["Sonara:DataDir"] = Root,
            ["Sonara:AdminPassword"] = "Test-Password-123!", ["Sonara:Scanner:ScanOnStartup"] = "false",
            ["Sonara:Scanner:Watch"] = "false", ["Sonara:Scanner:RescanIntervalMinutes"] = "0", ["Sonara:Podcasts:RefreshIntervalMinutes"] = "0",
        }));
    }
    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SONARA_TEST_DATABASE")) { Database = _database };
        var environment = new Dictionary<string, string?>
        {
            ["Sonara__Database__ConnectionString"] = cs.ConnectionString, ["Sonara__DataDir"] = Root,
            ["Sonara__AdminPassword"] = "Test-Password-123!", ["Sonara__Scanner__ScanOnStartup"] = "false",
            ["Sonara__Scanner__Watch"] = "false", ["Sonara__Scanner__RescanIntervalMinutes"] = "0", ["Sonara__Podcasts__RefreshIntervalMinutes"] = "0",
        };
        var previous = environment.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        HttpClient client;
        try
        {
            foreach (var (key, value) in environment) Environment.SetEnvironmentVariable(key, value);
            client = CreateClient();
        }
        finally { foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value); }
        using var initialClient = client; using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>(); var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var listener = await users.CreateAsync("listener", "Test-Password-123!", false); listener.AllFolders = false; listener.ShareRole = true;
        var denied = await users.CreateAsync("denied", "Test-Password-123!", false); denied.StreamRole = false; denied.DownloadRole = false; denied.CommentRole = false;
        var artist = new Artist { Name = "Shared artist" };
        var a = new Album { Title = "Visible album", Artist = artist, Genre = "Rock", Year = 2001, SongCount = 2 };
        var b = new Album { Title = "Hidden album", Artist = artist, Genre = "Secret", Year = 2002, SongCount = 1 };
        var f = new MusicFolder { Name = "One", Path = Path.Combine(Root, "one") };
        var g = new MusicFolder { Name = "Two", Path = Path.Combine(Root, "two") };
        Directory.CreateDirectory(Path.Combine(f.Path, "Artist", "Album")); Directory.CreateDirectory(g.Path);
        var t = new Track { Album = a, Artist = artist, MusicFolder = f, Title = "First", Path = Path.Combine(f.Path, "Artist", "Album", "first.wav"), Genre = "Rock", Year = 2001, DurationSeconds = 2, Suffix = "wav", ContentType = "audio/wav", BitRate = 128 };
        var u = new Track { Album = a, Artist = artist, MusicFolder = f, Title = "Second", Path = Path.Combine(f.Path, "Artist", "Album", "second.wav"), Genre = "Rock", Year = 2001 };
        var v = new Track { Album = b, Artist = artist, MusicFolder = g, Title = "Hidden song", Path = Path.Combine(g.Path, "secret.wav"), Genre = "Secret" };
        using (var stream = File.Create(t.Path)) using (var wav = new BinaryWriter(stream))
        {
            var length = 44100 * 2 * 2; wav.Write("RIFF"u8); wav.Write(36 + length); wav.Write("WAVEfmt "u8); wav.Write(16); wav.Write((short)1); wav.Write((short)1);
            wav.Write(44100); wav.Write(88200); wav.Write((short)2); wav.Write((short)16); wav.Write("data"u8); wav.Write(length); wav.Write(new byte[length]);
        }
        t.Size = new FileInfo(t.Path).Length;
        await File.WriteAllTextAsync(Path.ChangeExtension(t.Path, ".lrc"), "[00:00]Hello & welcome");
        db.AddRange(t, u, v); await db.SaveChangesAsync(); db.Add(new UserMusicFolder { UserId = listener.Id, MusicFolderId = f.Id }); await db.SaveChangesAsync();
        PublicTrack = t.Id; HiddenTrack = v.Id; PublicAlbum = a.Id; HiddenAlbum = b.Id; Folder = f.Id; HiddenFolder = g.Id;
    }
    async Task IAsyncLifetime.DisposeAsync()
    {
        if (!Enabled) return;
        using (var scope = Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<SonaraDbContext>().Database.EnsureDeletedAsync();
        await DisposeAsync(); if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
    public string Url(string operation, string user = "listener", string extra = "") => "/rest/" + operation + "?u=" + user + "&p=Test-Password-123!&v=1.16.1&c=test&f=json" + extra;
    public async Task<JsonObject> Call(string operation, string user = "listener", string extra = "")
    {
        using var client = CreateClient(); var response = await client.GetAsync(Url(operation, user, extra)); var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text); return JsonNode.Parse(text)!["subsonic-response"]!.AsObject();
    }
}
[Collection("PostgreSQL integration")]
public class ApiParityIntegrationTests(ParityHost host) : IClassFixture<ParityHost>
{
    private static void Ok(JsonObject b) => Assert.Equal("ok", b["status"]!.GetValue<string>());
    private static void Error(JsonObject b, int code) { Assert.Equal("failed", b["status"]!.GetValue<string>()); Assert.Equal(code, b["error"]!["code"]!.GetValue<int>()); }
    [DatabaseFact]
    public async Task Form_posts_bind_parameters_and_xml_matches_json()
    {
        using var client = host.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["u"] = "listener", ["p"] = "Test-Password-123!", ["v"] = "1.16.1", ["c"] = "test", ["f"] = "json", ["id"] = host.PublicTrack.ToString() });
        var result = await client.PostAsync("/rest/getSong.view", form); var b = JsonNode.Parse(await result.Content.ReadAsStringAsync())!["subsonic-response"]!;
        Assert.Equal("ok", b["status"]!.GetValue<string>()); Assert.Equal(host.PublicTrack.ToString(), b["song"]!["id"]!.GetValue<string>());
        var xml = XDocument.Parse(await client.GetStringAsync(host.Url("getSong", extra: "&id=" + host.PublicTrack).Replace("f=json", "f=xml")));
        Assert.Equal(host.PublicTrack.ToString(), xml.Root!.Element(SubsonicXml.Ns + "song")!.Attribute("id")!.Value);
    }
    [DatabaseFact]
    public async Task Folder_access_applies_to_ids_search_genres_and_bookmarks()
    {
        var folders = await host.Call("getMusicFolders"); Ok(folders); Assert.Single(folders["musicFolders"]!["musicFolder"]!.AsArray());
        Error(await host.Call("getSong", extra: "&id=" + host.HiddenTrack), 70); Error(await host.Call("getAlbum", extra: "&id=" + host.HiddenAlbum), 70);
        Error(await host.Call("createBookmark", extra: "&id=" + host.HiddenTrack + "&position=100"), 70); Error(await host.Call("createShare", extra: "&id=" + host.HiddenTrack), 70);
        var genres = await host.Call("getGenres"); Ok(genres); Assert.DoesNotContain("Secret", genres.ToJsonString());
        var search = await host.Call("search3", extra: "&query=Hidden"); Ok(search); Assert.Empty(search["searchResult3"]!["song"]!.AsArray());
        var first = await host.Call("search3", extra: "&query=&songCount=1&songOffset=0"); var second = await host.Call("search3", extra: "&query=&songCount=1&songOffset=1");
        Assert.NotEqual(first["searchResult3"]!["song"]![0]!["id"]!.ToString(), second["searchResult3"]!["song"]![0]!["id"]!.ToString());
        var list = await host.Call("getAlbumList2", extra: "&type=alphabeticalByName&musicFolderId=" + host.HiddenFolder); Ok(list); Assert.Empty(list["albumList2"]!["album"]!.AsArray());
    }
    [DatabaseFact]
    public async Task Ratings_play_history_album_modes_and_lyrics_work()
    {
        Ok(await host.Call("setRating", extra: "&id=al-" + host.PublicAlbum + "&rating=5")); Ok(await host.Call("setRating", extra: "&id=" + host.PublicTrack + "&rating=4"));
        var song = await host.Call("getSong", extra: "&id=" + host.PublicTrack); Ok(song); Assert.Equal(4, song["song"]!["userRating"]!.GetValue<int>());
        Ok(await host.Call("scrobble", extra: "&id=" + host.PublicTrack + "&submission=false")); var now = await host.Call("getNowPlaying"); Ok(now); Assert.NotEmpty(now["nowPlaying"]!["entry"]!.AsArray());
        Ok(await host.Call("scrobble", extra: "&id=" + host.PublicTrack));
        foreach (var mode in new[] { "highest", "recent", "frequent", "byYear&fromYear=2000&toYear=2005", "byYear&fromYear=2005&toYear=2000", "byGenre&genre=Rock", "starred", "random", "newest", "alphabeticalByArtist" }) Ok(await host.Call("getAlbumList2", extra: "&type=" + mode));
        var lyrics = await host.Call("getLyrics", extra: "&artist=Shared%20artist&title=First"); Ok(lyrics); Assert.Contains("Hello & welcome", lyrics["lyrics"]!["value"]!.GetValue<string>());
        Error(await host.Call("setRating", extra: "&id=" + host.PublicTrack + "&rating=6"), 0);
    }
    [DatabaseFact]
    public async Task Roles_enforce_stream_download_chat_and_admin_operations()
    {
        Error(await host.Call("stream", "denied", "&id=" + host.PublicTrack), 50); Error(await host.Call("download", "denied", "&id=" + host.PublicTrack), 50);
        Error(await host.Call("addChatMessage", "denied", "&message=hello"), 50); Error(await host.Call("getSettings"), 50);
        Error(await host.Call("createInternetRadioStation", extra: "&name=x&streamUrl=https://example.com/audio"), 50);
    }
    [DatabaseFact]
    public async Task Share_links_validate_membership_expiration_and_deletion()
    {
        var created = await host.Call("createShare", extra: "&id=" + host.PublicTrack + "&description=Test"); Ok(created); var token = created["shares"]!["share"]![0]!["id"]!.GetValue<string>();
        using var client = host.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/share/" + token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/share/{token}/stream/{host.HiddenTrack}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/share/{token}/stream/{host.PublicTrack}")).StatusCode);
        Ok(await host.Call("updateShare", extra: "&id=" + token + "&expires=1")); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/share/" + token)).StatusCode);
        Ok(await host.Call("deleteShare", extra: "&id=" + token)); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/share/" + token)).StatusCode);
    }
    [DatabaseFact]
    public async Task Settings_are_secret_free_and_radio_chat_persist()
    {
        var settings = await host.Call("getSettings", "admin"); Ok(settings); Assert.DoesNotContain("ConnectionString", settings.ToJsonString()); Assert.DoesNotContain("Test-Password", settings.ToJsonString());
        Ok(await host.Call("updateSettings", "admin", "&key=Scanner.Parallelism&value=3")); Error(await host.Call("updateSettings", "admin", "&key=Database.ConnectionString&value=bad"), 0);
        Ok(await host.Call("createInternetRadioStation", "admin", "&name=TestRadio&streamUrl=https://example.com/audio")); var radio = await host.Call("getInternetRadioStations"); Ok(radio);
        var id = radio["internetRadioStations"]!["internetRadioStation"]![0]!["id"]!.ToString();
        Ok(await host.Call("updateInternetRadioStation", "admin", "&id=" + id + "&name=Changed&streamUrl=https://example.com/other")); Ok(await host.Call("deleteInternetRadioStation", "admin", "&id=" + id));
        Ok(await host.Call("addChatMessage", extra: "&message=hello%20world")); var chat = await host.Call("getChatMessages"); Ok(chat); Assert.Contains("hello world", chat.ToJsonString());
    }
    [DatabaseFact]
    public async Task Legacy_browsing_navigates_and_stream_supports_range()
    {
        var indexes = await host.Call("getIndexes"); Ok(indexes); var id = indexes["indexes"]!["index"]![0]!["artist"]![0]!["id"]!.GetValue<string>();
        var directory = await host.Call("getMusicDirectory", extra: "&id=" + id); Ok(directory); var sub = directory["directory"]!["child"]![0]!["id"]!.ToString(); Ok(await host.Call("getMusicDirectory", extra: "&id=" + sub));
        Ok(await host.Call("getArtistInfo", extra: "&id=" + id));
        Ok(await host.Call("getAlbumInfo", extra: "&id=" + sub));
        Ok(await host.Call("getSimilarSongs", extra: "&id=" + sub));
        Ok(await host.Call("star", extra: "&id=" + sub));
        Ok(await host.Call("unstar", extra: "&id=" + sub));
        Ok(await host.Call("setRating", extra: "&id=" + sub + "&rating=4"));
        var albums = await host.Call("getAlbumList", extra: "&type=newest"); Ok(albums); var album = albums["albumList"]!["album"]![0]!["id"]!.ToString(); Ok(await host.Call("getMusicDirectory", extra: "&id=" + album));
        using var client = host.CreateClient(); using var request = new HttpRequestMessage(HttpMethod.Get, host.Url("stream", extra: "&id=" + host.PublicTrack)); request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 31);
        var result = await client.SendAsync(request); Assert.Equal(HttpStatusCode.PartialContent, result.StatusCode); Assert.Equal(32, (await result.Content.ReadAsByteArrayAsync()).Length);
    }
    [DatabaseFact]
    public async Task User_permissions_can_be_updated_and_all_folder_access_revoked()
    {
        Ok(await host.Call("createUser", "admin", "&username=roles-test&password=Test-Password-123!&allFolders=false&musicFolderId=" + host.Folder));
        var visible = await host.Call("getMusicFolders", "roles-test"); Ok(visible); Assert.Single(visible["musicFolders"]!["musicFolder"]!.AsArray());
        Ok(await host.Call("updateUser", "admin", "&username=roles-test&streamRole=false&shareRole=true&maxBitRate=96&allFolders=false"));
        var empty = await host.Call("getMusicFolders", "roles-test"); Ok(empty); Assert.Empty(empty["musicFolders"]!["musicFolder"]!.AsArray());
        Error(await host.Call("stream", "roles-test", "&id=" + host.PublicTrack), 50);
        Error(await host.Call("updateUser", "admin", "&username=admin&adminRole=false"), 0);
        Error(await host.Call("updateUser", "admin", "&username=roles-test&maxBitRate=1"), 0);
        Ok(await host.Call("deleteUser", "admin", "&username=roles-test"));
    }
    [DatabaseFact]
    public async Task Public_shares_follow_current_owner_folder_permissions()
    {
        Ok(await host.Call("createUser", "admin", "&username=share-owner&password=Test-Password-123!&shareRole=true&allFolders=false&musicFolderId=" + host.Folder));
        var created = await host.Call("createShare", "share-owner", "&id=" + host.PublicTrack); Ok(created);
        var token = created["shares"]!["share"]![0]!["id"]!.GetValue<string>();
        Ok(await host.Call("updateUser", "admin", "&username=share-owner&allFolders=false"));
        using var client = host.CreateClient();
        var hidden = await client.GetStringAsync($"/share/{token}/stream/{host.PublicTrack}"); Assert.Contains("not found", hidden);
        Ok(await host.Call("updateUser", "admin", "&username=share-owner&shareRole=false"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/share/" + token)).StatusCode);
        Ok(await host.Call("deleteUser", "admin", "&username=share-owner"));
    }
    [DatabaseFact]
    public async Task Saved_queue_retains_duplicate_occurrence_and_can_be_cleared()
    {
        using var client = host.CreateClient();
        var form = new FormUrlEncodedContent(new List<KeyValuePair<string, string>> {
            new("u", "listener"), new("p", "Test-Password-123!"), new("f", "json"), new("c", "test"), new("v", "1.16.1"),
            new("id", host.PublicTrack.ToString()), new("id", host.PublicTrack.ToString()), new("current", host.PublicTrack.ToString()), new("currentIndex", "1"), new("position", "12000") });
        var saved = await client.PostAsync("/rest/savePlayQueue", form); Assert.True(saved.IsSuccessStatusCode);
        var queue = await host.Call("getPlayQueue"); Ok(queue);
        Assert.Equal(1, queue["playQueue"]!["currentIndex"]!.GetValue<int>()); Assert.Equal(2, queue["playQueue"]!["entry"]!.AsArray().Count);
        Assert.Equal(12000, queue["playQueue"]!["position"]!.GetValue<int>());
        Ok(await host.Call("savePlayQueue")); Assert.Null((await host.Call("getPlayQueue"))["playQueue"]);
    }
    [DatabaseFact]
    public async Task Paged_artist_genre_and_directory_views_keep_folder_scope()
    {
        var artists = await host.Call("getArtists", extra: "&size=1&offset=0&musicFolderId=" + host.Folder); Ok(artists);
        Assert.Single(artists["artists"]!["index"]!.AsArray());
        var none = await host.Call("getArtists", extra: "&size=1&offset=1&musicFolderId=" + host.Folder); Ok(none); Assert.Empty(none["artists"]!["index"]!.AsArray());
        var blocked = await host.Call("getGenres", extra: "&musicFolderId=" + host.HiddenFolder); Ok(blocked); Assert.Empty(blocked["genres"]!["genre"]!.AsArray());
        var root = await host.Call("getMusicDirectory", extra: "&id=" + host.Folder + "&size=1"); Ok(root);
        var artistDir = root["directory"]!["child"]![0]!["id"]!.ToString();
        var albumRoot = await host.Call("getMusicDirectory", extra: "&id=" + artistDir + "&size=1"); Ok(albumRoot);
        var albumDir = albumRoot["directory"]!["child"]![0]!["id"]!.ToString();
        var first = await host.Call("getMusicDirectory", extra: "&id=" + albumDir + "&size=1&offset=0");
        var second = await host.Call("getMusicDirectory", extra: "&id=" + albumDir + "&size=1&offset=1"); Ok(first); Ok(second);
        Assert.Equal(2, first["directory"]!["totalChildren"]!.GetValue<int>());
        Assert.NotEqual(first["directory"]!["child"]![0]!["id"]!.ToString(), second["directory"]!["child"]![0]!["id"]!.ToString());
        Assert.False(first["directory"]!["child"]![0]!["isDir"]!.GetValue<bool>());
    }

    [DatabaseFact]
    public async Task Hls_segments_are_authenticated_and_reject_path_traversal()
    {
        using var client = host.CreateClient(); var response = await client.GetAsync(host.Url("hls", extra: "&id=" + host.PublicTrack + "&bitRate=96")); var manifest = await response.Content.ReadAsStringAsync(); Assert.StartsWith("#EXTM3U", manifest);
        var segment = manifest.Split('\n').First(l => l.StartsWith("/rest/")); var bytes = await client.GetAsync(segment); Assert.Equal(HttpStatusCode.OK, bytes.StatusCode); Assert.NotEmpty(await bytes.Content.ReadAsByteArrayAsync());
        Error(await host.Call("hls", extra: "&id=" + host.PublicTrack + "&part=../secret.key"), 70);
        Assert.Contains("Required parameter", await client.GetStringAsync("/rest/hls?f=json&id=" + host.PublicTrack + "&part=segment-00000.ts"));
    }
}

[Collection("PostgreSQL integration")]
public class MigrationIntegrationTests
{
    [DatabaseFact]
    public async Task Upgrade_preserves_existing_user_permissions()
    {
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SONARA_TEST_DATABASE"))
            { Database = "sonara_upgrade_" + Guid.NewGuid().ToString("N") };
        await using var db = new SonaraDbContext(new DbContextOptionsBuilder<SonaraDbContext>().UseNpgsql(connection.ConnectionString).Options);
        try
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
            await migrator.MigrateAsync("20261114233506_AddBookmarksPlayQueuePodcasts");
            await db.Database.ExecuteSqlRawAsync("""INSERT INTO "Users" ("Username", "PasswordHash", "SubsonicSecret", "IsAdmin", "CreatedUtc") VALUES ('existing-user', '', '', false, now())""");
            await migrator.MigrateAsync();
            var user = await db.Users.SingleAsync();
            Assert.True(user.AllFolders && user.StreamRole && user.DownloadRole && user.SettingsRole && user.PlaylistRole && user.CoverArtRole);
            Assert.False(user.ShareRole); Assert.False(user.PodcastRole);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}

[CollectionDefinition("PostgreSQL integration", DisableParallelization = true)]
public sealed class PostgresIntegrationCollection { }
