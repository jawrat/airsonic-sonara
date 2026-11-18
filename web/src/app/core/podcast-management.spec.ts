import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth';
import { SubsonicApi, SubsonicError } from './subsonic';

describe('Podcast management requests', () => {
  let http: HttpTestingController;
  let api: SubsonicApi;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController); api = TestBed.inject(SubsonicApi);
    TestBed.inject(AuthService).session.set({ username: 'admin', token: 'token', salt: 'salt', isAdmin: true });
  });
  afterEach(() => http.verify());
  it('posts OPML in the body and distinguishes preview from application', async () => {
    const xml = '<opml><body><outline text="Björk &amp; friends" xmlUrl="https://example.com/feed?a=1&amp;b=2"/></body></opml>';
    const result = api.importPodcastOpml(xml, true);
    const req = http.expectOne('/rest/importPodcastOpml');
    expect(req.request.method).toBe('POST'); expect(req.request.params.keys()).toHaveLength(0);
    const form = new URLSearchParams(req.request.body);
    expect(form.get('opml')).toBe(xml); expect(form.get('dryRun')).toBe('true'); expect(form.get('t')).toBe('token');
    req.flush({ 'subsonic-response': { status: 'ok', podcastImport: { dryRun: true, ready: 1, queued: 0, existing: 0, duplicate: 0, invalid: 0, item: [] } } });
    expect((await result).ready).toBe(1);
  });
  it('uses form POST for cleanup and propagates server refusal', async () => {
    const result = api.runRetention();
    const failure = expect(result).rejects.toBeInstanceOf(SubsonicError);
    const req = http.expectOne('/rest/runPodcastRetention'); expect(req.request.method).toBe('POST');
    req.flush({ 'subsonic-response': { status: 'failed', error: { code: 50, message: 'Not authorized' } } });
    await failure;
  });
  it('sends episode pin state explicitly', async () => {
    const result = api.setEpisodePinned('8', false);
    const req = http.expectOne('/rest/setPodcastEpisodePinned');
    const form = new URLSearchParams(req.request.body); expect(form.get('id')).toBe('8'); expect(form.get('pinned')).toBe('false');
    req.flush({ 'subsonic-response': { status: 'ok' } }); await result;
  });
});
