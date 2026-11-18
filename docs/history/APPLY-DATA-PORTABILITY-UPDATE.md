# Sonara data portability update

Apply this overlay at the repository root after the player/library overlay, replacing matching files. No new database migration is introduced. Keep your existing `.env` and volumes.

## Build and run

From `install/compose`, rebuild the image so PostgreSQL client tools are installed:

```sh
docker compose -p sonara up --build -d
```

Use your existing Compose project name and override flags if different. Do not use `down -v`: that deletes persistent data. For native development, install PostgreSQL client tools matching your database major version (17 for the supplied Compose stack). Configure `Sonara__Backups__PgDumpPath` and `Sonara__Backups__PgRestorePath` with their executable paths if they are not on PATH.

## Backups

Administrators get a Backups page with Create, status, refresh and download controls. Backups contain a PostgreSQL custom-format dump, `secret.key`, and a checksum manifest. The key is required to decrypt restored Subsonic account credentials; backup creation verifies that the current key decrypts the stored credentials.

These are metadata and credential backups. They do **not** include music, downloaded podcast media, artwork files, other filesystem content, `.env`, or certificates. Back up those separately and preserve your media mounts and paths. Archives contain sensitive database contents and the credential key and are not encrypted. Download a copy to separate, protected storage.

Backups default to `<DataDir>/backups`. Server configuration `Sonara__Backups__Folder` can point to a separately mounted writable folder. It is not a user-selectable path in the API.

Settings exposes `Backups.IntervalHours` (default 0, scheduling disabled) and `Backups.KeepLatest` (default 7). Restart after changing them. Scheduled backups become due based on the newest managed archive; startup checks begin after one minute. A successful background backup prunes older Sonara-managed backup filenames only. Failure leaves existing archives in place. Other files are never selected for retention. CLI-created backups are retained until a later successful background backup applies retention. Keep external copies independently of this retention policy.

CLI commands, run with the same environment/configuration as the app:

```sh
dotnet Sonara.Api.dll --create-backup
dotnet Sonara.Api.dll --inspect-backup /path/to/backup.zip
```

Inspect validates archive structure, format, hashes and the key without connecting to PostgreSQL. Hashes detect corruption; they do not authenticate a backup. Restore only backups you trust: PostgreSQL restores execute the SQL contained in the dump.

## Offline restore with the supplied Compose stack

Restore is deliberately an offline CLI operation. Stop Sonara first. The tool takes the same data-directory lock as the web application, refuses an active instance, and restores only into an already-created **empty** database with no other sessions. The original database is not overwritten.

Run these from `install/compose`, substituting the actual archive filename shown on the Backups page:

```sh
docker compose -p sonara stop sonara
docker compose -p sonara exec db createdb -U sonara sonara_restored
docker compose -p sonara run --rm --no-deps sonara dotnet Sonara.Api.dll --inspect-backup /var/sonara/backups/ACTUAL_BACKUP_FILENAME.zip
docker compose -p sonara run --rm --no-deps sonara dotnet Sonara.Api.dll --restore-backup /var/sonara/backups/ACTUAL_BACKUP_FILENAME.zip --target-database sonara_restored --confirm-stopped
```

Only after restore succeeds, edit your existing `.env` to add or change:

```dotenv
SONARA_DATABASE=sonara_restored
```

Then start the app:

```sh
docker compose -p sonara up -d
```

The optional database variable now selects both the app database and database health check. Changing it does not create a database in an existing PostgreSQL volume; that is why `createdb` is a separate step. Use a new, simple target name for each restore attempt. Keep Sonara stopped if any step fails.

To inspect/restore a downloaded archive outside the container volume, add a read-only bind mount to the one-off `run` command and use `/restore/backup.zip` as the archive path. Example in PowerShell:

```powershell
$sonaraBackupPath = (Resolve-Path 'C:\Backups\your-backup.zip').Path
docker compose -p sonara run --rm --no-deps --volume "${sonaraBackupPath}:/restore/backup.zip:ro" sonara dotnet Sonara.Api.dll --inspect-backup /restore/backup.zip
```

Use that same mount for the restore command. Native restore uses the same CLI flags and the destination app's configured DataDir/connection settings; `--target-database` changes the target database name only. Use the same app version and a compatible PostgreSQL server/client version for recovery, then upgrade normally.

Restore preserves the previous key as `secret.key.before-restore-<timestamp>.bak`, restores the database transactionally, verifies the restored credentials against the archive key, and installs that key in DataDir. The SQL transaction and filesystem key replacement are not a single atomic operation. If a late verification or key-installation step fails, the new database may already contain data: keep the app stopped, retain the archive and previous key, correct the failure, and retry into another empty database. Media paths remain as stored in the database.

## Playlist import/export

The Playlists page accepts UTF-8 M3U, M3U8 and PLS files up to 1 MB and 5,000 entries. Choose the matching music folder if needed, select a file, review matched/unmatched results, and explicitly create a new private playlist. Preview does not write a playlist. The first 200 entry results are displayed; totals cover the whole file. Only matched entries are imported, in original order, including duplicates. Existing playlists are unchanged. Reselect the file after changing the matching folder.

Matching uses the scanned catalog accessible to the current user. It supports indexed absolute paths, portable folder-alias/relative paths and unique relative-path suffixes across different host/drive prefixes. Ambiguous entries are not guessed. Remote URLs are unsupported. Import never opens arbitrary paths or fetches remote URLs. This imports playlist references, not music files; legacy ANSI and XSPF are not supported.

Each playlist page exports M3U8 or PLS, subject to playlist visibility and the caller's folder access. Exported paths are portable `./FolderAlias/relative/path` references, without server absolute paths or authentication tokens. They are suitable for transferring library playlists; another player needs corresponding media paths. They are not authenticated remote-stream URLs.

## API additions and validation

Sonara extensions: admin-only `getBackups`, `createBackup`, `downloadBackup`; playlist-role `importPlaylistFile` (dryRun defaults true), and visibility-checked `exportPlaylistFile`. The web client uses authenticated form POST for creation/import and checks download error responses before saving files.

Validation: 237 backend tests passed, including real PostgreSQL 17 dump/restore, credential recovery, locking, non-empty-target refusal, archive corruption, retention and access checks; 42 frontend tests passed; Angular production build passed. The Docker image build and visual browser layout were not exercised in this environment.

PostgreSQL reference: https://www.postgresql.org/docs/17/app-pgdump.html and https://www.postgresql.org/docs/17/app-pgrestore.html.
