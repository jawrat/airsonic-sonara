# Sonara uploads, avatars and library cleanup

Apply this overlay at the repository root **after the per-user transcoding overlay**, replacing matching files. Keep your existing `.env` and volumes. This update includes the additive `AddUploadsAndAvatars` migration; normal startup applies it automatically. Stop older app instances before upgrading.

From `install/compose`, using your existing project name/override flags:

```sh
docker compose -p sonara up --build -d
```

Do not use `down -v`. Native builds need the usual .NET/Angular toolchain and ffmpeg (also used for custom avatar normalization). The 46 classic PNGs, provenance and notices are copied into .NET build/publish output; preserve those files when distributing the app.

## Enable music uploads

The supplied Compose stack now mounts `${UPLOAD_DIR:-./uploads}` at `/var/uploads`. You can optionally set `UPLOAD_DIR` in your existing `.env`; otherwise an `uploads` directory beside the Compose file is used. The container entrypoint makes the root of this mount owned by PUID:PGID. Existing child-directory ownership/ACLs are not changed recursively. Your existing `/var/music` mount remains read-only.

1. Rebuild/recreate the app with the updated Compose file.
2. In **Settings → Music folders**, add `/var/uploads` with a name such as Incoming. Save it, then check **Allow uploads** and save that folder again. Existing folders default to uploads disabled. You can instead enable another writable music folder; read-only mounts will refuse writes.
3. In **Users → Permissions**, grant **Upload music to enabled destinations**. Also grant access to that music folder (or all enabled folders). Administrators bypass user role/folder restrictions but must still use an enabled destination with Allow uploads checked.
4. Open **Upload music**, choose a destination, optionally enter an Artist/Album subfolder, choose audio files and upload.

Uploads run one file per request, sequentially in the web client. Results are shown per file; failures remain available for retry. Successful files are not retried automatically. Each successful upload requests a scan; an active scan is followed by another pass when needed. Files may take a moment to appear in browsing.

Supported extensions match the scanner: MP3, FLAC, OGG/OGA/Opus, M4A, AAC, WAV and WMA. The file must contain readable audio. Archives, playlist files and arbitrary filesystem imports are not accepted. Uploads preserve the original filename and metadata. Existing files are never overwritten; rename collisions before uploading.

The default file limit is 100 MB. **Settings → Maximum music upload size** accepts 1–1024 MB and requires restart. The server bounds multipart bodies and validates filenames/subfolders; path traversal, reserved filenames, symbolic-link/junction destinations and unavailable folders are rejected. Destination paths are derived from registered, authorized music folders. Upload destinations must be ordinary directories managed by the server owner; do not allow untrusted local processes to replace directory components while requests are writing.

Staging files are stored briefly in DataDir and then on the destination filesystem under a non-audio temporary extension. The final filename is published without overwriting an existing path. Failed requests clean up their temporary files; an abrupt process/container termination can leave `.sonara-upload-*.tmp` or `upload-staging` leftovers. After stopping Sonara, those temporary files can be removed. Ensure staging and destination storage have space. There is no per-user storage quota in this update.

Uploaded audio is filesystem media: database backups do **not** contain it. Back up your upload mount separately, like your music and podcast media. If a reverse proxy imposes a lower request-size limit, adjust it to your intended upload limit plus multipart overhead.

## Classic and custom avatars

**Account → Avatar** offers 46 classic Subsonic-lineage avatars, the default silhouette, or a custom image. Administrators can use **Users → Playback / avatar** to manage anyone's avatar. User-list thumbnails show the selected image. Users can change only their own avatar; admin edits use the same validation.

Classic PNGs are copied unchanged from the open-source Airsonic-Advanced repository, rather than a proprietary Subsonic release. `THIRD-PARTY-NOTICES.md`, `LICENSE.txt` and the exact upstream commit/hash manifest accompany them. Sonara is distributed under GPL-3.0. No Java implementation code was copied.

Custom images accept PNG/JPEG up to 2 MB and 4096 × 4096 pixels. They are decoded by ffmpeg, reduced/padded to a 128 × 128 PNG and stored in PostgreSQL. SVGs and remote image URLs are not accepted. Invalid uploads leave the existing avatar unchanged. Selecting another built-in/default avatar removes the previous custom image from the account. Custom avatars are included in database backups. Avatar changes do not require a server restart. Existing accounts keep the default silhouette until they choose one.

## Library cleanup

Every successful scan refreshes album/artist counters and removes empty albums, then deletes artists with **no albums and no tracks**. Track-only performers remain; records in disabled or temporarily unavailable folders remain referenced and are preserved. This is database cleanup only; it does not delete audio files. Existing safeguards against treating an empty/unmounted music volume as wholesale deletion are retained. An administrator's normal Rescan runs cleanup; no separate destructive cleanup button is added.

## API additions

- `getUploadFolders`: enabled, upload-enabled folders accessible to the caller, plus effective size limit; requires upload role/admin.
- Multipart POST `uploadMusic`: `musicFolderId`, optional `subfolder`, exactly one file; requires the same role and folder checks. No overwrite option.
- `getAvatars`: built-in avatar IDs/names.
- POST `setAvatar`: optional `username` (defaults to caller), `avatarId` (empty for silhouette, built-in ID, or `custom` with one multipart file). Cross-account writes require admin.
- `getAvatar`: existing username lookup now returns the selected image; `avatarId` optionally previews a valid built-in image. Authenticated cover-art permission still applies.
- `getUser`/`getUsers`: avatar selection and custom-image availability; `uploadRole` now reflects the supported permission. `getMusicFolderSettings`/`saveMusicFolder`: administrator `allowUploads` flag.

The web client sends file bodies through authenticated multipart POST and surfaces per-file protocol errors. `.view` aliases are included for these operations. These upload/avatar management APIs are Sonara extensions, not a claim that standard Subsonic defines upload endpoints.

## Validation

253 backend tests and 47 frontend tests passed. Automated checks exercise audio upload/scan, role and folder restrictions, disabled upload destinations, corrupt audio, duplicate preservation, traversal/link rejection, request-size limits, classic/custom avatars, cross-account write rejection and artist cleanup. Frontend checks exercise sequential per-file results, avatar selection and saving folder upload opt-in. Angular production build and .NET Release publish were checked, including all 46 packaged PNGs and license/provenance files. Docker image execution and visual browser rendering were not exercised in this environment.
