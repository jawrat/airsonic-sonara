import { TestBed } from '@angular/core/testing';
import { SubsonicApi } from '../core/subsonic';
import { SharesPage } from './shares';

describe('Shared link management',()=>{
  it('requires inline confirmation and refreshes the list after revoking',async()=>{
    const share={id:'abcdefghijklmnop',url:'https://music.example/s/abcdefghijklmnop',description:'Live mix',username:'listener',visitCount:2,created:'2026-11-19T00:00:00Z',entry:[]};
    const getShares=vi.fn().mockResolvedValueOnce([share]).mockResolvedValue([]),deleteShare=vi.fn(async()=>undefined);
    TestBed.configureTestingModule({imports:[SharesPage],providers:[{provide:SubsonicApi,useValue:{getShares,deleteShare}}]});
    const f=TestBed.createComponent(SharesPage);f.detectChanges();
    const click=(text:string)=>{const b=[...f.nativeElement.querySelectorAll('button')].find((b:HTMLButtonElement)=>b.textContent?.trim()===text) as HTMLButtonElement;b.click();f.detectChanges();};
    await vi.waitFor(()=>{f.detectChanges();expect(f.nativeElement.querySelector('a')?.href).toBe(share.url);});
    click('Revoke');expect(deleteShare).not.toHaveBeenCalled();click('Cancel');expect(deleteShare).not.toHaveBeenCalled();
    click('Revoke');click('Revoke link');
    await vi.waitFor(()=>{f.detectChanges();expect(f.nativeElement.textContent).toContain('No shared links yet');});
    expect(deleteShare).toHaveBeenCalledWith(share.id);expect(getShares).toHaveBeenCalledTimes(2);
  });
});
