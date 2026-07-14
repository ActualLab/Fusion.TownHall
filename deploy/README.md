# Deployment

Hosts TownHall as a public website on the shared VM, behind Cloudflare:

- `townhall.actuallab.net` → TownHall (`townhall-app`)
- `townhall-db` → PostgreSQL for the app; not exposed anywhere

## Topology

```
Browser ─HTTPS─> Cloudflare (proxied) ─HTTPS─> edge Caddy :443 ─HTTP─> app :8080 ──> db :5432
```

The **edge Caddy** is the one from the BoardGames deployment on the same VM: it
owns ports 80/443, terminates TLS with a Cloudflare wildcard Origin cert
(`*.actuallab.net`), and reverse-proxies each subdomain to the matching
container. The app container publishes no ports and joins that Caddy's network
(`boardgames_default`, referenced as the external `edge` network).

**PostgreSQL** (`townhall-db`, `postgres:18` — same version as the local
`docker-compose.yml`) publishes no ports either and sits on the stack-internal
network, so it's reachable only from `townhall-app`. To access it, SSH to the
VM and run:

```bash
docker exec -it townhall-db psql -U postgres townhall
```

Data is persisted on the host in `/var/lib/townhall/postgres` (the standard
FHS location for service state), so it survives redeploys and container
recreation. The app applies EF Core migrations (`src/TownHall.Db/Migrations`)
on startup.

## First-time host setup

```bash
git clone https://github.com/ActualLab/Fusion.TownHall /opt/apps/townhall
sudo mkdir -p /var/lib/townhall/postgres
cd /opt/apps/townhall/deploy
docker compose -f docker-compose.prod.yml up -d --build
```

Add the subdomain to the edge Caddyfile (already done in the BoardGames repo's
`deploy/Caddyfile`) and to Cloudflare DNS (proxied A record → VM IP).

## Auto-deploy on push

A systemd timer polls `origin/main` every minute and rebuilds + restarts when
it moves. No GitHub secrets or inbound webhooks.

```bash
sudo cp systemd/townhall-deploy.* /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now townhall-deploy.timer
```

Force a deploy immediately: `deploy/deploy.sh --force`.

## Notes

- The Postgres major version is pinned (`postgres:18`) in both compose files;
  bump both together and plan a data migration — the data dir persists.
