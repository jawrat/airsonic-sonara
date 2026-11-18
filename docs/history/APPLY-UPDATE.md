# Apply the Sonara API and LCARS update

This is a source overlay for the uploaded Sonara 1.0.0 project. Extract its contents into the directory containing `Sonara.slnx`, overwriting the matching source files. It contains no `.env`, database, encryption key, IDE state, `bin`, `obj`, or `node_modules` files.

## Before restarting

Stop the running Sonara instance. Back up your PostgreSQL database and `DataDir/secret.key` together. Keep your existing `.env`, music mounts and connection string. The included EF migration adds tables and user permissions; startup applies it automatically. **Do not generate an additional migration for this update.**

PowerShell, from the project root (adjust the ZIP location):

```powershell
Expand-Archive -Path "$HOME\Downloads\sonara-api-parity-overlay.zip" -DestinationPath . -Force
dotnet build
dotnet test
Push-Location web
npm ci
npm run build
npm test -- --watch=false
Pop-Location
```

For your existing Docker deployment, rebuild through your usual Compose command so the new Angular build is copied into the runtime image. For the default compose configuration:

```powershell
Push-Location install/compose
docker compose -p sonara up --build -d
Pop-Location
```

For local development, restart the API and Angular dev server as usual. The README's local web command should be run from the project root's `web` directory.

## What changed

- LCARS brand block: bottom-left rounding; Status block: top-left rounding. Mobile layout remains responsive.
- All routes in the referenced Airsonic controller are accounted for; see `docs/API-COVERAGE.md` for supported behavior and explicit limitations. Route presence is not a claim of complete Airsonic feature parity.
- Database-backed server Settings page and music-folder management, plus user permissions and folder assignments in Users.
- Shared database query filters restrict songs, albums, artists, covers, streams, playlists, bookmarks and queues to enabled, authorized folders.
- GET and form POST, including repeated parameters and `.view` aliases. Query parameters take precedence over form parameters with the same name.
- Local ratings, play history, now-playing, lyrics, radio/chat, OPML export, public shares and audio HLS.
- Existing users retain their previous listening permissions. Podcast management and sharing stay off by default for ordinary users. Administrators bypass role restrictions, but disabled folders stay hidden from normal library browsing.

Server settings are non-secret, allowlisted database overrides. Saved changes take effect on restart and override the corresponding environment/configuration values; the UI shows the running value when a change is pending. Credentials, database connection strings and encryption keys remain outside the Settings API. Set **Public URL for share links** for your external hostname, particularly behind a reverse proxy. Blank uses the request's scheme/host.

Music-folder changes affect access immediately; rescan to discover media and restart to refresh file watchers. Disabling/removing a folder through the API preserves files and library records. A populated folder's path cannot be edited in place: disable it and register the new path instead.

Stream bitrate limits apply to ordinary users, including public shares and podcasts. They do not restrict a separately granted download-originals permission. Supported limits are 32–512 kbps (0 = unlimited); the transcoder itself caps encoded output at 320 kbps. HLS caches AAC segments privately under `DataDir/cache/hls`; segments require current authentication and folder access. First access waits for conversion; cache entries idle for over 24 hours are removed when subsequent conversions occur.

## Verification performed

- .NET 10 build: succeeded with zero warnings/errors.
- Backend: 193 passing tests, including 11 PostgreSQL integration/upgrade tests, with no skips in the verified run.
- Angular production build and 20 frontend tests: passed.
- PostgreSQL integration tests cover form POST/XML/JSON, scoped browsing/search, ratings/history, roles, share membership/expiration/revocation, original-file ranges, HLS segment authorization, settings, radio/chat, legacy directory IDs and migration defaults.
- A headless browser check could not run because this execution environment blocks Chromium's Unix sockets. The corner has been corrected in CSS; please inspect it in your local browser.

Ordinary `dotnet test` runs the unit tests and skips the database integration tests unless `SONARA_TEST_DATABASE` is set. To run them, provide a PostgreSQL connection string for a test server whose user can create/drop databases. Each test fixture creates a uniquely named database and drops only that database afterward. FFmpeg must be available for the HLS integration test. Never use production credentials for this test server.

After startup, check Library, Settings, Users → Permissions, and your usual Subsonic client. Settings and permissions can be reviewed before changing anything. The migration is additive; returning to older binaries would leave the new tables/columns in place.
