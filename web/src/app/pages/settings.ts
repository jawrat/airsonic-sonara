import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SubsonicApi } from '../core/subsonic';

interface Setting { key: string; label: string; kind: string; value: string | number; effectiveValue: string; min?: number; max?: number }
interface Folder { id: number; name: string; path: string; enabled: boolean; allowUploads?: boolean }

@Component({
  selector: 'app-settings',
  imports: [FormsModule],
  template: `
    <h1>Settings</h1>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (loading()) { <p class="muted">Loading settings…</p> }
    <form class="panel" (ngSubmit)="save()">
      <h2>Server</h2>
      <p class="muted">Saved server settings take effect after restarting Sonara.</p>
      @for (s of settings(); track s.key) {
        <label class="setting"><span>{{ s.label }}</span>
          @if (s.kind === 'bool') {
            <select [name]="s.key" [(ngModel)]="s.value"><option value="True">Enabled</option><option value="False">Disabled</option></select>
          } @else if (s.kind === 'format') {
            <select [name]="s.key" [(ngModel)]="s.value"><option>mp3</option><option>opus</option><option>aac</option></select>
          } @else {
            <input [name]="s.key" [(ngModel)]="s.value" [type]="s.kind === 'int' ? 'number' : 'url'" [min]="s.min ?? null" [max]="s.max ?? null" />
          }
          @if (pending(s)) { <small class="muted">Pending restart · running value: {{ s.effectiveValue || '(empty)' }}</small> }
        </label>
      }
      <button type="submit" [disabled]="busy() || loading()">Save server settings</button>
    </form>
    <section class="panel folders">
      <h2>Music folders</h2>
      <p class="muted">Paths refer to the server, or to paths inside its container. Disabling a folder preserves its files and library records. Restart after folder changes to refresh file watchers.</p>
      @for (f of folders(); track f.id) {
        <form class="folder" (ngSubmit)="saveFolder(f)">
          <input aria-label="Folder name" name="name" [(ngModel)]="f.name" required />
          <input aria-label="Server path" name="path" [(ngModel)]="f.path" required />
          <label><input type="checkbox" name="enabled" [(ngModel)]="f.enabled" /> Enabled</label>
          <label><input type="checkbox" name="allowUploads" [(ngModel)]="f.allowUploads" /> Allow uploads</label>
          <button type="submit" [disabled]="busy()">Save folder</button>
        </form>
      }
      <h3>Add folder</h3>
      <form class="folder" (ngSubmit)="saveFolder(newFolder)">
        <input aria-label="New folder name" name="newName" [(ngModel)]="newFolder.name" placeholder="Name" required />
        <input aria-label="New folder server path" name="newPath" [(ngModel)]="newFolder.path" placeholder="/var/music" required />
        <button type="submit" [disabled]="busy()">Add folder</button>
      </form>
    </section>
  `,
  styles: [`
    .setting { display: grid; grid-template-columns: minmax(12rem, 1fr) minmax(8rem, 20rem); gap: .5rem 1rem; margin: 1rem 0; align-items: center }
    .setting small { grid-column: 1 / -1 } .folders { margin-top: 1rem } .folder { display:flex; gap:.6rem; flex-wrap:wrap; margin:1rem 0 } .folder input:not([type=checkbox]) { flex:1; min-width:10rem }
    @media(max-width:40rem) { .setting { grid-template-columns:1fr } }
  `],
})
export class SettingsPage {
  private readonly api = inject(SubsonicApi);
  readonly settings = signal<Setting[]>([]);
  readonly folders = signal<Folder[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  newFolder = { name: '', path: '', enabled: true };
  constructor() { void this.load(); }
  private async load(): Promise<void> {
    try {
      const [settings, folders] = await Promise.all([
        this.api.call<{ settings: { setting: Setting[] } }>('getSettings'),
        this.api.call<{ musicFolderSettings: { folder: Folder[] } }>('getMusicFolderSettings'),
      ]);
      this.settings.set(settings.settings.setting.map(s => ({ ...s, value: s.kind === 'bool' ? (String(s.value).toLowerCase() === 'true' ? 'True' : 'False') : s.value })));
      this.folders.set(folders.musicFolderSettings.folder);
    } catch (e) { this.error.set(e instanceof Error ? e.message : 'Could not load settings'); }
    finally { this.loading.set(false); }
  }
  pending(setting: Setting): boolean { return String(setting.value).toLowerCase() !== setting.effectiveValue.toLowerCase(); }
  async save(): Promise<void> {
    this.busy.set(true); this.error.set(''); this.message.set('');
    try {
      await this.api.call('updateSettings', { key: this.settings().map(s => s.key), value: this.settings().map(s => String(s.value)) });
      this.message.set('Settings saved. Restart Sonara to apply them.'); await this.load();
    } catch (e) { this.error.set(e instanceof Error ? e.message : 'Save failed'); }
    finally { this.busy.set(false); }
  }
  async saveFolder(folder: { id?: number; name: string; path: string; enabled: boolean; allowUploads?: boolean }): Promise<void> {
    this.busy.set(true); this.error.set(''); this.message.set('');
    try {
      await this.api.call('saveMusicFolder', { ...(folder.id ? { id: folder.id } : {}), name: folder.name, path: folder.path, enabled: folder.enabled, allowUploads: folder.allowUploads ?? false });
      this.newFolder = { name: '', path: '', enabled: true };
      this.message.set('Folder saved. Rescan to update the library; restart to refresh file watchers.'); await this.load();
    } catch (e) { this.error.set(e instanceof Error ? e.message : 'Save failed'); }
    finally { this.busy.set(false); }
  }
}
