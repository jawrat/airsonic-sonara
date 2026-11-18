# Sonara artist-sidebar library layout

Apply this overlay at the repository root after the uploads/avatars update and PostgreSQL backup-client fix, replacing matching files. No new migration is introduced. Keep your `.env`, media and database volumes.

From the repository root, using your existing Compose flags:

```sh
docker compose -p sonara -f install/compose/docker-compose.yaml up --build -d
```

No host Node/npm upgrade is required for this Docker build. If building the Angular client directly, use its supported Node version as discussed previously. Refresh your browser after recreating the container.

## Layout and navigation

The library now uses two columns entirely inside the existing LCARS content area:

- Left: folder selector, artist-name filter, All albums button, letter shortcuts, and an independently scrollable artist index with album counts.
- Right: album artwork, album title, artist, year/track count, added date, sort selector, search and paging controls.

The artist index contains all authorized album artists for the selected folder rather than just the first artist page. It groups and sorts names using the server's ignored-article list, so The Allman Brothers is under A and The Band is under B; display names remain unchanged. The local artist-name filter does not change the album results or make a server request with every keystroke. Letter shortcuts jump within the sidebar, and its up-arrow buttons return to the letters.

Select an artist to show that artist's albums in the right pane while keeping the index visible. The selected artist is highlighted. All albums returns to the recently-added home view. Clicking album artwork or title opens its normal album detail page; links to artists on album cards stay in the library browser. Folder selection applies to both artist indexing and album results, and clears a previous artist selection.

The artist index reloads when you change folders or press Refresh, not each time you page/sort albums. Refresh also updates the current album/search results after a scan. A missing/failed artist index has its own loading/error/retry state and does not prevent album results from loading.

## Default order and other views

The home defaults to **Recently added**, the clearer label for the existing `newest` API ordering. It sorts by when the album was added to Sonara's database, not release year or media file modification time. Existing albums retain their stored dates; this update does not rewrite them. Cards display those added dates using the browser's locale.

Alternative sorts remain available: album A–Z, artist A–Z, random, recently played, most played, highest rated, favorites, year range and genre. Year/genre controls appear when applicable. Existing artist/genre/folder browsing, search, song queue controls and 48-item album paging are retained.

Selection, folder, sort and paging are URL state, so browser Back/Forward and bookmarks work. Opening the unfiltered home URL starts with Recently added. A bookmarked older URL with an explicit sort still uses that sort.

On smaller screens the artist pane stacks above results, has a bounded scroll area, and includes Show/Hide controls. The album grid adapts to the available content width. Colors use the current theme variables, so LCARS and plain themes retain their appearance. The outer LCARS navigation/frame and player are unchanged.

## API and validation

`getAlbumList` and `getAlbumList2` accept optional `artistId` in addition to their existing folder, sort and pagination parameters. This is an additive Sonara extension: omitted IDs preserve existing client behavior. Filtering runs against the same authorized/scoped album query, so an artist filter cannot expose albums from inaccessible folders. Invalid IDs return a protocol error. The web client requests the standard complete `getArtists` index for sidebar navigation.

254 backend tests and 51 frontend tests passed, and the production Angular build passed. Automated tests cover the default order, grouped/filterable index, ignored leading articles, URL navigation, selected-artist paging, folder-scoped results and invalid IDs. The production Angular build is checked. Browser rendering could not be exercised here because Chromium cannot start under this environment's socket restrictions; Docker itself is unavailable here.
