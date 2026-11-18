import { Component, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatDuration } from '../core/format';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';
import { QueueActions } from '../ui/queue-actions';
import { Cover } from '../ui/cover';
import { StarButton } from '../ui/star-button';

@Component({
  selector: 'app-starred',
  imports: [RouterLink, Cover, StarButton, QueueActions],
  template: `
    <h1>Starred</h1>
    @if (starred.error()) {
      <p class="error" role="alert">{{ starred.error()?.message }}</p>
    }
    @if (starred.value(); as s) {
      @if (!s.songs.length && !s.albums.length) {
        <p class="muted">Nothing starred yet. Use the ☆ next to a song or album.</p>
      }

      @if (s.albums.length) {
        <h2>Albums</h2>
        <ul class="grid">
          @for (a of s.albums; track a.id) {
            <li class="card">
              <app-cover [id]="a.coverArt" [size]="300" />
              <a [routerLink]="['/album', a.id]" class="title">{{ a.name }}</a>
              <span class="muted">{{ a.artist }}</span>
            </li>
          }
        </ul>
      }

      @if (s.songs.length) {
        <h2>Songs</h2>
        <button type="button" (click)="player.play(s.songs, 0)">Play all</button>
        <button type="button" (click)="player.enqueue(s.songs)">Add favorites to queue</button>
        <ol class="tracks">
          @for (song of s.songs; track song.id; let i = $index) {
            <li [class.active]="player.current()?.id === song.id">
              <button type="button" class="link" (click)="player.play(s.songs, i)">
                {{ song.title }}
              </button>
              <a class="muted" [routerLink]="['/artist', song.artistId]">{{ song.artist }}</a>
              <span class="muted time">{{ fmt(song.duration) }}</span>
              <app-queue-actions [song]="song" />
              <app-star-button [target]="{ songId: song.id }" [starred]="song.starred" />
            </li>
          }
        </ol>
      }
    } @else if (starred.isLoading()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class StarredPage {
  protected readonly player = inject(PlayerService);
  private readonly api = inject(SubsonicApi);
  protected readonly fmt = formatDuration;
  protected readonly starred = resource({ loader: () => this.api.getStarred() });
}
