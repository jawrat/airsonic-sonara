#!/bin/sh
# Runs the server as PUID:PGID (default 1000:1000) so files written to the mounted
# data volume are owned by you rather than root. Set PUID=0 PGID=0 to run as root.
set -e

PUID="${PUID:-1000}"
PGID="${PGID:-1000}"

if [ "$(id -u)" = "0" ] && { [ "$PUID" != "0" ] || [ "$PGID" != "0" ]; }; then
  chown "$PUID:$PGID" "${Sonara__DataDir:-/var/sonara}"
  # Docker creates a missing bind-mount source as root; make sure the podcast folder is writable.
  podcasts="${Sonara__Podcasts__Folder:-/var/podcast}"
  if [ -d "$podcasts" ]; then chown "$PUID:$PGID" "$podcasts" || true; fi
  if [ -d /var/uploads ]; then chown "$PUID:$PGID" /var/uploads || true; fi
  export HOME="${Sonara__DataDir:-/var/sonara}"
  exec setpriv --reuid="$PUID" --regid="$PGID" --clear-groups "$@"
fi

exec "$@"
