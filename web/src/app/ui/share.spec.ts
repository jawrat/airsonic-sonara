import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SubsonicApi } from '../core/subsonic';
import { Share } from './share';

describe('Share controls',()=>{
  it('creates only on request, displays a copyable short link, and clears stale results on navigation',async()=>{
    const createShare=vi.fn(async(_ids:string[],_description:string,_expires:number)=>({id:'abcdefghijklmnop',url:'https://music.example/s/abcdefghijklmnop'}));
    TestBed.configureTestingModule({imports:[Share],providers:[provideRouter([]),{provide:SubsonicApi,useValue:{createShare}}]});
    const f=TestBed.createComponent(Share);f.componentRef.setInput('ids',['al-4']);f.componentRef.setInput('description','An album');f.detectChanges();
    expect(createShare).not.toHaveBeenCalled();f.nativeElement.querySelector('button').click();f.detectChanges();
    expect(f.nativeElement.textContent).toContain('without signing in');expect(createShare).not.toHaveBeenCalled();
    const before=Date.now();([...f.nativeElement.querySelectorAll('button')].find((b:HTMLButtonElement)=>b.textContent?.trim()==='Create share link') as HTMLButtonElement).click();
    await vi.waitFor(()=>{f.detectChanges();expect(f.nativeElement.querySelector('input')?.value).toContain('/s/abcdefghijklmnop');});
    expect(createShare.mock.calls[0][0]).toEqual(['al-4']);expect(createShare.mock.calls[0][1]).toBe('An album');
    expect(createShare.mock.calls[0][2]).toBeGreaterThanOrEqual(before+30*86400000);
    f.componentRef.setInput('ids',['al-5']);f.detectChanges();expect(f.nativeElement.querySelector('input')).toBeNull();
  });
  it('shows creation errors and permits a retry',async()=>{
    const createShare=vi.fn().mockRejectedValueOnce(new Error('No accessible tracks')).mockResolvedValue({id:'abcdefghijklmnop',url:'https://music.example/s/abcdefghijklmnop'});
    TestBed.configureTestingModule({imports:[Share],providers:[provideRouter([]),{provide:SubsonicApi,useValue:{createShare}}]});
    const f=TestBed.createComponent(Share);f.componentRef.setInput('ids',['dir-1-']);f.detectChanges();f.nativeElement.querySelector('button').click();f.detectChanges();
    const create=()=>[...f.nativeElement.querySelectorAll('button')].find((b:HTMLButtonElement)=>b.textContent?.trim()==='Create share link') as HTMLButtonElement;
    create().click();await vi.waitFor(()=>{f.detectChanges();expect(f.nativeElement.querySelector('[role=alert]')?.textContent).toContain('No accessible tracks');});
    create().click();await vi.waitFor(()=>{f.detectChanges();expect(f.nativeElement.querySelector('input')).not.toBeNull();});
  });
});
