import { DatePipe } from '@angular/common';
import { Component, effect, inject, input, resource, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth';
import { formatDuration } from '../core/format';
import { PodcastChannel, PodcastEpisode, episodeToSong } from '../core/models';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';
import { QueueActions } from '../ui/queue-actions';
import { Cover } from '../ui/cover';

@Component({
  selector: 'app-podcast',
  imports: [Cover, DatePipe, QueueActions],
  template: `
    @if (podcast.error()) {
      <p class="error" role="alert">{{ podcast.error()?.message }}</p>
    }
    @if (podcast.value(); as p) {
      <header class="head">
        <app-cover class="art" [id]="p.coverArt" [label]="p.title" [size]="400" />
        <div>
          <h1>{{ p.title }}</h1>
          @if (p.description) {
            <p class="muted desc">{{ p.description }}</p>
          }
          @if (p.status === 'error') {
            <p class="error" role="alert">{{ p.errorMessage }}</p>
          }
          <div class="toolbar">
            <button type="button" (click)="playLatest(p)" [disabled]="!downloaded(p).length">Play latest</button>
            <button type="button" (click)="queueDownloaded(p)" [disabled]="!downloaded(p).length">Queue downloaded episodes</button>
            @if (canManage()) {
              <button type="button" (click)="refresh(p)" [disabled]="busy()">Refresh</button>
              <button type="button" (click)="unsubscribe(p)" [disabled]="busy()">Unsubscribe</button>
            }
          </div>
        </div>
      </header>

      @if (message()) {
        <p [class.error]="isError()" role="status">{{ message() }}</p>
      }

      <ul class="tracks">
        @for (e of p.episode; track e.id) {
          <li class="episode" [class.active]="!!e.streamId && player.current()?.id === e.streamId">
            <div class="main">
              @if (e.status === 'completed') {
                <button type="button" class="link" (click)="play(p, e)">{{ e.title }}</button>
              } @else {
                <span>{{ e.title }}</span>
              }
              <span class="muted small">
                {{ e.publishDate | date: 'mediumDate' }}
                @if (e.pinned) { · pinned (protected from cleanup) }
                @if (e.duration) {
                  · {{ fmt(e.duration) }}
                }
                @switch (e.status) {
                  @case ('downloading') {
                    · downloading…
                  }
                  @case ('error') {
                    · <span class="error">{{ e.errorMessage || 'download failed' }}</span>
                  }
                  @case ('new') {
                    · not downloaded
                  }
                  @case ('deleted') {
                    · removed
                  }
                }
              </span>
            </div>
            <span class="time">
              @if (e.status === 'completed') {
                <button type="button" (click)="play(p, e)">Play</button>
                <app-queue-actions [song]="episodeSong(e, p)" />
              }
              @if (canManage()) {
                <button type="button" (click)="pin(e)" [disabled]="busy()">{{ e.pinned ? 'Unpin' : 'Pin' }}</button>
                @if (e.status === 'completed') {
                  <button type="button" (click)="removeDownload(e)">Remove file</button>
                } @else if (e.status !== 'downloading') {
                  <button type="button" (click)="download(e)">Download</button>
                }
              }
            </span>
          </li>
        } @empty {
          <li class="muted">No episodes found.</li>
        }
      </ul>
    } @else if (podcast.isLoading()) {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .head {
      display: grid;
      grid-template-columns: 9rem 1fr;
      gap: 1.25rem;
      align-items: start;
      margin-bottom: 1rem;
    }
    .desc {
      display: -webkit-box;
      -webkit-line-clamp: 4;
      -webkit-box-orient: vertical;
      overflow: hidden;
      margin: 0 0 0.75rem;
    }
    .episode {
      align-items: center;
    }
    .main {
      display: grid;
      min-width: 0;
    }
    .time {
      display: flex;
      gap: 0.4rem;
    }
    @media (max-width: 40rem) {
      .head {
        grid-template-columns: 6rem 1fr;
      }
    }
  `,
})
export class PodcastPage {
  private readonly api = inject(SubsonicApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly player = inject(PlayerService);

  readonly id = input.required<string>();
  protected readonly fmt = formatDuration;
  protected readonly episodeSong = episodeToSong;
  private readonly permissions = resource({ loader: () => this.api.call<{ user: { podcastRole: boolean } }>('getUser') });
  protected readonly canManage = () => this.auth.session()?.isAdmin === true || this.permissions.value()?.user.podcastRole === true;
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly isError = signal(false);

  protected readonly podcast = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.getPodcast(params),
  });

  constructor() {
    // While anything is downloading, look again every few seconds.
    effect((onCleanup) => {
      const downloading = this.podcast.value()?.episode.some((e) => e.status === 'downloading');
      if (!downloading) return;
      const timer = setInterval(() => this.podcast.reload(), 4000);
      onCleanup(() => clearInterval(timer));
    });
  }

  protected downloaded(p: PodcastChannel): PodcastEpisode[] {
    return (p.episode ?? []).filter((e) => e.status === 'completed');
  }

  /** Plays from this episode onward (the list is newest first, so "next" is the older one). */
  protected play(p: PodcastChannel, episode: PodcastEpisode): void {
    const queue = this.downloaded(p);
    this.player.play(
      queue.map((e) => episodeToSong(e, p)),
      Math.max(0, queue.findIndex((e) => e.id === episode.id)),
    );
  }

  protected queueDownloaded(p: PodcastChannel): void { this.player.enqueue(this.downloaded(p).map(e => episodeToSong(e, p))); }

  protected playLatest(p: PodcastChannel): void {
    const latest = this.downloaded(p)[0];
    if (latest) this.play(p, latest);
  }

  protected refresh(p: PodcastChannel): Promise<boolean> {
    return this.run(async () => {
      await this.api.refreshPodcasts(p.id);
      // The refresh runs in the background; check back shortly.
      setTimeout(() => this.podcast.reload(), 3000);
    }, 'Checking for new episodes…');
  }

  protected async unsubscribe(p: PodcastChannel): Promise<void> {
    if (!window.confirm(`Unsubscribe from "${p.title}" and delete its downloaded episodes?`)) return;
    if (await this.run(() => this.api.deletePodcast(p.id), '')) void this.router.navigateByUrl('/podcasts');
  }

  protected download(e: PodcastEpisode): Promise<boolean> {
    return this.run(async () => {
      await this.api.downloadEpisode(e.id);
      this.podcast.reload();
    }, '');
  }

  protected pin(e: PodcastEpisode): Promise<boolean> {
    return this.run(async () => { await this.api.setEpisodePinned(e.id, !e.pinned); this.podcast.reload(); }, e.pinned ? 'Episode unpinned.' : 'Episode protected from automatic cleanup.');
  }

  protected removeDownload(e: PodcastEpisode): Promise<boolean> {
    return this.run(async () => {
      await this.api.deleteEpisode(e.id);
      this.podcast.reload();
    }, '');
  }

  private async run(action: () => Promise<void>, success: string): Promise<boolean> {
    this.busy.set(true);
    this.message.set('');
    this.isError.set(false);
    try {
      await action();
      this.message.set(success);
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
