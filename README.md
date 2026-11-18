# Sonara

Sonara is a self-hosted music server inspired by Airsonic-Advanced, with a .NET 10 API, PostgreSQL database and Angular web client. The repository name is `airsonic-sonara`; the application is named Sonara and its Compose service is `sonara`.

**Status:** development software. The API implements the inventoried Airsonic REST operations with documented limitations and deliberate exclusions. Endpoint coverage does not imply complete behavioral parity or verified compatibility with every Subsonic client. See [API coverage](docs/API-COVERAGE.md).

## Features

- Music scanning, search, artist/album/folder/genre browsing, original streaming and ffmpeg transcoding.
- An Airsonic-style artist sidebar and album grid inside a themeable LCARS frame. Recently added is the default home order.
- Playlists, M3U/M3U8/PLS import/export, saved queues, shuffle/repeat, bookmarks and favorites.
- Local listening history, ratings, now-playing information, public music shares, radio/chat APIs and local lyrics.
- Podcasts: feed subscriptions, optional Podcast Index search, selective OPML import, downloads, pinning and opt-in retention.
- User administration, roles, folder access and separate personal transcoding preferences and administrator bitrate limits.
- Permission-controlled audio uploads and classic/custom avatars.
- Database/key backups, optional scheduling/retention and offline restoration into a fresh database.

Video, jukebox and external scrobbling are outside the current scope. LDAP, DLNA, Sonos and Chromecast are not implemented. External artist/album metadata providers and online lyrics lookup are not implemented; recommendation endpoints use documented local fallbacks.

## Quick start with Docker Compose

Run these commands from `install/compose`:

```sh
cp .env.example .env
```

On PowerShell, use `Copy-Item .env.example .env` instead. Edit the new `.env`:

- Set `MUSIC_DIR` to an existing absolute music-library path.
- Set a unique `POSTGRES_PASSWORD`. The supplied configuration embeds it in a connection string; use letters/digits, for example a value generated with `openssl rand -hex 24`.
- Set `SONARA_PORT` if host port 4040 is already in use. For example, use 4041 alongside an Airsonic instance using 4040.
- Optionally set an initial `SONARA_ADMIN_PASSWORD` and Podcast Index credentials.

Do not commit the actual `.env`. Then start the stack:

```sh
docker compose -p sonara up --build -d
```

Open `http://localhost:4040`, or the host port selected in `.env`. From another machine, use the Docker host's address. Host Node/npm and .NET installations are not required for Docker builds.

On the first start with an empty user database, Sonara creates the administrator. If no initial password was configured, a random password is printed in the app log:

```sh
docker compose -p sonara logs sonara
```

User accounts and passwords persist across ordinary container recreation. Database migrations are applied automatically at app startup.

If invoking Compose from the repository root, use the same project name:

```sh
docker compose -p sonara -f install/compose/docker-compose.yaml up --build -d
```

## Storage and ports

| Host configuration | Container location | Purpose |
| --- | --- | --- |
| `MUSIC_DIR` | `/var/music` | Existing music, mounted read-only |
| `PODCAST_DIR` (default `./podcasts`) | `/var/podcast` | Downloaded podcast media |
| `PLAYLIST_DIR` (default `./playlists`) | `/var/playlists` | Playlist filesystem mount |
| `UPLOAD_DIR` (default `./uploads`) | `/var/uploads` | Writable upload destination |
| Compose volume `sonara-data` | `/var/sonara` | Credential key, backups and runtime files |
| Compose volume `pg-data` | PostgreSQL data directory | Persistent database |

Relative bind-mount paths resolve from `install/compose`, not from the container filesystem. For long-lived deployments, use explicit host media paths outside the source checkout. Do not change existing paths until you have moved the files deliberately; see [operations](docs/OPERATIONS.md).

Keep the podcast directory outside scanned music directories to avoid indexing episodes as ordinary music. Keep both the database and `/var/sonara/secret.key`: the key is required to decrypt stored Subsonic credentials. Sonara's database backup archives do not include media files.

`SONARA_PORT` changes only the published host port; the application still listens on container port 4040. PostgreSQL is not published by the main Compose file. The optional development override publishes it on `127.0.0.1:5432` only.

The supplied app container runs with `PUID=1000` and `PGID=1000`. If necessary, change the service environment values to match your host ownership. Existing media child-directory ACLs/ownership are not changed recursively.

**Avoid `docker compose down -v` unless you intend to delete the stack's named database/data volumes.**

## Documentation

- [Operations, configuration, media storage and backup/restore](docs/OPERATIONS.md)
- [Development and tests](docs/DEVELOPMENT.md)
- [API coverage and limitations](docs/API-COVERAGE.md)
- [Historical update notes](docs/history/README.md)

For deployment behind HAProxy, keep the internal port unchanged and route the new hostname to the selected published port. Configure `Sonara__PublicBaseUrl` for public share links when needed. Proxy/TLS configuration and Raspberry Pi deployment are separate deployment steps; ARM64 image execution and Pi performance have not yet been validated.

## Project layout

| Directory | Contents |
| --- | --- |
| `src/Sonara.Core` | Entities |
| `src/Sonara.Data` | EF Core/Npgsql database model and migrations |
| `src/Sonara.Api` | ASP.NET Core server, scanning, media, API and background jobs |
| `web` | Angular web client |
| `tests/Sonara.Tests` | Backend tests |
| `install/compose` | Main Compose stack and optional IDE development override |
| `docker` | Container entrypoint |
| `docs` | Current documentation and historical update notes |

## License and attribution

Sonara is distributed under GPL-3.0; see [LICENSE.txt](LICENSE.txt). The classic avatar PNGs are copied unchanged from the open-source Airsonic-Advanced source tree in the Subsonic lineage. Original filenames, source commit and hashes are recorded in [avatar provenance](src/Sonara.Api/Assets/avatars/PROVENANCE.json). See [third-party notices](THIRD-PARTY-NOTICES.md). No Java implementation code was ported.
