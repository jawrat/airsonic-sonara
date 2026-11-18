# Operations

This guide describes the current application. Historical overlay notes are retained under [history](history/README.md) for reference, not as the current installation procedure.

## Compose configuration

Use `install/compose/docker-compose.yaml`. The obsolete PostgreSQL/SQLite variants are no longer part of the supported setup; Sonara uses PostgreSQL only. `docker-compose.dev.yaml` is an optional localhost database-port override for native development.

The main Compose file reads `install/compose/.env`. Keep that file private; commit only the sanitized `.env.example`.

| Variable | Purpose |
| --- | --- |
| `MUSIC_DIR` | Required absolute host music-library directory; mounted read-only |
| `POSTGRES_PASSWORD` | Required database password; letters/digits for the supplied connection-string interpolation |
| `SONARA_PORT` | Published host HTTP port; default 4040. The Compose mapping is `${SONARA_PORT:-4040}:4040` |
| `SONARA_DATABASE` | Database name; default `sonara`. Also selects the target after a restore |
| `PODCAST_DIR` | Host podcast directory; default `./podcasts` |
| `PLAYLIST_DIR` | Host playlist directory; default `./playlists` |
| `UPLOAD_DIR` | Host uploads directory; default `./uploads` |
| `SONARA_ADMIN_PASSWORD` | Optional first-run administrator password; does not reset an existing account |
| `PODCASTINDEX_API_KEY` / `PODCASTINDEX_API_SECRET` | Optional Podcast Index search credentials |

Relative media paths are resolved against `install/compose`. Keep the Compose project name consistent: changing it can select newly named, empty volumes instead of the existing data.

## Move media outside the source checkout

For a new long-lived deployment, choose host directories outside the Git checkout, for example `/mnt/media/music`, `/mnt/media/podcasts`, `/mnt/media/playlists` and `/mnt/media/uploads`. These are examples, not automatically created directories.

For an existing installation:

1. Stop the app service while leaving the database volume intact.
2. Copy the media into the chosen host directories and verify the copy and permissions.
3. Change the corresponding host paths in `.env`.
4. Recreate the app with the same Compose project name. Keep its container paths `/var/music`, `/var/podcast`, `/var/playlists` and `/var/uploads` unchanged so stored database paths remain usable.
5. Verify playback/downloads before removing old copies.

Do not put the podcast directory inside a scanned music folder. Database backups do not include any of these media directories; back them up independently.

## Application settings

Application settings use the `Sonara` configuration section. Environment variables use double underscores, for example `Sonara__Scanner__Watch=false`. In Docker, add such variables under the app service environment (or an override); merely adding an arbitrary name to `.env` does not inject it into the container.

The Settings screen exposes an allowlisted set of non-secret options. Its changes require an app restart. Database settings overrides for those keys are applied after environment/configuration binding at startup. Folder administration and individual account preferences are stored directly in the database.

Key defaults:

| Option | Default / meaning |
| --- | --- |
| `Sonara__PublicBaseUrl` | Empty; external origin for generated public share links |
| `Sonara__DataDir` | `data` natively, `/var/sonara` in the supplied image |
| `Sonara__Database__ConnectionString` | Required Npgsql connection string |
| `Sonara__Scanner__ScanOnStartup` | `true` |
| `Sonara__Scanner__RescanIntervalMinutes` | 360; 0 disables periodic scanning |
| `Sonara__Scanner__Watch` | `true`; network filesystem notifications may be unreliable |
| `Sonara__Scanner__WatchDebounceSeconds` | 15 |
| `Sonara__Scanner__Parallelism` | 0 means automatic, capped at 8 |
| `Sonara__Transcoding__Enabled` | `true` |
| `Sonara__Transcoding__DefaultFormat` | `mp3` |
| `Sonara__Transcoding__DefaultBitRate` | 192 kbps |
| `Sonara__Transcoding__MaxConcurrent` | 0 means automatic |
| `Sonara__Podcasts__RefreshIntervalMinutes` | 120; 0 disables periodic feed refresh |
| `Sonara__Podcasts__AutoDownloadLatest` | 3 episodes on subscription; later new episodes are downloaded during refresh |
| `Sonara__Podcasts__MaxCoverMegabytes` | 20 |
| `Sonara__Podcasts__MaxEpisodeMegabytes` | 1024 |
| `Sonara__Podcasts__RetentionDays` / `RetentionKeepLatest` | 0; cleanup disabled until configured |
| `Sonara__Podcasts__AllowPrivateAddresses` | `false`; enable only when LAN-hosted feeds are intended |
| `Sonara__Uploads__MaxFileMegabytes` | 100 |
| `Sonara__Backups__IntervalHours` | 0; scheduled backups disabled |
| `Sonara__Backups__KeepLatest` | 7 completed managed archives |
| `Sonara__Backups__Folder` | Empty means `<DataDir>/backups` |

Natively, `Sonara__Podcasts__Folder` defaults to `<DataDir>/podcasts`; the supplied image sets `/var/podcast`. Its image also selects explicit PostgreSQL 17 `pg_dump`/`pg_restore` paths. Native backups require PostgreSQL client tools; configure `Sonara__Backups__PgDumpPath` and `Sonara__Backups__PgRestorePath` if they are not on PATH. Match the supplied PostgreSQL 17 server major version.

## Users, transcoding, uploads and avatars

Users can change their own playback preferences under Account; administrators can manage any account through Users. Preferred format/bitrate are separate from the administrator's maximum bitrate. Explicit client values override the corresponding personal preferences; the account maximum still applies to non-admin streams. `format=raw` does not bypass that maximum. Original-file downloads remain controlled by download permission. HLS uses AAC and honors a preferred bitrate when none is requested.

If transcoding is disabled, preferences do not enable it. Original audio is served where allowed; enforcing a bitrate cap without transcoding returns an error. Conversions require ffmpeg, included in the image.

For uploads, register `/var/uploads` (or another writable, non-overlapping music folder) in Settings, enable **Allow uploads**, then grant the user's upload role and folder access. Administrators bypass the user role/folder restriction but the destination must still opt in. Uploads validate audio, reject unsafe paths and never overwrite existing filenames. A successful upload requests a scan. The normal music mount remains read-only.

Custom avatars accept PNG/JPEG up to 2 MB and 4096 x 4096 pixels, normalized to a 128 x 128 PNG stored in the database. Selecting a built-in/default avatar removes the previous custom image. Classic avatar assets and their notices must remain in distributed builds.

## Podcasts and playlists

Add a feed by URL, or configure Podcast Index credentials to enable search. OPML import previews let you select subscriptions before importing them. Retention is opt-in and respects pinned/protected episodes. Use the cleanup preview before enabling retention for a large collection.

Podcast URLs are validated for public HTTP(S) destinations, including redirects, unless private-address support has explicitly been enabled. Cover and episode downloads have configured size limits and bounded retry behavior.

Playlist import accepts UTF-8 M3U/M3U8/PLS references to already scanned, accessible music. Preview first; applying creates a new private playlist from matched entries, preserving order and duplicates. Import is limited to 1 MB and 5,000 references. It neither uploads media nor fetches remote URLs. Exported M3U8/PLS paths are portable library references, not authenticated remote streaming URLs.

## Backups

Administrators can create/download backups from Backups. Archives contain the database dump, credential key and integrity manifest. Custom avatars are included as database data; external artwork/media, `.env` and certificates are not included.

Archives are unencrypted and contain sensitive account data and its decryption key. Keep protected copies outside the container/host. Checksums detect corruption, not authenticity: restore only trusted archives.

A successful background backup applies configured retention to managed Sonara archive filenames only; failure does not prune completed backups. The schedule is disabled by default. Periodic CLI-created backups are not independently pruned by the CLI; a later successful background backup applies retention.

The container must use PostgreSQL 17 client tools for the supplied database. From `install/compose`, verify if needed:

```sh
docker compose -p sonara exec sonara /usr/lib/postgresql/17/bin/pg_dump --version
docker compose -p sonara exec sonara /usr/lib/postgresql/17/bin/pg_restore --version
```

Both should report 17.x. Back up media mounts separately and rehearse recovery before relying on it.

## Offline recovery

Use the same app version for recovery, with compatible PostgreSQL server/client versions; upgrade afterwards. Restore is an offline CLI operation into an already-created **empty** database. It does not overwrite the original database. The target must have no other sessions; the data-directory lock prevents restoring while that app instance is running.

The following commands assume the supplied Compose stack and project name, run from `install/compose`. Substitute the actual archive filename:

```sh
docker compose -p sonara stop sonara
docker compose -p sonara exec db createdb -U sonara sonara_restored
docker compose -p sonara run --rm --no-deps sonara dotnet Sonara.Api.dll --inspect-backup /var/sonara/backups/ACTUAL_BACKUP_FILENAME.zip
docker compose -p sonara run --rm --no-deps sonara dotnet Sonara.Api.dll --restore-backup /var/sonara/backups/ACTUAL_BACKUP_FILENAME.zip --target-database sonara_restored --confirm-stopped
```

After restore succeeds, edit the existing `.env` to set:

```dotenv
SONARA_DATABASE=sonara_restored
```

Then start the stack with the same project name:

```sh
docker compose -p sonara up -d
```

Changing `SONARA_DATABASE` does not create a database in an existing PostgreSQL volume; that is why `createdb` is a separate step. Use a new simple database name for each recovery attempt. Keep the app stopped if recovery fails.

Restore preserves the previous credential key as `secret.key.before-restore-<timestamp>.bak`, restores the database using a transaction, verifies restored credentials and installs the archive key. The database transaction and filesystem replacement are not one atomic operation. A late failure can leave the new database populated: preserve the archive/key copies, correct the problem and retry into another empty database.

For a downloaded archive outside the data volume, add a read-only bind mount to the one-off `run` commands and use its container path as the archive filename. PowerShell example:

```powershell
$sonaraBackupPath = (Resolve-Path 'C:\Backups\your-backup.zip').Path
docker compose -p sonara run --rm --no-deps --volume "${sonaraBackupPath}:/restore/backup.zip:ro" sonara dotnet Sonara.Api.dll --inspect-backup /restore/backup.zip
```

Use that mount and `/restore/backup.zip` for the restore command as well. Preserve/recreate the media mounts independently at the stored container paths.

## Reverse proxy and parallel deployment

For a new instance alongside Airsonic, select a free host port such as `SONARA_PORT=4041`. The app still listens on 4040 inside its own container. HAProxy can target the Pi/host at that published port for the new hostname. Keep the existing Airsonic backend unchanged while testing Sonara.

Set `Sonara__PublicBaseUrl` to the final external origin when generating share links behind a proxy. The image enables forwarded-header support; verify trusted proxy configuration, forwarded scheme/host handling and generated URLs with the actual deployment. TLS termination, hostname routing and Raspberry Pi ARM64 validation will be configured separately. PostgreSQL should remain unexposed outside the private Compose network except for a deliberate localhost development override.
