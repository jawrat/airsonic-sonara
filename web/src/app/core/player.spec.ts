import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth';
import { Song } from './models';
import { PlayerService } from './player';
import { SubsonicApi } from './subsonic';

class FakeAudio extends EventTarget {
  static last: FakeAudio;
  paused = true; currentTime = 0; duration = 100; volume = 1; muted = false; preload = ''; src = '';
  constructor() { super(); FakeAudio.last = this; }
  play = vi.fn(async () => { this.paused = false; this.dispatchEvent(new Event('play')); });
  pause = vi.fn(() => { if (!this.paused) { this.paused = true; this.dispatchEvent(new Event('pause')); } });
  load = vi.fn(() => { this.currentTime = 0; });
  removeAttribute(_name: string) { this.src = ''; }
}
const song = (id: string, duration = 100): Song => ({ id, title: 'Track ' + id, albumId: '1', artistId: '1', duration, suffix: 'mp3' });

describe('Player and play queue', () => {
  let player: PlayerService;
  let api: { streamUrl: ReturnType<typeof vi.fn>; savePlayQueue: ReturnType<typeof vi.fn>; getPlayQueue: ReturnType<typeof vi.fn>; reportPlayback: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    vi.useFakeTimers(); vi.setSystemTime(new Date('2026-11-16T12:00:00Z')); localStorage.clear();
    vi.stubGlobal('Audio', FakeAudio);
    api = { streamUrl: vi.fn(id => '/stream/' + id), savePlayQueue: vi.fn(async () => undefined), getPlayQueue: vi.fn(async () => null), reportPlayback: vi.fn(async () => undefined) };
    TestBed.configureTestingModule({ providers: [{ provide: SubsonicApi, useValue: api }, { provide: AuthService, useValue: { session: () => ({ token: 'test-token' }) } }] });
    player = TestBed.inject(PlayerService);
  });
  afterEach(() => { player.stop(); vi.clearAllTimers(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
  it('keeps the exact duplicate occurrence current when moving and removing other rows', () => {
    player.play([song('1'), song('1'), song('2')], 1);
    const key = player.current()!.queueKey; const audio = FakeAudio.last; audio.currentTime = 42;
    player.move(2, 0); expect(player.current()!.queueKey).toBe(key); expect(player.index()).toBe(2); expect(audio.currentTime).toBe(42);
    player.remove(0); expect(player.current()!.queueKey).toBe(key); expect(player.index()).toBe(1); expect(audio.play).toHaveBeenCalledTimes(1);
    player.remove(1); expect(player.current()!.queueKey).not.toBe(key); expect(player.index()).toBe(0); expect(audio.currentTime).toBe(0);
  });
  it('shuffles only upcoming rows and can restore their original order', () => {
    vi.spyOn(Math, 'random').mockReturnValue(0);
    player.play([song('1'), song('2'), song('3'), song('4')], 1);
    player.toggleShuffle(); expect(player.queue().map(s => s.id)).toEqual(['1', '2', '4', '3']); expect(player.current()!.id).toBe('2');
    player.toggleShuffle(); expect(player.queue().map(s => s.id)).toEqual(['1', '2', '3', '4']);
  });
  it('repeat-one affects natural ending while Next still advances, and repeat-all wraps', () => {
    player.play([song('1'), song('2')]); player.cycleRepeat(); player.cycleRepeat();
    FakeAudio.last.dispatchEvent(new Event('ended')); expect(player.index()).toBe(0); expect(FakeAudio.last.play).toHaveBeenCalledTimes(2);
    player.next(); expect(player.index()).toBe(1);
    player.cycleRepeat(); FakeAudio.last.dispatchEvent(new Event('ended')); expect(player.playing()).toBe(false); expect(player.index()).toBe(1);
    player.cycleRepeat(); player.next(); expect(player.index()).toBe(0);
  });
  it('saves during continuous playback rather than postponing the timer forever', async () => {
    player.play([song('1', 1000)]);
    for (let i = 0; i < 25; i++) { await vi.advanceTimersByTimeAsync(100); FakeAudio.last.currentTime += .1; FakeAudio.last.dispatchEvent(new Event('timeupdate')); }
    expect(api.savePlayQueue).toHaveBeenCalled(); expect(api.savePlayQueue.mock.calls[0][3]).toBe(0);
  });
  it('clears the server queue explicitly but sign-out stop preserves the saved queue', async () => {
    player.play([song('1')]); await vi.advanceTimersByTimeAsync(1600); api.savePlayQueue.mockClear();
    player.stop(); await vi.advanceTimersByTimeAsync(2000); expect(api.savePlayQueue).not.toHaveBeenCalled();
    player.play([song('2')]); player.clearQueue(); await vi.advanceTimersByTimeAsync(1);
    expect(api.savePlayQueue).toHaveBeenLastCalledWith([], undefined, 0, undefined); expect(player.queue()).toEqual([]);
  });
  it('reports local listening once after half the track, without crediting a seek', async () => {
    player.play([song('1', 100)]); expect(api.reportPlayback).toHaveBeenCalledWith('1', false);
    FakeAudio.last.currentTime = 90; FakeAudio.last.dispatchEvent(new Event('seeking')); FakeAudio.last.dispatchEvent(new Event('timeupdate'));
    expect(api.reportPlayback.mock.calls.filter(c => c[1])).toHaveLength(0);
    for (let i = 0; i < 5; i++) { await vi.advanceTimersByTimeAsync(1000); FakeAudio.last.currentTime++; FakeAudio.last.dispatchEvent(new Event('timeupdate')); }
    expect(api.reportPlayback.mock.calls.filter(c => c[1])).toHaveLength(0);
    player.play([song('2', 10)]);
    for (let i = 0; i < 7; i++) { await vi.advanceTimersByTimeAsync(1000); FakeAudio.last.currentTime++; FakeAudio.last.dispatchEvent(new Event('timeupdate')); }
    expect(api.reportPlayback.mock.calls.filter(c => c[1])).toHaveLength(1);
  });
  it('does not report podcasts to the music play-history API', () => { player.play([song('pe-1')]); expect(api.reportPlayback).not.toHaveBeenCalled(); });
  it('restores duplicate index/position paused, and ignores an obsolete restore after clearing', async () => {
    api.getPlayQueue.mockResolvedValueOnce({ songs: [song('1'), song('1')], currentId: '1', currentIndex: 1, positionMs: 12000 });
    await player.restore(); FakeAudio.last.dispatchEvent(new Event('loadedmetadata'));
    expect(player.index()).toBe(1); expect(FakeAudio.last.currentTime).toBe(12); expect(FakeAudio.last.play).not.toHaveBeenCalled();
    player.stop(); let finish!: (value: unknown) => void;
    api.getPlayQueue.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const restoring = player.restore(); player.clearQueue(); finish({ songs: [song('2')], positionMs: 0 }); await restoring;
    expect(player.queue()).toEqual([]);
  });
  it('preserves a pending resume position if metadata has not loaded before the save timer', async () => {
    player.prepare([song('1')], 0, 42);
    await vi.advanceTimersByTimeAsync(1600);
    expect(api.savePlayQueue).toHaveBeenLastCalledWith(['1'], '1', 42000, 0);
  });
  it('persists volume/mute/repeat/shuffle preferences and clamps volume', () => {
    player.setVolume(.25); player.toggleMute(); player.cycleRepeat(); player.toggleShuffle();
    const second = TestBed.runInInjectionContext(() => new PlayerService());
    expect(second.volume()).toBe(.25); expect(second.muted()).toBe(true); expect(second.repeat()).toBe('all'); expect(second.shuffle()).toBe(true);
    second.setVolume(99); expect(second.volume()).toBe(1); second.stop();
  });
  it('inserts Play next after the current row and preserves playback when appending', () => {
    player.play([song('1'), song('2')]); const key = player.current()!.queueKey;
    player.enqueue([song('3')], true); player.enqueue([song('4')]); expect(player.queue().map(s => s.id)).toEqual(['1', '3', '2', '4']);
    expect(player.current()!.queueKey).toBe(key); expect(FakeAudio.last.play).toHaveBeenCalledTimes(1);
  });
});
