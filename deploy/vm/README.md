# KnowledgeVine testing VM

Steps 1–7 are deployed and verified. Step 7 was deployed/tested on 15 October 2026 after explicit user authorization; future deployments still need explicit authorization. See [PWA testing and VM verification](../../docs/portal-step-7-testing.md). SSH: `anrich@127.0.0.1`, port `2222`, VM name `ubuntu24testenv`. The installed OS is Ubuntu 24.04.5 LTS. All application files are under `/home/anrich/KnowledgeVine`.

Future changes are developed and verified locally. Deploy to this VM only when the user explicitly gives the go-ahead for that deployment; do not upload/rebuild automatically after each change.

Open [the VM portal login](http://127.0.0.1:8803/portal/login) from the laptop. The original admin is `anrich@knowledgevinemontessori.co.za`; use its current VM portal password, including any change made during reset testing. Deployment preserves later password changes. No password is recorded in this guide. Use `127.0.0.1` for this preview and `localhost:4200` for local development so their browser cookies remain separate.

## Layout and changes from the copied live files

| Component | VM setup |
| --- | --- |
| `knowledge-web` | The existing nginx/Angular container structure, listening on VM port 8083. Relative bind paths replace the copied `/root/KnowledgeVine` paths. |
| `knowledge-api` | Latest framework-dependent Linux x64 publish, running in its own .NET 10 image on port 8082. The host binding is loopback only. |
| `knowledge-db` | Dedicated PostgreSQL 18.6 container on the private KnowledgeVine network, with no published database port. |
| Browser access | VirtualBox's `KnowledgeVineWeb` NAT rule forwards laptop `127.0.0.1:8803` to VM port 8083. Existing SSH and HTTP forwarding rules were preserved. |
| API routing | nginx forwards `/api/` to `knowledge-api:8082`, removing `/api`. The existing `/images/` rule uses the same correct API port. Angular deep links resolve to `index.html`. |
| Persistence | PostgreSQL uses `knowledgevine_knowledgevine-postgres`. API protection keys and account-email previews use `.local/vm/portal`, mounted at `/var/lib/knowledgevine`. |

This retains the copied Docker/nginx layout, with changes required for the new portal. It is an **HTTP development preview**, rather than a complete reproduction of production HTTPS. Production must retain HTTPS/Secure cookies and receive its own database, credentials and keys. The VM's installed host PostgreSQL 16 and host nginx configuration were left untouched. No Erebor/GOTR work was performed.

The API runs as the image's non-root application user (UID 1654). Its private directory and key directory are mode 700. Runtime connection settings live in `.local/vm/api.env`; database administrator settings live in `.local/vm/postgres.env`. Generated database credentials are stored in `.local/vm/database.json`. These files are mode 600 and belong only to this VM; never commit or publish them. No local development credentials or accounts were copied to the VM.

The three database roles/settings mirror local development: a dedicated `knowledgevine_local` database owned by `knowledgevine_migrator`, and restricted runtime login `knowledgevine_app`. It has all ten Identity/school tables, EF migration history, thirteen functions, five triggers and the three application roles. The original admin was created through the existing Identity bootstrap. After step-6 verification/cleanup there is one original admin, one preexisting student and no daily notes. The original admin and student match their predeployment row fingerprints.

Development normally requires a loopback database. This VM explicitly sets `Portal__LocalDatabaseOnly=false` for its private `db` service. The KnowledgeVine database-name allowlist remains enforced, and local migration/test tools retain their strict loopback guards. `Portal__TrustedProxyAddresses__0` trusts only this stack's nginx address for forwarded scheme/client-IP headers. Fixed IPs belong to this VM's dedicated `172.28.88.0/24` network.

Step 3 uses the copied SMTP settings for portal mail. Compose sets `Portal__Mail__Mode=Smtp` and allows only `anrich@knowledgevinemontessori.co.za`, the approved test recipient. Step 4 adds `Portal__Mail__PreviewInvitations=true` in Development: teacher/parent setup invitations are private previews in the authenticated admin portal, while the completed admin's password-reset mail keeps SMTP. This override is ignored outside Development. `EmailSettings__ContactEnabled=false` keeps contact-form test delivery off. Local development defaults to private previews. See [email delivery settings](../../docs/portal-email-delivery.md) and [step-4 testing](../../docs/portal-step-4-testing.md). Do not add real recipients/send tests without explicit authorization. Npgsql uses `GSS Encryption Mode=Disable` for this password-authenticated private connection; see [Npgsql's guidance](https://www.npgsql.org/doc/security.html#gss-session-encryption-gss-api).

## Operate the existing deployment

On the VM, run commands from `/home/anrich/KnowledgeVine`:

```bash
docker compose ps
python3 scripts/database.py status
docker compose logs --tail 50 api web
docker compose restart api web
```

To rebuild after replacing the published frontend/API files:

```bash
docker compose build api web
docker compose up -d api web
```

The helper `scripts/database.py` is copied from this directory and refuses to run on another hostname. `configure` prepares/preserves the protected VM credentials; `provision` applies the guarded, idempotent schema SQL at `.local/vm/portal-schema.sql` and runtime grants. It runs SQL only inside the verified KnowledgeVine database container, using `postgres` for narrow provisioning and `knowledgevine_local` for application schema work. No host PostgreSQL administrator access is used.

The idempotent admin bootstrap can run in the API container:

```bash
docker compose exec api dotnet KnowledgeVineAPI.dll --bootstrap-admin
```

An existing admin is preserved without another password prompt. On a fresh database, the command asks for the supplied password twice without echoing it. Never put it in command arguments or an environment file.

Do not use `down -v`, delete the database volume/key directory, or run Docker-wide cleanup. Backups and restore work remain deferred until release preparation, as requested.

## Prepare future builds on the laptop

From the repository root:

```bash
dotnet publish KnowledgeVineAPI/KnowledgeVineAPI -c Release -r linux-x64 --self-contained false --artifacts-path /tmp/knowledgevine-vm-build -o .local/vm/build/api
cd knowledevinefront
npm run build
```

Upload the API build into VM `api/publish` and the frontend `dist/knowledevinefront/browser` into VM `webapp/knowledevinefront/browser`. Preserve the VM's appsettings files and private configuration; clear obsolete build assets only inside those two target directories. Do not upload laptop user secrets or protection keys. Deployment Dockerfiles, nginx configuration and Compose source are retained here for review and reuse.

## Verification

- Frontend and same-origin API routes returned HTTP 200; unauthenticated portal API calls returned HTTP 401 with no-store headers.
- Admin login through nginx, refresh and logout were verified in the browser. An admin request to a Teacher view redirects to the permitted Admin view.
- All three containers were recreated without deleting volumes. The database retained the admin and schema, and the existing browser session still worked after refresh, confirming persistent protection keys.
- Repeating admin bootstrap preserved the existing account. The runtime login is neither a superuser nor permitted to create databases/roles.
- nginx configuration validation passed. API runtime: .NET/ASP.NET Core 10.0.13; nginx: 1.31.7; database: PostgreSQL 18.6. The existing `nginx:alpine` tag resolved to the newer patch during the step-5 build; host services were not changed.
- Current frontend verification: 103 unit tests and 4 production-worker integration checks pass; the production build passes its budgets. The existing 127 backend tests passed in step 6; step 7 has no API changes.
- The user authorized one reset test email to the admin address. SMTP accepted it over verified TLS; comparing the credential state confirmed no password/security-stamp change. The public role-label edit is now included in the VM frontend build.

Steps 1–7 now run here. [Admin management](../../docs/portal-step-4-testing.md) and [teacher daily updates](../../docs/portal-step-5-testing.md) have been verified in the VM. The step-5 deployment recreated only API/web containers, with no migration or bootstrap. The database container/volume, existing records, authentication keys and appsettings were preserved. VM tests covered draft/share/correction, two-window conflicts, exact text recovery after sign-in, assignment loss during recovery, newly assigned teacher corrections, role/CSRF/date/length checks and 390px layouts. Three disposable accounts, three fictional children and their two notes were removed afterwards, along with temporary test tools/credentials. No real email was sent. The parent feed is now implemented and verified in step 6. The PWA is now deployed in step 7; real-phone installation verification and the installation guide remain step 8; registration and additional-role/admin creation are not public flows.

Step-5 build SHA-256 checks match both the laptop artifacts and deployed files:

| File | SHA-256 |
| --- | --- |
| API `KnowledgeVineAPI.dll` | `0e9fd22e67fcabfc4bf5ee84eec4cbc3948513bfb0bb32b82ef6d3d0e65e58ae` |
| Frontend `index.html` | `f6f5dba16b28b8ed5861696dbca068e09c0501f37be703f9ee55eb0a2e64e541` |

## Step 6 deployment

The authorized step-6 deployment added `20261015065839_ParentFeedAccess`, replacing only the parent-feed SQL helper with current-year/enrollment filtering. API/web images were built in KnowledgeVine staging before the targeted restart; only API/web were recreated. The database container ID is unchanged. Dockerfiles, nginx/Compose configuration, appsettings, private credentials/environment and key files match their predeployment fingerprints.

Parent API/SQL/browser checks passed for linked enrolled children, family isolation, drafts/other years/direct-note IDs, newest-first cursor pages, live corrections, empty siblings, departure/unlink revocation, login/logout and 390px layouts. The three disposable accounts, three children, 27 notes, links and temporary test tools/credentials were removed. Every preexisting application-table row fingerprint matches the baseline exactly; there is one original admin, one existing student and no notes. No real emails, production/GOTR or host service/runtime changes were made. The current schema export remains in `.local/vm/portal-schema.sql`.

| Step-6 deployed file | SHA-256 |
| --- | --- |
| API `KnowledgeVineAPI.dll` | `d1ff573e75fa45495acba07d15026620db52cb35552096aabd3b47c58707c253` |
| Frontend `index.html` | `6aa001c8e809aa380ba5ed1b7a0127eb7e5a330623c7a53198966c4ceda75e6f` |

See [the step-6 testing guide and screenshots](../../docs/portal-step-6-testing.md).

## Step 7 deployment

Added the portal-scoped PWA and installed the latest frontend; changed only web nginx headers for `ngsw-worker.js`, `ngsw.json` and `manifest.webmanifest`. The worker is explicitly restricted to `/portal/`, public assets only; API responses bypass it and remain no-store. Manifest/worker files revalidate instead of freezing an old release.

Only the web service was recreated. API/database container IDs, all existing application rows, migration history, credentials/appsettings and authentication keys match their baseline. No migration, bootstrap, test school records or email was needed. A temporary KnowledgeVine-only container validated the new nginx image/config before deployment and was removed. Staging was removed afterwards.

VM checks verified manifest/worker MIME/scope/revalidation headers, byte-for-byte build matches, portal deep links, private 401/no-store and database readiness. Browser checks verified actual worker registration, phone help/16px inputs/44px controls and an update across two deployments: unfinished work blocked reload, then an explicit safe update succeeded. No native application was installed on the laptop.

The current loopback HTTP preview supports desktop PWA checks. Actual iPhone/Android installation/standalone/session/offline/update verification requires the step-8 HTTPS preview; no TLS or host service setup was changed here. See [step-7 testing and screenshots](../../docs/portal-step-7-testing.md).

| Step-7 deployed file | SHA-256 |
| --- | --- |
| Frontend `index.html` | `0d6d36c618dc6937fc761be7da73bdd5d0d6d0d5df0cf35259494ebf5b759531` |
| `ngsw.json` | `efb28177f399da31ea95a7e9406561cf475edf4a7fade97ee37068468af64472` |
| `ngsw-worker.js` | `b40815897c232c1440eb5ff4c0804933ad8f7809ee1f7fd9a824711e383b441b` |
| `manifest.webmanifest` | `408bfc7c436b56ee780bd18bfb0aec9b8623e2723a9618dee77f81835692609d` |
