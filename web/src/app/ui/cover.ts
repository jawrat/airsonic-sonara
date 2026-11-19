import { Component, computed, inject, input, signal } from '@angular/core';
import { SubsonicApi } from '../core/subsonic';

/** Decorative artwork, with a stable colored fallback for missing or broken images. */
@Component({
  selector: 'app-cover',
  host: { 'aria-hidden': 'true' },
  template: `
    <span class="placeholder" [style.background]="color()" [hidden]="!!src() && loadedFor() === src() && failedFor() !== src()">{{ initials() }}</span>
    @if (src(); as url) {
      <img [src]="url" alt="" loading="lazy" [hidden]="failedFor() === url" (load)="loadedFor.set(url)" (error)="failedFor.set(url)" />
    }
  `,
  styles: `
    :host { display:block; position:relative; aspect-ratio:1; border-radius:.5rem; overflow:hidden; }
    .placeholder { position:absolute; inset:0; display:flex; align-items:center; justify-content:center;
      color:#fff; font-family:system-ui,sans-serif; font-size:clamp(1rem,4vw,3rem); font-weight:700;
      letter-spacing:.06em; text-shadow:0 1px 2px #0006; }
    img { position:absolute; inset:0; display:block; width:100%; height:100%; object-fit:cover; }
    img[hidden],.placeholder[hidden] { display:none; }
  `,
})
export class Cover {
  private readonly api = inject(SubsonicApi);
  readonly id = input<string | undefined>();
  readonly label = input('');
  readonly size = input<number | undefined>();
  readonly folderId = input<number | undefined>();
  protected readonly loadedFor = signal<string | null>(null);
  protected readonly failedFor = signal<string | null>(null);
  protected readonly src = computed(() => this.api.coverArtUrl(this.id(), this.size(), this.folderId()));
  protected readonly initials = computed(() => {
    const words = this.label().trim().split(/\s+/).filter(Boolean);
    return words.length ? words.slice(0, 2).map(w => Array.from(w)[0]).join('').toLocaleUpperCase() : '♫';
  });
  protected readonly color = computed(() => {
    const colors = ['#315b85', '#6b447d', '#28665e', '#8a4b3a', '#5b5a91', '#755c27', '#346779', '#80506a'];
    let hash = 0;
    for (const char of this.label() || this.id() || 'music') hash = (Math.imul(hash, 31) + char.codePointAt(0)!) >>> 0;
    return colors[hash % colors.length];
  });
}
