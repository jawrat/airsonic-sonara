import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { PlayerService } from '../core/player';
import { SubsonicApi } from '../core/subsonic';
import { Library, readLibraryState } from './library';

const rootA='dir-7-QWxwaGE',rootZ='dir-7-WmVicmE',child='dir-7-QWxwaGEvTmV3';
const albumRows=(n:number)=>Array.from({length:n},(_,i)=>({id:String(i+1),name:'Album '+i,songCount:1,duration:1,artist:'Grateful Dead',artistId:'22',created:'2026-11-17T12:00:00Z'}));
function setup(initial:Record<string,string>={}) {
  const params=new BehaviorSubject(convertToParamMap(initial));
  const api={coverArtUrl:(id?:string)=>id?'/art/'+id:null,getFolders:vi.fn(async()=>[{id:7,name:'Music'}]),getGenres:vi.fn(async()=>[]),
    getFolderIndex:vi.fn(async()=>[{id:rootZ,title:'Zebra actual folder',isDir:true},{id:rootA,title:'Alpha actual folder',isDir:true}]),
    getFolderList:vi.fn(async()=>({name:'Music folders',child:[{id:child,title:'New show',parentName:'Alpha actual folder',isDir:true,created:'2026-11-19T12:00:00Z',coverArt:child}]})),
    getDirectory:vi.fn(async()=>({name:'Alpha actual folder',parent:'dir-7-',indexId:rootA,child:[{id:child,title:'New show',isDir:true,created:'2026-11-19T12:00:00Z'}]})),
    getAlbumList:vi.fn(async()=>albumRows(49)),getArtists:vi.fn(async()=>[{id:'22',name:'Grateful Dead',albumCount:1,coverArt:'ar-22'}]),
    getArtistIndex:vi.fn(async()=>[]),
  };
  TestBed.configureTestingModule({imports:[Library],providers:[provideRouter([]),{provide:ActivatedRoute,useValue:{queryParamMap:params,snapshot:{queryParamMap:params.value}}},{provide:SubsonicApi,useValue:api},{provide:PlayerService,useValue:{}}]});
  const fixture=TestBed.createComponent(Library);fixture.detectChanges();return{fixture,api,params};
}

describe('Physical folder home',()=>{
  it('defaults to folders, validates sorts and keeps legacy music-root links useful',()=>{
    expect(readLibraryState(convertToParamMap({})).view).toBe('folders');
    const s=readLibraryState(convertToParamMap({view:'bad',type:'bad',folderSort:'bad',offset:'-50',fromYear:'2020',toYear:'1990'}));
    expect(s.view).toBe('folders');expect(s.folderSort).toBe('newest');expect(s.offset).toBe(0);expect(s.fromYear).toBe(2020);expect(s.toYear).toBe(1990);
    for(const dir of ['7','dir-7-']){const root=readLibraryState(convertToParamMap({view:'folders',dir}));expect(root.dir).toBe('');expect(root.folderId).toBe(7);}
    expect(readLibraryState(convertToParamMap({view:'artistAlbums',artist:'../bad'})).view).toBe('albums');
  });
  it('uses actual alphabetical folder names on the left and recent subfolder tiles without a Music wrapper',async()=>{
    const{fixture,api}=setup();
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('.folder-grid .tile-button')).not.toBeNull();});
    const links=[...fixture.nativeElement.querySelectorAll('.folder-group a')] as HTMLAnchorElement[];
    expect(links.map(a=>a.textContent?.trim())).toEqual(['Alpha actual folder','Zebra actual folder']);
    expect(links[0].href).toContain('dir='+rootA);expect(api.getFolderIndex).toHaveBeenCalledWith(undefined);
    expect(api.getFolderList).toHaveBeenCalledWith(49,0,undefined,'newest');expect(api.getAlbumList).not.toHaveBeenCalled();expect(api.getArtistIndex).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('.folder-grid').textContent).toContain('New show');
    expect(fixture.nativeElement.querySelector('.folder-grid').textContent).toContain('Added');
    expect(fixture.nativeElement.querySelector('.folder-grid').textContent).not.toContain('Music');
    expect(fixture.nativeElement.querySelector('.folder-grid img').src).toContain(child);
  });
  it('filters the complete sidebar locally without refetching it',async()=>{
    const{fixture,api}=setup();await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelectorAll('.folder-group')).toHaveLength(2);});
    const input=fixture.nativeElement.querySelector('.folder-filter input') as HTMLInputElement;input.value='zebra';input.dispatchEvent(new Event('input'));fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.folder-group')).toHaveLength(1);expect(api.getFolderIndex).toHaveBeenCalledTimes(1);
  });
  it('drills into folders and returns Up directly to home while retaining the selected music folder',async()=>{
    const{fixture,api}=setup({view:'folders',dir:rootA,folder:'7'});
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('a[aria-current=page]')).not.toBeNull();});
    expect(api.getDirectory).toHaveBeenCalledWith(rootA,49,0,'newest');expect(api.getFolderIndex).toHaveBeenCalledWith(7);
    const navigate=vi.spyOn(TestBed.inject(Router),'navigate').mockResolvedValue(true);
    ([...fixture.nativeElement.querySelectorAll('button')].find((b:HTMLButtonElement)=>b.textContent?.trim()==='Up') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith(['/'],{queryParams:{view:'folders',dir:null,offset:0},queryParamsHandling:'merge'});
  });
  it('preserves scope and sort on paging without refetching the sidebar',async()=>{
    const{fixture,api,params}=setup({folder:'7'});
    await vi.waitFor(()=>{fixture.detectChanges();expect(api.getFolderList).toHaveBeenCalled();});
    params.next(convertToParamMap({folder:'7',folderSort:'alphabeticalByName',offset:'48'}));fixture.detectChanges();
    await vi.waitFor(()=>expect(api.getFolderList).toHaveBeenLastCalledWith(49,48,7,'alphabeticalByName'));
    expect(api.getFolderIndex).toHaveBeenCalledTimes(1);
  });
  it('keeps loose root-level songs playable without inventing a root directory tile',async()=>{
    const{fixture,api,params}=setup();
    api.getFolderList.mockResolvedValue({name:'Music folders',child:[{id:'5',title:'Loose song',isDir:false,duration:10}]} as never);
    params.next(convertToParamMap({offset:'48'}));fixture.detectChanges();
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('.tracks')?.textContent).toContain('Loose song');});
    expect(fixture.nativeElement.querySelector('.folder-grid .tile-button')).toBeNull();
  });
});

describe('Alternate album and artist views',()=>{
  it('keeps 48-album paging and artist links while the sidebar remains folders',async()=>{
    const{fixture,api,params}=setup({view:'albums',folder:'7',type:'alphabeticalByName'});
    await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelectorAll('.album-grid li.card')).toHaveLength(48);});
    expect(api.getAlbumList).toHaveBeenCalledWith('alphabeticalByName',49,0,{musicFolderId:7});
    const navigate=vi.spyOn(TestBed.inject(Router),'navigate').mockResolvedValue(true);
    ([...fixture.nativeElement.querySelectorAll('button')].find((b:HTMLButtonElement)=>b.textContent?.trim()==='Next page') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith(['/'],{queryParams:{offset:48},queryParamsHandling:'merge'});
    params.next(convertToParamMap({view:'artistAlbums',artist:'22',folder:'7',offset:'48'}));fixture.detectChanges();
    await vi.waitFor(()=>expect(api.getAlbumList).toHaveBeenLastCalledWith('newest',49,48,{musicFolderId:7,artistId:'22'}));
    expect(api.getFolderIndex).toHaveBeenCalledTimes(1);
  });
  it('returns the sidebar Home action to recently added folders',async()=>{
    const{fixture}=setup({view:'albums',folder:'7'});await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('.all-folders')).not.toBeNull();});
    const navigate=vi.spyOn(TestBed.inject(Router),'navigate').mockResolvedValue(true);fixture.nativeElement.querySelector('.all-folders').click();
    expect(navigate).toHaveBeenCalledWith(['/'],{queryParams:{view:'folders',folderSort:'newest',type:'newest',artist:null,q:null,offset:0,dir:null,genre:null},queryParamsHandling:'merge'});
  });
  it('retains artist tiles and tag-based album selection as an alternate tab',async()=>{
    const{fixture}=setup({view:'artists',folder:'7'});await vi.waitFor(()=>{fixture.detectChanges();expect(fixture.nativeElement.querySelector('.artist-grid img')).not.toBeNull();});
    expect(fixture.nativeElement.querySelector('.artist-grid img').src).toContain('ar-22');
    expect(fixture.nativeElement.querySelector('.artist-grid a').href).toContain('artist=22');
    expect(fixture.nativeElement.querySelector('.artist-grid a').href).toContain('folder=7');
  });
});
