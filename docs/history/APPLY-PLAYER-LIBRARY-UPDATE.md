# Sonara player, queue and library browsing update

Apply this overlay to your current project after the podcast-selection fixes. Extract its contents into the directory containing `Sonara.slnx`, overwriting matching files. Rebuild/restart through your existing workflow.

The included `AddPlayQueueCurrentIndex` migration adds one nullable column to `PlayQueues`, preserving existing queues/users/library records. Startup applies it automatically; do not generate another migration. Back up PostgreSQL and `DataDir/secret.key` before migrating and keep the existing Docker volumes.

```powershell
Expand-Archive -Path "$HOME\Downloads\sonara-player-library-overlay.zip" -DestinationPath . -Force
dotnet build
dotnet test
Push-Location web
npm ci
npm run build
npm test -- --watch=false
Pop-Location
```

## Playback and queue

- Shuffle upcoming tracks, repeat off/all/one, volume and mute are available in the player bar. Volume/mute/shuffle/repeat preferences persist in this browser. They are not server-side account preferences.
- Shuffle preserves the current track and already-played rows. Turning it off restores the original order of upcoming rows. Manual reordering turns shuffle off and preserves the visible order. Repeat-all reshuffles on a wrap when shuffle is enabled.
- Repeat-one repeats on a natural ending; the Next button still advances intentionally. Repeat-all wraps at the end. With repeat off, playback stops at the end of the queue.
- Play queue is in the sidebar and linked from the player bar. Jump to a row, move rows up/down, drag to reorder, remove rows, clear upcoming rows or clear the entire queue. Move buttons provide a keyboard/touch alternative to dragging.
- Reordering/removing a different row preserves the exact current occurrence and playback position, including queues containing duplicate songs. Removing the current row selects its successor, or the previous row if it was last, while preserving playing/paused state.
- Next inserts after the current row. Queue appends without interrupting playback. Album/playlist/favorites collections and downloaded podcast episodes can be added to the existing queue. Starting Play all still replaces the queue intentionally. Adding to an empty queue prepares it paused.
- Queues remain capped at the existing server limit of 5,000 entries. Saved queues now include a `currentIndex` extension so another device can restore the correct duplicate occurrence. Older clients can keep using the standard `current` ID.
- Queue saving uses form POST rather than a potentially very large query string. Continuous time updates no longer postpone saving indefinitely. Clearing the queue saves an empty queue; signing out stops local playback while retaining the last saved queue for a later session. Sync remains best effort, with failures shown on the queue page.
- Playback failures show a retry message. Resume positions are preserved even when metadata loads slowly. Restored queues stay paused.

## Local listening history

The Angular player now reports music playback to Sonara's existing local history/now-playing API. A play is recorded once per loaded occurrence after listening to half the track or four minutes, whichever is shorter. Seeking forward does not itself earn listening credit. Pause/resume does not generate a second play. Now-playing reports are refreshed while listening.

This uses local Sonara data only and does not call Last.fm or any external scrobbling service. Recently played and Most played album lists can now populate from web-client listening. Podcast episodes continue to use saved queues/bookmarks for position; they are not submitted to the music-only play-history endpoint. Playback reporting is best effort and does not block audio when reporting fails.

## Library browsing

- Albums, Artists, Genres and Folders views replace the limited initial gallery.
- Pages show 48 items; requests fetch one extra item to determine whether a next page exists. Search pages advance artist/album/song result offsets together, with up to 48 of each category per page. Genres can be browsed as albums or songs.
- All album-list modes are exposed: Newest, album A–Z, artist A–Z, Random, Recently played, Most played, Highest rated, Favorites, By year and By genre. Reversed year ranges are supported.
- Random is a refreshable sample rather than unstable random pagination. Highest rated uses existing local ratings; this batch does not add a new rating editor.
- The music-folder filter remains constrained to folders the signed-in user can access. Artist/album listing counts respect the selected folder. Album/artist detail pages continue to show the caller's authorized catalog for that entity.
- Folder browsing follows registered/scanned paths and supplies an Up button. Directory paging returns only the requested entries' song metadata; the server still inspects compact path/title rows to assemble immediate subdirectories.
- Browsing state is in URL query parameters, preserving filters/pages when returning with browser Back. Album/artist links continue using existing detail pages.
- Play displayed tracks / Queue displayed tracks act on the currently displayed songs, not unseen pages.

For other clients, standard `getArtists` and `getMusicDirectory` calls remain unpaged unless optional `size`/`offset` parameters are supplied. `getArtists` also accepts the optional `query` filter; `getGenres` respects optional `musicFolderId`. Directory responses include `totalChildren` as a Sonara extension.

## Verification

- Backend build and 225 backend tests, including PostgreSQL integration/migration tests: passed, with no skips in the verified run.
- Angular production build and 37 frontend tests: passed.
- New tests cover duplicate queue occurrences, stable current playback during edits, shuffle/repeat behavior, persistent volume/preferences, continuous saving, explicit clearing versus sign-out, restore races, pending seek positions, local listening credit, library URL validation/paging and folder-scoped backend lists/directories.
- Browser rendering has not been verified here; the earlier Chromium Unix-socket restriction remains. Please check the expanded player bar and queue on your desktop/mobile browser after applying.

This batch implements the suggested player/queue and library-browsing work. Backup/restore, music uploads, playlist-file import/export, and the remaining API-only feature screens are separate backlog items.
