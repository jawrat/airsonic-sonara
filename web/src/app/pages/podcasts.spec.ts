import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../core/auth';
import { SubsonicApi } from '../core/subsonic';
import { PodcastsPage } from './podcasts';

describe('OPML subscription choices', () => {
  it('starts unchecked and sends only selected feed URLs', async () => {
    const importPodcastOpml = vi.fn(async (_xml: string, dryRun: boolean) => ({ dryRun, ready: dryRun ? 2 : 0, queued: dryRun ? 0 : 1, existing: 0, duplicate: 0, invalid: 0,
      item: [{ title: 'Podcast A', url: 'https://example.com/a', status: dryRun ? 'ready' : 'notSelected' }, { title: 'Podcast B', url: 'https://example.com/b', status: dryRun ? 'ready' : 'queued' }] }));
    TestBed.configureTestingModule({ imports: [PodcastsPage], providers: [provideRouter([]),
      { provide: AuthService, useValue: { session: () => ({ isAdmin: true }) } },
      { provide: SubsonicApi, useValue: { getPodcasts: async () => [], call: async () => ({ user: { podcastRole: true } }), importPodcastOpml } },
    ] });
    const fixture = TestBed.createComponent(PodcastsPage); fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input[type=file]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [{ size: 7, text: async () => '<opml/>' }] });
    input.dispatchEvent(new Event('change'));
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelectorAll('input[type=checkbox]').length).toBe(2); });
    const boxes = fixture.nativeElement.querySelectorAll('input[type=checkbox]') as NodeListOf<HTMLInputElement>;
    expect(boxes[0].checked).toBe(false); expect(boxes[1].checked).toBe(false);
    boxes[1].click(); fixture.detectChanges();
    const button = [...fixture.nativeElement.querySelectorAll('button')].find((b: Element) => b.textContent?.includes('Subscribe to 1 selected')) as HTMLButtonElement;
    expect(button.disabled).toBe(false); button.click();
    await vi.waitFor(() => expect(importPodcastOpml).toHaveBeenLastCalledWith('<opml/>', false, ['https://example.com/b']));
  });
});
