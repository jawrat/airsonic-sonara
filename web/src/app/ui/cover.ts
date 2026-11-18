import { Component, computed, inject, input, signal } from '@angular/core';
import { SubsonicApi } from '../core/subsonic';

/** Square cover art. Falls back to a plain tile when there is no art or it fails to load. */
@Component({
  selector: 'app-cover',
  template: `
    @if (src(); as url) {
      <img [src]="url" alt="" loading="lazy" [hidden]="failedFor() === id()" (error)="failedFor.set(id())" />
    }
  `,
  styles: `
    :host {
      display: block;
      aspect-ratio: 1;
      background: var(--border);
      border-radius: 0.5rem;
      overflow: hidden;
    }
    img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
  `,
})
export class Cover {
  private readonly api = inject(SubsonicApi);

  readonly id = input<string | undefined>();
  /** Requested width in pixels; the server scales down and caches. Omit for the original. */
  readonly size = input<number | undefined>();
  protected readonly failedFor = signal<string | undefined>(undefined);
  protected readonly src = computed(() => this.api.coverArtUrl(this.id(), this.size()));
}
