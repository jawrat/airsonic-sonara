import { Share } from '../ui/share';
import { Component, inject, input, resource, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth';
import { formatDuration } from '../core/format';
import { QueueActions } from '../ui/queue-actions';
import { PlayerService } from '../core/player';
import { StarButton } from '../ui/star-button';
import { SubsonicApi } from '../core/subsonic';

@Component({
  selector: 'app-playlist',
  imports: [Share, RouterLink, QueueActions, StarButton],
  template: `
    @if (playlist.error()) {
      <p class="error" role="alert">{{ playlist.error()?.message }}</p>
    }
    @if (playlist.value(); as p) {
      <h1>{{ p.name }}</h1>
      <p class="muted">
        {{ p.songCount }} tracks · {{ fmt(p.duration) }}
        @if (p.owner) {
          · {{ p.owner }}
        }
      </p>
      <div class="toolbar">
        <button type="button" (click)="player.play(p.entry, 0)" [disabled]="!p.entry.length">
          Play all
        </button>
        <button type="button" (click)="player.enqueue(p.entry)" [disabled]="!p.entry.length">Add playlist to queue</button>
        <app-share [ids]="['pl-' + p.id]" [description]="p.name" [disabled]="!p.entry.length" />
        @if (canEdit(p.owner)) {
          <button type="button" (click)="rename(p.id, p.name)">Rename</button>
          <label class="small">
            <input
              type="checkbox"
              [checked]="p.public"
              (change)="setPublic(p.id, $any($event.target).checked)"
            />
            Public
          </label>
          <button type="button" (click)="remove(p.id, p.name)">Delete</button>
        }
      </div>

      <div class="toolbar"><label>Export format <select #format><option value="m3u8">M3U8</option><option value="pls">PLS</option></select></label><button type="button" (click)="exportFile(p.id, format.value)" [disabled]="exporting()">Export playlist file</button></div>
      @if (exportError()) { <p class="error" role="alert">{{ exportError() }}</p> }
      <ol class="tracks">
        @for (s of p.entry; track $index; let i = $index) {
          <li [class.active]="player.current()?.id === s.id">
            <span class="muted num">{{ i + 1 }}</span>
            <button type="button" class="link" (click)="player.play(p.entry, i)">
              {{ s.title }}
            </button>
            <a class="muted" [routerLink]="['/artist', s.artistId]">{{ s.artist }}</a>
            <span class="muted time">{{ fmt(s.duration) }}</span>
            <app-queue-actions [song]="s" />
            <app-star-button [target]="{ songId: s.id }" [starred]="s.starred" />
            @if (canEdit(p.owner)) {
              <button type="button" class="link" (click)="removeAt(p.id, i)" aria-label="Remove">
                ✕
              </button>
            }
          </li>
        }
      </ol>
    } @else if (playlist.isLoading()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class PlaylistPage {
  protected readonly player = inject(PlayerService);
  private readonly api = inject(SubsonicApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly id = input.required<string>();
  protected readonly fmt = formatDuration;
  protected readonly exporting = signal(false); protected readonly exportError = signal('');
  protected async exportFile(id: string, format: string): Promise<void> {
    this.exporting.set(true); this.exportError.set('');
    try { const url = URL.createObjectURL(await this.api.exportPlaylistFile(id, format)); const a = document.createElement('a'); a.href = url; a.download = `sonara-playlist-${id}.${format}`; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
    catch (e) { this.exportError.set(e instanceof Error ? e.message : 'Export failed.'); }
    finally { this.exporting.set(false); }
  }

  protected readonly playlist = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.getPlaylist(params),
  });

  /** The server enforces ownership; this only decides whether to show the controls. */
  protected canEdit(owner: string | undefined): boolean {
    const s = this.auth.session();
    return !!s && (s.isAdmin || owner === s.username);
  }

  protected async rename(id: string, current: string): Promise<void> {
    const name = window.prompt('Playlist name', current)?.trim();
    if (!name || name === current) return;
    await this.api.updatePlaylist(id, { name });
    this.playlist.reload();
  }

  protected async setPublic(id: string, value: boolean): Promise<void> {
    await this.api.updatePlaylist(id, { public: value });
    this.playlist.reload();
  }

  protected async removeAt(id: string, index: number): Promise<void> {
    await this.api.updatePlaylist(id, { removeIndexes: [index] });
    this.playlist.reload();
  }

  protected async remove(id: string, name: string): Promise<void> {
    if (!window.confirm(`Delete playlist "${name}"?`)) return;
    await this.api.deletePlaylist(id);
    void this.router.navigateByUrl('/playlists');
  }
}
