import { Injectable, computed, inject, signal } from '@angular/core';
import { AuthService } from './auth';
import { Song } from './models';
import { SubsonicApi } from './subsonic';

export interface QueueItem extends Song { queueKey: number }
export type RepeatMode = 'off' | 'all' | 'one';
const MAX_QUEUE = 5000;
const PREFS = 'sonara.player';

@Injectable({ providedIn: 'root' })
export class PlayerService {
  private readonly api = inject(SubsonicApi);
  private readonly auth = inject(AuthService);
  private readonly audio = new Audio();
  readonly queue = signal<QueueItem[]>([]);
  readonly index = signal(-1);
  readonly playing = signal(false);
  readonly position = signal(0);
  readonly duration = signal(0);
  readonly volume = signal(1);
  readonly muted = signal(false);
  readonly shuffle = signal(false);
  readonly repeat = signal<RepeatMode>('off');
  readonly error = signal('');
  readonly syncError = signal('');
  readonly current = computed(() => this.queue()[this.index()] ?? null);
  readonly hasNext = computed(() => !!this.current() && (this.index() < this.queue().length - 1 || this.repeat() === 'all'));
  readonly hasPrev = computed(() => !!this.current() && (this.index() > 0 || this.position() > 3 || this.repeat() === 'all'));
  private sequence = 0;
  private baseOrder: number[] = [];
  private generation = 0;
  private loadVersion = 0;
  private pendingSeek: number | null = null;
  private saveTimer: ReturnType<typeof setTimeout> | undefined;
  private lastSaved = 0;
  private saves: Promise<void> = Promise.resolve();
  private listened = 0;
  private submitted = false;
  private previousTime = 0;
  private previousWall = 0;
  private lastAnnouncement = 0;

  constructor() {
    this.readPreferences();
    this.audio.preload = 'metadata';
    this.audio.volume = this.volume(); this.audio.muted = this.muted();
    this.audio.addEventListener('play', () => {
      this.playing.set(true); this.resetTick(); this.announce();
    });
    this.audio.addEventListener('pause', () => { this.playing.set(false); this.saveSoon(); });
    this.audio.addEventListener('timeupdate', () => {
      this.position.set(this.audio.currentTime);
      const wall = Date.now(); const delta = this.audio.currentTime - this.previousTime;
      const elapsed = Math.max(0, (wall - this.previousWall) / 1000);
      if (!this.audio.paused && delta > 0 && delta <= Math.max(5, elapsed + 1)) this.listened += Math.min(delta, elapsed + 1);
      this.previousTime = this.audio.currentTime; this.previousWall = wall;
      this.submitIfListened();
      if (!this.audio.paused && wall - this.lastSaved >= 15000) this.saveSoon();
      if (!this.audio.paused && wall - this.lastAnnouncement >= 60000) this.announce();
    });
    this.audio.addEventListener('seeking', () => this.resetTick());
    this.audio.addEventListener('durationchange', () => {
      this.duration.set(Number.isFinite(this.audio.duration) ? this.audio.duration : (this.current()?.duration ?? 0));
    });
    this.audio.addEventListener('loadedmetadata', () => {
      if (this.pendingSeek !== null) {
        this.audio.currentTime = this.pendingSeek; this.position.set(this.pendingSeek); this.pendingSeek = null;
      }
      this.resetTick();
    });
    this.audio.addEventListener('ended', () => {
      this.submitIfListened();
      if (this.repeat() === 'one') this.load(this.index(), true, 0);
      else if (this.hasNext()) this.next();
      else { this.playing.set(false); this.saveSoon(); }
    });
    this.audio.addEventListener('error', () => {
      if (!this.current()) return;
      this.playing.set(false); this.error.set('This track could not be played. Retry it or skip to another track.');
    });
  }

  play(songs: Song[], startIndex = 0, startAtSeconds = 0): void {
    this.generation++;
    this.replaceQueue(songs);
    if (!this.queue().length) { this.clearQueue(); return; }
    this.load(Math.max(0, Math.min(startIndex, this.queue().length - 1)), true, startAtSeconds);
    if (this.shuffle()) this.shuffleUpcoming();
  }
  prepare(songs: Song[], startIndex: number, startAtSeconds: number): void {
    this.generation++; this.replaceQueue(songs);
    if (!this.queue().length) { this.stop(); return; }
    if (this.queue().length) this.load(Math.max(0, Math.min(startIndex, this.queue().length - 1)), false, startAtSeconds);
  }
  async restore(): Promise<void> {
    if (this.current()) return;
    const generation = this.generation; const token = this.auth.session()?.token;
    try {
      const saved = await this.api.getPlayQueue();
      if (!saved || this.current() || generation !== this.generation || token !== this.auth.session()?.token) return;
      const i = saved.currentIndex !== undefined && saved.currentIndex >= 0 && saved.currentIndex < saved.songs.length
        ? saved.currentIndex : Math.max(0, saved.songs.findIndex(s => s.id === saved.currentId));
      this.prepare(saved.songs, i, saved.positionMs / 1000);
    } catch { /* Saved queues are best effort. */ }
  }
  toggle(): void {
    if (!this.current()) return;
    if (this.audio.paused) this.resume(); else this.audio.pause();
  }
  retry(): void { if (this.current()) this.load(this.index(), true, this.position()); }
  next(): void {
    if (!this.current()) return;
    if (this.index() < this.queue().length - 1) this.load(this.index() + 1, true, 0);
    else if (this.repeat() === 'all') {
      if (this.shuffle()) this.shuffleWholeQueue();
      this.load(0, true, 0);
    }
  }
  prev(): void {
    if (!this.current()) return;
    if (this.audio.currentTime > 3) this.seek(0);
    else if (this.index() > 0) this.load(this.index() - 1, true, 0);
    else if (this.repeat() === 'all') this.load(this.queue().length - 1, true, 0);
    else this.seek(0);
  }
  jump(index: number): void { if (this.queue()[index]) this.load(index, true, 0); }
  seek(seconds: number): void {
    if (!Number.isFinite(seconds) || !this.current()) return;
    this.audio.currentTime = Math.max(0, Math.min(seconds, this.duration() || seconds));
    this.position.set(this.audio.currentTime); this.resetTick(); this.saveSoon();
  }
  setVolume(value: number): void {
    if (!Number.isFinite(value)) return;
    this.volume.set(Math.max(0, Math.min(1, value))); this.audio.volume = this.volume(); this.persistPreferences();
  }
  toggleMute(): void { this.muted.update(v => !v); this.audio.muted = this.muted(); this.persistPreferences(); }
  cycleRepeat(): void {
    this.repeat.set(this.repeat() === 'off' ? 'all' : this.repeat() === 'all' ? 'one' : 'off'); this.persistPreferences();
  }
  toggleShuffle(): void {
    this.shuffle.update(v => !v);
    if (this.shuffle()) this.shuffleUpcoming();
    else {
      const prefix = this.queue().slice(0, this.index() + 1);
      const order = new Map(this.baseOrder.map((key, i) => [key, i]));
      this.queue.set([...prefix, ...this.queue().slice(this.index() + 1).sort((a, b) => (order.get(a.queueKey) ?? 0) - (order.get(b.queueKey) ?? 0))]);
    }
    this.persistPreferences(); this.saveSoon();
  }
  enqueue(songs: Song[], next = false): void {
    const added = this.items(songs.slice(0, Math.max(0, MAX_QUEUE - this.queue().length)));
    if (!added.length) return;
    if (!this.current()) { this.prepare(added, 0, 0); return; }
    const i = next ? this.index() + 1 : this.queue().length;
    const q = [...this.queue()]; q.splice(i, 0, ...added); this.queue.set(q);
    const keys = added.map(s => s.queueKey);
    if (next) this.baseOrder.splice(this.baseOrder.indexOf(this.current()!.queueKey) + 1, 0, ...keys);
    else this.baseOrder.push(...keys);
    this.saveSoon();
  }
  move(from: number, to: number): void {
    if (from === to || !this.queue()[from] || !this.queue()[to]) return;
    const current = this.current()?.queueKey; const q = [...this.queue()]; const [item] = q.splice(from, 1); q.splice(to, 0, item);
    this.queue.set(q); this.index.set(q.findIndex(s => s.queueKey === current));
    this.baseOrder = q.map(s => s.queueKey); this.shuffle.set(false); this.persistPreferences(); this.saveSoon();
  }
  remove(index: number): void {
    if (!this.queue()[index]) return;
    const wasPlaying = this.playing(); const current = this.current()?.queueKey;
    const q = [...this.queue()]; const [removed] = q.splice(index, 1); this.baseOrder = this.baseOrder.filter(key => key !== removed.queueKey);
    if (!q.length) { this.clearQueue(); return; }
    this.queue.set(q);
    const remaining = q.findIndex(s => s.queueKey === current);
    if (remaining >= 0) this.index.set(remaining);
    else this.load(Math.min(index, q.length - 1), wasPlaying, 0);
    this.saveSoon();
  }
  clearUpcoming(): void {
    this.queue.set(this.queue().slice(0, this.index() + 1)); this.baseOrder = this.queue().map(s => s.queueKey); this.saveSoon();
  }
  clearQueue(): void { this.stop(); this.queueSave([], undefined, 0, undefined); }
  stop(): void {
    this.generation++; this.loadVersion++; clearTimeout(this.saveTimer); this.saveTimer = undefined;
    // Reset state before pause dispatches, so sign-out never persists an empty queue over the saved one.
    this.queue.set([]); this.baseOrder = []; this.index.set(-1); this.playing.set(false);
    this.audio.pause(); this.audio.removeAttribute('src'); this.audio.load();
    this.position.set(0); this.duration.set(0); this.pendingSeek = null; this.error.set(''); this.syncError.set('');
  }
  private replaceQueue(songs: Song[]): void {
    this.queue.set(this.items(songs.slice(0, MAX_QUEUE))); this.baseOrder = this.queue().map(s => s.queueKey);
  }
  private items(songs: Song[]): QueueItem[] { return songs.filter(s => !!s.id).map(s => ({ ...s, queueKey: ++this.sequence })); }
  private load(i: number, autoplay: boolean, seconds: number): void {
    const song = this.queue()[i]; if (!song) return;
    this.loadVersion++; this.audio.pause(); this.index.set(i); this.playing.set(false); this.error.set('');
    this.position.set(seconds); this.duration.set(song.duration); this.pendingSeek = seconds > 0 ? seconds : null;
    this.listened = 0; this.submitted = false; this.lastAnnouncement = 0;
    this.audio.src = this.api.streamUrl(song.id); this.audio.load(); this.resetTick();
    if (autoplay) this.resume(); this.saveSoon();
  }
  private resume(): void {
    const key = this.current()?.queueKey; const version = this.loadVersion;
    void this.audio.play().catch(() => {
      if (key !== this.current()?.queueKey || version !== this.loadVersion) return;
      this.playing.set(false); this.error.set('Playback could not start. Press Play to retry.');
    });
  }
  private resetTick(): void { this.previousTime = this.audio.currentTime; this.previousWall = Date.now(); }
  private announce(): void {
    const song = this.current(); if (!song || !/^\d+$/.test(song.id)) return;
    this.lastAnnouncement = Date.now(); void this.api.reportPlayback(song.id, false).catch(() => undefined);
  }
  private submitIfListened(): void {
    const song = this.current(); const threshold = this.duration() > 0 ? Math.min(this.duration() / 2, 240) : 240;
    if (this.submitted || !song || !/^\d+$/.test(song.id) || this.listened < threshold) return;
    this.submitted = true; void this.api.reportPlayback(song.id, true).catch(() => undefined);
  }
  private shuffled(items: QueueItem[]): QueueItem[] {
    const q = [...items]; for (let i = q.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [q[i], q[j]] = [q[j], q[i]]; } return q;
  }
  private shuffleUpcoming(): void { this.queue.set([...this.queue().slice(0, this.index() + 1), ...this.shuffled(this.queue().slice(this.index() + 1))]); }
  private shuffleWholeQueue(): void { this.queue.set(this.shuffled(this.queue())); }
  private saveSoon(): void {
    if (this.saveTimer !== undefined || !this.current()) return;
    this.saveTimer = setTimeout(() => {
      this.saveTimer = undefined; this.lastSaved = Date.now();
      this.queueSave(this.queue().map(s => s.id), this.current()?.id, (this.pendingSeek ?? this.audio.currentTime) * 1000, this.index());
    }, 1500);
  }
  private queueSave(ids: string[], current: string | undefined, position: number, currentIndex: number | undefined): void {
    const token = this.auth.session()?.token; const generation = this.generation;
    this.saves = this.saves.catch(() => undefined).then(async () => {
      if (!token || token !== this.auth.session()?.token || generation !== this.generation) return;
      await this.api.savePlayQueue(ids, current, position, currentIndex);
      if (generation === this.generation) this.syncError.set('');
    }).catch(() => {
      if (token === this.auth.session()?.token && generation === this.generation) this.syncError.set('The queue could not be synced to the server. Further changes will retry.');
    });
  }
  private readPreferences(): void {
    try {
      const p = JSON.parse(localStorage.getItem(PREFS) ?? '{}');
      if (typeof p.volume === 'number' && Number.isFinite(p.volume)) this.volume.set(Math.max(0, Math.min(1, p.volume)));
      this.muted.set(p.muted === true); this.shuffle.set(p.shuffle === true);
      if (p.repeat === 'off' || p.repeat === 'all' || p.repeat === 'one') this.repeat.set(p.repeat);
    } catch { /* Browser storage may be unavailable. */ }
  }
  private persistPreferences(): void {
    try { localStorage.setItem(PREFS, JSON.stringify({ volume: this.volume(), muted: this.muted(), shuffle: this.shuffle(), repeat: this.repeat() })); } catch { }
  }
}
