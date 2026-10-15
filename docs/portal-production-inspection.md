# Erebor: KnowledgeVine deployment inspection

**Historical predeployment inspection.** The user subsequently authorized deployment on 15 October 2026. Release `20261015-114126` is now live; see [the deployment record](portal-step-9-deployment.md) for current containers, ports, backups and GOTR preservation evidence. Statements below about pending authorization/no server changes describe the inspection, not the later release.

Inspected **15 October 2026**. Authorization was for read-only inspection, **not deployment**. The user subsequently authorized reading nginx configuration outside the project. Findings are recorded locally; no server configuration, application file, container, database, certificate or service was changed.

The user then expanded read-only access to **the entire server, including GOTR**, to check deployment isolation. The [expanded inspection below](#expanded-read-only-gotr-and-shared-service-inspection) supersedes the earlier reading-scope restriction. Deployment and server changes remain unauthorized.

## Confirmed project boundary

- Host: `erebor.dedicated.co.za`; KnowledgeVine root: `/root/KnowledgeVine`.
- Compose project: `knowledgevine`. Both running containers identify that root and `/root/KnowledgeVine/docker-compose.yml` in their Compose labels.
- Docker network: `knowledgevine_knowledge-network`, bridge, `172.20.0.0/16`, gateway `172.20.0.1`. Exactly two containers are attached: KnowledgeVine web and API.
- Project directories: `api` and `webapp`. During the initial narrower inspection no GOTR folder, container or site configuration was opened; those were subsequently inspected read-only with explicit user authorization. **No PostgreSQL connection or query was made** in either inspection, including to `GOTR_Live`.
- The user permits stopping KnowledgeVine web/API during a future authorized deployment. This inspection did not stop or restart them.

## Current containers and ports

| Container | Compose service | Host mapping | Runtime | Persistence |
| --- | --- | --- | --- | --- |
| `knowledge-web` | `web` | All interfaces, `8083` to container `80` | nginx 1.31.6 | Host frontend directory and nginx file mounted read-only. |
| `knowledge-api` | `api` | All interfaces, `8082` to container `8082` | .NET/ASP.NET Core 9.0.20; Production | No bind mounts or volumes. API/configuration copied into its image. |

Both use `unless-stopped`. Container IDs and start times remained unchanged during inspection; both started on 9 October 2026 and remained running.

The API Dockerfile uses the .NET 9 ASP.NET image and launches `dotnet KnowledgeVineAPI.dll`. Its published runtime configuration includes .NET 9.0.20. The planned .NET 10 portal must receive a compatible **KnowledgeVine-only** image/publish combination; replacing DLLs inside the old image is insufficient. Use the framework-dependent publishing/container approach already verified on the VM. Do not change the host .NET installation or any GOTR image/runtime.

`EXPOSE 8080` in the API Dockerfile is metadata. Its actual `ASPNETCORE_URLS` and published port are **8082**.

## Actual request path and HTTPS

The user authorized reading these files outside the project:

- `/etc/nginx/nginx.conf` - shared/global configuration and include paths.
- `/etc/nginx/sites-enabled/knowledgevinemontessori.co.za` - symlink to `/etc/nginx/sites-available/knowledgevinemontessori.co.za`.
- `/etc/nginx/sites-available/knowledgevinemontessori.co.za` - school-only virtual host.
- `/etc/letsencrypt/options-ssl-nginx.conf` - referenced shared TLS options.

The other enabled site was initially left unread, then read after the user explicitly expanded the inspection to GOTR. No `nginx -T`, reload, restart or global configuration change was performed. No TLS private key was read.

The host nginx is **1.24.0 (Ubuntu)**. The school virtual host already defines:

| Incoming request | Existing upstream |
| --- | --- |
| School HTTPS website, including `/portal/...` after release | `http://localhost:8083` |
| School HTTPS `/api/...` | `http://localhost:8082/...`, with `/api/` removed |
| School HTTP port 80 | Redirect to the same hostname over HTTPS |

Both `knowledgevinemontessori.co.za` and `www.knowledgevinemontessori.co.za` currently serve the website; there is no redirect between those two HTTPS hostnames. Use the agreed `www` origin for account links and verify both entry paths, since host-only authentication cookies do not transfer between them.

TLS hostname/certificate validation passed for `www`. The certificate covers both names and expires **6 December 2026**. The referenced Certbot options enable TLS 1.2/1.3; shared TLS settings need not be changed for this deployment.

### HTTPS forwarding needs a school-only adjustment

Both school `location` blocks pass Host, X-Real-IP and X-Forwarded-For, but **neither sets `X-Forwarded-Proto`**. The new portal must receive the trusted original HTTPS scheme. Planned adjustment, **not applied**:

1. Add `proxy_set_header X-Forwarded-Proto $scheme;` to the school website and API locations.
2. If the Docker ports are bound to IPv4 loopback as proposed below, make these upstreams explicitly `127.0.0.1:8083` and `127.0.0.1:8082/` to avoid `localhost` IPv4/IPv6 ambiguity.
3. Configure the new API to trust the actual upstream proxy address. The observed host-proxy connection reaches the API through the current bridge gateway, `172.20.0.1`; confirm it again at release. **Do not copy the VM's `172.28.88.x` addresses.**
4. Include the school-file diff, configuration validation, graceful nginx reload and rollback in the authorized release procedure. Reading permission does not authorize an edit/reload now. Because host nginx also serves another client, do not restart it or change shared/global or GOTR settings.

The public `/api/` location goes **directly to the API**, bypassing the web container. Adding `/api/` only to the container's nginx file would not fix missing scheme forwarding on this public path.

### Direct public HTTP ports need a reviewed binding change

Read-only requests from the laptop confirmed direct access to:

- API HTTP port **8082**: Kestrel returned 404 for the currently absent portal session endpoint.
- Web HTTP port **8083**: nginx returned the website HTML with HTTP 200.

For the portal release, propose retaining **the same port numbers**, but publishing them on `127.0.0.1` so the HTTPS host proxy is their entry point. Review any intended direct-port dependencies before applying that change. This requires only KnowledgeVine's Compose mappings plus the school upstream-address adjustment above; no GOTR ports or firewall rules need alteration. It has **not** been applied.

## Active frontend and container nginx

Actual web mounts:

| Host source | Container destination |
| --- | --- |
| `/root/KnowledgeVine/webapp/knowledevinefront/browser` | `/usr/share/nginx/html`, read-only |
| `/root/KnowledgeVine/webapp/nginx.conf` | `/etc/nginx/conf.d/default.conf`, read-only |

The host bundle overrides the files copied into the web image. Rebuilding the image alone does not replace those active assets. Stage a complete release before changing the mounted bundle/configuration, and retain the previous bundle for rollback. Avoid serving a mix of old/new assets during cutover; the user has authorized a KnowledgeVine-only interruption during deployment.

The existing container configuration has SPA fallback and an `/images/` proxy to API port **8080**, although the API listens on **8082**. Align that KnowledgeVine route when preparing production. The current entry bundle's contact URL uses the school's non-www HTTPS `/api/Email/SendEmail` endpoint.

There are currently no manifest, service-worker or installation-guide files in the active bundle. Missing manifest requests return SPA HTML with HTTP 200. The new release needs the already-tested worker/manifest MIME, scope and revalidation handling, private-page no-store headers, and explicit 404 handling for missing worker/manifest/guide files. Only enable the PDF email link once the final guide is actually published and verified.

## Database, keys and settings

- Current KnowledgeVine Compose has only `api` and `web`: no database service or persistent volume declaration.
- No `knowledge-db` container or Compose-labelled KnowledgeVine volumes were found.
- The host-published and running API `appsettings.json` match. Neither has a Portal section or ConnectionStrings section; no portal/database environment configuration was present in the API container.
- School SMTP settings are configured, including port 587. Credential values were not output or copied into this document; no email was sent.
- Production therefore needs separately provisioned KnowledgeVine PostgreSQL storage, protected environment-specific credentials, migrations/runtime permissions and persistent authentication keys. Keep its database port unpublished. Do not reuse GOTR's database, role or service, or copy VM demo data/keys/credentials.
- The API currently runs as `appuser`; the tested .NET 10 image uses a different application UID. Prepare ownership/permissions for the new KnowledgeVine key directory deliberately before launch.

## Capacity snapshot

At inspection: **2 CPUs**, approximately **3,969 MiB RAM**, **3,022 MiB available RAM**, **2,048 MiB swap** with about **1,792 MiB free**, and **65.68 GiB available disk space** on the project filesystem. Load averages were low. This is a point-in-time measurement, not a load/capacity guarantee. Plan resource use for the new KnowledgeVine database/builds without changing another client's limits or services.

Docker Engine reports 29.1.3 and Compose 5.0.0. No host package/runtime/service update was performed or is required merely to change KnowledgeVine's .NET container image.

## Read-only verification and limitations

- Public HTTPS home returned 200 and matched the active host-mounted frontend index.
- Public `/api/portal/auth/session` returned 404, consistent with the old API having no portal implementation/configuration.
- A **GET** to the existing public email **POST** endpoint returned 405, confirming the route reaches the API. No POST, contact submission, invitation or SMTP delivery test was made.
- Current container IDs/start times and recorded configuration fingerprints were preserved through the inspection.
- No build, upload, migration, bootstrap, backup, database connection, container stop/recreation, certificate operation, nginx validation/reload or VM operation was performed.

This inspection supplies release-planning facts; it does not mark production readiness or authorize deployment. Next work, when requested, is preparing the reviewed production files, backup/restore/rollback procedure and release checks. The user has chosen to perform actual iPhone/Android checks after the first controlled live deployment and before real-family onboarding. Final guide screenshots/publication and its welcome-email link follow those checks.

## Expanded read-only GOTR and shared-service inspection

Explicitly authorized on **15 October 2026**. Read-only checks covered Docker inventory and runtime settings, GOTR's legacy deployment files and active release metadata, its nginx site, host PostgreSQL service/cluster metadata, network/port/storage mappings and host/container resource use. Credentials and client records were not included in the output. No database connection, SQL query, business API request or deployment script execution was needed.

### Actual running GOTR release

GOTR's active release is **`20261010-104532`**. Four release containers are running:

| Container | Host loopback port | Container port | Purpose |
| --- | --- | --- | --- |
| `gotr-api-20261010-104532` | 18080 | 8080 | Active API |
| `gotr-web-20261010-104532` | 18081 | 80 | Active frontend |
| `gotr-api-fallback-20261010-104532` | 18082 | 8080 | Fallback API |
| `gotr-web-fallback-20261010-104532` | 18083 | 80 | Fallback frontend |

These ports are separate from KnowledgeVine's 8082/8083. The host GOTR site at `/etc/nginx/sites-enabled/gasontherun.conf` routes `gasontherun.online` to 18081 for the website/images and 18080 for `/api/`, and already passes the original HTTPS scheme. School and client use separate `server_name` blocks/certificate paths within the same host nginx service.

The original `gotr-api` and `gotr-web` containers are stopped. Their old `/root/gotr/docker-compose.yml` refers to 8080/8081, and its API Dockerfile still names .NET 9. **Those files do not describe the current running release.** Both active and fallback APIs actually contain **.NET/ASP.NET Core 10.0.12**. Do not start the legacy stack, recreate any GOTR service, change its runtime or remove its active/fallback images during KnowledgeVine work.

### Database protection

Both running GOTR APIs' environment configuration points to **database `gotr` at `172.18.0.1:5432`**. The PostgreSQL 16 `main` host cluster/service is running. This establishes the configured production target without querying the database. The older `GOTR_Live` name from the chat was not assumed to be the active target; whether it also exists was deliberately not queried. **All existing host PostgreSQL databases remain protected.**

KnowledgeVine will use a new, dedicated PostgreSQL container on its own Docker network, with its own database/roles/volume and no host-published database port. Nothing in this plan requires connecting to or changing the existing host cluster, its roles, HBA/configuration, data directory, service or GOTR connection settings. Migration/backup/restore tooling must target the verified KnowledgeVine database container explicitly rather than host port 5432.

### Networks and persistent paths

- GOTR's four running containers are on `gotr_gotr-network`, subnet **172.18.0.0/16**. KnowledgeVine's two containers are on `knowledgevine_knowledge-network`, **172.20.0.0/16**. No container is shared between these networks. Existing default/legacy bridges use 172.17.0.0/16 and 172.19.0.0/16. Reuse KnowledgeVine's current network; do not copy VM addresses or alter GOTR's network.
- The active/fallback frontend mounts are under `/root/gotr/releases/20261010-104532`.
- Both GOTR APIs use `/root/gotr/images` and the active release's operator feature-flags file. Both are protected even though fallback ports are not the currently selected public route.
- KnowledgeVine's mounted application paths are under `/root/KnowledgeVine`; no application bind mount or declared volume is shared with GOTR. Stopped legacy Certbot containers/volumes were inventoried but left intact.

### Shared resources and resulting release constraints

The projects share **host nginx, Docker daemon, CPU/RAM and the underlying filesystem**. `/root/KnowledgeVine`, `/root/gotr`, Docker storage and the PostgreSQL 16 data directory are on the same filesystem. Path/network separation does not remove resource contention or the shared nginx reload boundary.

The wider capacity snapshot still showed approximately **3,027 MiB available RAM** and **65.68 GiB free disk**. The six running containers had no configured hard memory/CPU limits; inspected cgroup memory/CPU limits were unrestricted and reported no OOM events at that point. This is observational evidence, not a stress test or a guarantee under future load.

Production preparation must therefore:

1. Build/publish the application on the laptop, stage the complete KnowledgeVine release, and budget server image-pull/build/backup disk use before cutover.
2. Per the user's subsequent sizing decision, leave hard container CPU/RAM limits **unset for the initial release**. The portal will serve at most 20 students. Check actual resource use during post-deployment verification and revisit limits only if measurements justify them. The earlier limit proposal was optional, not a release requirement; no fixed values were selected. Do not alter GOTR's limits or sacrifice its fallback pair for capacity.
3. Scope Docker operations explicitly to project `knowledgevine` and its exact services. No global stop/down/prune, dangling-image cleanup, volume removal, package upgrade, daemon restart or host PostgreSQL operation is part of the rollout.
4. Limit any later host nginx edit to the reviewed school virtual-host forwarding/upstream changes. Preserve the shared main file, GOTR site/certificate configuration and routing. Validate the complete nginx configuration before an authorized **graceful reload**, then verify both websites and relevant API routes. A reload still involves their shared nginx master and must remain a deliberate release step.
5. Compare GOTR container IDs/images/start times/restart counts, ports, networks, service identities and protected configuration fingerprints around the later release. Check its active public route and retain the unchanged fallback release.

### Preservation verified at completion

All baseline comparisons passed:

- **9 containers**: IDs, image IDs, running/stopped state, start times, restart counts, ports and network addresses unchanged.
- **14 host configuration files**: unchanged fingerprints, including the PostgreSQL configuration/HBA/ident files, both nginx sites, GOTR operator configuration and relevant Dockerfiles/Compose files.
- **2 running GOTR API configuration files**: unchanged fingerprints.
- **5 host units**: identities/state/start timestamps unchanged, covering nginx, Docker, PostgreSQL's wrapper/cluster and the Certbot timer.
- Both public HTTPS front pages returned **HTTP 200** during read-only checks.

No client record was read, no SQL/database connection was made, and no server file, credential, service, image, container, network, volume or database was changed by this inspection. Only these local documentation notes were updated. The inspection found **no direct port/network/application-path collision** with the proposed KnowledgeVine layout; production preparation must still address shared-resource use, backup/restore/rollback and the school-only nginx change before deployment is authorized.
