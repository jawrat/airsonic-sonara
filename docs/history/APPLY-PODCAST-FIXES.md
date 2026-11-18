# Sonara: selective OPML subscriptions and podcast transfer fixes

Apply this source overlay after `sonara-podcast-management-overlay.zip`, into the directory containing `Sonara.slnx`, overwriting matching files. Rebuild/restart through your existing workflow, retaining Docker volumes. No new database migration is needed.

```powershell
Expand-Archive -Path "$HOME\Downloads\sonara-podcast-selection-fixes.zip" -DestinationPath . -Force
dotnet build
dotnet test
Push-Location web
npm ci
npm run build
npm test -- --watch=false
Pop-Location
```

## Choose subscriptions from OPML

The preview starts with no podcasts selected. New feeds have checkboxes; existing, duplicate and invalid rows are not selectable. Use Select all new podcasts, Clear selection, or individual checkboxes. The subscribe button shows the selected count and is disabled when nothing is selected. Only selected feed URLs are sent when applying the import. Choosing a new OPML file clears the previous selection.

The API now requires one or more repeated `selectedUrl` form parameters when `dryRun=false`. Every selected URL must normalize to a valid feed entry in the submitted OPML; unknown selections are rejected before saving anything. A request without selections no longer subscribes to every feed. Preview requests remain unchanged. Existing subscriptions remain untouched.

## Oversized podcast covers

The previous log meant an image exceeded the fixed 5 MB artwork limit. Audio was unaffected; the server simply had no local cover to display.

The new default is 20 MB, still bounded. Change **Settings → Maximum podcast cover image size (MB)** and restart to apply it, or set `Sonara__Podcasts__MaxCoverMegabytes` (supported range 1–100). Refresh the affected podcast after rebuilding to retry its cover download. Images exceeding the configured limit are still skipped, so 20 MB is not a guarantee that every remote cover fits.

## Interrupted/TLS episode downloads

The download path now retries transient remote read/connection failures and retryable HTTP responses, up to three attempts. Each attempt starts from byte zero in the partial file. The completed episode file is only published after a successful transfer; failed attempts are never appended together. Partial files are removed after final failure or cancellation when the filesystem permits it.

Disk-write errors, size-limit violations, permanent HTTP responses and certificate-authentication errors do not trigger retries. Short Retry-After delays are honored; if the server asks for more than 30 seconds, the attempt fails rather than retrying earlier than requested. Certificate checks and protocol validation have not been disabled.

The prior generic “decryption operation failed” message came from downloading remote audio and points to an HTTPS/TLS transport error, not stored-password decryption. The supplied log omitted the inner error, so the exact remote/server/runtime cause has not been established. New download diagnostics include the bounded inner exception chain and redact HTTP(S) URLs, which can contain signed links or private feed tokens.

Retry the failed episode after rebuilding. If it continues to fail, the new final warning should identify the underlying error rather than just the outer message.

## Verification

- Backend build and 223 backend tests passed, including the PostgreSQL integration tests with no skips in the verified run.
- Angular production build and 24 frontend tests passed.
- Tests cover default-unchecked UI selection, selected-only server subscription creation, missing/unknown selection rejection, interrupted transfers restarting without mixed bytes, retry exhaustion/partial cleanup, permanent errors and size limits, long Retry-After handling, inner-error diagnostics/URL redaction, and artwork exceeding the old 5 MB limit.
- No live reproduction of the reported remote podcast's TLS failure was possible from the log excerpt alone. Browser rendering was not verified; the prior local Chromium socket limitation still applies.
