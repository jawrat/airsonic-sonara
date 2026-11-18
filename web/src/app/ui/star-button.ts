import { Component, inject, input, linkedSignal, signal } from '@angular/core';
import { StarTarget } from '../core/models';
import { SubsonicApi } from '../core/subsonic';

/** A heart that stars or unstars a song or album. Updates immediately and reverts if the server refuses. */
@Component({
  selector: 'app-star-button',
  template: `
    <button
      type="button"
      class="link star"
      [class.on]="on()"
      [attr.aria-pressed]="on()"
      [attr.aria-label]="on() ? 'Remove star' : 'Add star'"
      [disabled]="busy()"
      (click)="toggle()"
    >
      {{ on() ? '★' : '☆' }}
    </button>
  `,
  styles: `
    .star.on {
      color: gold;
    }
  `,
})
export class StarButton {
  readonly target = input.required<StarTarget>();
  readonly starred = input<string | undefined>();

  private readonly api = inject(SubsonicApi);
  protected readonly on = linkedSignal(() => !!this.starred());
  protected readonly busy = signal(false);

  protected async toggle(): Promise<void> {
    const next = !this.on();
    this.on.set(next);
    this.busy.set(true);
    try {
      await this.api.setStar(this.target(), next);
    } catch {
      this.on.set(!next);
    } finally {
      this.busy.set(false);
    }
  }
}
