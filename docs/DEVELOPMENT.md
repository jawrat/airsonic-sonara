# Development

## Prerequisites

- .NET 10 SDK.
- PostgreSQL 17 for the supplied stack.
- Node supported by the checked-in Angular 22 dependencies: Node 22.22.3+ in the 22.x line, 24.15.0+ in 24.x, or 26.x.
- npm compatible with that Node installation; use `npm ci` with the committed lockfile rather than upgrading dependencies incidentally.
- ffmpeg for transcoding, resizing and media integration tests.
- PostgreSQL 17 client tools for native backup/restore operations and their integration tests.

A project-specific Node environment avoids changing unrelated projects. A global Angular CLI or newest global npm is not required. Docker builds use their own Node/.NET stages.

## Run the API and web client

Create `install/compose/.env` from its template and fill in the required values. From the repository root, start an isolated development database using the optional localhost-port override:

```sh
docker compose -p sonara-dev -f install/compose/docker-compose.yaml -f install/compose/docker-compose.dev.yaml up -d db
```

This project name uses separate development volumes. The override publishes PostgreSQL on localhost:5432. Use a different host port if 5432 is already occupied and adjust the native connection string accordingly.

PowerShell, from the repository root:

```powershell
$env:Sonara__Database__ConnectionString = 'Host=localhost;Database=sonara;Username=sonara;Password=<YOUR_DEV_POSTGRES_PASSWORD>'
$env:Sonara__DataDir = Join-Path (Get-Location) 'data'
# Optional: set an existing absolute music directory for first-run registration.
# $env:Sonara__MusicFolder = 'D:\Music'
dotnet run --project src/Sonara.Api --urls http://localhost:4040
```

Replace the development password placeholder locally; do not save a real password into source-controlled configuration. `AdminPassword` is optional; on the first start a generated value appears in the log. Registered accounts persist in the database.

In a second terminal:

```sh
cd web
npm ci
npm start
```

The development proxy in `web/proxy.conf.json` targets the API at localhost:4040. Keep that port free, or change the API URL and proxy target together. The Docker `SONARA_PORT` setting does not change native API URLs. The web development URL is printed by Angular CLI.

## Build and tests

From the repository root:

```sh
dotnet build Sonara.slnx
dotnet test Sonara.slnx
```

From `web`:

```sh
npm ci
npm run build
npm test -- --watch=false
```

PostgreSQL integration tests are conditional. Without their environment settings, `dotnet test` skips them; a successful run with skips is not the same as exercising database/backup behavior.

Set `SONARA_TEST_DATABASE` to a dedicated disposable PostgreSQL instance. Its role must be able to create and drop the uniquely named test databases. Fixtures create/drop their own test databases. Do not point integration tests at production services.

For backup integration tests also set `SONARA_TEST_PG_DUMP` and `SONARA_TEST_PG_RESTORE` to PostgreSQL 17 executables. ffmpeg must be available for media integration tests. Example PowerShell placeholders:

```powershell
$env:SONARA_TEST_DATABASE = 'Host=localhost;Database=postgres;Username=<TEST_ROLE>;Password=<TEST_PASSWORD>'
$env:SONARA_TEST_PG_DUMP = 'C:\Program Files\PostgreSQL\17\bin\pg_dump.exe'
$env:SONARA_TEST_PG_RESTORE = 'C:\Program Files\PostgreSQL\17\bin\pg_restore.exe'
dotnet test Sonara.slnx
```

## Database migrations

The app applies EF Core migrations at startup. Use a `dotnet-ef` tool matching the EF Core major version (10.x). After changing entities/model configuration, generate a migration from the repository root:

```sh
dotnet ef migrations add <Name> --project src/Sonara.Data --startup-project src/Sonara.Api
```

The design-time context factory constructs the database model without starting the web host; migration generation does not require a running database. Commit generated migration code and the model snapshot. A pending-model-changes startup error requires a proper migration, not suppression of the check.

## Contributions and distribution

Keep source, tests, lockfiles, migrations, licenses and avatar provenance under version control. Keep `.env`, credential keys, database/media archives, runtime media and generated build output out of Git and Docker build contexts.

The avatar directory is case-sensitive source content. Avoid broad ignore rules that accidentally hide source directories such as `src/Sonara.Api/Backups` on Windows. Root-only backup exclusions should be anchored, for example `/backups/` in `.gitignore`.

Use the [API coverage notes](API-COVERAGE.md) to distinguish implemented behavior from local fallbacks and excluded features. See [operations](OPERATIONS.md) for recovery and deployment behavior. Old overlays are documented under [history](history/README.md); they are not steps required to install a fresh clone.
