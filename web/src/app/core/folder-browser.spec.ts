import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth';
import { SubsonicApi } from './subsonic';

describe('Folder browser transport',()=>{
  it('loads the complete physical folder index independently of tag artists',async()=>{
    TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});TestBed.inject(AuthService).session.set({username:'listener',token:'t',salt:'s',isAdmin:false});
    const api=TestBed.inject(SubsonicApi),http=TestBed.inject(HttpTestingController),pending=api.getFolderIndex(7);
    const request=http.expectOne(r=>r.url==='/rest/getIndexes');expect(request.request.params.get('musicFolderId')).toBe('7');expect(request.request.params.has('size')).toBe(false);
    request.flush({'subsonic-response':{status:'ok',indexes:{index:[{artist:[{id:'dir-7-QQ',name:'Actual folder'}]}]}}});
    expect(await pending).toEqual([{id:'dir-7-QQ',title:'Actual folder',isDir:true,coverArt:'dir-7-QQ'}]);http.verify();
  });
});
