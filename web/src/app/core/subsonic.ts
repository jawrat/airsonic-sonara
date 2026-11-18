import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService, SubsonicEnvelope } from './auth';
import {
  Album,
  AlbumFilters,
  Artist,
  MusicFolder,
  Genre,
  MusicDirectory,
  AlbumListType,
  AlbumWithSongs,
  ArtistWithAlbums,
  ScanStatus,
  SearchResult,
  Bookmark,
  Playlist,
  PlaylistWithSongs,
  PodcastChannel,
  PodcastSearchResult,
  SavedQueue,
  Song,
  StarTarget,
  Starred,
  UserInfo,
} from './models';

type Params = Record<string, string | number | boolean | ReadonlyArray<string | number>>;

/** Passwords travel hex-encoded ("enc:" form of the Subsonic API) so they survive URL encoding untouched. */
export function toEnc(password: string): string {
  const hex = Array.from(new TextEncoder().encode(password), (b) => b.toString(16).padStart(2, '0'));
  return 'enc:' + hex.join('');
}

export class SubsonicError extends Error {
  constructor(
    readonly code: number,
    message: string,
  ) {
    super(message);
  }
}

@Injectable({ providedIn: 'root' })
export class SubsonicApi {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  async getAlbumList(type: AlbumListType, size = 60, offset = 0, filters: AlbumFilters = {}): Promise<Album[]> {
    const r = await this.call<{ albumList2: { album?: Album[] } }>('getAlbumList2', {
      type,
      size,
      offset,
      ...this.filterParams(filters),
    });
    return r.albumList2.album ?? [];
  }

  async getAlbum(id: string): Promise<AlbumWithSongs> {
    const r = await this.call<{ album: AlbumWithSongs }>('getAlbum', { id });
    return { ...r.album, song: r.album.song ?? [] };
  }

  async getArtist(id: string): Promise<ArtistWithAlbums> {
    const r = await this.call<{ artist: ArtistWithAlbums }>('getArtist', { id });
    return { ...r.artist, album: r.artist.album ?? [] };
  }

  async search(query: string, size = 20, offset = 0, musicFolderId?: number): Promise<SearchResult> {
    const r = await this.call<{
      searchResult3: { artist?: SearchResult['artists']; album?: Album[]; song?: Song[] };
    }>('search3', { query, artistCount: size, albumCount: size, songCount: size, artistOffset: offset, albumOffset: offset, songOffset: offset, ...(musicFolderId ? { musicFolderId } : {}) });
    return {
      artists: r.searchResult3.artist ?? [],
      albums: r.searchResult3.album ?? [],
      songs: r.searchResult3.song ?? [],
    };
  }

  private filterParams(filters: AlbumFilters): Params {
    const params: Params = {};
    for (const [key, value] of Object.entries(filters)) if (value !== undefined && value !== '') params[key] = value;
    return params;
  }
  async getFolders(): Promise<MusicFolder[]> {
    return (await this.call<{ musicFolders: { musicFolder?: MusicFolder[] } }>('getMusicFolders')).musicFolders.musicFolder ?? [];
  }
  async getArtistIndex(musicFolderId?: number): Promise<{ name: string; artist: Artist[] }[]> {
    const result = await this.call<{ artists: { ignoredArticles?: string; index?: { name: string; artist?: Artist[] }[] } }>('getArtists', musicFolderId ? { musicFolderId } : {});
    const articles = new Set((result.artists.ignoredArticles ?? 'The El La Los Las Le Les').toLocaleLowerCase().split(/\s+/));
    const sortName = (artist: Artist) => {
      const name = artist.name.trim(); const words = name.split(/\s+/);
      return words.length > 1 && articles.has(words[0].toLocaleLowerCase()) ? words.slice(1).join(' ') : name;
    };
    const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true });
    const artists = (result.artists.index ?? []).flatMap(group => group.artist ?? [])
      .sort((a,b) => collator.compare(sortName(a),sortName(b)) || collator.compare(a.name,b.name) || collator.compare(a.id,b.id));
    const groups = new Map<string, Artist[]>();
    for (const artist of artists) {
      const first = Array.from(sortName(artist))[0] ?? '#'; const letter = /\p{L}/u.test(first) ? first.toLocaleUpperCase() : '#';
      const group = groups.get(letter) ?? []; group.push(artist); groups.set(letter,group);
    }
    return Array.from(groups, ([name,artist]) => ({name,artist})).sort((a,b) => a.name === '#' ? -1 : b.name === '#' ? 1 : collator.compare(a.name,b.name));
  }
  async getArtists(size = 49, offset = 0, musicFolderId?: number, query = ''): Promise<Artist[]> {
    const r = await this.call<{ artists: { index?: { artist?: Artist[] }[] } }>('getArtists', { size, offset, query, ...(musicFolderId ? { musicFolderId } : {}) });
    return (r.artists.index ?? []).flatMap(i => i.artist ?? []);
  }
  async getGenres(musicFolderId?: number): Promise<Genre[]> {
    return (await this.call<{ genres: { genre?: Genre[] } }>('getGenres', musicFolderId ? { musicFolderId } : {})).genres.genre ?? [];
  }
  async getGenreSongs(genre: string, count = 49, offset = 0, musicFolderId?: number): Promise<Song[]> {
    return (await this.call<{ songsByGenre: { song?: Song[] } }>('getSongsByGenre', { genre, count, offset, ...(musicFolderId ? { musicFolderId } : {}) })).songsByGenre.song ?? [];
  }
  async getDirectory(id: string, size = 49, offset = 0): Promise<MusicDirectory> {
    const r = await this.call<{ directory: MusicDirectory }>('getMusicDirectory', { id, size, offset });
    return { ...r.directory, child: r.directory.child ?? [] };
  }
  async reportPlayback(id: string, submission: boolean): Promise<void> {
    await this.callForm('scrobble', { id, submission, time: Date.now() });
  }

  async startScan(): Promise<ScanStatus> {
    return (await this.call<{ scanStatus: ScanStatus }>('startScan')).scanStatus;
  }

  async getScanStatus(): Promise<ScanStatus> {
    return (await this.call<{ scanStatus: ScanStatus }>('getScanStatus')).scanStatus;
  }

  async getPlaylists(): Promise<Playlist[]> {
    const r = await this.call<{ playlists: { playlist?: Playlist[] } }>('getPlaylists');
    return r.playlists.playlist ?? [];
  }

  async getPlaylist(id: string): Promise<PlaylistWithSongs> {
    const r = await this.call<{ playlist: PlaylistWithSongs }>('getPlaylist', { id });
    return { ...r.playlist, entry: r.playlist.entry ?? [] };
  }

  async createPlaylist(name: string, songIds: string[] = []): Promise<PlaylistWithSongs> {
    const r = await this.call<{ playlist: PlaylistWithSongs }>('createPlaylist', {
      name,
      songId: songIds,
    });
    return { ...r.playlist, entry: r.playlist.entry ?? [] };
  }

  async updatePlaylist(
    id: string,
    changes: { name?: string; public?: boolean; addSongIds?: string[]; removeIndexes?: number[] },
  ): Promise<void> {
    const extra: Params = { playlistId: id };
    if (changes.name !== undefined) extra['name'] = changes.name;
    if (changes.public !== undefined) extra['public'] = changes.public;
    if (changes.addSongIds?.length) extra['songIdToAdd'] = changes.addSongIds;
    if (changes.removeIndexes?.length) extra['songIndexToRemove'] = changes.removeIndexes;
    await this.call('updatePlaylist', extra);
  }

  async deletePlaylist(id: string): Promise<void> {
    await this.call('deletePlaylist', { id });
  }

  async getStarred(): Promise<Starred> {
    const r = await this.call<{
      starred2: { artist?: Starred['artists']; album?: Album[]; song?: Song[] };
    }>('getStarred2');
    return {
      artists: r.starred2.artist ?? [],
      albums: r.starred2.album ?? [],
      songs: r.starred2.song ?? [],
    };
  }

  async setStar(target: StarTarget, starred: boolean): Promise<void> {
    const extra: Params =
      'songId' in target
        ? { id: target.songId }
        : 'albumId' in target
          ? { albumId: target.albumId }
          : { artistId: target.artistId };
    await this.call(starred ? 'star' : 'unstar', extra);
  }

  async getBookmarks(): Promise<Bookmark[]> {
    const r = await this.call<{ bookmarks: { bookmark?: Bookmark[] } }>('getBookmarks');
    return r.bookmarks.bookmark ?? [];
  }

  async createBookmark(songId: string, positionMs: number, comment?: string): Promise<void> {
    const extra: Params = { id: songId, position: Math.round(positionMs) };
    if (comment) extra['comment'] = comment;
    await this.call('createBookmark', extra);
  }

  async deleteBookmark(songId: string): Promise<void> {
    await this.call('deleteBookmark', { id: songId });
  }

  async savePlayQueue(songIds: string[], currentId: string | undefined, positionMs: number, currentIndex?: number): Promise<void> {
    const extra: Params = { id: songIds, position: Math.round(positionMs), c: 'sonara-web' };
    if (currentId) extra['current'] = currentId;
    if (currentIndex !== undefined) extra['currentIndex'] = currentIndex;
    await this.callForm('savePlayQueue', extra);
  }

  async getPlayQueue(): Promise<SavedQueue | null> {
    const r = await this.call<{ playQueue?: { entry?: Song[]; current?: string; position?: number; currentIndex?: number } }>(
      'getPlayQueue',
    );
    const q = r.playQueue;
    if (!q?.entry?.length) return null;
    return { songs: q.entry, currentId: q.current, positionMs: q.position ?? 0, currentIndex: q.currentIndex };
  }

  async getPodcasts(): Promise<PodcastChannel[]> {
    const r = await this.call<{ podcasts: { channel?: PodcastChannel[] } }>('getPodcasts', {
      includeEpisodes: false,
    });
    return r.podcasts.channel ?? [];
  }

  async getPodcast(id: string): Promise<PodcastChannel & { episode: NonNullable<PodcastChannel['episode']> }> {
    const r = await this.call<{ podcasts: { channel?: PodcastChannel[] } }>('getPodcasts', {
      id,
      includeEpisodes: true,
    });
    const channel = r.podcasts.channel?.[0];
    if (!channel) throw new SubsonicError(70, 'Podcast not found');
    return { ...channel, episode: channel.episode ?? [] };
  }

  async createPodcast(url: string): Promise<void> {
    await this.call('createPodcastChannel', { url });
  }

  async deletePodcast(id: string): Promise<void> {
    await this.call('deletePodcastChannel', { id });
  }

  async refreshPodcasts(id?: string): Promise<void> {
    await this.call('refreshPodcasts', id ? { id } : {});
  }

  async downloadEpisode(id: string): Promise<void> {
    await this.call('downloadPodcastEpisode', { id });
  }

  async deleteEpisode(id: string): Promise<void> {
    await this.call('deletePodcastEpisode', { id });
  }

  async searchPodcasts(query: string): Promise<PodcastSearchResult[]> {
    const r = await this.call<{ podcastSearch: { feed?: PodcastSearchResult[] } }>('searchPodcasts', { query });
    return r.podcastSearch.feed ?? [];
  }

  async getUsers(): Promise<UserInfo[]> {
    const r = await this.call<{ users: { user?: UserInfo[] } }>('getUsers');
    return r.users.user ?? [];
  }

  async createUser(username: string, password: string, adminRole: boolean): Promise<void> {
    await this.call('createUser', { username, password: toEnc(password), adminRole });
  }

  async updateUser(username: string, changes: { adminRole?: boolean; password?: string }): Promise<void> {
    const extra: Params = { username };
    if (changes.adminRole !== undefined) extra['adminRole'] = changes.adminRole;
    if (changes.password) extra['password'] = toEnc(changes.password);
    await this.call('updateUser', extra);
  }

  async deleteUser(username: string): Promise<void> {
    await this.call('deleteUser', { username });
  }

  async changePassword(username: string, password: string): Promise<void> {
    await this.call('changePassword', { username, password: toEnc(password) });
  }

  /** URL usable directly as an <audio> src. Auth travels as a token, not the password. */
  streamUrl(id: string): string {
    const params = new URLSearchParams({ ...this.auth.currentParams(), id });
    return `/rest/stream?${params}`;
  }

  /** Image URL for an <img> tag, or null if there is no cover id. Auth travels as a token. */
  coverArtUrl(id: string | undefined | null, size?: number): string | null {
    if (!id) return null;
    const params = new URLSearchParams({ ...this.auth.currentParams(), id });
    if (size) params.set('size', String(size));
    return `/rest/getCoverArt?${params}`;
  }

  async importPodcastOpml(opml: string, dryRun = true, selectedUrls: string[] = []): Promise<PodcastImport> {
    return (await this.callForm<{ podcastImport: PodcastImport }>('importPodcastOpml', { opml, dryRun, selectedUrl: selectedUrls })).podcastImport;
  }

  async exportPodcastOpml(): Promise<Blob> {
    return firstValueFrom(this.http.get('/rest/exportPodcasts/opml', { params: this.auth.currentParams(), responseType: 'blob' }));
  }

  async setEpisodePinned(id: string, pinned: boolean): Promise<void> {
    await this.callForm('setPodcastEpisodePinned', { id, pinned });
  }

  async retentionPreview(): Promise<PodcastRetentionPreview> {
    return (await this.call<{ podcastRetention: PodcastRetentionPreview }>('getPodcastRetentionPreview')).podcastRetention;
  }

  async runRetention(): Promise<{ removed: number; failed: number; bytes: number }> {
    return (await this.callForm<{ podcastRetentionResult: { removed: number; failed: number; bytes: number } }>('runPodcastRetention', {})).podcastRetentionResult;
  }

  async getBackups(): Promise<BackupList> {
    return (await this.call<{ backups: BackupList }>('getBackups')).backups;
  }
  async createBackup(): Promise<void> { await this.callForm('createBackup', {}); }
  async downloadBackup(name: string): Promise<Blob> { return this.downloadFile('downloadBackup', { name }, 'application/zip'); }
  async importPlaylistFile(content: string, format: string, name: string, dryRun: boolean, musicFolderId?: number): Promise<PlaylistImport> {
    return (await this.callForm<{ playlistImport: PlaylistImport }>('importPlaylistFile', { content, format, name, dryRun, ...(musicFolderId ? { musicFolderId } : {}) })).playlistImport;
  }
  async exportPlaylistFile(id: string, format: string): Promise<Blob> {
    return this.downloadFile('exportPlaylistFile', { id, format }, format === 'pls' ? 'audio/x-scpls' : 'audio/x-mpegurl');
  }
  private async downloadFile(endpoint: string, extra: Params, type: string): Promise<Blob> {
    const result = await firstValueFrom(this.http.get(`/rest/${endpoint}`, { params: { ...this.auth.currentParams(), ...extra }, responseType: 'blob', observe: 'response' }));
    if (!result.body) throw new Error('The server returned an empty file.');
    if (!result.headers.get('Content-Type')?.toLowerCase().startsWith(type)) {
      const text = await result.body.text();
      try { const body = JSON.parse(text)['subsonic-response']; throw new SubsonicError(body.error?.code ?? 0, body.error?.message ?? 'Download failed'); }
      catch (e) { if (e instanceof SubsonicError) throw e; throw new Error('The server could not prepare this download.'); }
    }
    return result.body;
  }

  avatarUrl(username: string, version = 0): string {
    return '/rest/getAvatar?' + new URLSearchParams({...this.auth.currentParams(), username, avatarVersion: String(version)});
  }
  builtinAvatarUrl(avatarId: string): string {
    return '/rest/getAvatar?' + new URLSearchParams({...this.auth.currentParams(), avatarId});
  }
  async setAvatar(username: string, avatarId: string, file?: File): Promise<void> {
    const form = new FormData(); form.set('username', username); form.set('avatarId', avatarId);
    if (file) form.set('file', file);
    await this.multipart('setAvatar', form);
  }
  async uploadMusic(musicFolderId: number, subfolder: string, file: File): Promise<void> {
    const form = new FormData(); form.set('musicFolderId', String(musicFolderId)); form.set('subfolder', subfolder); form.set('file', file);
    await this.multipart('uploadMusic', form);
  }
  private async multipart(endpoint: string, form: FormData): Promise<void> {
    const res = await firstValueFrom(this.http.post<SubsonicEnvelope<object>>('/rest/' + endpoint, form, {params:this.auth.currentParams()}));
    const body = res['subsonic-response'];
    if (body.status !== 'ok') throw new SubsonicError(body.error?.code ?? 0, body.error?.message ?? 'Upload failed');
  }

  async setTranscodingPreferences(username: string, preferredTranscodeFormat: string, preferredTranscodeBitRate: number): Promise<void> {
    await this.callForm('setTranscodingPreferences', { username, preferredTranscodeFormat, preferredTranscodeBitRate });
  }

  private async callForm<T = object>(endpoint: string, extra: Params): Promise<T> {
    const form = new URLSearchParams();
    for (const [key, value] of Object.entries({ ...this.auth.currentParams(), ...extra }))
      for (const v of Array.isArray(value) ? value : [value]) form.append(key, String(v));
    const res = await firstValueFrom(this.http.post<SubsonicEnvelope<T>>(`/rest/${endpoint}`, form.toString(), {
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    }));
    const body = res['subsonic-response'];
    if (body.status !== 'ok') throw new SubsonicError(body.error?.code ?? 0, body.error?.message ?? 'Request failed');
    return body;
  }

  async call<T = object>(endpoint: string, extra: Params = {}): Promise<T> {
    const params = { ...this.auth.currentParams(), ...extra };
    const res = await firstValueFrom(
      this.http.get<SubsonicEnvelope<T>>(`/rest/${endpoint}`, { params }),
    );
    const body = res['subsonic-response'];
    if (body.status !== 'ok') {
      throw new SubsonicError(body.error?.code ?? 0, body.error?.message ?? 'Request failed');
    }
    return body;
  }
}

export interface PodcastImport {
  dryRun: boolean;
  ready: number;
  queued: number;
  existing: number;
  duplicate: number;
  invalid: number;
  item: { title: string; url: string; status: string; message?: string }[];
}
export interface PodcastRetentionPreview {
  enabled: boolean;
  days: number;
  keepLatest: number;
  protected: number;
  count: number;
  bytes: number;
  episode: { id: number; title: string; channel: string; bytes: number; reason: string }[];
}

export interface BackupList {
  busy: boolean; phase: string; lastFile?: string; lastError?: string; lastCompleted?: string;
  backup: { name: string; bytes: number; created: string }[];
}
export interface PlaylistImport {
  dryRun: boolean; playlistId?: string; matched: number; unmatched: number; totalEntries: number;
  entry: { index: number; path: string; status: string; title?: string }[];
}
