namespace Sonara.Core.Entities;

public class MusicFolder
{
    public bool AllowUploads { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class Artist
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Denormalised; refreshed by the scanner.</summary>
    public int AlbumCount { get; set; }

    public List<Album> Albums { get; set; } = [];
}

public class Album
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public int? Year { get; set; }
    public string? Genre { get; set; }

    /// <summary>Denormalised; refreshed by the scanner.</summary>
    public int SongCount { get; set; }

    /// <summary>Denormalised; refreshed by the scanner.</summary>
    public int DurationSeconds { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<Track> Tracks { get; set; } = [];
}

public class Track
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    public int AlbumId { get; set; }
    public Album Album { get; set; } = null!;

    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;

    public int MusicFolderId { get; set; }
    public MusicFolder MusicFolder { get; set; } = null!;

    /// <summary>Absolute path on disk.</summary>
    public string Path { get; set; } = "";

    public int? TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public int DurationSeconds { get; set; }
    public int BitRate { get; set; }
    public long Size { get; set; }
    public string Suffix { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class User
{
    public string? Email { get; set; }
    public bool SettingsRole { get; set; } = true;
    public bool StreamRole { get; set; } = true;
    public bool DownloadRole { get; set; } = true;
    public bool PlaylistRole { get; set; } = true;
    public bool CoverArtRole { get; set; } = true;
    public bool PodcastRole { get; set; }
    public bool ShareRole { get; set; }
    public bool UploadRole { get; set; }
    public bool CommentRole { get; set; } = true;
    public bool AllFolders { get; set; } = true;
    public string AvatarId { get; set; } = "";
    public byte[]? CustomAvatar { get; set; }
    public int MaxBitRate { get; set; }
    public string PreferredTranscodeFormat { get; set; } = "";
    public int PreferredTranscodeBitRate { get; set; }

    public int Id { get; set; }
    public string Username { get; set; } = "";

    /// <summary>One-way hash, used for the web UI / future session login.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>
    /// AES-GCM encrypted password. The Subsonic token scheme (md5(password + salt))
    /// requires the server to know the password, so it has to be reversible.
    /// </summary>
    public string SubsonicSecret { get; set; } = "";

    public bool IsAdmin { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class Playlist
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public bool IsPublic { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
    public List<PlaylistEntry> Entries { get; set; } = [];
}

public class PlaylistEntry
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;
    public int TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public int Position { get; set; }
}

/// <summary>A user's star on exactly one of a track, album or artist.</summary>
public class Star
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int? TrackId { get; set; }
    public int? AlbumId { get; set; }
    public int? ArtistId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A saved listening position in one track (one per user and track), e.g. for long recordings.</summary>
public class Bookmark
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    /// <summary>Exactly one of TrackId / EpisodeId is set.</summary>
    public int? TrackId { get; set; }
    public Track? Track { get; set; }
    public int? EpisodeId { get; set; }
    public PodcastEpisode? Episode { get; set; }
    public long PositionMs { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>The play queue a user last saved, so another device can pick up where they left off.</summary>
public class PlayQueue
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public List<int> TrackIds { get; set; } = [];
    public int? CurrentTrackId { get; set; }
    public int? CurrentIndex { get; set; }
    public long PositionMs { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
}

public enum PodcastStatus
{
    New,
    Downloading,
    Completed,
    Error,
    Deleted,
}

public class PodcastChannel
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>Local copy of the channel image, if it could be downloaded.</summary>
    public string? ImagePath { get; set; }
    public PodcastStatus Status { get; set; } = PodcastStatus.New;
    public string? ErrorMessage { get; set; }
    public DateTime? LastRefreshUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<PodcastEpisode> Episodes { get; set; } = [];
}

public class PodcastEpisode
{
    public int Id { get; set; }
    public int ChannelId { get; set; }
    public PodcastChannel Channel { get; set; } = null!;
    /// <summary>The feed's identifier for the episode (falls back to the audio URL).</summary>
    public string Guid { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? PublishUtc { get; set; }
    public string AudioUrl { get; set; } = "";
    public DateTime? DownloadedUtc { get; set; }
    public bool Pinned { get; set; }
    public PodcastStatus Status { get; set; } = PodcastStatus.New;
    public string? ErrorMessage { get; set; }
    public string? FilePath { get; set; }
    public long Size { get; set; }
    public string? ContentType { get; set; }
    public string? Suffix { get; set; }
    public int DurationSeconds { get; set; }
}

public class UserMusicFolder
{
    public int UserId { get; set; }
    public int MusicFolderId { get; set; }
}
public class ServerSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
public class MediaRating
{
    public int UserId { get; set; }
    // t-{id}, al-{id}, ar-{id}; filesystem directory IDs are separate from tag IDs.
    public string MediaId { get; set; } = "";
    public int Rating { get; set; }
}
public class PlayEvent
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public int TrackId { get; set; }
    public DateTime PlayedUtc { get; set; } = DateTime.UtcNow;
}
public class NowPlayingEntry
{
    public int UserId { get; set; }
    public string Client { get; set; } = "";
    public int TrackId { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
}
public class MediaShare
{
    public string Id { get; set; } = "";
    public int UserId { get; set; }
    public string? Description { get; set; }
    public List<int> TrackIds { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresUtc { get; set; }
    public DateTime? LastVisitedUtc { get; set; }
    public long VisitCount { get; set; }
}
public class InternetRadioStation
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string StreamUrl { get; set; } = "";
    public string? HomePageUrl { get; set; }
}
public class ChatMessage
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
