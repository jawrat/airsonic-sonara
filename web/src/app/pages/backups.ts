import { DatePipe } from '@angular/common';
import { Component, effect, inject, resource, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SubsonicApi } from '../core/subsonic';

@Component({
  selector: 'app-backups', imports: [DatePipe, RouterLink],
  template: `
    <h1>Backups</h1>
    <p>Archives include the database and credential encryption key. They contain account data: keep downloaded copies private.</p>
    <p class="muted">Music files, podcast media, environment configuration and certificates are not included. Copy backups to separate storage for recovery from a lost volume.</p>
    <div class="toolbar"><button type="button" (click)="create()" [disabled]="requesting() || data.value()?.busy">Create backup</button><button type="button" (click)="data.reload()">Refresh</button><a routerLink="/settings">Schedule and retention settings</a></div>
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (data.error()) { <p class="error" role="alert">{{ data.error()?.message }}</p> }
    @if (data.value(); as value) {
      <p role="status">Status: {{ value.phase }} @if (value.busy) { · checking again automatically… }</p>
      @if (value.lastError) { <p class="error" role="alert">{{ value.lastError }}</p> }
      <ul class="files">
        @for (file of value.backup; track file.name) {
          <li class="panel"><div><strong>{{ file.name }}</strong><p class="muted small">{{ file.created | date:'medium' }} · {{ size(file.bytes) }} MB</p></div><button type="button" (click)="download(file.name)" [disabled]="downloading()">Download</button></li>
        } @empty { <li>No completed backups yet.</li> }
      </ul>
    } @else if (data.isLoading()) { <p>Loading…</p> }
    <h2>Restore</h2>
    <p>Restore is an offline command. Stop Sonara, verify the archive, and restore to an empty database. The data-directory lock prevents replacing a live server's key. Follow the backup/restore instructions included with this update.</p>
  `,
  styles: [` .files { list-style:none; padding:0; display:grid; gap:.7rem; } .files li { display:flex; align-items:center; justify-content:space-between; gap:1rem; flex-wrap:wrap; } strong { overflow-wrap:anywhere; } `],
})
export class BackupsPage {
  private readonly api = inject(SubsonicApi);
  protected readonly data = resource({ loader: () => this.api.getBackups() });
  protected readonly requesting = signal(false); protected readonly downloading = signal(false);
  protected readonly error = signal(''); protected readonly message = signal('');
  constructor() { effect(onCleanup => { if (!this.data.value()?.busy) return; const timer = setInterval(() => this.data.reload(), 2000); onCleanup(() => clearInterval(timer)); }); }
  protected size(bytes: number): string { return (bytes / 1024 / 1024).toFixed(1); }
  protected async create(): Promise<void> {
    this.requesting.set(true); this.error.set(''); this.message.set('');
    try { await this.api.createBackup(); this.message.set('Backup queued. Keep this page open or return to download it later.'); this.data.reload(); }
    catch (e) { this.error.set(e instanceof Error ? e.message : 'Backup could not be queued.'); }
    finally { this.requesting.set(false); }
  }
  protected async download(name: string): Promise<void> {
    this.downloading.set(true); this.error.set('');
    try { const url = URL.createObjectURL(await this.api.downloadBackup(name)); const a = document.createElement('a'); a.href = url; a.download = name; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
    catch (e) { this.error.set(e instanceof Error ? e.message : 'Download failed.'); }
    finally { this.downloading.set(false); }
  }
}
