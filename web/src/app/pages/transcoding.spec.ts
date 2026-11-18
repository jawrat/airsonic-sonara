import { TestBed } from '@angular/core/testing';
import { SubsonicApi } from '../core/subsonic';
import { TranscodingSettings } from './transcoding';

describe('Playback preferences', () => {
  it('loads the selected user and saves their preferences', async () => {
    const call = vi.fn(async () => ({user:{maxBitRate:128,adminRole:false,preferredTranscodeFormat:'opus',preferredTranscodeBitRate:96}}));
    const setTranscodingPreferences = vi.fn(async () => undefined);
    TestBed.configureTestingModule({imports:[TranscodingSettings],providers:[{provide:SubsonicApi,useValue:{call,setTranscodingPreferences}}]});
    const fixture = TestBed.createComponent(TranscodingSettings); fixture.componentRef.setInput('username','listener');fixture.detectChanges();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('select')).not.toBeNull(); });
    expect(call).toHaveBeenCalledWith('getUser',{username:'listener'});
    expect(fixture.nativeElement.querySelector('select').value).toBe('opus');
    const rate = fixture.nativeElement.querySelector('input');rate.value='128';rate.dispatchEvent(new Event('input'));fixture.detectChanges();
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit',{cancelable:true}));
    await vi.waitFor(() => expect(setTranscodingPreferences).toHaveBeenCalledWith('listener','opus',128));
  });
  it('reports load failures without showing editable defaults', async () => {
    TestBed.configureTestingModule({imports:[TranscodingSettings],providers:[{provide:SubsonicApi,useValue:{call:async () => {throw new Error('Access denied');}}}]});
    const fixture = TestBed.createComponent(TranscodingSettings);fixture.componentRef.setInput('username','other');fixture.detectChanges();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('Access denied'); });
    expect(fixture.nativeElement.textContent).toContain('Access denied');expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });
});
