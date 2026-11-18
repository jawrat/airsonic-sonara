import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SubsonicApi } from '../core/subsonic';
import { BackupsPage } from './backups';
import { PlaylistsPage } from './playlists';

describe('Data portability screens', () => {
  it('queues a manual backup and refreshes its status', async () => {
    const createBackup = vi.fn(async () => undefined);
    const getBackups = vi.fn(async () => ({ busy: false, phase: 'idle', backup: [] }));
    TestBed.configureTestingModule({ imports: [BackupsPage], providers: [provideRouter([]), { provide: SubsonicApi, useValue: { createBackup, getBackups } }] });
    const fixture = TestBed.createComponent(BackupsPage); fixture.detectChanges();
    await fixture.whenStable(); fixture.detectChanges();
    const create = [...fixture.nativeElement.querySelectorAll('button')].find((b: Element) => b.textContent?.trim() === 'Create backup') as HTMLButtonElement;
    create.click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(createBackup).toHaveBeenCalledTimes(1); expect(fixture.nativeElement.textContent).toContain('Backup queued'); });
    expect(getBackups.mock.calls.length).toBeGreaterThan(1);
  });
  it('previews a playlist before creating a private imported copy', async () => {
    const importPlaylistFile = vi.fn(async (_content: string, _format: string, _name: string, dryRun: boolean) => ({ dryRun, matched: 3, unmatched: 1, totalEntries: 4, entry: [] }));
    TestBed.configureTestingModule({ imports: [PlaylistsPage], providers: [provideRouter([]), { provide: SubsonicApi, useValue: { getFolders: async () => [], getPlaylists: async () => [], importPlaylistFile } }] });
    const fixture = TestBed.createComponent(PlaylistsPage); fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input[type=file]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [{ name: 'Favorites.m3u8', size: 5, text: async () => '#EXTM3U\ntrack.mp3' }] });
    input.dispatchEvent(new Event('change'));
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('3 matched'); });
    expect(importPlaylistFile).toHaveBeenLastCalledWith('#EXTM3U\ntrack.mp3', 'm3u8', 'Favorites', true, undefined);
    const apply = [...fixture.nativeElement.querySelectorAll('button')].find((b: Element) => b.textContent?.trim() === 'Create private playlist') as HTMLButtonElement;
    apply.click();
    await vi.waitFor(() => expect(importPlaylistFile).toHaveBeenLastCalledWith('#EXTM3U\ntrack.mp3', 'm3u8', 'Favorites', false, undefined));
  });
});
