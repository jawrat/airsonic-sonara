export interface Artist {
  id: string;
  name: string;
  albumCount: number;
  starred?: string;
}

export interface Album {
  id: string;
  name: string;
  artist?: string;
  artistId?: string;
  songCount: number;
  duration: number;
  year?: number;
  genre?: string;
  coverArt?: string;
  created?: string;
  starred?: string;
}

export interface Song {
  id: string;
  title: string;
  coverArt?: string;
  album?: string;
  albumId: string;
  artist?: string;
  artistId: string;
  track?: number;
  discNumber?: number;
  year?: number;
  duration: number;
  suffix: string;
  bitRate?: number;
  starred?: string;
  /** Set for podcast episodes: the channel they belong to. Their albumId/artistId are empty. */
  channelId?: string;
}

export interface AlbumWithSongs extends Album {
  song: Song[];
}

export interface ArtistWithAlbums extends Artist {
  album: Album[];
}

export interface SearchResult {
  artists: Artist[];
  albums: Album[];
  songs: Song[];
}

export type AlbumListType = 'newest' | 'alphabeticalByName' | 'alphabeticalByArtist' | 'random' | 'highest' | 'frequent' | 'recent' | 'starred' | 'byYear' | 'byGenre';

export interface ScanStatus {
  scanning: boolean;
  count: number;
}

export interface Playlist {
  id: string;
  name: string;
  owner?: string;
  public: boolean;
  songCount: number;
  duration: number;
}

export interface PlaylistWithSongs extends Playlist {
  entry: Song[];
}

export interface UserInfo {
  username: string;
  adminRole: boolean;
  allFolders: boolean;
  folder: number[];
  maxBitRate: number;
  preferredTranscodeFormat?: string;
  preferredTranscodeBitRate?: number;
  settingsRole: boolean;
  streamRole: boolean;
  downloadRole: boolean;
  uploadRole?: boolean;
  avatarId?: string;
  hasCustomAvatar?: boolean;
  playlistRole: boolean;
  coverArtRole: boolean;
  podcastRole: boolean;
  shareRole: boolean;
  commentRole: boolean;
}

export interface Bookmark {
  position: number; // milliseconds
  comment?: string;
  changed: string;
  entry: Song;
}

export interface SavedQueue {
  songs: Song[];
  currentId?: string;
  currentIndex?: number;
  positionMs: number;
}

export interface Starred {
  artists: Artist[];
  albums: Album[];
  songs: Song[];
}

export type StarTarget = { songId: string } | { albumId: string } | { artistId: string };

export type EpisodeStatus = 'new' | 'downloading' | 'completed' | 'error' | 'deleted';

export interface PodcastEpisode {
  pinned?: boolean;
  downloaded?: string;
  id: string;
  channelId: string;
  title: string;
  description?: string;
  publishDate?: string;
  status: EpisodeStatus;
  errorMessage?: string;
  /** Present once the episode is downloaded; this is the id to stream. */
  streamId?: string;
  duration: number;
  size: number;
  suffix?: string;
}

export interface PodcastChannel {
  id: string;
  url: string;
  title: string;
  description?: string;
  coverArt: string;
  status: string;
  errorMessage?: string;
  episode?: PodcastEpisode[];
}

export interface PodcastSearchResult {
  title: string;
  author?: string;
  description?: string;
  url: string;
  image?: string;
}

/** An episode in the shape the player understands. Only meaningful once downloaded. */
export function episodeToSong(e: PodcastEpisode, channel: PodcastChannel): Song {
  return {
    id: e.streamId ?? '',
    title: e.title,
    album: channel.title,
    albumId: '',
    artist: channel.title,
    artistId: '',
    channelId: channel.id,
    coverArt: channel.coverArt,
    duration: e.duration,
    suffix: e.suffix ?? 'mp3',
  };
}

export interface MusicFolder { id: number; name: string }
export interface Genre { value: string; songCount: number; albumCount: number }
export interface DirectoryEntry extends Partial<Song> { id: string; title: string; isDir: boolean; parent?: string }
export interface MusicDirectory { id: string; name: string; parent?: string; child: DirectoryEntry[] }
export interface AlbumFilters { artistId?: string; musicFolderId?: number; genre?: string; fromYear?: number; toYear?: number }
