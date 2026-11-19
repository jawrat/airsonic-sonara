import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth';
import { SubsonicApi } from './subsonic';

describe('Password administration transport',()=>{
  it('keeps new passwords in POST bodies for creation, changes and resets',async()=>{
    TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});
    TestBed.inject(AuthService).session.set({username:'admin',token:'token',salt:'salt',isAdmin:true});
    const api=TestBed.inject(SubsonicApi),http=TestBed.inject(HttpTestingController);
    const actions=[['createUser',()=>api.createUser('listener','Example-test-password!',false)],['updateUser',()=>api.updateUser('listener',{password:'Example-test-password!'})],['changePassword',()=>api.changePassword('admin','Example-test-password!')]] as const;
    for(const [operation,action] of actions){const pending=action();const request=http.expectOne('/rest/'+operation);expect(request.request.method).toBe('POST');expect(request.request.urlWithParams).not.toContain('password');expect(new URLSearchParams(request.request.body).get('password')).toMatch(/^enc:/);request.flush({'subsonic-response':{status:'ok'}});await pending;}
    http.verify();
  });
});
