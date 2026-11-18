using Microsoft.EntityFrameworkCore;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapCommunity(RouteGroupBuilder rest)
    {
        Map(rest, "getInternetRadioStations", GetInternetRadioStations);
        Map(rest, "createInternetRadioStation", CreateInternetRadioStation);
        Map(rest, "updateInternetRadioStation", UpdateInternetRadioStation);
        Map(rest, "deleteInternetRadioStation", DeleteInternetRadioStation);
        Map(rest, "getChatMessages", GetChatMessages);
        Map(rest, "addChatMessage", AddChatMessage);
    }
    public static bool IsPublicHttpUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host);
    private static async Task<IResult> GetInternetRadioStations(SonaraDbContext db)
    {
        var stations = await db.InternetRadioStations.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
        return SubsonicResult.Ok(Obj(("internetRadioStations", Obj(("internetRadioStation", Arr(stations.Select(r => Obj(("id", r.Id.ToString()), ("name", r.Name), ("streamUrl", r.StreamUrl), ("homePageUrl", r.HomePageUrl)))))))));
    }
    private static Task<IResult> CreateInternetRadioStation(string? name, string? streamUrl, string? homepageUrl, HttpContext h, SonaraDbContext db) => SaveRadio(null, name, streamUrl, homepageUrl, h, db);
    private static Task<IResult> UpdateInternetRadioStation(string? id, string? name, string? streamUrl, string? homepageUrl, HttpContext h, SonaraDbContext db) =>
        id is null ? Task.FromResult(MissingParam()) : SaveRadio(id, name, streamUrl, homepageUrl, h, db);
    private static async Task<IResult> SaveRadio(string? id, string? name, string? streamUrl, string? homepageUrl, HttpContext h, SonaraDbContext db)
    {
        if (!IsAdmin(h)) return Forbidden();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(streamUrl)) return MissingParam();
        if (name.Length > 200 || !IsPublicHttpUrl(streamUrl) || (!string.IsNullOrWhiteSpace(homepageUrl) && !IsPublicHttpUrl(homepageUrl)))
            return SubsonicResult.Error(0, "Use a name up to 200 characters and HTTP(S) URLs without credentials.");
        var row = id is null ? new InternetRadioStation() : await db.InternetRadioStations.FindAsync(TaggedId(id, ""));
        if (row is null) return NotFound();
        if (id is null) db.Add(row);
        row.Name = name.Trim(); row.StreamUrl = streamUrl; row.HomePageUrl = homepageUrl;
        await db.SaveChangesAsync(); return SubsonicResult.Ok();
    }
    private static async Task<IResult> DeleteInternetRadioStation(string? id, HttpContext h, SonaraDbContext db)
    {
        if (!IsAdmin(h)) return Forbidden();
        if (!int.TryParse(id, out var n)) return MissingParam();
        if (await db.InternetRadioStations.Where(r => r.Id == n).ExecuteDeleteAsync() == 0) return NotFound();
        return SubsonicResult.Ok();
    }
    private static async Task<IResult> GetChatMessages(long? since, SonaraDbContext db)
    {
        DateTime cutoff;
        try { cutoff = since.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(since.Value).UtcDateTime : DateTime.UtcNow.AddHours(-24); }
        catch (ArgumentOutOfRangeException) { return SubsonicResult.Error(0, "Invalid timestamp."); }
        var messages = await db.ChatMessages.AsNoTracking().Where(m => m.CreatedUtc > cutoff)
            .OrderByDescending(m => m.CreatedUtc).ThenByDescending(m => m.Id).Take(500)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Message, m.CreatedUtc, m.Id, u.Username }).ToListAsync();
        return SubsonicResult.Ok(Obj(("chatMessages", Obj(("chatMessage", Arr(messages.OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id).Select(m => Obj(("username", m.Username), ("time", new DateTimeOffset(m.CreatedUtc).ToUnixTimeMilliseconds()), ("message", m.Message)))))))));
    }
    private static async Task<IResult> AddChatMessage(string? message, HttpContext h, SonaraDbContext db)
    {
        if (string.IsNullOrWhiteSpace(message)) return MissingParam();
        if (message.Length > 2000) return SubsonicResult.Error(0, "Messages may contain at most 2000 characters.");
        db.Add(new ChatMessage { UserId = SubsonicAuthFilter.CurrentUser(h)!.Id, Message = message.Trim() });
        await db.SaveChangesAsync(); return SubsonicResult.Ok();
    }
}
