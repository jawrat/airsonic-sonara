import { AvatarSettings } from './avatars';
import { TranscodingSettings } from './transcoding';
import { Component, inject, resource, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UserInfo } from '../core/models';
import { AuthService } from '../core/auth';
import { SubsonicApi } from '../core/subsonic';

@Component({
  selector: 'app-users',
  imports: [FormsModule, TranscodingSettings, AvatarSettings],
  template: `
    <h1>Users</h1>

    <form class="toolbar" (submit)="create($event, name, pw, admin)">
      <input #name placeholder="Username" aria-label="Username" autocomplete="off" />
      <input #pw type="password" placeholder="Password (8+ chars)" aria-label="Password" autocomplete="new-password" />
      <label class="small"><input #admin type="checkbox" /> Admin</label>
      <button type="submit">Add user</button>
    </form>

    @if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    }
    @if (users.error()) {
      <p class="error" role="alert">{{ users.error()?.message }}</p>
    }

    @if (users.value(); as list) {
      <ul class="tracks">
        @for (u of list; track u.username) {
          <li>
            <img [src]="api.avatarUrl(u.username)" alt="" width="40" height="40" style="object-fit:contain" />
            <strong>{{ u.username }}</strong>
            @if (u.adminRole) {
              <span class="muted small">admin</span>
            }
            <span class="time">
              <button type="button" (click)="toggleAdmin(u.username, !u.adminRole)">
                {{ u.adminRole ? 'Remove admin' : 'Make admin' }}
              </button>
              <button type="button" (click)="editPermissions(u)">Permissions</button>
              <button type="button" (click)="playback.set(u.username)">Playback / avatar</button>
              <button type="button" (click)="resetPassword(u.username)">Set password</button>
              @if (u.username !== auth.session()?.username) {
                <button type="button" (click)="remove(u.username)">Delete</button>
              }
            </span>
          </li>
        }
      </ul>
    } @else if (users.isLoading()) {
      <p class="muted">Loading…</p>
    }
    @if (playback(); as username) {
      <section class="panel"><h2>{{ username }}</h2><app-avatars [username]="username" /><app-transcoding [username]="username" /><button type="button" (click)="playback.set(null)">Close playback settings</button></section>
    }
    @if (editing(); as edit) {
      <form class="panel" (ngSubmit)="savePermissions()">
        <h2>Permissions · {{ edit.username }}</h2>
        @if (edit.adminRole) { <p class="muted">Administrators bypass role and folder restrictions.</p> }
        @for (role of roles; track role.key) {
          <label style="display:block;margin:.5rem 0"><input type="checkbox" [name]="role.key" [(ngModel)]="permissions[role.key]" /> {{ role.label }}</label>
        }
        <label style="display:block;margin:.5rem 0"><input type="checkbox" name="allFolders" [(ngModel)]="edit.allFolders" /> All enabled music folders</label>
        @if (!edit.allFolders) {
          @for (f of folderOptions.value(); track f.id) {
            <label style="display:block;margin:.5rem 0"><input type="checkbox" [name]="'folder' + f.id" [checked]="selectedFolders.includes(f.id)" (change)="toggleFolder(f.id)" /> {{ f.name }}</label>
          }
          @if (folderOptions.error()) { <p class="error">Could not load folders.</p> }
        }
        <label>Maximum stream bitrate (0 = unlimited) <input type="number" name="maxBitRate" [(ngModel)]="edit.maxBitRate" min="0" max="512" /></label>
        <div class="toolbar" style="margin-top:1rem"><button type="submit">Save permissions</button><button type="button" (click)="editing.set(null)">Cancel</button></div>
      </form>
    }
  `,
})
export class UsersPage {
  protected readonly auth = inject(AuthService);
  protected readonly api = inject(SubsonicApi);
  protected readonly error = signal('');
  protected readonly users = resource({ loader: () => this.api.getUsers() });

  protected readonly playback = signal<string | null>(null);
  protected readonly editing = signal<UserInfo | null>(null);
  protected readonly roles = [
    { key: 'settingsRole', label: 'Change own password' }, { key: 'streamRole', label: 'Stream music' },
    { key: 'downloadRole', label: 'Download originals' }, { key: 'uploadRole', label: 'Upload music to enabled destinations' }, { key: 'playlistRole', label: 'Manage playlists' },
    { key: 'coverArtRole', label: 'View cover art' }, { key: 'podcastRole', label: 'Manage podcasts' },
    { key: 'shareRole', label: 'Create public shares' }, { key: 'commentRole', label: 'Rate music and post chat' },
  ];
  protected permissions: Record<string, boolean> = {};
  protected selectedFolders: number[] = [];
  protected readonly folderOptions = resource({ loader: async () =>
    (await this.api.call<{ musicFolders: { musicFolder: { id: number; name: string }[] } }>('getMusicFolders')).musicFolders.musicFolder });
  protected editPermissions(user: UserInfo): void {
    this.editing.set({ ...user });
    this.selectedFolders = [...(user.folder ?? [])];
    const values = user as unknown as Record<string, boolean>;
    this.permissions = Object.fromEntries(this.roles.map(r => [r.key, values[r.key]]));
  }
  protected toggleFolder(id: number): void {
    this.selectedFolders = this.selectedFolders.includes(id) ? this.selectedFolders.filter(i => i !== id) : [...this.selectedFolders, id];
  }
  protected async savePermissions(): Promise<void> {
    const edit = this.editing(); if (!edit) return;
    await this.run(async () => {
      await this.api.call('updateUser', { username: edit.username, ...this.permissions,
        allFolders: edit.allFolders, maxBitRate: edit.maxBitRate,
        ...(!edit.allFolders && this.selectedFolders.length ? { musicFolderId: this.selectedFolders } : {}) });
      this.editing.set(null);
    });
  }

  protected async create(
    event: Event,
    name: HTMLInputElement,
    pw: HTMLInputElement,
    admin: HTMLInputElement,
  ): Promise<void> {
    event.preventDefault();
    await this.run(async () => {
      await this.api.createUser(name.value.trim(), pw.value, admin.checked);
      name.value = pw.value = '';
      admin.checked = false;
    });
  }

  protected toggleAdmin(username: string, adminRole: boolean): Promise<void> {
    return this.run(() => this.api.updateUser(username, { adminRole }));
  }

  protected async resetPassword(username: string): Promise<void> {
    const password = window.prompt(`New password for ${username} (8+ characters)`);
    if (!password) return;
    await this.run(() => this.api.updateUser(username, { password }));
  }

  protected async remove(username: string): Promise<void> {
    if (!window.confirm(`Delete user "${username}" and their playlists?`)) return;
    await this.run(() => this.api.deleteUser(username));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.error.set('');
    try {
      await action();
      this.users.reload();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Failed');
    }
  }
}
