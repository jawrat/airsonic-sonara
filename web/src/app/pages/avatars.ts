import { Component, effect, inject, input, signal } from '@angular/core';
import { SubsonicApi } from '../core/subsonic';
import { UserInfo } from '../core/models';

@Component({
  selector:'app-avatars',
  template:`
    <h2>Avatar</h2>
    @if (loaded()) {
      <img class="current" [src]="api.avatarUrl(username(), version())" alt="Current avatar" />
      <p class="muted">Choose a classic Subsonic avatar or upload your own PNG/JPEG (2 MB; up to 4096 × 4096 pixels).</p>
      <button type="button" [disabled]="busy()" (click)="choose('')">Use default silhouette</button>
      <div class="avatars" role="group" aria-label="Classic avatars">
        @for (avatar of avatars(); track avatar.id) {
          <button type="button" [disabled]="busy()" [class.selected]="selected() === avatar.id" [attr.aria-pressed]="selected() === avatar.id" [attr.aria-label]="avatar.name" [title]="avatar.name" (click)="choose(avatar.id)">
            <img [src]="api.builtinAvatarUrl(avatar.id)" [alt]="avatar.name" loading="lazy" />
          </button>
        }
      </div>
      <label>Upload custom avatar <input type="file" accept="image/png,image/jpeg" [disabled]="busy()" (change)="upload($event)" /></label>
    } @else if (!error()) { <p>Loading avatars…</p> }
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (message()) { <p role="status">{{ message() }}</p> }
  `,
  styles:`.avatars{display:flex;gap:.5rem;flex-wrap:wrap;margin:1rem 0;max-width:48rem}.avatars button{padding:.25rem;border:2px solid transparent}.avatars button.selected{border-color:var(--accent,#8bf)}.avatars img{width:48px;height:48px;object-fit:contain}.current{width:96px;height:96px;object-fit:contain}label{display:grid;gap:.5rem}`,
})
export class AvatarSettings {
  readonly username = input.required<string>();
  protected readonly api = inject(SubsonicApi);
  protected readonly avatars = signal<{id:string;name:string}[]>([]);
  protected readonly loaded = signal(false);
  protected readonly selected = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected readonly version = signal(0);
  private revision = 0;
  constructor() {
    effect(() => {
      const username = this.username(); const revision = ++this.revision;
      this.loaded.set(false);this.error.set('');this.message.set('');
      void Promise.all([this.api.call<{avatars:{avatar:{id:string;name:string}[]}}>('getAvatars'), this.api.call<{user:UserInfo}>('getUser',{username})])
        .then(([list,account]) => {if(revision !== this.revision)return;this.avatars.set(list.avatars.avatar);this.selected.set(account.user.avatarId ?? '');this.loaded.set(true);})
        .catch(e => {if(revision === this.revision)this.error.set(e instanceof Error ? e.message : 'Could not load avatars.');});
    });
  }
  protected async choose(id:string,file?:File):Promise<void> {
    this.busy.set(true);this.error.set('');this.message.set('');const revision=this.revision;
    try {await this.api.setAvatar(this.username(),id,file);if(revision===this.revision){this.selected.set(id);this.version.update(v=>v+1);this.message.set('Avatar saved.');}}
    catch(e){if(revision===this.revision)this.error.set(e instanceof Error ? e.message : 'Could not save avatar.');}
    finally{this.busy.set(false);}
  }
  protected async upload(event:Event):Promise<void>{
    const input=event.target as HTMLInputElement;const file=input.files?.[0];if(!file)return;
    if(file.size>2*1024*1024){this.error.set('Choose an image up to 2 MB.');input.value='';return;}
    await this.choose('custom',file);input.value='';
  }
}
