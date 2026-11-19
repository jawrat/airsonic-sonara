# Cover tiles and legacy-client artwork update

This is a source overlay for the Sonara version deployed from the initial jawrat/airsonic-sonara commit (07f8fcf), including the existing library layout. Extract it into the repository root. It replaces only the listed source/test files and adds this guide; it contains no credentials, deployment overrides, media, or database files.

## Changes

- Artists use cover tiles with names and album counts, while the left-hand artist index stays a compact list.
- Music roots and nested directories use matching clickable tiles. Existing folder navigation and paging remain.
- Artist and directory covers come from accessible descendant albums. Selection respects user folder permissions and the selected music folder.
- Missing or failed artwork displays a stable colored tile with title initials, or a music symbol when there is no title. This applies to library, artist, album, starred, podcast, and player covers.
- getArtists, getMusicFolders, getIndexes, getMusicDirectory, and legacy artist results now supply the corresponding cover IDs. getCoverArt accepts artist and encoded directory IDs as well as existing album/podcast IDs. Older folder-based clients can therefore discover and request these images.
- Folder/embedded artwork selection rejects obvious non-image data, skips invalid conventional cover files in favor of other candidates, and derives Content-Type from the bytes instead of trusting extensions or tag MIME labels.
- ffmpeg resize warnings now identify the cover ID.

No schema migration or new library scan is required.

## Apply in Windows

Use the development checkout you intend to publish under jawrat. If Visual Studio still uses the original separate checkout, apply the overlay there too before making further changes, so the copies do not drift.

From the repository root in PowerShell (adjust the downloaded ZIP path if necessary):

```powershell
Expand-Archive -LiteralPath "$HOME\Downloads\sonara-cover-tiles-update.zip" -DestinationPath . -Force

dotnet test ./tests\Sonara.Tests\Sonara.Tests.csproj
Push-Location .\web
npm ci
npm test -- --watch=false
npm run build -- --configuration production
Pop-Location

git diff --check
git diff --stat
```

Close/reload affected open files in Visual Studio if it prompts about external changes. The ZIP replaces complete files; review diffs before committing if you have edited these files since the initial deployment.

Commit and push the reviewed changes from the jawrat checkout using its existing repository-local identity. Include the new test files and docs/COVER-TILES-UPDATE.md. Do not stage .env or the Pi-specific override.

## Update the Pi after pushing

```bash
cd /home/ubuntu/airsonic-sonara
git pull --ff-only

docker compose -p sonara \
    -f install/compose/docker-compose.yaml \
    -f install/compose/docker-compose.pi.yaml \
    build sonara

docker compose -p sonara \
    -f install/compose/docker-compose.yaml \
    -f install/compose/docker-compose.pi.yaml \
    up -d --no-build --wait --wait-timeout 180 sonara
```

Keep both Compose files in these commands: the Pi override holds the music group, network, podcast mask, and build settings. Do not remove volumes.

Hard-refresh the web client. Check Artists, root Folders, nested folders, and albums without artwork. No database reset or rescan is needed.

## Retest iSub

Browse a folder containing an album whose artwork you can see in Sonara, then try playing it. Refresh its folder listing so iSub receives the newly supplied cover IDs. Missing cover IDs are a confirmed server-side gap fixed here; the actual phone has not been tested, so this does not establish that every iSub artwork failure has the same cause.

If artwork still fails, note whether the same album works in the browser and capture the new warning's cover ID (for example al-123). Do not paste authenticated cover URLs or passwords. An invalid-data ffmpeg warning concerns the image bytes, not the audio stream. Signature checks reject obvious junk but do not fully validate an image; genuinely damaged or unsupported covers may still fail. This update does not alter or repair media files.

## Verification

- Web: 56 tests passed; production Angular build passed.
- Server: 255 tests passed with temporary PostgreSQL 17; two unrelated backup/restore tests skipped because their optional tool-path environment variables were not configured.
- Release API publish passed.
- Integration coverage includes user/selected-folder isolation, hidden-folder and traversal rejection, representative artwork, and a legacy getCoverArt.view request with f=xml and size=160 returning a real ffmpeg-generated JPEG.

Representative lookup reads one candidate track per album, checks conventional images across those album directories, and limits embedded tag reads to the first 20 candidates. This avoids walking all audio tags for each tile on the Pi. It can use the colored fallback if the only usable embedded image is beyond that limit. Covers load lazily and resized images use the existing cache.

## Files

- src/Sonara.Api/Media/CoverArtFinder.cs
- src/Sonara.Api/Media/CoverArtResizer.cs
- src/Sonara.Api/Subsonic/SubsonicEndpoints.cs
- src/Sonara.Api/Subsonic/BrowsingEndpoints.cs
- tests/Sonara.Tests/CoverArtFinderTests.cs
- tests/Sonara.Tests/LibraryCoverTests.cs
- tests/Sonara.Tests/CoverImageFixture.cs
- web/src/app/core/models.ts
- web/src/app/core/subsonic.ts
- web/src/app/pages/library.ts
- web/src/app/pages/library.spec.ts
- web/src/app/pages/album.ts
- web/src/app/pages/artist.ts
- web/src/app/pages/starred.ts
- web/src/app/pages/podcast.ts
- web/src/app/pages/podcasts.ts
- web/src/app/ui/cover.ts
- web/src/app/ui/cover.spec.ts
- web/src/app/ui/player-bar.ts
