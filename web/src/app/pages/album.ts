import { Component, inject, input, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatDuration } from '../core/format';
import { QueueActions } from '../ui/queue-actions';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';
import { StarButton } from '../ui/star-button';
import { AddToPlaylist } from '../ui/add-to-playlist';
import { Cover } from '../ui/cover';

@Component({
  selector: 'app-album',
  imports: [RouterLink, QueueActions, Cover, AddToPlaylist, StarButton],
  template: `
    @if (album.error()) {
      <p class="error" role="alert">{{ album.error()?.message }}</p>
    }
    @if (album.value(); as a) {
      <header class="head">
        <app-cover class="art" [id]="a.coverArt" [size]="400" />
        <div class="info">
          <h1>{{ a.name }}</h1>
        <p class="muted">
          @if (a.artistId) {
            <a [routerLink]="['/artist', a.artistId]">{{ a.artist }}</a>
          }
          {{ a.year ? ' · ' + a.year : '' }} · {{ a.songCount }} tracks · {{ fmt(a.duration) }}
        </p>
        <button type="button" (click)="player.play(a.song, 0)" [disabled]="!a.song.length">
          Play all
        </button>
        <button type="button" (click)="player.enqueue(a.song)" [disabled]="!a.song.length">Add album to queue</button>
        <app-star-button [target]="{ albumId: a.id }" [starred]="a.starred" />
        </div>
      </header>

      <ol class="tracks">
        @for (s of a.song; track s.id; let i = $index) {
          <li [class.active]="player.current()?.id === s.id">
            <span class="muted num">{{ s.track ?? i + 1 }}</span>
            <button type="button" class="link" (click)="player.play(a.song, i)">
              {{ s.title }}
            </button>
            @if (s.artist && s.artist !== a.artist) {
              <span class="muted">{{ s.artist }}</span>
            }
            <span class="muted time">{{ fmt(s.duration) }}</span>
            <app-queue-actions [song]="s" />
            <app-star-button [target]="{ songId: s.id }" [starred]="s.starred" />
            <app-add-to-playlist [songId]="s.id" />
          </li>
        }
      </ol>
    } @else if (album.isLoading()) {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .head h1 {
      margin: 0 0 0.25rem;
    }
    .head {
      display: flex;
      align-items: flex-end;
      gap: 1.25rem;
      margin-bottom: 1.5rem;
    }
    .art {
      width: 10rem;
      flex: none;
    }
  `,
})
export class AlbumPage {
  protected readonly player = inject(PlayerService);
  private readonly api = inject(SubsonicApi);

  /** Bound from the route parameter via withComponentInputBinding(). */
  readonly id = input.required<string>();
  protected readonly fmt = formatDuration;

  protected readonly album = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.getAlbum(params),
  });
}
