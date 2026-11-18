# PostgreSQL 17 backup-client fix

Apply this small overlay at the Sonara repository root after the uploads/avatars update. Replace `Dockerfile` and `.dockerignore` (merge the notices exception if you have custom ignore rules). It introduces no database migration and changes no application source, host Node/npm installation, connection string or database volume.

The image now pins its runtime base to .NET 10 Ubuntu Noble, installs **postgresql-client-17** from the PostgreSQL project's signed Noble APT repository, and explicitly selects `/usr/lib/postgresql/17/bin/pg_dump` and `pg_restore`. Both binaries must execute successfully during the image build. This replaces Ubuntu's generic package, which had selected PostgreSQL 16. The `.dockerignore` also includes the earlier `!THIRD-PARTY-NOTICES.md` fix.

From your repository root, rebuild and recreate the app using your existing project/Compose flags:

```sh
docker compose -p sonara -f install/compose/docker-compose.yaml up --build -d
```

Do not delete volumes or use `down -v`. No `--no-cache` is needed: the changed Dockerfile invalidates the runtime package-install layer.

Verify the running image's tools:

```sh
docker compose -p sonara -f install/compose/docker-compose.yaml exec sonara /usr/lib/postgresql/17/bin/pg_dump --version
docker compose -p sonara -f install/compose/docker-compose.yaml exec sonara /usr/lib/postgresql/17/bin/pg_restore --version
```

Both should report **17.x**. A different patch release from the server's 17.12 is fine. If you explicitly set `Sonara__Backups__PgDumpPath` or `Sonara__Backups__PgRestorePath` in custom Compose/environment configuration, remove any obsolete overrides or point them at the paths above.

Then open **Backups → Create backup** and wait for completion; confirm a new downloadable ZIP appears. The previous failed attempt did not produce a completed backup. Keep your existing backups and use the offline empty-database recovery procedure in `APPLY-DATA-PORTABILITY-UPDATE.md` for a restore rehearsal. This fix does not change that procedure or add media files to metadata backups.

Validation: official Noble repository checked for the versioned client package; PostgreSQL 17 real backup/restore integration tests rerun. A Docker daemon is unavailable in this environment, so the image itself could not be built/run here. The version checks during your image build and the commands above verify the actual container tools.

Official package installation instructions: https://www.postgresql.org/download/linux/ubuntu/.
