import { Component, inject, resource, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatDuration } from '../core/format';
import { PlaylistImport, SubsonicApi } from '../core/subsonic';

@Component({
  selector: 'app-playlists',
  imports: [RouterLink],
  template: `
    <h1>Playlists</h1>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (message()) { <p role="status">{{ message() }}</p> }
    <form class="toolbar" (submit)="create($event, name)">
      <input #name placeholder="New playlist name" aria-label="New playlist name" />
      <button type="submit">Create</button>
    </form>

    <section class="panel import">
      <h2>Import a playlist file</h2>
      <p class="muted">M3U/M3U8 or PLS, up to 1 MB and 5000 tracks. Only references matching accessible scanned music are imported; files and remote URLs are never fetched.</p>
      <div class="toolbar"><label>Match within <select #folder><option value="">All accessible music folders</option>@for (f of folders.value() ?? []; track f.id) { <option [value]="f.id">{{ f.name }}</option> }</select></label>
      <input type="file" accept=".m3u,.m3u8,.pls" aria-label="Playlist file" [disabled]="busy()" (change)="choose($event, +folder.value || undefined)" /></div>
      @if (preview(); as result) {
        <p>{{ result.matched }} matched · {{ result.unmatched }} missing, ambiguous or unsupported. Only matched rows will be added, preserving order and duplicates.</p>
        @if (result.dryRun) { <form class="toolbar" (submit)="apply($event, importedName.value)"><input #importedName [value]="suggestedName()" placeholder="Playlist name" aria-label="Imported playlist name" required maxlength="200" /><button type="submit" [disabled]="busy() || !result.matched">Create private playlist</button></form> }
        <p class="muted small">Showing up to 200 of {{ result.totalEntries }} references. Choose the file again after changing the music-folder filter.</p>
        <ul class="preview">@for (entry of result.entry; track entry.index) { <li><strong>{{ entry.title || entry.path }}</strong><span class="muted small"> · {{ entry.status }}</span></li> }</ul>
      }
    </section>
    @if (playlists.error()) {
      <p class="error" role="alert">{{ playlists.error()?.message }}</p>
    }
    @if (playlists.value(); as list) {
      @if (!list.length) {
        <p class="muted">No playlists yet. Create one, then add songs from an album page.</p>
      }
      <ul class="tracks">
        @for (p of list; track p.id) {
          <li>
            <a [routerLink]="['/playlist', p.id]">{{ p.name }}</a>
            @if (p.public) {
              <span class="muted small">public</span>
            }
            <span class="muted small">{{ p.owner }}</span>
            <span class="muted time">{{ p.songCount }} tracks · {{ fmt(p.duration) }}</span>
          </li>
        }
      </ul>
    } @else if (playlists.isLoading()) {
      <p class="muted">Loading…</p>
    }
  `,
  styles: [` .import { margin:1rem 0; } .preview { max-height:20rem; overflow:auto; padding-left:1.2rem; overflow-wrap:anywhere; } input[type=file],select { max-width:100%; } label { display:grid; gap:.25rem; } `],
})
export class PlaylistsPage {
  private readonly api = inject(SubsonicApi);
  protected readonly fmt = formatDuration;
  protected readonly folders = resource({ loader: () => this.api.getFolders() });
  protected readonly preview = signal<PlaylistImport | null>(null); protected readonly suggestedName = signal('Imported playlist');
  protected readonly busy = signal(false); protected readonly error = signal(''); protected readonly message = signal('');
  private fileText = ''; private format = ''; private folderId: number | undefined;
  protected async choose(event: Event, folderId?: number): Promise<void> {
    this.preview.set(null); this.fileText = ''; this.error.set(''); this.message.set('');
    const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return;
    this.busy.set(true);
    try {
      if (file.size > 1024 * 1024) throw new Error('Choose a playlist file no larger than 1 MB.');
      this.format = file.name.split('.').pop()?.toLowerCase() ?? ''; this.folderId = folderId;
      this.suggestedName.set(file.name.replace(/\.[^.]+$/, '').slice(0, 200)); this.fileText = await file.text();
      this.preview.set(await this.api.importPlaylistFile(this.fileText, this.format, this.suggestedName(), true, this.folderId));
    } catch (e) { this.error.set(e instanceof Error ? e.message : 'Could not read this playlist.'); }
    finally { this.busy.set(false); }
  }
  protected async apply(event: Event, name: string): Promise<void> {
    event.preventDefault(); if (!this.fileText || !name.trim()) return;
    this.busy.set(true); this.error.set('');
    try {
      const result = await this.api.importPlaylistFile(this.fileText, this.format, name, false, this.folderId);
      this.preview.set(result); this.message.set(`Created private playlist with ${result.matched} tracks.`); this.playlists.reload();
    } catch (e) { this.error.set(e instanceof Error ? e.message : 'Import failed.'); }
    finally { this.busy.set(false); }
  }
  protected readonly playlists = resource({ loader: () => this.api.getPlaylists() });

  protected async create(event: Event, input: HTMLInputElement): Promise<void> {
    event.preventDefault();
    const name = input.value.trim();
    if (!name) return;
    this.error.set('');
    try { await this.api.createPlaylist(name); input.value = ''; this.playlists.reload(); }
    catch (e) { this.error.set(e instanceof Error ? e.message : 'Could not create playlist.'); }
  }
}
