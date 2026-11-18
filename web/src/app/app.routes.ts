import { UploadsPage } from './pages/uploads';
import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './core/auth.guard';
import { AlbumPage } from './pages/album';
import { ArtistPage } from './pages/artist';
import { BookmarksPage } from './pages/bookmarks';
import { AccountPage } from './pages/account';
import { BackupsPage } from './pages/backups';
import { QueuePage } from './pages/queue';
import { Library } from './pages/library';
import { PodcastPage } from './pages/podcast';
import { PodcastsPage } from './pages/podcasts';
import { PlaylistPage } from './pages/playlist';
import { PlaylistsPage } from './pages/playlists';
import { StarredPage } from './pages/starred';
import { UsersPage } from './pages/users';
import { SettingsPage } from './pages/settings';
import { Login } from './pages/login';

export const routes: Routes = [
  { path: 'login', component: Login },
  { path: '', component: Library, canActivate: [authGuard] },
  { path: 'queue', component: QueuePage, canActivate: [authGuard] },
  { path: 'album/:id', component: AlbumPage, canActivate: [authGuard] },
  { path: 'artist/:id', component: ArtistPage, canActivate: [authGuard] },
  { path: 'playlists', component: PlaylistsPage, canActivate: [authGuard] },
  { path: 'playlist/:id', component: PlaylistPage, canActivate: [authGuard] },
  { path: 'starred', component: StarredPage, canActivate: [authGuard] },
  { path: 'bookmarks', component: BookmarksPage, canActivate: [authGuard] },
  { path: 'podcasts', component: PodcastsPage, canActivate: [authGuard] },
  { path: 'podcast/:id', component: PodcastPage, canActivate: [authGuard] },
  { path: 'uploads', component: UploadsPage, canActivate: [authGuard] },
  { path: 'account', component: AccountPage, canActivate: [authGuard] },
  { path: 'backups', component: BackupsPage, canActivate: [adminGuard] },
  { path: 'settings', component: SettingsPage, canActivate: [adminGuard] },
  { path: 'users', component: UsersPage, canActivate: [adminGuard] },
  { path: '**', redirectTo: '' },
];
