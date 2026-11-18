import { TestBed } from '@angular/core/testing';
import { SubsonicApi } from '../core/subsonic';
import { UploadsPage } from './uploads';
import { AvatarSettings } from './avatars';
import { SettingsPage } from './settings';

describe('Uploads and avatars',()=>{
  it('uploads files sequentially, preserves per-file results and leaves failures available for retry',async()=>{
    const uploadMusic=vi.fn(async(_folder:number,_sub:string,file:File)=>{if(file.name==='bad.mp3')throw new Error('Invalid audio');});
    TestBed.configureTestingModule({imports:[UploadsPage],providers:[{provide:SubsonicApi,useValue:{call:async()=>({uploadFolders:{folder:[{id:7,name:'Incoming'}],maxFileMegabytes:100}}),uploadMusic}}]});
    const fixture=TestBed.createComponent(UploadsPage);fixture.detectChanges();
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('input[type=file]')).not.toBeNull();});
    const input=fixture.nativeElement.querySelector('input[type=file]');
    const good=new File(['audio'],'good.mp3'),bad=new File(['bad'],'bad.mp3');Object.defineProperty(input,'files',{value:[good,bad]});input.dispatchEvent(new Event('change'));fixture.detectChanges();
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit',{cancelable:true}));
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('bad.mp3 — Invalid audio');});
    expect(uploadMusic).toHaveBeenNthCalledWith(1,7,'',good);expect(uploadMusic).toHaveBeenNthCalledWith(2,7,'',bad);
    expect(fixture.nativeElement.textContent).toContain('good.mp3 — uploaded');
  });
  it('saves a built-in avatar for the selected account',async()=>{
    const setAvatar=vi.fn(async()=>undefined);
    TestBed.configureTestingModule({imports:[AvatarSettings],providers:[{provide:SubsonicApi,useValue:{call:async(op:string)=>op==='getAvatars'?{avatars:{avatar:[{id:'Vinyl',name:'Vinyl'}]}}:{user:{avatarId:''}},setAvatar,avatarUrl:()=>'/avatar',builtinAvatarUrl:()=>'/vinyl'}}]});
    const fixture=TestBed.createComponent(AvatarSettings);fixture.componentRef.setInput('username','listener');fixture.detectChanges();
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('button[aria-label=Vinyl]')).not.toBeNull();});
    fixture.nativeElement.querySelector('button[aria-label=Vinyl]').click();
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.textContent).toContain('Avatar saved');});
    expect(setAvatar).toHaveBeenCalledWith('listener','Vinyl',undefined);
  });
  it('sends the administrator upload opt-in when saving folder settings',async()=>{
    const call=vi.fn(async(op:string)=>op==='getSettings'?{settings:{setting:[]}}:op==='getMusicFolderSettings'?{musicFolderSettings:{folder:[{id:7,name:'Incoming',path:'/var/uploads',enabled:true,allowUploads:false}]}}:{});
    TestBed.configureTestingModule({imports:[SettingsPage],providers:[{provide:SubsonicApi,useValue:{call}}]});
    const fixture=TestBed.createComponent(SettingsPage);fixture.detectChanges();
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('input[name=allowUploads]')).not.toBeNull();});
    const checkbox=fixture.nativeElement.querySelector('input[name=allowUploads]');checkbox.checked=true;checkbox.dispatchEvent(new Event('change'));fixture.detectChanges();
    fixture.nativeElement.querySelector('form.folder').dispatchEvent(new Event('submit',{cancelable:true}));
    await vi.waitFor(()=>expect(call).toHaveBeenCalledWith('saveMusicFolder',{id:7,name:'Incoming',path:'/var/uploads',enabled:true,allowUploads:true}));
  });
});
