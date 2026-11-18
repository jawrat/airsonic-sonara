import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService, authParams, makeToken } from './auth';
import { formatDuration } from './format';
import { episodeToSong } from './models';
import { SubsonicApi, SubsonicError, toEnc } from './subsonic';

describe('makeToken', () => {
  it('matches the example in the Subsonic API docs', () => {
    expect(makeToken('sesame', 'c19b2d')).toBe('26719a1196d2a940705a59634eb18eab');
  });

  it('never includes the password in request params', () => {
    const params = authParams({ username: 'bob', token: 'tok', salt: 'salt', isAdmin: false });
    expect(Object.values(params)).not.toContain('hunter2');
    expect(params).toMatchObject({ u: 'bob', t: 'tok', s: 'salt', f: 'json' });
  });
});

describe('formatDuration', () => {
  it('formats minutes and hours', () => {
    expect(formatDuration(0)).toBe('0:00');
    expect(formatDuration(185)).toBe('3:05');
    expect(formatDuration(3725)).toBe('1:02:05');
  });

  it('is forgiving about bad input', () => {
    expect(formatDuration(undefined)).toBe('0:00');
    expect(formatDuration(NaN)).toBe('0:00');
    expect(formatDuration(-5)).toBe('0:00');
  });
});

describe('SubsonicApi', () => {
  let http: HttpTestingController;
  let api: SubsonicApi;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(SubsonicApi);
    TestBed.inject(AuthService).session.set({ username: 'bob', token: 'tok', salt: 'salt', isAdmin: false });
  });

  it('sends token auth on every call and unwraps the envelope', async () => {
    const promise = api.getAlbumList('newest', 5);
    const req = http.expectOne((r) => r.url === '/rest/getAlbumList2');
    expect(req.request.params.get('u')).toBe('bob');
    expect(req.request.params.get('t')).toBe('tok');
    expect(req.request.params.get('type')).toBe('newest');
    req.flush({
      'subsonic-response': {
        status: 'ok',
        albumList2: { album: [{ id: '1', name: 'A', songCount: 3, duration: 100 }] },
      },
    });
    expect((await promise).map((a) => a.id)).toEqual(['1']);
  });

  it('treats a missing album array as empty', async () => {
    const promise = api.getAlbumList('newest');
    http.expectOne((r) => r.url === '/rest/getAlbumList2').flush({
      'subsonic-response': { status: 'ok', albumList2: {} },
    });
    expect(await promise).toEqual([]);
  });

  it('throws SubsonicError when status is failed', async () => {
    const promise = api.getAlbum('9');
    http.expectOne((r) => r.url === '/rest/getAlbum').flush({
      'subsonic-response': { status: 'failed', error: { code: 70, message: 'not found' } },
    });
    await expect(promise).rejects.toBeInstanceOf(SubsonicError);
  });

  it('sends repeated parameters for playlist edits', async () => {
    const promise = api.updatePlaylist('3', { addSongIds: ['1', '2'], removeIndexes: [0], public: true });
    const req = http.expectOne((r) => r.url === '/rest/updatePlaylist');
    expect(req.request.params.getAll('songIdToAdd')).toEqual(['1', '2']);
    expect(req.request.params.getAll('songIndexToRemove')).toEqual(['0']);
    expect(req.request.params.get('playlistId')).toBe('3');
    expect(req.request.params.get('public')).toBe('true');
    req.flush({ 'subsonic-response': { status: 'ok' } });
    await promise;
  });

  it('stars songs with id and albums with albumId, and requests sized cover art', async () => {
    const p1 = api.setStar({ songId: '5' }, true);
    const r1 = http.expectOne((r) => r.url === '/rest/star');
    expect(r1.request.params.get('id')).toBe('5');
    r1.flush({ 'subsonic-response': { status: 'ok' } });
    await p1;

    const p2 = api.setStar({ albumId: '7' }, false);
    const r2 = http.expectOne((r) => r.url === '/rest/unstar');
    expect(r2.request.params.get('albumId')).toBe('7');
    r2.flush({ 'subsonic-response': { status: 'ok' } });
    await p2;

    expect(api.coverArtUrl('al-1', 300)).toContain('size=300');
    expect(api.coverArtUrl('al-1')).not.toContain('size=');
  });

  it('saves the play queue with repeated ids, the current song and a position in ms', async () => {
    const promise = api.savePlayQueue(['4', '5', '6'], '5', 12345.6);
    const req = http.expectOne((r) => r.url === '/rest/savePlayQueue');
    expect(req.request.method).toBe('POST');
    const form = new URLSearchParams(req.request.body);
    expect(form.getAll('id')).toEqual(['4', '5', '6']);
    expect(form.get('current')).toBe('5');
    expect(form.get('position')).toBe('12346');
    req.flush({ 'subsonic-response': { status: 'ok' } });
    await promise;
  });

  it('returns null when no play queue was saved', async () => {
    const promise = api.getPlayQueue();
    http.expectOne((r) => r.url === '/rest/getPlayQueue').flush({ 'subsonic-response': { status: 'ok' } });
    expect(await promise).toBeNull();
  });

  it('reads podcasts and turns downloaded episodes into playable songs', async () => {
    const promise = api.getPodcast('4');
    const req = http.expectOne((r) => r.url === '/rest/getPodcasts');
    expect(req.request.params.get('id')).toBe('4');
    req.flush({
      'subsonic-response': {
        status: 'ok',
        podcasts: {
          channel: [
            {
              id: '4',
              url: 'https://x/feed',
              title: 'Show',
              coverArt: 'pc-4',
              status: 'completed',
              episode: [{ id: '9', channelId: '4', title: 'Ep', status: 'completed', streamId: 'pe-9', duration: 60, size: 1 }],
            },
          ],
        },
      },
    });
    const channel = await promise;
    const song = episodeToSong(channel.episode[0], channel);
    expect(song.id).toBe('pe-9');
    expect(song.channelId).toBe('4');
    expect(song.coverArt).toBe('pc-4');
  });

  it('reports a missing podcast as an error', async () => {
    const promise = api.getPodcast('404');
    http.expectOne((r) => r.url === '/rest/getPodcasts').flush({ 'subsonic-response': { status: 'ok', podcasts: {} } });
    await expect(promise).rejects.toBeInstanceOf(SubsonicError);
  });

  it('hex-encodes passwords so they never travel in clear text', () => {
    expect(toEnc('sesame')).toBe('enc:736573616d65');
    expect(toEnc('é')).toBe('enc:c3a9');
  });

  it('builds cover art URLs with a token, and returns null without an id', () => {
    expect(api.coverArtUrl(undefined)).toBeNull();
    const url = api.coverArtUrl('al-7')!;
    expect(url.startsWith('/rest/getCoverArt?')).toBe(true);
    expect(url).toContain('id=al-7');
    expect(url).toContain('t=tok');
    expect(url).not.toContain('p=');
  });

  it('builds stream URLs with a token, not a password', () => {
    const url = api.streamUrl('42');
    expect(url.startsWith('/rest/stream?')).toBe(true);
    expect(url).toContain('id=42');
    expect(url).toContain('t=tok');
    expect(url).not.toContain('p=');
  });
});
