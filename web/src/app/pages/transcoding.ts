import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SubsonicApi } from '../core/subsonic';
import { UserInfo } from '../core/models';

@Component({
  selector: 'app-transcoding',
  imports: [FormsModule],
  template: `
    <h2>Playback transcoding</h2>
    @if (loading()) { <p>Loading playback settings…</p> }
    @else if (loaded()) {
      <form class="stack" (ngSubmit)="save()">
        <label>Preferred format
          <select name="transcodeFormat" [(ngModel)]="format">
            <option value="">Default (original unless conversion is requested)</option>
            <option value="raw">Original audio</option>
            <option value="mp3">MP3</option><option value="aac">AAC</option><option value="opus">Opus</option>
          </select>
        </label>
        <label>Preferred bitrate (kbps; 0 = default)
          <input type="number" name="transcodeBitrate" [(ngModel)]="bitrate" min="0" max="320" required />
        </label>
        <p class="muted">Choose 0 or 32–320 kbps. Explicit player options override these preferences. The administrator's bitrate limit still applies. Original audio ignores the preferred bitrate. HLS uses AAC and honors the preferred bitrate.</p>
        <p class="muted">Administrator limit: {{ cap() ? cap() + ' kbps' : 'unlimited' }}{{ admin() ? ' (administrators bypass this limit)' : '' }}.</p>
        <button type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save playback settings' }}</button>
      </form>
    }
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (done()) { <p role="status">Playback settings saved. They apply to the next stream.</p> }
  `,
  styles: `.stack { display:grid;gap:.75rem;max-width:36rem } label { display:grid;gap:.25rem }`,
})
export class TranscodingSettings {
  readonly username = input.required<string>();
  private readonly api = inject(SubsonicApi);
  protected readonly loading = signal(true);
  protected readonly loaded = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly done = signal(false);
  protected readonly cap = signal(0);
  protected readonly admin = signal(false);
  protected format = '';
  protected bitrate = 0;
  private revision = 0;
  constructor() {
    effect(() => {
      const name = this.username(); const revision = ++this.revision;
      this.loading.set(true); this.loaded.set(false); this.error.set(''); this.done.set(false);
      void this.api.call<{user: UserInfo}>('getUser', {username:name}).then(({user}) => {
        if (revision !== this.revision) return;
        this.format = user.preferredTranscodeFormat ?? ''; this.bitrate = user.preferredTranscodeBitRate ?? 0;
        this.cap.set(user.maxBitRate); this.admin.set(user.adminRole); this.loaded.set(true);
      }).catch(e => { if (revision === this.revision) this.error.set(e instanceof Error ? e.message : 'Could not load playback settings.'); })
        .finally(() => { if (revision === this.revision) this.loading.set(false); });
    });
  }
  protected async save(): Promise<void> {
    this.error.set(''); this.done.set(false);
    if (!Number.isInteger(this.bitrate) || (this.bitrate !== 0 && (this.bitrate < 32 || this.bitrate > 320))) {
      this.error.set('Choose 0 or a bitrate from 32 to 320 kbps.'); return;
    }
    this.saving.set(true); const revision = this.revision;
    try { await this.api.setTranscodingPreferences(this.username(), this.format, this.bitrate); if (revision === this.revision) this.done.set(true); }
    catch (e) { if (revision === this.revision) this.error.set(e instanceof Error ? e.message : 'Could not save playback settings.'); }
    finally { this.saving.set(false); }
  }
}
