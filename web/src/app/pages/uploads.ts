import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SubsonicApi } from '../core/subsonic';

@Component({
  selector:'app-uploads',imports:[FormsModule],
  template:`
    <h1>Upload music</h1>
    <p class="muted">Upload audio to a folder enabled by your administrator. Existing files are never overwritten. Successful uploads are scanned into the library automatically.</p>
    @if (loading()) { <p>Loading destinations…</p> }
    @else if (folders().length) {
      <form class="stack" (ngSubmit)="upload()">
        <label>Destination <select name="folder" [(ngModel)]="folder" [disabled]="busy()">@for(f of folders();track f.id){<option [ngValue]="f.id">{{ f.name }}</option>}</select></label>
        <label>Subfolder (optional, for example Artist/Album) <input name="subfolder" [(ngModel)]="subfolder" [disabled]="busy()" maxlength="500" /></label>
        <label>Audio files (up to {{ maxSize() }} MB each) <input #picker type="file" multiple accept=".mp3,.flac,.ogg,.oga,.opus,.m4a,.aac,.wav,.wma" [disabled]="busy()" (change)="select($event)" /></label>
        <button type="submit" [disabled]="busy() || !files.length">{{ busy() ? 'Uploading…' : 'Upload selected files' }}</button>
      </form>
    } @else if (!error()) { <p>No upload destinations are enabled for your account. Ask an administrator to enable a writable music folder and grant access.</p> }
    @if(error()){<p class="error" role="alert">{{error()}}</p>}
    <ul aria-live="polite">@for(result of results();track $index){<li>{{result}}</li>}</ul>
  `,
  styles:`.stack{display:grid;gap:1rem;max-width:36rem}label{display:grid;gap:.35rem}`,
})
export class UploadsPage {
  private readonly api=inject(SubsonicApi);
  protected readonly folders=signal<{id:number;name:string}[]>([]);
  protected readonly loading=signal(true);
  protected readonly maxSize=signal(100);
  protected readonly busy=signal(false);
  protected readonly error=signal('');
  protected readonly results=signal<string[]>([]);
  protected files:File[]=[];
  protected folder=0;
  protected subfolder='';
  constructor(){void this.api.call<{uploadFolders:{folder:{id:number;name:string}[];maxFileMegabytes:number}}>('getUploadFolders').then(r=>{this.folders.set(r.uploadFolders.folder);this.maxSize.set(r.uploadFolders.maxFileMegabytes);this.folder=r.uploadFolders.folder[0]?.id??0;}).catch(e=>this.error.set(e instanceof Error?e.message:'Could not load upload destinations.')).finally(()=>this.loading.set(false));}
  protected select(event:Event):void{this.files=Array.from((event.target as HTMLInputElement).files??[]);this.results.set([]);}
  protected async upload():Promise<void>{
    if(this.busy()||!this.folder)return;
    this.busy.set(true);this.error.set('');this.results.set([]);
    const files=[...this.files];const retry:File[]=[];
    for(const file of files){
      try{if(file.size>this.maxSize()*1024*1024)throw new Error('File exceeds the upload limit.');await this.api.uploadMusic(this.folder,this.subfolder,file);this.results.update(r=>[...r,file.name+' — uploaded; scan requested.']);}
      catch(e){retry.push(file);this.results.update(r=>[...r,file.name+' — '+(e instanceof Error?e.message:'Upload failed.')]);}
    }
    this.files=retry;this.busy.set(false);
  }
}
