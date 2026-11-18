import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import SparkMD5 from 'spark-md5';

export interface Session {
  username: string;
  token: string;
  salt: string;
  isAdmin: boolean;
}

export interface SubsonicBody {
  status: 'ok' | 'failed';
  error?: { code: number; message: string };
}

export interface SubsonicEnvelope<T = object> {
  'subsonic-response': SubsonicBody & T;
}

export const API_VERSION = '1.16.1';
export const CLIENT_NAME = 'sonara-web';
const STORAGE_KEY = 'sonara.session';

/**
 * Subsonic token auth: hex md5(password + salt). The browser keeps only the token and
 * salt, never the password, and the token (not the password) is what ends up in stream URLs.
 */
export function makeToken(password: string, salt: string): string {
  return SparkMD5.hash(password + salt);
}

export function randomSalt(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(8));
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

export function authParams(session: Session): Record<string, string> {
  return {
    u: session.username,
    t: session.token,
    s: session.salt,
    v: API_VERSION,
    c: CLIENT_NAME,
    f: 'json',
  };
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly session = signal<Session | null>(this.restore());
  readonly isLoggedIn = computed(() => this.session() !== null);

  constructor() {
    // A restored session may predate the admin flag, or the user's rights may have changed.
    const restored = this.session();
    if (restored) {
      void this.fetchIsAdmin(restored).then((isAdmin) => {
        const current = this.session();
        if (current && current.token === restored.token && current.isAdmin !== isAdmin) {
          const next = { ...current, isAdmin };
          this.session.set(next);
          this.persist(next);
        }
      });
    }
  }

  async login(username: string, password: string): Promise<void> {
    const salt = randomSalt();
    const session: Session = { username, token: makeToken(password, salt), salt, isAdmin: false };

    const res = await firstValueFrom(
      this.http.get<SubsonicEnvelope>('/rest/ping', { params: authParams(session) }),
    );
    const body = res['subsonic-response'];
    if (body.status !== 'ok') {
      throw new Error(body.error?.message ?? 'Login failed');
    }

    session.isAdmin = await this.fetchIsAdmin(session);
    this.session.set(session);
    this.persist(session);
  }

  /** After the user changes their own password the old token is no longer valid; re-derive it. */
  rekey(newPassword: string): void {
    const current = this.session();
    if (!current) return;
    const salt = randomSalt();
    const next: Session = { ...current, salt, token: makeToken(newPassword, salt) };
    this.session.set(next);
    this.persist(next);
  }

  private async fetchIsAdmin(session: Session): Promise<boolean> {
    try {
      const res = await firstValueFrom(
        this.http.get<SubsonicEnvelope<{ user?: { adminRole?: boolean } }>>('/rest/getUser', {
          params: { ...authParams(session), username: session.username },
        }),
      );
      return res['subsonic-response'].user?.adminRole === true;
    } catch {
      return false;
    }
  }

  logout(): void {
    this.session.set(null);
    this.persist(null);
  }

  /** Query parameters to add to every Subsonic call. */
  currentParams(): Record<string, string> {
    const session = this.session();
    if (!session) throw new Error('Not logged in');
    return authParams(session);
  }

  private restore(): Session | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      return raw ? ({ isAdmin: false, ...JSON.parse(raw) } as Session) : null;
    } catch {
      return null;
    }
  }

  private persist(session: Session | null): void {
    try {
      if (session) sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
      else sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // Storage unavailable (private mode etc.): the session just won't survive a reload.
    }
  }
}
