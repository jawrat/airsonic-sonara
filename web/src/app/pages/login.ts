import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth';

@Component({
  selector: 'app-login',
  template: `
    <form class="login panel" (submit)="submit($event, user.value, pass.value)">
      <h1>Sonara</h1>
      <p class="muted">Sign in to your library</p>
      <label>
        Username
        <input #user name="username" autocomplete="username" required autofocus />
      </label>
      <label>
        Password
        <input #pass name="password" type="password" autocomplete="current-password" required />
      </label>
      @if (error(); as message) {
        <p class="error" role="alert">{{ message }}</p>
      }
      <button type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
    </form>
  `,
  styles: `
    .login {
      max-width: 22rem;
      margin: 12vh auto 0;
      display: grid;
      gap: 1rem;
    }
    h1 {
      margin: 0;
    }
    label {
      display: grid;
      gap: 0.35rem;
    }
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(event: Event, username: string, password: string): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(username.trim(), password);
      await this.router.navigateByUrl('/');
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Could not sign in');
    } finally {
      this.busy.set(false);
    }
  }
}
