import { Component, inject, input } from '@angular/core';
import { Song } from '../core/models';
import { PlayerService } from '../core/player';

@Component({
  selector: 'app-queue-actions',
  template: `<button type="button" class="link" (click)="player.enqueue([song()], true)" [attr.aria-label]="'Play next: ' + song().title">Next</button>
    <button type="button" class="link" (click)="player.enqueue([song()])" [attr.aria-label]="'Add to queue: ' + song().title">Queue</button>`,
  styles: [`:host { display: inline-flex; gap: .55rem; white-space: nowrap; }`],
})
export class QueueActions { readonly song = input.required<Song>(); protected readonly player = inject(PlayerService); }
