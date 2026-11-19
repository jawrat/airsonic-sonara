import { Component, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthService } from './core/auth';
import { ScanStatus } from './core/models';
import { PlayerService } from './core/player';
import { SubsonicApi } from './core/subsonic';
import { ThemeService } from './core/theme';
import { PlayerBar } from './ui/player-bar';

interface NavItem {
  path: string;
  label: string;
  color: string;
  exact?: boolean;
}

const TITLES: Record<string, string> = {
  '': 'Library',
  album: 'Album',
  artist: 'Artist',
  playlists: 'Playlists',
  shares: 'Shared links',
  queue: 'Play queue',
  playlist: 'Playlist',
  starred: 'Starred',
  bookmarks: 'Bookmarks',
  podcasts: 'Podcasts',
  podcast: 'Podcast',
  account: 'Account',
  uploads: 'Upload music',
  users: 'Users',
  settings: 'Settings',
  backups: 'Backups',
};

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, PlayerBar],
  template: `
    @if (auth.isLoggedIn()) {
      <div class="lc">
        <div class="lc-brand"><a routerLink="/">Sonara</a></div>

        <header class="lc-top">
          <span class="lc-title">{{ title() }}</span>
          <span class="lc-spacer"></span>
          <span class="lc-user">
            <a routerLink="/account">{{ auth.session()?.username }}</a>
          </span>
        </header>

        <div class="lc-bars" aria-hidden="true"><i></i><i></i><i></i><i></i><i></i></div>

        <nav class="lc-side" aria-label="Main">
          <div class="lc-status">
            <h3>Status</h3>
            <p>
              <span class="lc-dot" [class.busy]="scan()?.scanning"></span>
              {{ scan()?.scanning ? 'Scanning ' + scan()?.count : 'Active' }}
            </p>
            <p>{{ auth.session()?.isAdmin ? 'Administrator' : 'Listener' }}</p>
          </div>

          @for (item of nav; track item.path) {
            <a
              class="lc-nav"
              [class]="item.color"
              [routerLink]="item.path"
              routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: !!item.exact }"
              >{{ item.label }}</a
            >
          }
          @if (auth.session()?.isAdmin) {
            <a class="lc-nav c5" routerLink="/backups" routerLinkActive="active">Backups</a>
            <a class="lc-nav c4" routerLink="/settings" routerLinkActive="active">Settings</a>
            <a
              class="lc-nav c5"
              routerLink="/users"
              routerLinkActive="active"
              >Users</a
            >
            <button type="button" class="lc-nav c4" (click)="rescan()">Rescan</button>
          }
          <a class="lc-nav c4" routerLink="/uploads" routerLinkActive="active">Upload music</a>
          <a class="lc-nav c5" routerLink="/account" routerLinkActive="active">Account</a>
          <button type="button" class="lc-nav c6" (click)="logout()">Sign out</button>
          <span class="lc-fill"></span>
        </nav>

        <main class="lc-main" [class.with-player]="player.current()">
          <router-outlet />
        </main>
      </div>
      <app-player-bar />
    } @else {
      <main class="lc-login"><router-outlet /></main>
    }
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly player = inject(PlayerService);
  private readonly api = inject(SubsonicApi);
  private readonly router = inject(Router);
  // Constructed here so the stored theme is applied on startup.
  protected readonly theme = inject(ThemeService);

  protected readonly scan = signal<ScanStatus | null>(null);

  protected readonly nav: NavItem[] = [
    { path: '/', label: 'Library', color: 'c1', exact: true },
    { path: '/queue', label: 'Play queue', color: 'c3' },
    { path: '/starred', label: 'Starred', color: 'c2' },
    { path: '/playlists', label: 'Playlists', color: 'c2' },
    { path: '/shares', label: 'Shared links', color: 'c4' },
    { path: '/podcasts', label: 'Podcasts', color: 'c2' },
    { path: '/bookmarks', label: 'Bookmarks', color: 'c2' },
  ];

  protected readonly title = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => TITLES[e.urlAfterRedirects.split(/[/?#]/)[1] ?? ''] ?? 'Sonara'),
    ),
    { initialValue: 'Sonara' },
  );

  constructor() {
    // Poll the scan status while signed in so the status block shows scanning progress.
    effect((onCleanup) => {
      if (!this.auth.isLoggedIn()) {
        this.scan.set(null);
        return;
      }
      void this.player.restore();
      const poll = () => this.api.getScanStatus().then((s) => this.scan.set(s), () => undefined);
      void poll();
      const timer = setInterval(poll, 5000);
      onCleanup(() => clearInterval(timer));
    });
  }

  protected async rescan(): Promise<void> {
    this.scan.set(await this.api.startScan());
  }

  protected logout(): void {
    this.player.stop();
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}
