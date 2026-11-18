# Airsonic REST coverage

Reference: https://github.com/kagemomiji/airsonic-advanced/blob/main/airsonic-main/src/main/java/org/airsonic/player/controller/SubsonicRESTController.java

Inventory retrieved November 15, 2026: 74 operations. Every listed operation has GET/POST and `.view` routes; every authenticated call uses the standard JSON/XML envelope except binary media, HLS and OPML. There are deliberate semantic limitations below: this is endpoint coverage, not complete behavioral parity. DLNA/Sonos/Chromecast and external scrobbling remain deferred/excluded as previously agreed. No Java implementation code was ported.

| Operation | Behavior |
| --- | --- |
| `addChatMessage` | Implemented against the local library/database. |
| `changePassword` | Implemented against the local library/database. |
| `createBookmark` | Implemented against the local library/database. |
| `createPlaylist` | Implemented against the local library/database. |
| `createPodcastChannel` | Implemented against the local library/database. |
| `createShare` | Songs and al- album IDs; podcasts, arbitrary filesystem directory shares and downloads are not implemented. |
| `createUser` | Local authentication and implemented roles/folder access. LDAP, jukebox and video-conversion permissions are rejected when requested; upload permission is supported. |
| `deleteBookmark` | Implemented against the local library/database. |
| `deletePlaylist` | Implemented against the local library/database. |
| `deletePodcastChannel` | Implemented against the local library/database. |
| `deletePodcastEpisode` | Implemented against the local library/database. |
| `deleteShare` | Implemented against the local library/database. |
| `deleteUser` | Implemented against the local library/database. |
| `download` | Implemented against the local library/database. |
| `downloadPodcastEpisode` | Implemented against the local library/database. |
| `exportPodcasts/opml` | Implemented against the local library/database. |
| `getAlbum` | Implemented against the local library/database. |
| `getAlbumInfo` | Valid empty metadata result; no external provider. |
| `getAlbumInfo2` | Valid empty metadata result; no external provider. |
| `getAlbumList` | Implemented against the local library/database. |
| `getAlbumList2` | Implemented against the local library/database. |
| `getArtist` | Implemented against the local library/database. |
| `getArtistInfo` | Valid empty metadata/similar-artist result; no external provider. |
| `getArtistInfo2` | Valid empty metadata/similar-artist result; no external provider. |
| `getArtists` | Implemented against the local library/database. |
| `getAvatar` | Selected classic avatar or normalized custom PNG; default silhouette when unset. |
| `getBookmarks` | Implemented against the local library/database. |
| `getCaptions` | Explicit unsupported response (also unimplemented in the reference controller). |
| `getChatMessages` | Implemented against the local library/database. |
| `getCoverArt` | Implemented against the local library/database. |
| `getGenres` | Implemented against the local library/database. |
| `getIndexes` | Implemented against the local library/database. |
| `getInternetRadioStations` | Implemented against the local library/database. |
| `getLicense` | Implemented against the local library/database. |
| `getLyrics` | Local matching tracks: adjacent .lrc/.txt (maximum 1 MB), then embedded lyrics. No online lookup. |
| `getMusicDirectory` | Filesystem directories derived from scanned paths; also supports ar-/al- legacy tag-group IDs. |
| `getMusicFolders` | Implemented against the local library/database. |
| `getNewestPodcasts` | Implemented against the local library/database. |
| `getNowPlaying` | Implemented against the local library/database. |
| `getPlayQueue` | Implemented against the local library/database. |
| `getPlaylist` | Implemented against the local library/database. |
| `getPlaylists` | Implemented against the local library/database. |
| `getPodcasts` | Implemented against the local library/database. |
| `getRandomSongs` | Implemented against the local library/database. |
| `getScanStatus` | Implemented against the local library/database. |
| `getShares` | Implemented against the local library/database. |
| `getSimilarSongs` | Local artist/genre recommendation fallback, not Last.fm results. |
| `getSimilarSongs2` | Artist ID input; local artist/genre recommendation fallback, not Last.fm results. |
| `getSong` | Implemented against the local library/database. |
| `getSongsByGenre` | Implemented against the local library/database. |
| `getStarred` | Implemented against the local library/database. |
| `getStarred2` | Implemented against the local library/database. |
| `getTopSongs` | Ranks library songs by local play history, not Last.fm charts. |
| `getUser` | Implemented against the local library/database. |
| `getUsers` | Implemented against the local library/database. |
| `getVideoInfo` | Explicit unsupported response (also unimplemented in the reference controller). |
| `getVideos` | Empty collection: scanner indexes audio only. |
| `hls` | Audio AAC/TS HLS with authenticated segments and multiple bitrate variants; no video HLS. |
| `jukeboxControl` | Explicit unsupported response; jukebox is out of scope. |
| `ping` | Implemented against the local library/database. |
| `refreshPodcasts` | Implemented against the local library/database. |
| `savePlayQueue` | Implemented against the local library/database. |
| `scrobble` | Local history/now-playing only. External scrobbling remains intentionally excluded. |
| `search` | Legacy track-field filtering/pagination; no separate filesystem index. |
| `search2` | Legacy response shape and navigable ar-/al- tag-group IDs, using the tag catalog; not a separate filesystem-name search engine. |
| `search3` | Implemented against the local library/database. |
| `setRating` | Implemented against the local library/database. |
| `star` | Implemented against the local library/database. |
| `startScan` | Implemented against the local library/database. |
| `stream` | Implemented against the local library/database. |
| `unstar` | Implemented against the local library/database. |
| `updatePlaylist` | Implemented against the local library/database. |
| `updateShare` | Implemented against the local library/database. |
| `updateUser` | Local roles/folders/bitrate; upload permission supported; no LDAP/video/jukebox capability. |

## Sonara extensions

`searchPodcasts` (Podcast Index), `getOpenSubsonicExtensions`, `getSettings`, `updateSettings`, `getMusicFolderSettings`, `saveMusicFolder`, `deleteMusicFolder`, plus `allFolders`/`maxBitRate` user options. The Settings UI documents restart requirements. `deleteMusicFolder` disables rather than deleting files/records.

LDAP and device integrations remain future work. Video and jukebox are explicitly excluded from Sonara's scope. These are broader application features, not silently successful placeholder endpoints.

Legacy response IDs are opaque strings: songs remain numeric, artists use `ar-`, albums use `al-`, physical directories use `dir-<folder>-<base64url(relative-path)>`. Clients must use IDs returned by the relevant endpoint rather than construct them. Modern artist/album IDs remain numeric; podcast stream IDs remain `pe-`.

## Podcast management update

`importPodcastOpml`, `setPodcastEpisodePinned`, `getPodcastRetentionPreview` and `runPodcastRetention` are implemented Sonara extensions. OPML export now also has a web control. See [APPLY-PODCAST-UPDATE.md](history/APPLY-PODCAST-UPDATE.md) for retention protections, default-off settings and import behavior.

## Player/library update

Saved queues accept and return optional `currentIndex` for duplicate occurrences. The web client saves queues with form POST. `getArtists` accepts optional `size`, `offset` and `query`; `getMusicDirectory` accepts optional `size`/`offset` and returns `totalChildren`; default standard-client requests remain unpaged. `getGenres` honors `musicFolderId`. The web player reports local music listening through the existing `scrobble` API without any external service. See [APPLY-PLAYER-LIBRARY-UPDATE.md](history/APPLY-PLAYER-LIBRARY-UPDATE.md).

## Data portability update

`getBackups`, `createBackup`, and `downloadBackup` are administrator-only extensions. Restore is an offline CLI operation into an empty database, with a stopped-instance lock. `importPlaylistFile` previews by default and explicitly creates private playlists from accessible catalog matches; `exportPlaylistFile` exports visible, accessible entries as M3U8 or PLS. See [APPLY-DATA-PORTABILITY-UPDATE.md](history/APPLY-DATA-PORTABILITY-UPDATE.md) for backup scope and recovery instructions.

## Per-user transcoding update

`getUser`/`getUsers` include `preferredTranscodeFormat` and `preferredTranscodeBitRate`. The Sonara extension `setTranscodingPreferences` allows authenticated users to edit their own preferences and administrators to edit any user. It cannot change roles, folders or the administrator-set `maxBitRate`. Stream requests use preferences when corresponding client options are omitted; existing bitrate restrictions remain enforced. HLS remains AAC and uses the preferred bitrate when none is requested. See [APPLY-TRANSCODING-UPDATE.md](history/APPLY-TRANSCODING-UPDATE.md).

## Uploads, avatars and cleanup update

`getUploadFolders` and multipart POST `uploadMusic` require upload permission (admins bypass the user role). Enabled, accessible music folders must additionally opt in through `allowUploads`. POST `setAvatar` edits the caller or any account for administrators; `getAvatars` lists 46 bundled classic images. `getAvatar` serves selected/custom avatars and supports built-in previews through `avatarId`. Scanner count refresh removes only artists with no albums or tracks. See [APPLY-UPLOADS-AVATARS-UPDATE.md](history/APPLY-UPLOADS-AVATARS-UPDATE.md).

## Library sidebar update

`getAlbumList`/`getAlbumList2` accept optional positive `artistId`, filtering the existing folder-authorized query before ordering/pagination. The web library uses the complete authorized `getArtists` index for its grouped sidebar and labels the existing `newest` ordering Recently added. No database migration or default API behavior changes. See [APPLY-LIBRARY-LAYOUT-UPDATE.md](history/APPLY-LIBRARY-LAYOUT-UPDATE.md).
