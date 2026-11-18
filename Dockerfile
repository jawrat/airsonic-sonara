# syntax=docker/dockerfile:1

# ---- 1. Angular SPA ----------------------------------------------------------------------
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build -- --configuration production

# ---- 2. .NET API -------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Sonara.Core/Sonara.Core.csproj src/Sonara.Core/
COPY src/Sonara.Data/Sonara.Data.csproj src/Sonara.Data/
COPY src/Sonara.Api/Sonara.Api.csproj src/Sonara.Api/
RUN dotnet restore src/Sonara.Api/Sonara.Api.csproj
COPY src/ src/
COPY LICENSE.txt THIRD-PARTY-NOTICES.md ./
RUN dotnet publish src/Sonara.Api/Sonara.Api.csproj -c Release -o /app --no-restore

# ---- 3. Runtime --------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

# Match the Compose PostgreSQL 17 server. Ubuntu's generic client package can be older.
# Official PGDG repository/key instructions: https://www.postgresql.org/download/linux/ubuntu/
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl ffmpeg \
 && install -d /usr/share/postgresql-common/pgdg \
 && curl --fail --show-error --silent --location https://www.postgresql.org/media/keys/ACCC4CF8.asc -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
 && printf '%s\n' 'deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt noble-pgdg main' > /etc/apt/sources.list.d/pgdg.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends postgresql-client-17 \
 && /usr/lib/postgresql/17/bin/pg_dump --version \
 && /usr/lib/postgresql/17/bin/pg_restore --version \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./
COPY --from=web /web/dist/web/browser ./wwwroot
COPY docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh \
 && mkdir -p /var/music /var/podcast /var/playlists /var/sonara /var/uploads

# Same mount points as the Airsonic war image, so switching containers is painless.
ENV Sonara__Backups__PgDumpPath=/usr/lib/postgresql/17/bin/pg_dump \
    Sonara__Backups__PgRestorePath=/usr/lib/postgresql/17/bin/pg_restore \
    Sonara__DataDir=/var/sonara \
    Sonara__MusicFolder=/var/music \
    Sonara__Podcasts__Folder=/var/podcast \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    PUID=1000 \
    PGID=1000

VOLUME ["/var/sonara", "/var/music", "/var/podcast", "/var/playlists", "/var/uploads"]
EXPOSE 4040

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://localhost:4040/healthz || exit 1

ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "Sonara.Api.dll"]
