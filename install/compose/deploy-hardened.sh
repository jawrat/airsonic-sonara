#!/bin/sh
# Run from the Pi checkout after pulling the security update.
set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
[ -f install/compose/docker-compose.pi.yaml ] || { echo 'Pi override is missing; stop here.' >&2; exit 1; }
[ -f install/compose/.env ] || { echo 'The existing private .env is missing; stop here.' >&2; exit 1; }
sonara_proxy_ip="$(docker inspect haproxy --format '{{(index .NetworkSettings.Networks "haproxy_default").IPAddress}}')"
python3 - "$sonara_proxy_ip" <<'PY'
from pathlib import Path
import ipaddress, os, sys, tempfile
address = str(ipaddress.ip_address(sys.argv[1]))
p = Path('install/compose/.env')
text = p.read_text()
lines = [line for line in text.splitlines() if line.split('=',1)[0].strip() != 'SONARA_TRUSTED_PROXY_IP']
lines.append('SONARA_TRUSTED_PROXY_IP=' + address)
fd, temp = tempfile.mkstemp(prefix='.env-security-', dir=str(p.parent))
try:
    os.fchmod(fd, 0o600)
    with os.fdopen(fd, 'w') as f:
        f.write('\n'.join(lines) + '\n')
    os.replace(temp, p)
finally:
    if os.path.exists(temp): os.unlink(temp)
print('Configured the current HAProxy address as the trusted proxy. Existing secrets were preserved.')
PY
compose() {
    docker compose -p sonara \
        -f install/compose/docker-compose.yaml \
        -f install/compose/docker-compose.pi.yaml \
        -f install/compose/docker-compose.security.yaml "$@"
}
compose config --quiet
if docker image inspect sonara:dev >/dev/null 2>&1; then docker tag sonara:dev sonara:pre-security; fi
compose build --pull --no-cache sonara
compose up -d --no-build --wait --wait-timeout 180 sonara
sonara_host_port="$(compose config --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)["services"]["sonara"]["ports"][0]["published"])')"
curl -fsS --connect-timeout 5 --max-time 15 "http://127.0.0.1:${sonara_host_port}/healthz"
printf '\n'
docker exec haproxy wget -qO- http://sonara-app:4040/healthz
printf '\n'
printf 'Hardened Sonara is running. Verify the HTTPS headers with the command in the update guide.\n'
