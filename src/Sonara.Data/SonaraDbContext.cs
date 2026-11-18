using Microsoft.EntityFrameworkCore;
using Sonara.Core.Entities;

namespace Sonara.Data;

public class SonaraDbContext(DbContextOptions<SonaraDbContext> options) : DbContext(options)
{
    public DbSet<MusicFolder> MusicFolders => Set<MusicFolder>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistEntry> PlaylistEntries => Set<PlaylistEntry>();
    public DbSet<Star> Stars => Set<Star>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();
    public DbSet<PlayQueue> PlayQueues => Set<PlayQueue>();
    public DbSet<PodcastChannel> PodcastChannels => Set<PodcastChannel>();
    public DbSet<PodcastEpisode> PodcastEpisodes => Set<PodcastEpisode>();

    // Null for trusted background jobs. Authentication sets these before any library queries.
    public int? AccessUserId { get; set; }
    public bool AccessAllFolders { get; set; }
    public DbSet<UserMusicFolder> UserMusicFolders => Set<UserMusicFolder>();
    public DbSet<ServerSetting> ServerSettings => Set<ServerSetting>();
    public DbSet<MediaRating> MediaRatings => Set<MediaRating>();
    public DbSet<PlayEvent> PlayEvents => Set<PlayEvent>();
    public DbSet<NowPlayingEntry> NowPlayingEntries => Set<NowPlayingEntry>();
    public DbSet<MediaShare> MediaShares => Set<MediaShare>();
    public DbSet<InternetRadioStation> InternetRadioStations => Set<InternetRadioStation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ServerSetting>().HasKey(s => s.Key);
        b.Entity<UserMusicFolder>().HasKey(x => new { x.UserId, x.MusicFolderId });
        b.Entity<UserMusicFolder>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserMusicFolder>().HasOne<MusicFolder>().WithMany().HasForeignKey(x => x.MusicFolderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MediaRating>().HasKey(x => new { x.UserId, x.MediaId });
        b.Entity<MediaRating>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlayEvent>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlayEvent>().HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlayEvent>().HasIndex(x => new { x.UserId, x.PlayedUtc });
        b.Entity<NowPlayingEntry>().HasKey(x => new { x.UserId, x.Client });
        b.Entity<NowPlayingEntry>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<NowPlayingEntry>().HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MediaShare>().HasKey(x => x.Id);
        b.Entity<MediaShare>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChatMessage>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChatMessage>().HasIndex(x => x.CreatedUtc);
        b.Entity<MusicFolder>().HasQueryFilter(f => AccessUserId == null ||
            (f.Enabled && (AccessAllFolders || UserMusicFolders.Any(x => x.UserId == AccessUserId && x.MusicFolderId == f.Id))));
        b.Entity<Track>().HasQueryFilter(t => AccessUserId == null ||
            MusicFolders.Any(f => f.Id == t.MusicFolderId));
        b.Entity<Album>().HasQueryFilter(a => AccessUserId == null || Tracks.Any(t => t.AlbumId == a.Id));
        b.Entity<Artist>().HasQueryFilter(a => AccessUserId == null || Tracks.Any(t => t.ArtistId == a.Id ||
            Albums.IgnoreQueryFilters().Any(al => al.Id == t.AlbumId && al.ArtistId == a.Id)));

        b.Entity<User>().HasIndex(u => u.Username).IsUnique();

        b.Entity<Artist>().HasIndex(a => a.Name);

        b.Entity<Album>().HasIndex(a => new { a.ArtistId, a.Title });
        b.Entity<Album>()
            .HasOne(a => a.Artist).WithMany(a => a.Albums)
            .HasForeignKey(a => a.ArtistId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Track>().HasIndex(t => t.Path).IsUnique();
        b.Entity<Track>()
            .HasOne(t => t.Album).WithMany(a => a.Tracks)
            .HasForeignKey(t => t.AlbumId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Track>()
            .HasOne(t => t.Artist).WithMany()
            .HasForeignKey(t => t.ArtistId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Track>()
            .HasOne(t => t.MusicFolder).WithMany()
            .HasForeignKey(t => t.MusicFolderId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Playlist>()
            .HasOne(p => p.Owner).WithMany()
            .HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlaylistEntry>()
            .HasOne(e => e.Playlist).WithMany(p => p.Entries)
            .HasForeignKey(e => e.PlaylistId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlaylistEntry>()
            .HasOne(e => e.Track).WithMany()
            .HasForeignKey(e => e.TrackId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Star>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Star>().HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Star>().HasOne<Album>().WithMany().HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Star>().HasOne<Artist>().WithMany().HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Cascade);
        // One star per user per item. Postgres treats NULLs as distinct, so each kind needs its own filtered index.
        b.Entity<Star>().HasIndex(x => new { x.UserId, x.TrackId }).IsUnique().HasFilter("\"TrackId\" IS NOT NULL");
        b.Entity<Star>().HasIndex(x => new { x.UserId, x.AlbumId }).IsUnique().HasFilter("\"AlbumId\" IS NOT NULL");
        b.Entity<Star>().HasIndex(x => new { x.UserId, x.ArtistId }).IsUnique().HasFilter("\"ArtistId\" IS NOT NULL");

        b.Entity<Bookmark>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Bookmark>().HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Bookmark>().HasOne(x => x.Episode).WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Bookmark>().HasIndex(x => new { x.UserId, x.TrackId }).IsUnique().HasFilter("\"TrackId\" IS NOT NULL");
        b.Entity<Bookmark>().HasIndex(x => new { x.UserId, x.EpisodeId }).IsUnique().HasFilter("\"EpisodeId\" IS NOT NULL");

        b.Entity<PodcastChannel>().HasIndex(x => x.Url).IsUnique();
        b.Entity<PodcastChannel>().Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Entity<PodcastEpisode>().Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Entity<PodcastEpisode>().HasOne(x => x.Channel).WithMany(c => c.Episodes).HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PodcastEpisode>().HasIndex(x => new { x.ChannelId, x.Guid }).IsUnique();
        b.Entity<PodcastEpisode>().HasIndex(x => x.PublishUtc);

        b.Entity<PlayQueue>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlayQueue>().HasIndex(x => x.UserId).IsUnique();
    }
}
