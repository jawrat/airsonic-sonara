import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth';
import { SubsonicApi, SubsonicError } from './subsonic';

describe('Data portability requests', () => {
  let api: SubsonicApi; let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear(); TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController); api = TestBed.inject(SubsonicApi);
    TestBed.inject(AuthService).session.set({ username: 'admin', token: 'token', salt: 'salt', isAdmin: true });
  });
  afterEach(() => http.verify());
  it('uses authenticated form POST for backup creation', async () => {
    const result = api.createBackup(); const request = http.expectOne('/rest/createBackup');
    expect(request.request.method).toBe('POST'); expect(new URLSearchParams(request.request.body).get('t')).toBe('token');
    request.flush({ 'subsonic-response': { status: 'ok', queued: true } }); await result;
  });
  it('posts playlist contents as text and sends the selected folder', async () => {
    const text = '#EXTM3U\nMusic/Artist/one.mp3\n';
    const result = api.importPlaylistFile(text, 'm3u8', 'Imported', true, 7); const request = http.expectOne('/rest/importPlaylistFile');
    const form = new URLSearchParams(request.request.body); expect(form.get('content')).toBe(text); expect(form.get('musicFolderId')).toBe('7'); expect(form.get('dryRun')).toBe('true');
    request.flush({ 'subsonic-response': { status: 'ok', playlistImport: { dryRun: true, matched: 1, unmatched: 0, totalEntries: 1, entry: [] } } });
    expect((await result).matched).toBe(1);
  });
  it('does not save a JSON authorization error as a backup ZIP', async () => {
    const result = api.downloadBackup('backup.zip'); const assertion = expect(result).rejects.toBeInstanceOf(SubsonicError);
    const request = http.expectOne(r => r.url === '/rest/downloadBackup');
    const blob = new Blob(['{}'], { type: 'application/json' });
    Object.defineProperty(blob, 'text', { value: async () => JSON.stringify({ 'subsonic-response': { status: 'failed', error: { code: 50, message: 'Not authorized' } } }) });
    request.flush(blob, { headers: { 'Content-Type': 'application/json' } }); await assertion;
  });
});
