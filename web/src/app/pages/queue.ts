import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { formatDuration } from '../core/format';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';
import { PlaylistWithSongs } from '../core/models';

@Component({
  selector: 'app-queue', imports: [RouterLink, FormsModule],
  template: `
    <h1>Play queue</h1>
    @if (player.syncError()) { <p class="error" role="alert">{{ player.syncError() }}</p> }
    <p class="muted">{{ player.queue().length }} tracks · {{ totalDuration() }} · drag tracks or use the move buttons to reorder.</p>
    <div class="toolbar">
      <button type="button" (click)="player.toggleShuffle()" [attr.aria-pressed]="player.shuffle()">Shuffle {{ player.shuffle() ? 'on' : 'off' }}</button>
      <button type="button" (click)="player.cycleRepeat()">Repeat: {{ player.repeat() }}</button>
      <button type="button" (click)="player.clearUpcoming()" [disabled]="player.index() >= player.queue().length - 1">Clear upcoming</button>
      <button type="button" (click)="confirmClear.set(true)" [disabled]="!player.queue().length">Clear queue</button>
      <button type="button" (click)="showPlaylist.set(!showPlaylist())" [disabled]="!player.queue().length">Create playlist from queue</button>
    </div>
    @if (confirmClear() && player.queue().length) {
      <div class="toolbar" role="group" aria-label="Confirm clearing the queue"><span>Clear all tracks and stop playback?</span><button type="button" (click)="clear()">Clear all tracks</button><button type="button" (click)="confirmClear.set(false)">Cancel</button></div>
    }
    @if (showPlaylist()) {
      <form class="toolbar" (submit)="savePlaylist($event)"><label>Playlist name <input name="playlistName" [(ngModel)]="playlistName" required maxlength="200" [disabled]="saving()" /></label><button type="submit" [disabled]="saving() || !player.queue().length">{{ saving() ? 'Saving…' : 'Save private playlist' }}</button><button type="button" (click)="showPlaylist.set(false)">Cancel</button></form>
      <p class="muted small">Saves the queue in its displayed order, including repeated tracks. Music tracks only.</p>
    }
    @if (playlistError()) { <p class="error" role="alert">{{ playlistError() }}</p> }
    @if (createdPlaylist(); as p) { <p role="status">Created <a [routerLink]="['/playlist', p.id]">{{ p.name }}</a> with {{ p.songCount }} tracks.</p> }
    <ol class="tracks queue">
      @for (song of player.queue(); track song.queueKey; let i = $index) {
        <li draggable="true" (dragstart)="startDrag($event, song.queueKey)" (dragover)="$event.preventDefault()" (drop)="drop($event, song.queueKey)" (dragend)="dragging.set(null)" [class.active]="player.index() === i">
          <span class="muted num">{{ i + 1 }}</span>
          <div class="track-info">
            <button type="button" class="link" (click)="player.jump(i)" [attr.aria-label]="'Play queue track ' + (i + 1) + ': ' + song.title">{{ song.title }}</button>
            <span class="muted small">{{ song.artist }} · {{ song.album }} · {{ fmt(song.duration) }}</span>
          </div>
          <div class="actions">
            <button type="button" (click)="player.move(i, i - 1)" [disabled]="i === 0" [attr.aria-label]="'Move up: ' + song.title">↑</button>
            <button type="button" (click)="player.move(i, i + 1)" [disabled]="i === player.queue().length - 1" [attr.aria-label]="'Move down: ' + song.title">↓</button>
            <button type="button" (click)="player.remove(i)" [attr.aria-label]="'Remove from queue: ' + song.title">Remove</button>
          </div>
        </li>
      } @empty { <li>No tracks queued. <a routerLink="/">Browse the library</a></li> }
    </ol>
  `,
  styles: [`
    .queue li { align-items: center; } .track-info { display:grid; gap:.2rem; min-width:0; flex:1; overflow-wrap:anywhere; }
    .actions { display:flex; gap:.35rem; flex-wrap:wrap; justify-content:flex-end; } .actions button { padding:.4rem .7rem; }
    @media(max-width:40rem) { .queue li { flex-wrap:wrap; } .actions { margin-left:auto; } }
  `],
})
export class QueuePage {
  protected readonly player = inject(PlayerService);
  protected readonly fmt = formatDuration;
  protected readonly dragging = signal<number | null>(null);
  protected totalDuration(): string { return this.fmt(this.player.queue().reduce((sum, s) => sum + (s.duration || 0), 0)); }
  private readonly api = inject(SubsonicApi);
  protected readonly confirmClear = signal(false);
  protected readonly showPlaylist = signal(false);
  protected readonly saving = signal(false);
  protected readonly playlistError = signal('');
  protected readonly createdPlaylist = signal<PlaylistWithSongs | null>(null);
  protected playlistName = 'My play queue';
  protected clear(): void { this.player.clearQueue(); this.confirmClear.set(false); }
  protected async savePlaylist(event: Event): Promise<void> {
    event.preventDefault(); if (this.saving()) return;
    this.playlistError.set(''); this.createdPlaylist.set(null);
    const name = this.playlistName.trim(); const ids = this.player.queue().map(s => s.id);
    if (!name || !ids.length) return;
    if (ids.some(id => !/^[1-9]\d*$/.test(id))) { this.playlistError.set('Playlists currently support music tracks. Remove podcast episodes from the queue before saving it as a playlist.'); return; }
    this.saving.set(true);
    try { this.createdPlaylist.set(await this.api.createPlaylist(name, ids)); this.showPlaylist.set(false); }
    catch (e) { this.playlistError.set(e instanceof Error ? e.message : 'Could not create this playlist.'); }
    finally { this.saving.set(false); }
  }
  protected startDrag(event: DragEvent, key: number): void { this.dragging.set(key); event.dataTransfer?.setData('text/plain', String(key)); }
  protected drop(event: DragEvent, key: number): void {
    event.preventDefault(); const source = this.dragging(); if (source === null) return;
    this.player.move(this.player.queue().findIndex(s => s.queueKey === source), this.player.queue().findIndex(s => s.queueKey === key)); this.dragging.set(null);
  }
}
