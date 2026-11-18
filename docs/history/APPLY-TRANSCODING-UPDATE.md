# Sonara per-user transcoding update

Apply this overlay at the repository root after the data-portability update, replacing matching files. Rebuild your Docker image using your existing Compose project name and flags, for example from `install/compose`:

```sh
docker compose -p sonara up --build -d
```

Keep your `.env` and volumes. This update includes the additive `AddUserTranscodingPreferences` EF migration; normal startup applies it automatically. Existing accounts start with empty format and bitrate 0, preserving current playback behavior. Stop older app instances before upgrading. No password reset or new admin password is required.

## Controls

- Every authenticated user can set their own preferences under **Account → Playback transcoding**.
- Administrators can open **Users → Playback** for any account, including their own.
- The existing **Users → Permissions → Maximum stream bitrate** remains a separate, administrator-only limit. The preference API cannot change that limit, roles or folder access.

Preferences are stored in PostgreSQL, shared across sessions/devices, and take effect on the next stream request. Saving does not interrupt an already playing track. They also appear in database backups.

Preferred format choices are Default, Original, MP3, AAC and Opus. Preferred bitrate accepts 0 (default) or 32–320 kbps. Default format with bitrate 0 keeps original audio unless the client or an administrator limit requests conversion. A bitrate by itself is a bandwidth target, using the server default codec when conversion is needed. Original ignores the personal bitrate preference. Format choices still avoid unnecessary same-format encoding when the source already fits, and lossy sources are never upscaled.

Explicit client `format` and `maxBitRate` values override the corresponding preference, independently. An explicit `maxBitRate=0` suppresses the preferred bitrate. The administrator maximum is applied afterwards for non-admin accounts, so original/raw and a higher personal bitrate cannot bypass it. Administrators retain the existing exemption from enforced account limits. Downloads continue to serve originals under the existing download permission.

HLS always uses AAC; with no explicit HLS bitrate it uses the account's preferred bitrate, falling back to 192 kbps. HLS variants still respect the administrator limit. Podcast streams use the same preferences as music. Public share playback uses its owner's preferences and existing restrictions.

The server-wide transcoding enable switch still controls whether conversions are available; preferences do not enable a disabled transcoder. If transcoding is disabled and no enforced limit requires conversion, original files are served. Preferences are soft playback defaults, not a way to restrict original downloads. Actual browser codec support varies; MP3 is a practical general-purpose option.

## API

`getUser` and `getUsers` expose `preferredTranscodeFormat` (empty, raw, mp3, aac, opus) and `preferredTranscodeBitRate` (0 or 32–320). Authenticated form POST to `setTranscodingPreferences` takes both fields and an optional `username` (defaults to the caller). Editing another account requires admin. It validates all values before saving and returns the updated `user`. It accepts no role, password or account-limit changes. Self-service preferences are available to every authenticated user independently of the password-change role; streaming itself still requires stream permission.

## Validation

241 backend tests and 44 frontend tests passed; the Angular production build passed. Backend tests cover preference planning, real streaming conversion, raw client overrides, admin-limit enforcement, database persistence, cross-account authorization and atomic validation. Frontend tests cover loading/saving selected-account preferences and load failures. The Angular production build and PostgreSQL-backed integration suite were run; visual browser rendering and the Docker image build were not exercised here.
