import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';
import { QueuePage } from './queue';

const song = (id: string, key: number) => ({id,queueKey:key,title:'Track '+id,duration:10});
describe('Queue page controls', () => {
  function setup() {
    const queue = signal([song('2',1),song('1',2),song('2',3)]);
    const player = {queue,index:signal(0),syncError:signal(''),shuffle:signal(false),repeat:signal('off'),clearQueue:vi.fn(()=>queue.set([]))};
    const createPlaylist = vi.fn(async (name:string,ids:string[])=>({id:'9',name,songCount:ids.length,entry:[]}));
    TestBed.configureTestingModule({imports:[QueuePage],providers:[provideRouter([]),{provide:PlayerService,useValue:player},{provide:SubsonicApi,useValue:{createPlaylist}}]});
    const fixture=TestBed.createComponent(QueuePage);fixture.detectChanges();
    const click=(text:string)=>{const button=[...fixture.nativeElement.querySelectorAll('button')].find((b:HTMLButtonElement)=>b.textContent?.trim()===text) as HTMLButtonElement;expect(button).toBeDefined();button.click();fixture.detectChanges();};
    return {fixture,player,createPlaylist,click};
  }
  it('uses an inline confirmation, can cancel, and clears without a browser dialog',()=>{
    const {fixture,player,click}=setup();const confirm=vi.spyOn(window,'confirm');
    click('Clear queue');expect(player.clearQueue).not.toHaveBeenCalled();click('Cancel');expect(player.queue()).toHaveLength(3);
    click('Clear queue');click('Clear all tracks');expect(player.clearQueue).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('No tracks queued');expect(confirm).not.toHaveBeenCalled();
  });
  it('creates a playlist in displayed order with duplicates and leaves the queue alone',async()=>{
    const {fixture,player,createPlaylist,click}=setup();click('Create playlist from queue');await fixture.whenStable();
    const input=fixture.nativeElement.querySelector('input') as HTMLInputElement;input.value='Live Dead';input.dispatchEvent(new Event('input'));await fixture.whenStable();
    fixture.detectChanges();fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit',{cancelable:true}));
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('a[href="/playlist/9"]')).not.toBeNull();});
    expect(createPlaylist).toHaveBeenCalledWith('Live Dead',['2','1','2']);expect(player.queue()).toHaveLength(3);
  });
  it('does not silently discard podcast episodes when saving a playlist',async()=>{
    const {fixture,player,createPlaylist,click}=setup();player.queue.set([song('pe-3',1)]);fixture.detectChanges();click('Create playlist from queue');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit',{cancelable:true}));fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('podcast episodes');expect(createPlaylist).not.toHaveBeenCalled();
  });
});
