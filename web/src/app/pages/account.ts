import { AvatarSettings } from './avatars';
import { TranscodingSettings } from './transcoding';
import { Component, inject, signal } from '@angular/core';
import { AuthService } from '../core/auth';
import { SubsonicApi } from '../core/subsonic';
import { THEMES, ThemeService } from '../core/theme';

@Component({
  selector: 'app-account',
  imports: [TranscodingSettings, AvatarSettings],
  template: `
    <h1>Account</h1>
    <p class="muted">Signed in as {{ auth.session()?.username }}</p>

    <h2>Appearance</h2>
    <label class="field">
      Theme
      <select #t (change)="theme.set(t.value)" aria-label="Theme">
        @for (o of themes; track o.id) {
          <option [value]="o.id" [selected]="o.id === theme.theme()">{{ o.label }}</option>
        }
      </select>
    </label>

    @if (auth.session()?.username; as username) { <app-avatars [username]="username" /><app-transcoding [username]="username" /> }

    <h2>Change password</h2>
    <form class="stack" (submit)="submit($event, pw, again)">
      <label>New password <input #pw type="password" autocomplete="new-password" /></label>
      <label>Repeat <input #again type="password" autocomplete="new-password" /></label>
      <button type="submit">Change password</button>
    </form>
    @if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    }
    @if (done()) {
      <p role="status">Password changed.</p>
    }
  `,
  styles: `
    .field {
      max-width: 22rem;
    }
    .stack {
      display: grid;
      gap: 0.75rem;
      max-width: 22rem;
    }
    label,
    .field {
      display: grid;
      gap: 0.25rem;
    }
  `,
})
export class AccountPage {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly themes = THEMES;
  private readonly api = inject(SubsonicApi);
  protected readonly error = signal('');
  protected readonly done = signal(false);

  protected async submit(event: Event, pw: HTMLInputElement, again: HTMLInputElement): Promise<void> {
    event.preventDefault();
    this.error.set('');
    this.done.set(false);
    const username = this.auth.session()?.username;
    if (!username) return;
    if (pw.value !== again.value) {
      this.error.set('The passwords do not match.');
      return;
    }
    try {
      await this.api.changePassword(username, pw.value);
      // The token is derived from the password, so re-key the session or the next call would fail.
      this.auth.rekey(pw.value);
      pw.value = again.value = '';
      this.done.set(true);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Failed');
    }
  }
}
