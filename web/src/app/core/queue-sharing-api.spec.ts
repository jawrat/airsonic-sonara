import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth';
import { SubsonicApi } from './subsonic';

describe('Queue playlist and sharing API transport',()=>{
  it('posts long playlists with repeated track IDs rather than building an oversized URL',async()=>{
    TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});
    TestBed.inject(AuthService).session.set({username:'listener',token:'t',salt:'s',isAdmin:false});
    const api=TestBed.inject(SubsonicApi),http=TestBed.inject(HttpTestingController),ids=Array(1200).fill('12');
    const pending=api.createPlaylist('Long queue',ids);const req=http.expectOne('/rest/createPlaylist');
    expect(req.request.method).toBe('POST');expect(new URLSearchParams(req.request.body).getAll('songId')).toEqual(ids);
    req.flush({'subsonic-response':{status:'ok',playlist:{id:'1',name:'Long queue',entry:[]}}});await pending;http.verify();
  });
  it('posts an explicit empty queue without track IDs',async()=>{
    TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});
    TestBed.inject(AuthService).session.set({username:'listener',token:'t',salt:'s',isAdmin:false});
    const api=TestBed.inject(SubsonicApi),http=TestBed.inject(HttpTestingController);
    const pending=api.savePlayQueue([],undefined,0);const req=http.expectOne('/rest/savePlayQueue');
    expect(req.request.method).toBe('POST');const form=new URLSearchParams(req.request.body);expect(form.has('id')).toBe(false);expect(form.get('position')).toBe('0');
    req.flush({'subsonic-response':{status:'ok'}});await pending;http.verify();
  });
});
