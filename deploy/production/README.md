# KnowledgeVine production operations

First release: **20261015-114126**, Erebor, 15 October 2026. See [the deployment record](../../docs/portal-step-9-deployment.md). These are operator instructions, not commands to run automatically.

## Boundaries

Run server commands from **`/root/KnowledgeVine`** with Compose project **`knowledgevine`**. Preserve the external `knowledgevine_knowledge-network` network and dedicated database volume. Keep ports **127.0.0.1:8082** API and **127.0.0.1:8083** web; PostgreSQL has no published port. No hard CPU/RAM limits were selected.

Never run global Compose down/cleanup/prune operations, restart Docker or host PostgreSQL, query host/client databases, or alter GOTR containers, images, networks, files or volumes. The only reviewed outside-project write is the school nginx virtual host. Validate nginx and use a graceful reload, checking GOTR afterwards. Do not change shared/global or GOTR nginx configuration.

## Layout and release preparation

- Active compose: `/root/KnowledgeVine/docker-compose.yml`; root `.env` selects the release.
- Immutable release directory: `/root/KnowledgeVine/releases/20261015-114126/` (API publish, browser build, nginx config, Dockerfiles, schema and operator helpers).
- Protected `.local/production/`: generated PostgreSQL credentials, raw Compose env files, persistent portal keys, backup sets and release evidence. Never print or commit these files. Compose `env_file` **raw** format preserves existing SMTP special characters.
- `database.py configure/provision` are guarded initial provisioning tools, not an automatic application-startup migration path. Keep app and migration accounts separate; review future migrations and back up first. Do not rerun configure casually: it reads the preserved old school SMTP source.
- Initial admin was created with `docker compose ... run --rm --no-deps -T api --bootstrap-admin`, supplying the password through protected stdin. The image already has its dotnet entrypoint. Do not put passwords in arguments/logs or bootstrap on every startup. Existing admin/password changes must be preserved.
- Production API uses framework-dependent .NET **10.0.13** in its own image; no host runtime change. The existing KnowledgeVine nginx **1.31.6** base was preserved. Version/digest references and old images remain available.

For another release, first get the user's deployment authorization, refresh the inventory/backup, build/test locally, sanitize published appsettings, stage and verify complete artifacts, and review the actual school proxy diff. The first-release cutover helper is baseline-specific and should not be reused blindly. Never overwrite active browser assets piecemeal. Keep rollback images/assets and DB/key material.

## Routine inspection

On Erebor:

```bash
cd /root/KnowledgeVine
docker compose -p knowledgevine ps
python3 -B releases/20261015-114126/scripts/database.py status
```

Avoid raw `docker inspect`, environment dumps or Compose `config` output in shared logs: those may expose secrets. The read-only `scripts/inspect.py` emits a sanitized inventory, including protected configuration hashes and GOTR metadata, without querying any database.

## Manual backup and isolated restore verification

```bash
cd /root/KnowledgeVine
python3 -B releases/20261015-114126/scripts/maintenance.py backup
```

This prints the new private backup directory and checksums. It backs up only `knowledgevine_live` via **`docker exec knowledge-db pg_dump`**, plus persistent authentication keys/private configuration/release compose/environment/school proxy. It never connects to host PostgreSQL. Keep a quiet verification window so record counts do not change during the restore comparison.

Use the exact new directory printed by that command:

```bash
python3 -B releases/20261015-114126/scripts/maintenance.py restore-check /root/KnowledgeVine/.local/production/backups/YYYYMMDD-HHMMSS
```

The placeholder must be replaced with a real KnowledgeVine backup directory. The helper verifies hashes, restores to a temporary PostgreSQL container with **no network or published ports**, checks schema/account counts and recoverable keys/config, and removes only its own temporary container/anonymous volume. This is verification, not a live restore command.

Copy successful recovery sets off-server through SSH and encrypt them using the separate private local recovery key before placing them in `/home/anrich/dev/backups/knowledgevine`. The first-release encrypted copies use Python cryptography Fernet; decrypt with the saved key to recover the tar.gz containing the dump, configuration/key archive, checksums and restore result. Keep that key safe and separate from copies of the archive. A lost key cannot decrypt the backup. No recurring scheduler, retention cleanup or encryption upload service is configured.

## Scoped application rollback

For this first release, the prior app assets were never overwritten. Recovery files are in:

`/root/KnowledgeVine/.local/production/releases/20261015-114126/previous/`

1. Check GOTR and the current KnowledgeVine state. Preserve any new KnowledgeVine DB/key/config data with a backup before rollback.
2. Restore the saved `docker-compose.yml` to the project root. Restore the saved root environment file if present; for this release `root-env-was-absent` records that the old root `.env` did not exist. Preserve the current release env privately before removing that release selector.
3. Confirm the original default API/web image tags still match the saved `knowledgevine-*:pre-20261015-114126` image IDs. Use those preserved images if a later build has moved a default tag.
4. Recreate **only `api` and `web`** with `docker compose -p knowledgevine up -d --no-deps --no-build api web`. Do not use `down`, `--remove-orphans` or volume removal. Leave the separate new PostgreSQL container/volume and keys intact.
5. Restore `previous/school-nginx.conf` to the school vhost only; validate `nginx -t`, then `systemctl reload nginx`. Check both school and GOTR HTTPS sites and compare GOTR container/service identities.

The `cutover.py` helper applied this scoped rollback automatically on its initial mount-order comparison stop, before the successful release. A portal/schema rollback needs a separately reviewed recovery plan; do not restore over live student data casually.

## Release completion checks

Verify school routes and live build hashes, login/logout and private API 401/no-store boundaries, Secure HTTPS cookies, persisted DB/keys and API-restart session persistence. Check manifest/worker content type, `/portal/` scope and update headers. Confirm loopback app ports/unpublished database and actual resource use. Compare GOTR container IDs/images/state/start/restarts/mounts/ports/networks/configuration hashes and shared service identities. Keep the user's actual phone tests separate from desktop browser verification.

The final guide URL is deliberately unpublished until real phone screenshots/testing are complete. Keep `Portal:Mail:InstallationGuideUrl` unset until the PDF is verified. Future VM deployments still require the user's explicit go-ahead.
