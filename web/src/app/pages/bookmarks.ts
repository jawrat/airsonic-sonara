import { Component, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Bookmark } from '../core/models';
import { formatDuration } from '../core/format';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';

@Component({
  selector: 'app-bookmarks',
  imports: [RouterLink],
  template: `
    <h1>Bookmarks</h1>
    <p class="muted">Use the Bookmark button in the player to save your place in a track.</p>

    @if (bookmarks.error()) {
      <p class="error" role="alert">{{ bookmarks.error()?.message }}</p>
    }
    @if (bookmarks.value(); as list) {
      @if (!list.length) {
        <p class="muted">No bookmarks yet.</p>
      }
      <ul class="tracks">
        @for (b of list; track b.entry.id) {
          <li>
            <button type="button" class="link" (click)="resume(b)">{{ b.entry.title }}</button>
            @if (b.entry.artistId) {
              <a class="muted" [routerLink]="['/artist', b.entry.artistId]">{{ b.entry.artist }}</a>
            } @else {
              <a class="muted" [routerLink]="['/podcast', b.entry.channelId]">{{ b.entry.artist }}</a>
            }
            <span class="muted small">at {{ fmt(b.position / 1000) }}</span>
            <span class="time">
              <button type="button" (click)="resume(b)">Resume</button>
              <button type="button" (click)="remove(b)">Delete</button>
            </span>
          </li>
        }
      </ul>
    } @else if (bookmarks.isLoading()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class BookmarksPage {
  private readonly api = inject(SubsonicApi);
  private readonly player = inject(PlayerService);
  protected readonly fmt = formatDuration;
  protected readonly bookmarks = resource({ loader: () => this.api.getBookmarks() });

  protected resume(b: Bookmark): void {
    this.player.play([b.entry], 0, b.position / 1000);
  }

  protected async remove(b: Bookmark): Promise<void> {
    await this.api.deleteBookmark(b.entry.id);
    this.bookmarks.reload();
  }
}
