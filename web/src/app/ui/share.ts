import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MediaShare } from '../core/models';
import { SubsonicApi } from '../core/subsonic';

@Component({
  selector: 'app-share', imports: [FormsModule, RouterLink],
  template: `
    <button type="button" (click)="opened.set(!opened())" [disabled]="disabled()" [attr.aria-expanded]="opened()">Share</button>
    @if (opened()) {
      <section class="share-panel" aria-label="Share settings">
        <p>Anyone with this link can listen without signing in. The link contains a snapshot of the accessible music tracks.</p>
        @if (!created()) {
          <label>Link expires <select [(ngModel)]="days" [disabled]="busy()"><option [ngValue]="30">In 30 days</option><option [ngValue]="7">In 7 days</option><option [ngValue]="0">Never</option></select></label>
          <button type="button" (click)="create()" [disabled]="busy()">{{ busy() ? 'Creating…' : 'Create share link' }}</button>
        }
        @if (created(); as share) {
          <label>Share link <input #url readonly [value]="share.url" (click)="url.select()" /></label>
          <div class="share-actions"><button type="button" (click)="copy(share.url)">Copy link</button><a [href]="share.url" target="_blank" rel="noreferrer">Open link</a></div>
          <a routerLink="/shares">Manage or revoke links</a>
        }
        @if (message()) { <p role="status">{{ message() }}</p> }
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      </section>
    }
  `,
  styles: `:host{display:block;min-width:0}.share-panel{margin:.6rem 0;padding:.75rem;border:1px solid var(--border);border-radius:.5rem;background:var(--panel);font:inherit;max-width:32rem;overflow-wrap:anywhere}.share-panel p{margin:.3rem 0 .65rem}.share-panel label{display:grid;gap:.25rem;margin:.5rem 0}.share-panel input,.share-panel select{width:100%;min-width:0;box-sizing:border-box}.share-actions{display:flex;align-items:center;flex-wrap:wrap;gap:.65rem;margin:.5rem 0}`,
})
export class Share {
  readonly ids = input.required<string[]>();
  readonly description = input('');
  readonly disabled = input(false);
  private readonly api = inject(SubsonicApi);
  private readonly key = computed(() => JSON.stringify([this.ids(), this.description()]));
  protected readonly opened = signal(false);
  protected readonly busy = signal(false);
  protected readonly created = signal<MediaShare | null>(null);
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected days = 30;
  constructor() {
    effect(() => { this.key(); this.opened.set(false); this.created.set(null); this.error.set(''); this.message.set(''); this.busy.set(false); });
  }
  protected async create(): Promise<void> {
    if (this.busy() || this.created()) return;
    const key = this.key(); this.busy.set(true); this.error.set('');
    try {
      const expires = this.days ? Date.now() + this.days * 86400000 : 0;
      const share = await this.api.createShare([...this.ids()], this.description(), expires);
      if (key === this.key()) this.created.set(share);
    } catch (e) { if (key === this.key()) this.error.set(e instanceof Error ? e.message : 'Could not create this share.'); }
    finally { if (key === this.key()) this.busy.set(false); }
  }
  protected async copy(url: string): Promise<void> {
    try { await navigator.clipboard.writeText(url); this.message.set('Link copied.'); }
    catch { this.message.set('Select the link above and copy it.'); }
  }
}
