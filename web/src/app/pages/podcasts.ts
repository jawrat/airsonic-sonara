import { Component, effect, inject, resource, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth';
import { PodcastSearchResult } from '../core/models';
import { PodcastImport, PodcastRetentionPreview, SubsonicApi } from '../core/subsonic';
import { Cover } from '../ui/cover';

@Component({
  selector: 'app-podcasts',
  imports: [RouterLink, Cover],
  template: `
    <h1>Podcasts</h1>
    <div class="toolbar"><button type="button" (click)="exportOpml()" [disabled]="busy()">Export subscriptions (OPML)</button></div>
    @if (message()) { <p [class.error]="isError()" role="status">{{ message() }}</p> }
    @if (podcasts.error()) {
      <p class="error" role="alert">{{ podcasts.error()?.message }}</p>
    }

    @if (podcasts.value(); as list) {
      @if (!list.length) {
        <p class="muted">
          No podcasts yet.
          @if (admin()) {
            Search below or paste a feed address to subscribe.
          } @else {
            An administrator can add some.
          }
        </p>
      }
      <ul class="grid">
        @for (p of list; track p.id) {
          <li class="card">
            <app-cover [id]="p.coverArt" [label]="p.title" [size]="300" />
            <a [routerLink]="['/podcast', p.id]" class="title">{{ p.title }}</a>
            @if (p.status === 'new') { <span class="muted small">Subscription queued · checking feed…</span> }
            @if (p.status === 'error') {
              <span class="error small">{{ p.errorMessage }}</span>
            }
          </li>
        }
      </ul>
    } @else if (podcasts.isLoading()) {
      <p class="muted">Loading…</p>
    }

    @if (canManage()) {
      <h2>Import subscriptions</h2>
      <p class="muted">Choose an OPML file (up to 1 MB and 200 feeds). Preview before adding subscriptions. Existing subscriptions are preserved.</p>
      <input type="file" accept=".opml,.xml,text/xml,application/xml" aria-label="OPML subscription file" [disabled]="busy()" (change)="chooseOpml($event)" />
      @if (importPreview(); as preview) {
        <p>{{ preview.ready }} ready · {{ preview.queued }} queued · {{ preview.existing }} existing · {{ preview.duplicate }} duplicates · {{ preview.invalid }} invalid</p>
        @if (preview.dryRun && preview.ready > 0) {
          <div class="toolbar">
            <button type="button" (click)="selectAllImports()" [disabled]="busy()">Select all new podcasts</button>
            <button type="button" (click)="selectedImports.set([])" [disabled]="busy()">Clear selection</button>
            <button type="button" (click)="applyImport()" [disabled]="busy() || selectedImports().length === 0">Subscribe to {{ selectedImports().length }} selected</button>
          </div>
        }
        <ul class="results import-preview">
          @for (item of preview.item; track $index) {
            <li class="panel">
              @if (preview.dryRun && item.status === 'ready') {
                <label><input type="checkbox" [checked]="selectedImports().includes(item.url)" [disabled]="busy()" (change)="toggleImport(item.url)" /> <strong>{{ item.title }}</strong></label>
              } @else { <strong>{{ item.title }}</strong> }
              <span class="muted small"> · {{ item.status === 'notSelected' ? 'not selected' : item.status }}</span>
              @if (item.message) { <p class="error small">{{ item.message }}</p> }
            </li>
          }
        </ul>
      }
      <h2>Add a podcast</h2>
      <form class="toolbar" (submit)="addUrl($event, feed)">
        <input #feed type="url" placeholder="https://example.com/feed.xml" aria-label="Feed address" size="40" />
        <button type="submit" [disabled]="busy()">Subscribe</button>
      </form>

      <h2>Find podcasts</h2>
      <form class="toolbar" (submit)="search($event, term)">
        <input #term type="search" placeholder="Search podcastindex.org" aria-label="Search podcasts" size="40" />
        <button type="submit" [disabled]="busy()">Search</button>
      </form>

      @if (results().length) {
        <ul class="results">
          @for (r of results(); track r.url) {
            <li class="panel result">
              <div class="info">
                <strong>{{ r.title }}</strong>
                @if (r.author) {
                  <span class="muted small">{{ r.author }}</span>
                }
                @if (r.description) {
                  <span class="muted small desc">{{ r.description }}</span>
                }
              </div>
              <button type="button" [disabled]="busy() || subscribed(r.url)" (click)="subscribe(r)">
                {{ subscribed(r.url) ? 'Subscribed' : 'Subscribe' }}
              </button>
            </li>
          }
        </ul>
      }
    }
    @if (admin()) {
      <section class="panel storage">
        <h2>Downloaded episode storage</h2>
        <p class="muted">Configure retention in Settings and restart to apply it. Cleanup protects pinned episodes, bookmarks, saved play queues, and downloads less than 24 hours old. Removing a file preserves the episode so it can be downloaded again.</p>
        <button type="button" (click)="previewCleanup()" [disabled]="busy()">Preview cleanup</button>
        @if (retention(); as preview) {
          @if (!preview.enabled) { <p>Automatic cleanup is disabled.</p> }
          @else {
            <p>{{ preview.count }} files eligible · {{ megabytes(preview.bytes) }} MB · {{ preview.protected }} protected</p>
            <p class="muted small">Running policy: older than {{ preview.days || 'unlimited' }} days; latest {{ preview.keepLatest || 'unlimited' }} per podcast. Preview shows up to 200 candidates.</p>
            @if (preview.count > 0) { <button type="button" (click)="applyCleanup()" [disabled]="busy()">Remove eligible downloads</button> }
            <ul class="results import-preview">
              @for (e of preview.episode; track e.id) { <li>{{ e.channel }} · {{ e.title }} <span class="muted small">({{ e.reason }})</span></li> }
            </ul>
          }
        }
      </section>
    }
  `,
  styles: `
    .storage { margin-top: 1.5rem; }
    .import-preview { max-height: 22rem; overflow-y: auto; margin: 1rem 0; }
    input[type='file'], .toolbar input { max-width: 100%; min-width: 0; }
    .import-preview { overflow-wrap: anywhere; }
    .results {
      list-style: none;
      padding: 0;
      margin: 0;
      display: grid;
      gap: 0.5rem;
    }
    .result {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.9rem;
      align-items: center;
      padding: 0.6rem 0.9rem;
    }
    .info {
      display: grid;
      min-width: 0;
    }
    .desc {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
  `,
})
export class PodcastsPage {
  private readonly api = inject(SubsonicApi);
  private readonly auth = inject(AuthService);

  protected readonly admin = () => this.auth.session()?.isAdmin === true;
  private readonly permissions = resource({ loader: () => this.api.call<{ user: { podcastRole: boolean } }>('getUser') });
  protected readonly canManage = () => this.admin() || this.permissions.value()?.user.podcastRole === true;
  protected readonly importPreview = signal<PodcastImport | null>(null);
  protected readonly retention = signal<PodcastRetentionPreview | null>(null);
  private opmlText = '';
  protected readonly selectedImports = signal<string[]>([]);
  protected selectAllImports(): void { this.selectedImports.set(this.importPreview()?.item.filter(i => i.status === 'ready').map(i => i.url) ?? []); }
  protected toggleImport(url: string): void { this.selectedImports.update(urls => urls.includes(url) ? urls.filter(u => u !== url) : [...urls, url]); }
  protected megabytes(bytes: number): string { return (bytes / 1024 / 1024).toFixed(1); }

  protected async exportOpml(): Promise<void> {
    await this.run(async () => {
      const blob = await this.api.exportPodcastOpml(); const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = 'sonara-podcasts.opml'; anchor.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    }, 'Subscriptions exported.');
  }
  protected async chooseOpml(event: Event): Promise<void> {
    this.opmlText = ''; this.importPreview.set(null); this.selectedImports.set([]);
    const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return;
    await this.run(async () => {
      if (file.size > 1024 * 1024) throw new Error('Choose an OPML file no larger than 1 MB.');
      this.opmlText = await file.text();
      this.importPreview.set(await this.api.importPodcastOpml(this.opmlText, true));
    }, 'Import preview ready.');
  }
  protected async applyImport(): Promise<void> {
    if (!this.opmlText || !this.importPreview()?.dryRun || !this.selectedImports().length) return;
    await this.run(async () => { this.importPreview.set(await this.api.importPodcastOpml(this.opmlText, false, this.selectedImports())); }, 'Subscriptions queued. Feed checks and downloads run in the background.');
  }
  protected async previewCleanup(): Promise<void> {
    await this.run(async () => { this.retention.set(await this.api.retentionPreview()); }, 'Cleanup preview ready.');
  }
  protected async applyCleanup(): Promise<void> {
    if (!this.retention()?.count || !window.confirm('Remove the currently eligible downloaded episode files? Episode records and subscriptions will be kept.')) return;
    await this.run(async () => {
      const result = await this.api.runRetention();
      this.retention.set(await this.api.retentionPreview());
      this.message.set(`Removed ${result.removed} downloads; ${result.failed} failed.`);
    }, '');
  }
  protected readonly podcasts = resource({ loader: () => this.api.getPodcasts() });
  protected readonly results = signal<PodcastSearchResult[]>([]);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly isError = signal(false);
  private readonly justSubscribed = signal<string[]>([]);

  constructor() {
    effect((onCleanup) => {
      if (!this.podcasts.value()?.some(p => p.status === 'new')) return;
      const timer = setInterval(() => this.podcasts.reload(), 5000);
      onCleanup(() => clearInterval(timer));
    });
  }

  protected subscribed(url: string): boolean {
    const known = this.podcasts.value()?.some((p) => p.url === url) ?? false;
    return known || this.justSubscribed().includes(url);
  }

  protected async addUrl(event: Event, input: HTMLInputElement): Promise<void> {
    event.preventDefault();
    const url = input.value.trim();
    if (!url) return;
    if (await this.run(() => this.api.createPodcast(url), 'Subscribed.')) input.value = '';
  }

  protected async search(event: Event, input: HTMLInputElement): Promise<void> {
    event.preventDefault();
    const term = input.value.trim();
    if (!term) return;
    this.results.set([]);
    await this.run(async () => {
      const found = await this.api.searchPodcasts(term);
      this.results.set(found);
      if (!found.length) throw new Error('Nothing found.');
    }, '');
  }

  protected async subscribe(result: PodcastSearchResult): Promise<void> {
    if (await this.run(() => this.api.createPodcast(result.url), `Subscribed to ${result.title}.`)) {
      this.justSubscribed.update((l) => [...l, result.url]);
    }
  }

  private async run(action: () => Promise<void>, success: string): Promise<boolean> {
    this.busy.set(true);
    this.message.set('');
    this.isError.set(false);
    try {
      await action();
      if (success) this.message.set(success);
      this.podcasts.reload();
      return true;
    } catch (e) {
      this.message.set(e instanceof Error ? e.message : 'Failed');
      this.isError.set(true);
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
