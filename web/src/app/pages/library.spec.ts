import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';
import { Library, readLibraryState } from './library';

const albums = (n: number) => Array.from({ length: n }, (_, i) => ({ id: String(i), name: 'Album ' + i, songCount: 1, duration: 1 }));

describe('Library navigation and paging', () => {
  it('validates URL filters while preserving descending year ranges', () => {
    const s = readLibraryState(convertToParamMap({ view: 'bad', type: 'bad', offset: '-50', fromYear: '2020', toYear: '1990' }));
    expect(s.view).toBe('albums'); expect(s.type).toBe('newest'); expect(s.offset).toBe(0); expect(s.fromYear).toBe(2020); expect(s.toYear).toBe(1990);
  });
  it('requests page-size plus one, shows 48 rows, and advances with the folder filter intact', async () => {
    const params = new BehaviorSubject(convertToParamMap({ folder: '7', type: 'alphabeticalByName' }));
    const api = { coverArtUrl: () => null, getFolders: vi.fn(async () => [{ id: 7, name: 'Music' }]), getArtistIndex: vi.fn(async () => []), getGenres: vi.fn(async () => []), getAlbumList: vi.fn(async () => albums(49)) };
    TestBed.configureTestingModule({ imports: [Library], providers: [provideRouter([]),
      { provide: ActivatedRoute, useValue: { queryParamMap: params, snapshot: { queryParamMap: params.value } } },
      { provide: SubsonicApi, useValue: api }, { provide: PlayerService, useValue: {} },
    ] });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(Library); fixture.detectChanges();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelectorAll('li.card')).toHaveLength(48); });
    expect(api.getAlbumList).toHaveBeenCalledWith('alphabeticalByName', 49, 0, { musicFolderId: 7 });
    const next = [...fixture.nativeElement.querySelectorAll('button')].find((b: Element) => b.textContent?.trim() === 'Next page') as HTMLButtonElement;
    next.click(); expect(navigate).toHaveBeenCalledWith(['/'], { queryParams: { offset: 48 }, queryParamsHandling: 'merge' });
    params.next(convertToParamMap({ folder: '7', type: 'alphabeticalByName', offset: '48' })); fixture.detectChanges();
    await vi.waitFor(() => expect(api.getAlbumList).toHaveBeenLastCalledWith('alphabeticalByName', 49, 48, { musicFolderId: 7 }));
  });
});

describe('Artist sidebar and album home', () => {
  const index = [{ name: 'A', artist: [{id:'11',name:'Allman Brothers',albumCount:2}] },{name:'G',artist:[{id:'22',name:'Grateful Dead',albumCount:3}]}];
  function setup(initial:Record<string,string> = {}) {
    const params=new BehaviorSubject(convertToParamMap(initial));
    const api={coverArtUrl:()=>null,getFolders:vi.fn(async()=>[{id:7,name:'Music'}]),getGenres:vi.fn(async()=>[]),getArtistIndex:vi.fn(async()=>index),getAlbumList:vi.fn(async()=>[{...albums(1)[0],artist:'Grateful Dead',artistId:'22',created:'2026-11-17T12:00:00Z'}])};
    TestBed.configureTestingModule({imports:[Library],providers:[provideRouter([]),{provide:ActivatedRoute,useValue:{queryParamMap:params,snapshot:{queryParamMap:params.value}}},{provide:SubsonicApi,useValue:api},{provide:PlayerService,useValue:{}}]});
    const fixture=TestBed.createComponent(Library);fixture.detectChanges();return {fixture,params,api};
  }
  it('defaults to recently added with a complete grouped artist list and album dates',async()=>{
    const {fixture,api}=setup();
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelectorAll('.artist-group')).toHaveLength(2);expect(fixture.nativeElement.querySelector('.album-range')).not.toBeNull();});
    expect(api.getArtistIndex).toHaveBeenCalledWith(undefined);expect(api.getAlbumList).toHaveBeenCalledWith('newest',49,0,{musicFolderId:undefined});
    expect(fixture.nativeElement.querySelector('.album-range').textContent).toContain('Recently added');
    expect(fixture.nativeElement.querySelector('.card').textContent).toContain('Added');
    const link=fixture.nativeElement.querySelector('.artist-group:last-child a') as HTMLAnchorElement;
    expect(link.href).toContain('view=artistAlbums');expect(link.href).toContain('artist=22');
    const filter=fixture.nativeElement.querySelector('.artist-filter input') as HTMLInputElement;filter.value='dead';filter.dispatchEvent(new Event('input'));fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.artist-group')).toHaveLength(1);expect(api.getArtistIndex).toHaveBeenCalledTimes(1);
  });
  it('shows selected artist albums inline, retains the folder and does not refetch the index on paging',async()=>{
    const {fixture,api,params}=setup({view:'artistAlbums',artist:'22',folder:'7'});
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('h1').textContent).toBe('Grateful Dead');});
    expect(api.getAlbumList).toHaveBeenCalledWith('newest',49,0,{musicFolderId:7,artistId:'22'});
    expect(fixture.nativeElement.querySelector('a[aria-current=page]').textContent).toContain('Grateful Dead');
    params.next(convertToParamMap({view:'artistAlbums',artist:'22',folder:'7',offset:'48',type:'alphabeticalByName'}));fixture.detectChanges();
    await vi.waitFor(()=>expect(api.getAlbumList).toHaveBeenLastCalledWith('alphabeticalByName',49,48,{musicFolderId:7,artistId:'22'}));
    expect(api.getArtistIndex).toHaveBeenCalledTimes(1);
    const navigate=vi.spyOn(TestBed.inject(Router),'navigate').mockResolvedValue(true);
    (fixture.nativeElement.querySelector('.all-albums') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith(['/'],{queryParams:{view:'albums',type:'newest',artist:null,q:null,offset:0,dir:null,genre:null},queryParamsHandling:'merge'});
  });
  it('rejects an artist view without a valid artist identifier',()=>{
    expect(readLibraryState(convertToParamMap({view:'artistAlbums',artist:'../bad'})).view).toBe('albums');
  });
});
