# Sonara podcast management update

Apply this source overlay **after the API parity overlay**, to the directory containing `Sonara.slnx`. It does not contain `.env`, databases, encryption keys, build output or dependencies. Keep your Docker volumes; do not use `docker compose down -v` for an ordinary rebuild.

Stop the application and back up PostgreSQL and `DataDir/secret.key` together before applying the additive migration. Extract, overwriting matching source files:

```powershell
Expand-Archive -Path "$HOME\Downloads\sonara-podcast-management-overlay.zip" -DestinationPath . -Force
dotnet build
dotnet test
Push-Location web
npm ci
npm run build
npm test -- --watch=false
Pop-Location
```

Rebuild/restart through your existing Compose configuration, or restart the local API and Angular dev server. `AddPodcastRetention` is included and applies automatically at startup. Do not generate another migration. It adds nullable `DownloadedUtc` and a `Pinned` flag to episodes; existing episodes and subscriptions remain intact.

## New podcast controls

- **Export subscriptions (OPML)** downloads the subscription list. It does not export episode media or listening positions.
- **Import subscriptions** accepts an OPML/XML file up to 1 MB, with at most 200 feed entries, including nested folders. Preview identifies new, existing, duplicate and invalid feed addresses. Select desired new feeds with checkboxes (nothing is selected by default). Import adds only selected valid subscriptions and queues their feed refreshes. Existing subscriptions are not replaced or unsubscribed.
- Queued imports are persisted before background processing. A restart resumes subscriptions still waiting for their first refresh. A queued result means the subscription was saved, not that its feed has already been validated; failed feed checks appear on podcast cards and can be retried with Refresh.
- On a subscription's first refresh, only `AutoDownloadLatest` episodes are queued for download. Later refreshes queue newly discovered episodes. Setting that option to 0 means on-demand downloads only.
- **Pin/Unpin** protects an episode from automatic retention. This is a server-wide flag shared by listeners. Users with podcast-management permission can manage subscriptions, downloads and pins; administration rights are not required for those tasks.
- **Remove file** now leaves the episode visible and available for downloading again. File deletion failures retain the original stored path and report an error.

## Retention is disabled by default

Both settings below start at 0. Change them in Settings and restart Sonara to apply the saved policy, or supply environment variables:

| Setting | Behavior |
| --- | --- |
| `Sonara__Podcasts__RetentionDays` | Remove downloads older than this many days. 0 disables age-based cleanup. Age is measured from the download time, not the publication date. |
| `Sonara__Podcasts__RetentionKeepLatest` | Retain the newest N downloaded episodes per podcast, ranked by publication date (download time if undated), then episode ID. 0 disables the count limit. |

When both are enabled, an episode can qualify under either rule. The N-episode limit counts the whole downloaded collection, then protection rules are applied; protected episodes may keep storage above the configured limit.

Cleanup protects pinned episodes, bookmarks belonging to any user, episodes in any saved play queue, and downloads less than 24 hours old. That grace period also prevents a manually downloaded older episode from disappearing immediately under the count rule. Existing downloads without a stored timestamp use their file modification time; unknown/inaccessible timestamps are treated conservatively.

Only regular episode files immediately within their registered podcast channel directory can be removed. Other folders, directories masquerading as files and symbolic links are excluded. Failed filesystem deletions retain the record/path and are reported in the cleanup summary. Missing managed files can have their stale download state cleared. Cleanup keeps episode metadata and subscriptions; removed files remain eligible for an explicit new download, but a feed refresh does not automatically redownload old retained records.

For admins, Podcasts → **Downloaded episode storage → Preview cleanup** shows the running policy, eligible count/bytes, protected count and up to 200 candidate episodes. **Remove eligible downloads** asks for confirmation and recomputes eligibility before removing files; a preview is never a fixed deletion list. Counts can change between preview and execution.

Enabled retention runs after podcast refresh/download jobs and approximately every six hours (the periodic loop starts two minutes after startup). It runs even if periodic feed refreshing is disabled. Retention and managed file mutations coordinate within one Sonara server process; this remains a single-worker deployment, not a multi-replica distributed download system.

## API extensions

All support JSON/XML envelopes and `.view` aliases. Import and pinning require podcast-management permission; retention preview/execution require admin rights. The web client sends mutating requests below as form POSTs.

| Operation | Parameters |
| --- | --- |
| `importPodcastOpml` | `opml` XML text, `dryRun` boolean (defaults to true); repeated `selectedUrl` entries required when applying |
| `setPodcastEpisodePinned` | Episode `id`, explicit `pinned` boolean |
| `getPodcastRetentionPreview` | No additional parameters |
| `runPodcastRetention` | No additional parameters; recomputes the policy |

`exportPodcasts/opml` remains the OPML export endpoint. Standard podcast responses also include optional `downloaded` and `pinned` extension attributes.

## Verification

- Backend build and 216 backend tests, including PostgreSQL integration/upgrade tests: passed with no skipped tests in the verified run.
- Angular production build and 23 frontend tests: passed.
- New checks cover OPML parsing/limits/XXE rejection, preview without writes, repeated imports, export round-trip, first-refresh download limits, pin permissions, retention protection/grace rules, file-path boundaries, symbolic links, deletion failures, and preserving removed episode records.
- Database integration tests still require `SONARA_TEST_DATABASE` for a disposable PostgreSQL server with create/drop permissions. Ordinary `dotnet test` skips those when it is unset.
- Browser rendering was not verified here; the same Chromium socket restriction as the previous batch applies.

Video and jukebox are explicitly out of scope. Backup/restore and music uploads remain separate future work.
