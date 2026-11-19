import { DatePipe } from '@angular/common';
import { Component, inject, resource, signal } from '@angular/core';
import { SubsonicApi } from '../core/subsonic';

@Component({
  selector: 'app-shares', imports: [DatePipe],
  template: `
    <h1>Shared links</h1>
    <p class="muted">Anyone with a valid link can listen. Revoke a link to stop access. Administrators can manage all users’ links.</p>
    @if (shares.error()) { <p class="error" role="alert">{{ shares.error()?.message }}</p> }
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (shares.isLoading()) { <p role="status">Loading links…</p> }
    <button type="button" (click)="shares.reload()">Refresh</button>
    <ul class="share-list">
      @for (s of shares.value() ?? []; track s.id) {
        <li class="card"><h2>{{ s.description || 'Shared music' }}</h2><a [href]="s.url" target="_blank" rel="noreferrer">{{ s.url }}</a>
          <p class="muted small">{{ s.entry?.length ?? 0 }} tracks · {{ s.username }} · {{ s.visitCount }} visits · {{ s.expires ? 'Expires ' + (s.expires | date:'medium') : 'No expiration' }}</p>
          <div class="toolbar"><button type="button" (click)="copy(s.url)">Copy link</button><button type="button" (click)="confirm.set(s.id)" [disabled]="busy()">Revoke</button></div>
          @if (confirm() === s.id) { <div class="toolbar"><span>Disable this link permanently?</span><button type="button" (click)="revoke(s.id)" [disabled]="busy()">Revoke link</button><button type="button" (click)="confirm.set(null)">Cancel</button></div> }
        </li>
      } @empty { @if (!shares.isLoading()) { <li>No shared links yet. Create one from an album, folder, or playlist.</li> } }
    </ul>
  `,
  styles: `.share-list{list-style:none;padding:0;display:grid;gap:1rem}.share-list li{min-width:0;overflow-wrap:anywhere}.share-list h2{margin:0 0 .5rem}`,
})
export class SharesPage {
  private readonly api = inject(SubsonicApi);
  protected readonly shares = resource({ loader: () => this.api.getShares() });
  protected readonly confirm = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected async revoke(id: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true); this.error.set('');
    try { await this.api.deleteShare(id); this.confirm.set(null); this.message.set('Link revoked.'); this.shares.reload(); }
    catch (e) { this.error.set(e instanceof Error ? e.message : 'Could not revoke this link.'); }
    finally { this.busy.set(false); }
  }
  protected async copy(url: string): Promise<void> {
    try { await navigator.clipboard.writeText(url); this.message.set('Link copied.'); }
    catch { this.message.set('Select the displayed URL to copy it.'); }
  }
}
