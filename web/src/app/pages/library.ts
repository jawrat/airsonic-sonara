import { Share } from '../ui/share';
import { Component, computed, ElementRef, inject, resource, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { formatDuration } from '../core/format';
import { Album, AlbumListType, Artist, DirectoryEntry, Genre, Song } from '../core/models';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';
import { Cover } from '../ui/cover';
import { QueueActions } from '../ui/queue-actions';

export const LIBRARY_PAGE_SIZE = 48;
type View = 'albums' | 'artists' | 'genres' | 'genreSongs' | 'folders' | 'search' | 'artistAlbums';
interface State { artist: string; view: View; type: AlbumListType; folderSort: 'newest' | 'alphabeticalByName'; folderId?: number; dir: string; query: string; genre: string; fromYear: number; toYear: number; offset: number }
interface Page { albums: Album[]; artists: Artist[]; songs: Song[]; directories: DirectoryEntry[]; genres: Genre[]; more: boolean; name?: string; parent?: string; indexId?: string }
const VIEWS: View[] = ['albums', 'artists', 'genres', 'genreSongs', 'folders', 'search', 'artistAlbums'];
const MODES: { id: AlbumListType; label: string }[] = [
  { id: 'newest', label: 'Recently added' }, { id: 'alphabeticalByName', label: 'A–Z (album)' }, { id: 'alphabeticalByArtist', label: 'A–Z (artist)' },
  { id: 'random', label: 'Random selection' }, { id: 'recent', label: 'Recently played' }, { id: 'frequent', label: 'Most played' },
  { id: 'highest', label: 'Highest rated' }, { id: 'starred', label: 'Favorites' }, { id: 'byYear', label: 'By year' }, { id: 'byGenre', label: 'By genre' },
];
const emptyPage = (): Page => ({ albums: [], artists: [], songs: [], directories: [], genres: [], more: false });
export function readLibraryState(p: ParamMap): State {
  const number = (key: string, fallback: number) => { const raw = p.get(key); const n = raw === null ? NaN : Number(raw); return Number.isFinite(n) ? Math.trunc(n) : fallback; };
  const rawDir = p.get('dir') ?? '';
  const rootMatch = /^(?:dir-([1-9]\d*)-|([1-9]\d*))$/.exec(rawDir);
  return {
    artist: /^[1-9]\d*$/.test(p.get('artist') ?? '') ? p.get('artist')! : '',
    view: p.get('view') === 'artistAlbums' && !/^[1-9]\d*$/.test(p.get('artist') ?? '') ? 'albums' : VIEWS.includes(p.get('view') as View) ? p.get('view') as View : 'folders',
    type: MODES.some(m => m.id === p.get('type')) ? p.get('type') as AlbumListType : 'newest',
    folderSort: p.get('folderSort') === 'alphabeticalByName' ? 'alphabeticalByName' : 'newest',
    folderId: number('folder', 0) > 0 ? number('folder', 0) : rootMatch ? Number(rootMatch[1] ?? rootMatch[2]) : undefined,
    dir: rootMatch ? '' : rawDir, query: p.get('q') ?? '', genre: p.get('genre') ?? '',
    fromYear: Math.max(0, Math.min(9999, number('fromYear', 1900))), toYear: Math.max(0, Math.min(9999, number('toYear', new Date().getFullYear()))),
    offset: Math.max(0, number('offset', 0)),
  };
}

@Component({
  selector: 'app-library', imports: [Share, RouterLink, FormsModule, DatePipe, Cover, QueueActions],
  template: `
    <div class="library-layout">
      <aside class="folder-pane" aria-label="Folder browser">
        <div class="folder-heading"><h2>Folders</h2><button type="button" class="mobile-toggle" [attr.aria-expanded]="folderPanelOpen()" aria-controls="folder-browser" (click)="folderPanelOpen.set(!folderPanelOpen())">{{ folderPanelOpen() ? 'Hide' : 'Show' }}</button></div>
        <div id="folder-browser" [class.mobile-hidden]="!folderPanelOpen()">
          <label class="folder-picker">Music folder
            <select aria-label="Music folder" [ngModel]="state().folderId ?? ''" (ngModelChange)="folder($event)">
              <option value="">All accessible folders</option>
              @for (f of folders.value() ?? []; track f.id) { <option [ngValue]="f.id">{{ f.name }}</option> }
            </select>
          </label>
          <label class="folder-filter">Find a folder<input type="search" [value]="folderFilter()" (input)="folderFilter.set($any($event.target).value)" placeholder="Filter folders…" /></label>
          <button class="all-folders" type="button" (click)="home()" [attr.aria-pressed]="state().view === 'folders' && !state().dir">Recently added folders</button>
          @if (folderIndex.error()) { <p class="error" role="alert">{{ folderIndex.error()?.message }} <button type="button" (click)="folderIndex.reload()">Retry folders</button></p> }
          @if (folderIndex.isLoading()) { <p role="status">Loading folders…</p> }
          <nav class="alphabet" aria-label="Folder letters">@for (group of folderGroups(); track group.name) { <button type="button" (click)="jumpToLetter(group.name)">{{ group.name }}</button> }</nav>
          <div class="folder-scroll">
            @for (group of folderGroups(); track group.name) {
              <section class="folder-group" [attr.data-letter]="group.name">
                <h3 tabindex="-1">{{ group.name }} <button type="button" class="link" (click)="folderScrollTop()" aria-label="Back to folder letters">↑</button></h3>
                <ul>@for (directory of group.folders; track directory.id) { <li><a [routerLink]="['/']" [queryParams]="folderParams(directory.id)" [attr.aria-current]="state().view === 'folders' && (state().dir === directory.id || data.value()?.indexId === directory.id) ? 'page' : null">{{ directory.title }}</a></li> }</ul>
              </section>
            }
            @if (!folderIndex.isLoading() && !folderIndex.error() && !folderGroups().length) { <p class="muted">{{ folderFilter() ? 'No matching folders.' : 'No music folders found.' }}</p> }
          </div>
        </div>
      </aside>
      <section class="library-content" aria-label="Library results">
    <div class="library-heading"><h1>{{ state().view === 'artistAlbums' ? artistName() : 'Library' }}</h1><button type="button" (click)="refresh()" [disabled]="data.isLoading()">Refresh</button></div>
    <nav class="toolbar" aria-label="Library views">
      @for (v of tabs; track v.id) { <button type="button" (click)="view(v.id)" [attr.aria-pressed]="state().view === v.id || (v.id === 'genres' && state().view === 'genreSongs')">{{ v.label }}</button> }
    </nav>
    <div class="toolbar filters">
      <form class="search" (submit)="search($event, q.value)">
        <input #q type="search" [value]="state().query" [placeholder]="state().view === 'artists' ? 'Filter artists…' : 'Search artists, albums, songs…'" aria-label="Search library" />
        <button type="submit">Search</button>
      </form>
      @if (state().view === 'folders') { <label>Sort folders <select [ngModel]="state().folderSort" (ngModelChange)="sortFolders($event)"><option value="newest">Recently added</option><option value="alphabeticalByName">A–Z (folder)</option></select></label> }
      @if (state().view === 'albums' || state().view === 'artistAlbums') {
        <label>Sort albums <select [ngModel]="state().type" (ngModelChange)="mode($event)">@for (m of modes; track m.id) { <option [ngValue]="m.id">{{ m.label }}</option> }</select></label>
      }
    </div>
    @if ((state().view === 'albums' || state().view === 'artistAlbums') && state().type === 'byYear') {
      <form class="toolbar" (submit)="years($event, from.value, to.value)">
        <label>From year <input #from type="number" min="0" max="9999" [value]="state().fromYear" required /></label>
        <label>To year <input #to type="number" min="0" max="9999" [value]="state().toYear" required /></label>
        <button type="submit">Apply year range</button>
      </form>
    }
    @if (((state().view === 'albums' || state().view === 'artistAlbums') && state().type === 'byGenre') || state().view === 'genreSongs') {
      <div class="toolbar"><label>Genre <select [ngModel]="state().genre" (ngModelChange)="genre($event, state().view === 'genreSongs')"><option value="">Choose a genre</option>@for (g of genres.value() ?? []; track g.value) { <option [ngValue]="g.value">{{ g.value }}</option> }</select></label>
        @if (state().genre) { <button type="button" (click)="genre(state().genre, state().view !== 'genreSongs')">{{ state().view === 'genreSongs' ? 'Show albums' : 'Show songs' }}</button> }
      </div>
    }
    @if (folders.error()) { <p class="error" role="alert">{{ folders.error()?.message }}</p> }
    @if (genres.error()) { <p class="error" role="alert">{{ genres.error()?.message }}</p> }
    @if (data.error()) { <p class="error" role="alert">{{ data.error()?.message }} <button type="button" (click)="data.reload()">Retry</button></p> }
    @if (data.isLoading()) { <p role="status" class="muted">Loading…</p> }
    @if (data.value(); as r) {
      @if (state().view === 'folders') {
        @if (state().dir) { <div class="toolbar"><button type="button" (click)="openFolder(r.parent ?? '')">Up</button><h2>{{ r.name }}</h2><app-share [ids]="[state().dir.startsWith('dir-') ? state().dir : 'dir-' + state().dir + '-']" [description]="r.name ?? 'Music folder'" /></div> }
        @else { <h2>{{ state().folderSort === 'newest' ? 'Recently added folders' : 'Music folders' }}</h2> }
        @if (r.directories.length) { <p class="muted small">Folders {{ state().offset + 1 }}–{{ state().offset + r.directories.length }} · {{ state().folderSort === 'newest' ? 'Recently added' : 'A–Z' }}</p> }
        <ul class="grid album-grid folder-grid">
          @for (d of r.directories; track d.id) { <li class="card"><button type="button" class="tile-button" (click)="openFolder(d.id)"><app-cover [id]="d.coverArt ?? d.id" [label]="d.title" [size]="300" /><span class="title">{{ d.title }}</span></button>@if (d.parentName) { <span class="muted small folder-path">{{ d.parentName }}</span> }@if (d.created) { <span class="muted small">Added {{ d.created | date:'shortDate' }}</span> }<app-share [ids]="[d.id]" [description]="d.title" /></li> }
        </ul>
      }
      @if (r.genres.length) {
        <ul class="grid">@for (g of r.genres; track g.value) { <li class="card"><button type="button" class="link" (click)="genre(g.value, false)">{{ g.value }}</button><span class="muted small">{{ g.albumCount }} albums · {{ g.songCount }} tracks</span><button type="button" (click)="genre(g.value, true)">Browse songs</button></li> }</ul>
      }
      @if (r.artists.length) {
        @if (state().view === 'search') { <h2>Artists</h2> }
        <ul class="grid album-grid artist-grid">@for (a of r.artists; track a.id) { <li class="card"><a [routerLink]="['/']" [queryParams]="artistParams(a.id)" class="cover-link" [attr.aria-label]="'Open artist ' + a.name"><app-cover [id]="a.coverArt ?? ('ar-' + a.id)" [label]="a.name" [folderId]="state().folderId" [size]="300" /></a><a [routerLink]="['/']" [queryParams]="artistParams(a.id)" class="title">{{ a.name }}</a><span class="muted small">{{ a.albumCount }} {{ a.albumCount === 1 ? 'album' : 'albums' }}</span></li> }</ul>
      }
      @if (r.albums.length) {
        @if (state().view === 'search') { <h2>Albums</h2> }
        <p class="muted small album-range">Albums {{ state().offset + 1 }}–{{ state().offset + r.albums.length }}@if (state().view === 'albums' || state().view === 'artistAlbums') { · {{ modeLabel() }} }</p>
        <ul class="grid album-grid">@for (a of r.albums; track a.id) { <li class="card"><a [routerLink]="['/album', a.id]" class="cover-link" [attr.aria-label]="'Open album ' + a.name"><app-cover [id]="a.coverArt" [label]="a.name" [size]="300" /></a><a [routerLink]="['/album', a.id]" class="title">{{ a.name }}</a>
          @if (a.artistId) { <a [routerLink]="['/']" [queryParams]="artistParams(a.artistId)" class="muted album-artist" [title]="a.artist ?? ''">{{ a.artist }}</a> }
          <span class="muted small">{{ a.year ? a.year + ' · ' : '' }}{{ a.songCount }} tracks</span>
          @if (a.created) { <span class="muted small">Added {{ a.created | date:'shortDate' }}</span> }
          <app-share [ids]="['al-' + a.id]" [description]="a.name" />
        </li> }</ul>
      }
      @if (r.songs.length) {
        <div class="toolbar song-actions"><button type="button" (click)="player.play(r.songs)">Play displayed tracks</button><button type="button" (click)="player.enqueue(r.songs)">Queue displayed tracks</button></div>
        <ol class="tracks">@for (s of r.songs; track s.id; let i = $index) { <li>
          <button type="button" class="link" (click)="player.play(r.songs, i)">{{ s.title }}</button><span class="muted small">{{ s.artist }} · {{ s.album }}</span><span class="muted time">{{ fmt(s.duration) }}</span><app-queue-actions [song]="s" />
        </li> }</ol>
      }
      @if (!data.isLoading() && !r.albums.length && !r.artists.length && !r.songs.length && !r.genres.length && !r.directories.length) {
        <p class="muted">{{ needsGenre() ? 'Choose a genre to browse.' : 'No items match this selection.' }}</p>
      }
      @if (state().type === 'random' && (state().view === 'albums' || state().view === 'artistAlbums')) { <button type="button" (click)="data.reload()">Another random selection</button> }
      @if (state().offset > 0 || r.more) {
        <nav class="toolbar pagination" aria-label="Library pages">
          <button type="button" (click)="page(-1)" [disabled]="state().offset === 0 || data.isLoading()">Previous page</button>
          <span>Page {{ Math.floor(state().offset / pageSize) + 1 }}</span>
          <button type="button" (click)="page(1)" [disabled]="!r.more || data.isLoading()">Next page</button>
        </nav>
      }
    }
      </section>
    </div>
  `,
  styles: [`
    :host { display:block; min-width:0; }
    .library-layout { display:grid;grid-template-columns:minmax(12rem,15rem) minmax(0,1fr);gap:1.25rem;align-items:start; }
    .folder-pane { position:sticky;top:.75rem;min-width:0;border-right:1px solid var(--border);padding-right:1rem; }
    .folder-heading,.library-heading { display:flex;align-items:center;justify-content:space-between;gap:.5rem; }
    .folder-heading h2,.library-heading h1 { margin:0 0 .75rem; }
    .library-heading h1 { min-width:0;overflow-wrap:anywhere; }.library-heading button { flex-shrink:0; }
    .folder-picker,.folder-filter { display:grid;gap:.35rem;margin-bottom:.75rem; }
    .folder-picker select,.folder-filter input { width:100%;min-width:0; }
    .all-folders { width:100%;margin-bottom:.7rem;text-align:left; }
    .alphabet { display:flex;flex-wrap:wrap;gap:.15rem;margin:.4rem 0 .75rem; }
    .alphabet button { padding:.15rem .3rem;min-width:1.4rem;background:none;color:var(--accent);border:0;font-weight:600; }
    .folder-scroll { max-height:calc(100dvh - 24rem);min-height:10rem;overflow-y:auto;overscroll-behavior:contain;scrollbar-gutter:stable; }
    :host-context(.with-player) .folder-scroll { max-height:calc(100dvh - 34rem); }
    .folder-group { margin-bottom:.8rem; }.folder-group h3 { display:flex;justify-content:space-between;background:var(--panel-2);padding:.25rem .45rem;margin:0 0 .25rem;font-size:1rem; }
    .folder-group ul { list-style:none;padding:0;margin:0; }.folder-group li { margin:.1rem 0; }
    .folder-group a { display:flex;gap:.5rem;justify-content:space-between;padding:.3rem .45rem;border-radius:.25rem;overflow-wrap:anywhere; }
    .folder-group a[aria-current=page] { background:var(--panel-2);box-shadow:inset 3px 0 var(--accent);color:var(--text); }
    .folder-path { overflow-wrap:anywhere; }
    .library-content { min-width:0; }.mobile-toggle { display:none; }
    .album-grid { grid-template-columns:repeat(auto-fill,minmax(min(100%,10rem),1fr));gap:1rem; }
    .album-grid .card { padding:0;gap:.3rem;padding-bottom:.6rem;min-width:0; }
    .tile-button { display:block;width:100%;padding:0;border:0;background:none;color:var(--accent);text-align:left;border-radius:.35rem; }
    .tile-button .title { margin-top:.3rem; }.tile-button:focus-visible { outline:2px solid var(--accent);outline-offset:3px; }
    .cover-link { display:block; }.cover-link app-cover { border-radius:.35rem; }
    .album-grid .title { white-space:normal;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;line-height:1.3;font-family:inherit;font-size:1rem;text-transform:none;letter-spacing:normal; }
    .album-artist { overflow:hidden;text-overflow:ellipsis;white-space:nowrap; }.album-range { margin:.25rem 0 1rem; }
    @media(max-width:60rem) { .library-layout { grid-template-columns:minmax(10rem,12rem) minmax(0,1fr);gap:.8rem; } }
    @media(max-width:46rem) { .library-layout { grid-template-columns:minmax(0,1fr); }.folder-pane { position:static;border-right:0;border-bottom:1px solid var(--border);padding:0 0 1rem; }.folder-scroll { max-height:15rem;min-height:0; }.mobile-toggle { display:inline-block; }.mobile-hidden { display:none; }.album-grid { grid-template-columns:repeat(auto-fill,minmax(min(100%,9rem),1fr)); }.library-heading { margin-top:.5rem; } }
    .filters { align-items:end; } .filters label { display:grid; gap:.2rem; max-width:100%; } .search { display:flex; gap:.4rem; flex:1; min-width:12rem; }
    .search input { flex:1; min-width:0; } input,select { max-width:100%; } input[type=number] { width:8rem; }
    button[aria-pressed=true] { box-shadow:inset 0 0 0 2px var(--accent); }
    .artist-list,.folder-list { list-style:none; padding:0; display:grid; gap:.7rem; } .song-actions,.pagination { margin-top:1.2rem; }
    .tracks li { flex-wrap:wrap; } .title { overflow-wrap:anywhere; } .artist-list li { overflow-wrap:anywhere; }
  `],
})
export class Library {
  private readonly api = inject(SubsonicApi); protected readonly player = inject(PlayerService);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  protected readonly state = toSignal(this.route.queryParamMap.pipe(map(readLibraryState)), { initialValue: readLibraryState(this.route.snapshot.queryParamMap) });
  protected readonly folderFilter = signal('');
  protected readonly folderPanelOpen = signal(true);
  private readonly indexFolder = computed(() => this.state().folderId ?? 0);
  protected readonly folderIndex = resource({ params: () => this.indexFolder(), loader: ({ params }) => this.api.getFolderIndex(params || undefined) });
  protected readonly folderGroups = computed(() => {
    const query = this.folderFilter().trim().toLocaleLowerCase();
    const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
    const items = [...(this.folderIndex.value() ?? [])].filter(d => !query || d.title.toLocaleLowerCase().includes(query)).sort((a,b) => collator.compare(a.title,b.title) || a.id.localeCompare(b.id));
    const groups = new Map<string, DirectoryEntry[]>();
    for (const item of items) { const first = Array.from(item.title.trim())[0] ?? '#'; const name = /\p{L}/u.test(first) ? first.toLocaleUpperCase() : '#'; if (!groups.has(name)) groups.set(name,[]); groups.get(name)!.push(item); }
    return [...groups].map(([name,folders]) => ({name,folders})).sort((a,b) => a.name === '#' ? -1 : b.name === '#' ? 1 : collator.compare(a.name,b.name));
  });
  protected readonly artistName = computed(() => this.data.value()?.albums[0]?.artist ?? 'Artist albums');
  protected readonly modeLabel = computed(() => MODES.find(m => m.id === this.state().type)?.label ?? 'Recently added');
  protected readonly pageSize = LIBRARY_PAGE_SIZE; protected readonly fmt = formatDuration; protected readonly Math = Math;
  protected readonly modes = MODES;
  protected readonly tabs: { id: View; label: string }[] = [{ id: 'albums', label: 'Albums' }, { id: 'artists', label: 'Artists' }, { id: 'genres', label: 'Genres' }, { id: 'folders', label: 'Folders' }];
  protected readonly folders = resource({ loader: () => this.api.getFolders() });
  protected readonly genres = resource({ params: () => ({ folder: this.state().folderId }), loader: ({ params }) => this.api.getGenres(params.folder) });
  protected readonly data = resource({ params: () => this.state(), loader: async ({ params: s }): Promise<Page> => {
    const r = emptyPage(); const n = LIBRARY_PAGE_SIZE; const requestSize = n + 1;
    if (s.view === 'albums' || s.view === 'artistAlbums') {
      if (s.type === 'byGenre' && !s.genre) return r;
      const items = await this.api.getAlbumList(s.type, requestSize, s.offset, { musicFolderId: s.folderId, ...(s.view === 'artistAlbums' ? { artistId: s.artist } : {}), ...(s.type === 'byGenre' ? { genre: s.genre } : {}), ...(s.type === 'byYear' ? { fromYear: s.fromYear, toYear: s.toYear } : {}) });
      r.albums = items.slice(0, n); r.more = s.type !== 'random' && items.length > n;
    } else if (s.view === 'artists') {
      const items = await this.api.getArtists(requestSize, s.offset, s.folderId, s.query); r.artists = items.slice(0, n); r.more = items.length > n;
    } else if (s.view === 'genres') {
      const items = await this.api.getGenres(s.folderId); r.genres = items.slice(s.offset, s.offset + n); r.more = items.length > s.offset + n;
    } else if (s.view === 'genreSongs') {
      if (!s.genre) return r;
      const items = await this.api.getGenreSongs(s.genre, requestSize, s.offset, s.folderId); r.songs = items.slice(0, n); r.more = items.length > n;
    } else if (s.view === 'folders') {
      const directory = s.dir ? await this.api.getDirectory(s.dir, requestSize, s.offset, s.folderSort) : await this.api.getFolderList(requestSize, s.offset, s.folderId, s.folderSort); const items = directory.child;
      r.name = directory.name; r.parent = directory.parent; r.indexId = directory.indexId; r.more = items.length > n;
      r.directories = items.slice(0, n).filter(c => c.isDir);
      r.songs = items.slice(0, n).filter(c => !c.isDir).map(c => ({ ...c, albumId: c.albumId ?? '', artistId: c.artistId ?? '', duration: c.duration ?? 0, suffix: c.suffix ?? 'mp3' }));
    } else if (s.view === 'search' && s.query) {
      const found = await this.api.search(s.query, requestSize, s.offset, s.folderId);
      r.albums = found.albums.slice(0, n); r.artists = found.artists.slice(0, n); r.songs = found.songs.slice(0, n);
      r.more = found.albums.length > n || found.artists.length > n || found.songs.length > n;
    }
    return r;
  } });
  protected folderParams(id: string): Record<string,string|number> { const s=this.state(); return {view:'folders',dir:id,folderSort:s.folderSort,...(s.folderId ? {folder:s.folderId} : {})}; }
  protected sortFolders(folderSort: string): void { this.update({folderSort,offset:0}); }
  protected artistParams(id: string): Record<string,string|number> {
    const s = this.state();
    return { view: 'artistAlbums', artist: id, type: 'newest', ...(s.folderId ? { folder: s.folderId } : {}) };
  }
  protected home(): void { this.update({ view: 'folders', folderSort: 'newest', type: 'newest', artist: null, q: null, offset: 0, dir: null, genre: null }); }
  protected refresh(): void { this.data.reload();this.folderIndex.reload(); }
  protected jumpToLetter(letter: string): void {
    const section = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('.folder-group')).find(s => s.dataset['letter'] === letter);
    if (section) { const scroll = this.host.nativeElement.querySelector<HTMLElement>('.folder-scroll'); if(scroll) scroll.scrollTop = section.offsetTop - scroll.offsetTop; section.querySelector<HTMLElement>('h3')?.focus({preventScroll:true}); }
  }
  protected folderScrollTop(): void { const scroll=this.host.nativeElement.querySelector<HTMLElement>('.folder-scroll');if(scroll)scroll.scrollTop=0; }
  protected needsGenre(): boolean { return (this.state().view === 'genreSongs' || this.state().type === 'byGenre' && (this.state().view === 'albums' || this.state().view === 'artistAlbums')) && !this.state().genre; }
  private update(changes: Record<string, string | number | null>): void {
    void this.router.navigate(['/'], { queryParams: changes, queryParamsHandling: 'merge' });
  }
  protected view(view: View): void { this.update({ view, artist: null, offset: 0, q: null, dir: null }); }
  protected folder(id: number | string): void { this.update({ folder: id || null, artist: null, view: this.state().view === 'artistAlbums' ? 'albums' : this.state().view, offset: 0, dir: null }); }
  protected mode(type: AlbumListType): void { this.update({ type, offset: 0 }); }
  protected openFolder(dir: string): void { this.update({ view: 'folders', dir: /^(?:dir-[1-9]\d*-|[1-9]\d*)$/.test(dir) ? null : dir || null, offset: 0 }); }
  protected genre(genre: string, songs: boolean): void { this.update({ genre: genre || null, type: 'byGenre', view: songs ? 'genreSongs' : this.state().view === 'artistAlbums' ? 'artistAlbums' : 'albums', offset: 0, q: null }); }
  protected search(event: Event, text: string): void { event.preventDefault(); this.update({ q: text.trim() || null, artist: null, view: this.state().view === 'artists' ? 'artists' : text.trim() ? 'search' : 'albums', offset: 0, dir: null }); }
  protected years(event: Event, from: string, to: string): void { event.preventDefault(); if (from !== '' && to !== '' && Number.isInteger(+from) && Number.isInteger(+to)) this.update({ fromYear: +from, toYear: +to, offset: 0 }); }
  protected page(direction: number): void { this.update({ offset: Math.max(0, this.state().offset + direction * LIBRARY_PAGE_SIZE) }); }
}
