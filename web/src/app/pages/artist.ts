import { Component, inject, input, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SubsonicApi } from '../core/subsonic';
import { Cover } from '../ui/cover';

@Component({
  selector: 'app-artist',
  imports: [RouterLink, Cover],
  template: `
    @if (artist.error()) {
      <p class="error" role="alert">{{ artist.error()?.message }}</p>
    }
    @if (artist.value(); as a) {
      <h1>{{ a.name }}</h1>
      <ul class="grid">
        @for (al of a.album; track al.id) {
          <li class="card">
            <app-cover [id]="al.coverArt" [label]="al.name" [size]="300" />
            <a [routerLink]="['/album', al.id]" class="title">{{ al.name }}</a>
            <span class="muted small">
              {{ al.year ?? '' }}{{ al.year ? ' · ' : '' }}{{ al.songCount }} tracks
            </span>
          </li>
        }
      </ul>
    } @else if (artist.isLoading()) {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .grid {
      list-style: none;
      padding: 0;
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(12rem, 1fr));
      gap: 1rem;
    }
    .card {
      display: grid;
      gap: 0.25rem;
    }
    .title {
      font-weight: 600;
    }
  `,
})
export class ArtistPage {
  private readonly api = inject(SubsonicApi);

  readonly id = input.required<string>();

  protected readonly artist = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.getArtist(params),
  });
}
