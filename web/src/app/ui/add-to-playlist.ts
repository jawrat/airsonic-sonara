import { Component, inject, input, signal } from '@angular/core';
import { Playlist } from '../core/models';
import { SubsonicApi } from '../core/subsonic';

/** A small "+" menu that adds one song to an existing playlist, or to a new one. */
@Component({
  selector: 'app-add-to-playlist',
  template: `
    <select
      #sel
      class="add"
      aria-label="Add to playlist"
      (focus)="load()"
      (change)="choose(sel.value); sel.value = ''"
    >
      <option value="" selected>＋ Playlist</option>
      @for (p of playlists(); track p.id) {
        <option [value]="p.id">{{ p.name }}</option>
      }
      <option value="__new">New playlist…</option>
    </select>
    @if (message()) {
      <span class="muted small" role="status">{{ message() }}</span>
    }
  `,
  styles: `
    .add {
      max-width: 9rem;
    }
  `,
})
export class AddToPlaylist {
  readonly songId = input.required<string>();

  private readonly api = inject(SubsonicApi);
  protected readonly playlists = signal<Playlist[]>([]);
  protected readonly message = signal('');
  private loaded = false;

  protected async load(): Promise<void> {
    if (this.loaded) return;
    this.loaded = true;
    try {
      this.playlists.set(await this.api.getPlaylists());
    } catch {
      this.loaded = false;
    }
  }

  protected async choose(value: string): Promise<void> {
    if (!value) return;
    try {
      if (value === '__new') {
        const name = window.prompt('New playlist name')?.trim();
        if (!name) return;
        const created = await this.api.createPlaylist(name, [this.songId()]);
        this.playlists.update((list) => [...list, created]);
        this.say(`Added to ${name}`);
      } else {
        await this.api.updatePlaylist(value, { addSongIds: [this.songId()] });
        this.say('Added');
      }
    } catch (e) {
      this.say(e instanceof Error ? e.message : 'Failed');
    }
  }

  private say(text: string): void {
    this.message.set(text);
    setTimeout(() => this.message.set(''), 2500);
  }
}
