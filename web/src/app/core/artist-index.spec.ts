import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth';
import { SubsonicApi } from './subsonic';

describe('Library artist index API', () => {
  it('requests the complete authorized index without a paging limit and preserves folder scope', async () => {
    TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});
    TestBed.inject(AuthService).session.set({username:'listener',token:'token',salt:'salt',isAdmin:false});
    const api=TestBed.inject(SubsonicApi),http=TestBed.inject(HttpTestingController);
    const result=api.getArtistIndex(7);
    const request=http.expectOne(r=>r.url==='/rest/getArtists');
    expect(request.request.params.get('musicFolderId')).toBe('7');expect(request.request.params.has('size')).toBe(false);
    request.flush({'subsonic-response':{status:'ok',artists:{ignoredArticles:'The',index:[{name:'G',artist:[{id:'22',name:'Grateful Dead',albumCount:3}]},{name:'T',artist:[{id:'11',name:'The Allman Brothers',albumCount:2}]}]}}});
    const groups=await result;expect(groups[0].name).toBe('A');expect(groups[0].artist[0].name).toBe('The Allman Brothers');expect(groups[1].artist[0].name).toBe('Grateful Dead');http.verify();
  });
});
