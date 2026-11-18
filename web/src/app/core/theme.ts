import { Injectable, signal } from '@angular/core';

export interface ThemeOption {
  id: string;
  label: string;
}

export const THEMES: ThemeOption[] = [
  { id: 'lcars', label: 'LCARS (blue)' },
  { id: 'lcars-classic', label: 'LCARS (classic)' },
  { id: 'dark', label: 'Plain dark' },
  { id: 'light', label: 'Plain light' },
];

const KEY = 'sonara.theme';
const DEFAULT = 'lcars';

/** Picks the colour theme. Stored per browser; index.html applies it before first paint. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal(this.read());

  constructor() {
    this.apply(this.theme());
  }

  set(id: string): void {
    if (!THEMES.some((t) => t.id === id)) return;
    this.theme.set(id);
    this.apply(id);
    try {
      localStorage.setItem(KEY, id);
    } catch {
      // storage unavailable: the choice just won't survive a reload
    }
  }

  private read(): string {
    try {
      const stored = localStorage.getItem(KEY);
      if (stored && THEMES.some((t) => t.id === stored)) return stored;
    } catch {
      // ignore
    }
    return DEFAULT;
  }

  private apply(id: string): void {
    document.documentElement.dataset['theme'] = id;
  }
}
