import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatDuration } from '../core/format';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';
import { Cover } from './cover';

@Component({
  selector: 'app-player-bar', imports: [RouterLink, Cover],
  template: `
    @if (player.current(); as song) {
      <footer class="bar" aria-label="Audio player">
        <div class="now">
          <app-cover [id]="song.coverArt" [label]="song.album || song.title" [size]="96" class="art" />
          <div class="meta">
            <a [routerLink]="song.channelId ? ['/podcast', song.channelId] : ['/album', song.albumId]" class="title">{{ song.title }}</a>
            <span class="muted small">{{ song.artist }}</span>
            <a routerLink="/queue" class="small">Queue {{ player.index() + 1 }} / {{ player.queue().length }}</a>
          </div>
        </div>
        <div class="controls">
          <button type="button" (click)="player.prev()" [disabled]="!player.hasPrev()" aria-label="Previous">⏮</button>
          <button type="button" (click)="player.toggle()" [attr.aria-label]="player.playing() ? 'Pause' : 'Play'">{{ player.playing() ? '⏸' : '▶' }}</button>
          <button type="button" (click)="player.next()" [disabled]="!player.hasNext()" aria-label="Next">⏭</button>
          <button type="button" (click)="player.toggleShuffle()" [attr.aria-pressed]="player.shuffle()" aria-label="Shuffle">Shuffle {{ player.shuffle() ? 'on' : 'off' }}</button>
          <button type="button" (click)="player.cycleRepeat()" [attr.aria-label]="'Repeat: ' + player.repeat()">Repeat {{ player.repeat() }}</button>
          <button type="button" (click)="bookmark()">{{ marked() ? 'Saved' : 'Bookmark' }}</button>
        </div>
        <div class="seek">
          <span class="muted small">{{ fmt(player.position()) }}</span>
          <input #range type="range" min="0" [max]="player.duration() || 0" step="1" [value]="player.position()" (input)="player.seek(+range.value)" aria-label="Seek" />
          <span class="muted small">{{ fmt(player.duration()) }}</span>
        </div>
        <div class="volume">
          <button type="button" (click)="player.toggleMute()" [attr.aria-pressed]="player.muted()" [attr.aria-label]="player.muted() ? 'Unmute' : 'Mute'">{{ player.muted() ? 'Unmute' : 'Mute' }}</button>
          <label>Volume {{ Math.round(player.volume() * 100) }}%
            <input #volume type="range" min="0" max="1" step="0.01" [value]="player.volume()" (input)="player.setVolume(+volume.value)" aria-label="Volume" />
          </label>
        </div>
        @if (player.error()) { <div class="error playback-error" role="alert">{{ player.error() }} <button type="button" (click)="player.retry()">Retry</button></div> }
      </footer>
    }
  `,
  styles: `
    .bar { position:fixed; z-index:10; right:0; bottom:0; left:var(--side-w); display:grid;
      grid-template-columns:minmax(10rem,1fr) minmax(15rem,1.6fr) minmax(8rem,.7fr); grid-template-areas:'now controls volume' 'now seek volume' 'error error error';
      align-items:center; gap:.5rem 1rem; padding:.7rem 1rem; background:var(--panel); border-top:.35rem solid var(--c1); border-radius:var(--r-big) 0 0 0; box-shadow:0 -.3rem 0 var(--bg); }
    .now { grid-area:now; display:flex; align-items:center; gap:.65rem; min-width:0; } .art { width:3rem; flex:none; }
    .meta { display:grid; min-width:0; } .meta > * { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
    .title { font-family:var(--font-display); font-size:1.15rem; text-transform:uppercase; color:var(--text); }
    .controls { grid-area:controls; display:flex; justify-content:center; flex-wrap:wrap; gap:.3rem; } .controls button { padding:.4rem .65rem; }
    button[aria-pressed=true] { box-shadow:inset 0 0 0 2px var(--accent); }
    .seek { grid-area:seek; display:flex; align-items:center; gap:.5rem; min-width:0; } .seek input { flex:1; min-width:0; }
    .volume { grid-area:volume; display:grid; gap:.3rem; min-width:0; } .volume label { font-size:.85rem; display:grid; } .volume input { width:100%; min-width:0; }
    .playback-error { grid-area:error; font-size:.9rem; }
    @media(max-width:56rem) { .bar { left:0; border-radius:0; grid-template-columns:1fr minmax(7rem,.5fr); grid-template-areas:'now volume' 'controls controls' 'seek seek' 'error error'; gap:.5rem; padding:.55rem .7rem; } }
  `,
})
export class PlayerBar {
  protected readonly player = inject(PlayerService); private readonly api = inject(SubsonicApi);
  protected readonly fmt = formatDuration; protected readonly marked = signal(false); protected readonly Math = Math;
  protected async bookmark(): Promise<void> {
    const song = this.player.current(); if (!song) return;
    try { await this.api.createBookmark(song.id, this.player.position() * 1000); this.marked.set(true); setTimeout(() => this.marked.set(false), 2000); } catch { }
  }
}
