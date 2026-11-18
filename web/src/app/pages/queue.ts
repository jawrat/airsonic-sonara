import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatDuration } from '../core/format';
import { PlayerService } from '../core/player';

@Component({
  selector: 'app-queue', imports: [RouterLink],
  template: `
    <h1>Play queue</h1>
    @if (player.syncError()) { <p class="error" role="alert">{{ player.syncError() }}</p> }
    <p class="muted">{{ player.queue().length }} tracks · {{ totalDuration() }} · drag tracks or use the move buttons to reorder.</p>
    <div class="toolbar">
      <button type="button" (click)="player.toggleShuffle()" [attr.aria-pressed]="player.shuffle()">Shuffle {{ player.shuffle() ? 'on' : 'off' }}</button>
      <button type="button" (click)="player.cycleRepeat()">Repeat: {{ player.repeat() }}</button>
      <button type="button" (click)="player.clearUpcoming()" [disabled]="player.index() >= player.queue().length - 1">Clear upcoming</button>
      <button type="button" (click)="clear()" [disabled]="!player.queue().length">Clear queue</button>
    </div>
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
  protected clear(): void { if (window.confirm('Clear the entire play queue on this account?')) this.player.clearQueue(); }
  protected startDrag(event: DragEvent, key: number): void { this.dragging.set(key); event.dataTransfer?.setData('text/plain', String(key)); }
  protected drop(event: DragEvent, key: number): void {
    event.preventDefault(); const source = this.dragging(); if (source === null) return;
    this.player.move(this.player.queue().findIndex(s => s.queueKey === source), this.player.queue().findIndex(s => s.queueKey === key)); this.dragging.set(null);
  }
}
