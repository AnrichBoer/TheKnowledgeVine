# Knowledge Vine portal: local development

Portal steps 1–7 verified locally through 15 October 2026 with PostgreSQL 18.6 and .NET 10. Steps 1–7 are also deployed and verified on the separately configured testing VM after explicit user authorization. Step 8's local checks and guide draft are complete; physical-device HTTPS checks and final guide publication remain pending. Its PWA navigation fix is local only. See [step-8 results](portal-step-8-testing.md).

The local database is **knowledgevine_local**, on **127.0.0.1:5432**. The setup connects only to the `postgres` maintenance database and this dedicated database. It never connects to `GOTR_Live`. The PostgreSQL administrator username on this machine is `postgres`.

## Private settings when cloning

The working API `appsettings.json` is ignored by Git because it contains environment-specific SMTP credentials. The original local file is retained. For a fresh checkout, copy the safe template before configuring the API:

```bash
cp KnowledgeVineAPI/KnowledgeVineAPI/appsettings.example.json KnowledgeVineAPI/KnowledgeVineAPI/appsettings.json
```

The template has an empty SMTP password. Supply real credentials privately through .NET user secrets or deployment environment files; never commit them. Database configuration continues to use the setup scripts and ignored/user-secret storage below. Build outputs, environment files, recovery archives and authentication/encryption keys are excluded from Git. Removing an already tracked credential file does not erase older Git history; an SMTP credential previously committed to the public repository needs separate rotation and corresponding environment updates.

## What exists

- Identity accounts: `users`, `roles`, `user_roles`, `user_claims`, `role_claims`, `user_logins`, `user_tokens`.
- School data: `students`, `parent_students`, `daily_updates`.
- EF migration tracking: `__EFMigrationsHistory`.
- Seeded application roles: Admin, Teacher and Parent. The original admin account exists; there are no students.
- Thirteen PostgreSQL functions and five triggers enforce and support the agreed workflows.
- Shared login/setup/reset screens, protected role entry and authenticated API sessions.
- Admin Accounts/Students screens, teacher/parent creation, resends, readiness-gated assignments, parent links, deactivation/reactivation and enrollment controls. See [step-4 testing](portal-step-4-testing.md).
- Teacher draft/share/correction screens and read-only parent daily feeds. See [teacher testing](portal-step-5-testing.md) and [parent testing](portal-step-6-testing.md).

The exact columns, keys, indexes and checks are recorded in [the initial migration](../KnowledgeVineAPI/KnowledgeVineAPI/Data/Migrations/20261014131006_InitialPortalSchema.cs). [The exported schema SQL](../KnowledgeVineAPI/KnowledgeVineAPI/Data/Migrations/portal-schema.sql) includes all three migrations and seeded roles. The planning document remains [the implementation plan](portal-implementation-plan.md).

| Functions | Purpose |
| --- | --- |
| `portal_user_has_role`, `portal_user_is_ready`, `portal_teacher_is_ready` | Active-role and completed-account-setup checks. |
| `portal_assign_teacher` | Admin assignment; the teacher must have an active Teacher role, confirmed email and a password hash. Passing a null teacher unassigns the student. |
| `portal_link_parent`, `portal_unlink_parent` | Admin-managed child access; parent links can be prepared before password setup. |
| `portal_save_daily_update` | Save/share one note per child/date; require the current version for changes and preserve first-share/original-author metadata. |
| `portal_parent_updates` | Current South African calendar year's shared notes for a ready parent's linked enrolled children, newest first, with child filtering and bounded pagination. |
| `portal_teacher_students` | Active assigned students and date-specific draft/shared/not-started state; default date uses Africa/Johannesburg. |
| `portal_stamp_updated_at`, `portal_check_teacher_assignment`, `portal_check_parent_link`, `portal_check_daily_update` | Trigger functions maintaining timestamps and enforcing relationship/note rules for direct writes as well as helper calls. |

All functions run with the calling database role's privileges. The runtime login is an application service account with CRUD permissions; it is **not** an end-user security boundary. Actor IDs come from the authenticated server session; each API operation enforces its role/student permissions. Never accept an arbitrary teacher, parent or admin actor ID from a browser request.

Private drafts are excluded from the parent-feed function. Shared notes require nonblank text; drafts can be blank. The writing target is about three normal paragraphs, with the existing working limit of 2,000 PostgreSQL characters. Shared notes stay shared after corrections. Student/date/original-author fields cannot be changed on an existing note. Referenced students and accounts cannot be deleted accidentally through cascading school-data relationships.

The user has since confirmed full retention of dated notes, parent access limited to the current South African calendar year, and immediate loss of a child's parent access when that child leaves. Step 6 applies `20261015065839_ParentFeedAccess` locally, replacing the parent-feed function with year/enrollment filters. Parent APIs apply the same rules to all feed/direct-note reads. Older notes remain stored. Released migration resources are unchanged; the separately authorized VM deployment also applied this migration. Authentication now enforces the confirmed 30-minute inactivity timeout without remembered login, and initial setup links expire after 24 hours.

## Credentials and keys

| Login | Local purpose |
| --- | --- |
| `knowledgevine_migrator` | Owns only the new KnowledgeVine database and applies schema changes. |
| `knowledgevine_app` | Runtime table CRUD/function execution, read-only migration history; no schema CREATE, superuser, database creation, role creation or row-security bypass privileges. |

Generated credentials are in the ignored `.local/portal/database.json`, with owner-only access. Development connection strings are also in .NET user secrets for `knowledge-vine-portal-local`; they are not added to appsettings or committed. The PostgreSQL administrator password is not saved by these scripts.

Persistent Identity protection keys are configured at `.local/portal/keys`. Keep this directory across application restarts. Production needs its own persistent key volume and database credentials; do not copy development secrets to the server.

## Setup and migration commands

Run from the repository root. This machine is already configured; these commands document how to reproduce it.

```bash
dotnet tool restore
python3 scripts/portal_database.py create --admin-user postgres
ASPNETCORE_ENVIRONMENT=Development dotnet run --project KnowledgeVineAPI/KnowledgeVineAPI --no-launch-profile -- --portal-migrate
python3 scripts/portal_database.py grant-runtime
python3 scripts/portal_database.py status
```

The create command prompts for the local administrator password. It does not replace existing KnowledgeVine credentials or reset an existing database. It stops if the existing database has an unexpected owner, or a KnowledgeVine login exists without the saved local credential file. The migration command validates the local database before connecting and only runs in Development. Normal API startup does not migrate automatically.

For future model changes:

```bash
dotnet ef migrations add DescribeTheChange --project KnowledgeVineAPI/KnowledgeVineAPI --output-dir Data/Migrations
ASPNETCORE_ENVIRONMENT=Development dotnet run --project KnowledgeVineAPI/KnowledgeVineAPI --no-launch-profile -- --portal-migrate
python3 scripts/portal_database.py grant-runtime
python3 scripts/export_portal_schema.py
```

Review generated changes before applying them. Keep versioned SQL resources immutable once released; future function changes belong in a new resource and migration. The exported SQL has a target guard before any schema writes. If manually using it, use `psql -v ON_ERROR_STOP=1` and an explicit KnowledgeVine database target.

## Start the local API

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5222 dotnet run --project KnowledgeVineAPI/KnowledgeVineAPI --no-launch-profile --artifacts-path /tmp/knowledge-vine-portal-build
```

- Database readiness: http://127.0.0.1:5222/health/database
- Existing API documentation: http://127.0.0.1:5222/swagger

The database health route is available only in Development. It checks connectivity and unapplied migrations and returns no school data. There are no public student/note endpoints yet. The existing contact email service and SMTP settings are retained; local database verification does not send email. Frontend contact and portal requests now use same-origin `/api` routing. The development proxy points to the local API; the VM nginx proxy points to its own API, rather than the live website.

## Start the frontend and use the portal

In a second terminal, from the repository root:

```bash
cd knowledevinefront
npm start -- --host 127.0.0.1 --port 4200
```

Open [the local portal login](http://localhost:4200/portal/login). Use `anrich@knowledgevinemontessori.co.za` and the password supplied by the user. The public site's Parent Portal link opens the same page. A successful admin login opens `/portal/admin`; Teacher and Parent roles have their own protected entry screens. These screens now include admin account/student management, teacher notes and the read-only parent feed.

The Angular proxy forwards `/api/**` to `http://127.0.0.1:5222`, removing `/api`, so portal requests and cookies share the frontend origin. It changes no production hosting configuration. Keep both local processes running during preview.

| API path | Behaviour |
| --- | --- |
| `GET /portal/auth/csrf` | Issues the private antiforgery cookie and readable `XSRF-TOKEN` request cookie. Angular sends `X-XSRF-TOKEN` on same-origin mutations. |
| `POST /portal/auth/login` | Email/password login for an active, confirmed account with a portal role. |
| `GET /portal/auth/session` | Returns the signed-in account, permitted roles and expiry; does not renew the session. |
| `POST /portal/auth/activity` | Explicit user activity renews the 30-minute idle window. |
| `POST /portal/auth/logout` | Clears the current browser session cookie. |
| `POST /portal/auth/forgot-password` | Generic account-recovery response; generates a local preview for an eligible account. |
| `POST /portal/auth/setup-password` / `reset-password` | Validates and consumes the account-bound link, updates the password and returns to login. |
| `GET /portal/access/admin`, `/teacher`, `/parent` | Authenticated role checks used by entry guards. |
| `GET /portal/parent/children` | Ready Parent's linked enrolled children, current school year and server time/year boundary. |
| `GET /portal/parent/children/{child}/updates?before=YYYY-MM-DD&limit=20` | Current-year shared notes, newest first, with exclusive school-date cursor and limit 1–50. |
| `GET /portal/parent/children/{child}/updates/{note}` | One shared note with the same family/enrollment/year checks; otherwise 404. |

Portal API responses use `Cache-Control: no-store`; private data is not written to localStorage, sessionStorage or an offline cache. Authentication cookies are HttpOnly, SameSite Strict and nonpersistent; outside Development they are always Secure and require HTTPS. Each request rechecks account readiness, roles and the security stamp. Server expiry is authoritative. The browser renews only for trusted foreground input, rather than unattended background reads.

Five unsuccessful password attempts lock an account for 15 minutes. A successful reset clears that lockout. Password setup/reset requires 12–128 characters with uppercase, lowercase, a digit and a symbol. The initial admin's existing password remains unchanged. Deactivating an account, changing its roles or successfully resetting its password invalidates existing authenticated access.

## Local account-email previews

Development defaults to a preview sender. Recovery creates owner-readable HTML and plain-text files in `.local/portal/mail-preview`, next to the configured key directory; this directory is ignored by Git and is not exposed by the API or Angular server. It is created on the first eligible request. These files contain live account-bound setup/reset links: treat them as private and do not commit or publish them.

Step 3 now connects the mail interface to the existing school SMTP settings and uses the same branded templates for previews and delivery. Outside Development, SMTP is the default and missing configuration returns HTTP 503. Development SMTP requires an explicit recipient allowlist; the VM allows the original admin's approved address and disables contact-form delivery. One authorized reset test was accepted by SMTP without changing the account password. See [email delivery settings and checks](portal-email-delivery.md).

Pending accounts receive 24-hour setup links; resending replaces the previous link. Completed accounts receive a password-reset link. Links carry `userId` and `token` in the URL fragment, which does not reach HTTP access logs. The page removes that fragment from browser history and keeps it only in memory; after reloading the page, reopen the original email link. Successful completion is single-use and redirects to login without automatic sign-in. Creating/inviting new parents and teachers belongs to step 4.

## Verify

```bash
python3 scripts/test_portal_database.py
dotnet ef migrations has-pending-model-changes --project KnowledgeVineAPI/KnowledgeVineAPI
python3 scripts/portal_database.py status
```

The school-data PostgreSQL tests use the restricted runtime login and fictional records in transactions that are rolled back. They cover setup eligibility, private drafts, linked parents/siblings, revoked access, admin-only helper operations, valid note text, duplicate prevention, corrections, SQL and EF concurrency, relationship protection and runtime permissions. Bootstrap and HTTP authentication tests use the migration login to create session-local temporary Identity tables that disappear on connection close; they never change persisted user accounts. Bootstrap tests verify Identity hashing, duplicate/conflicting accounts, preserved password changes/deactivation, role-grant rollback and a competing admin appearing during password entry. Full HTTP authentication checks verify CSRF, cookies, roles, logout, idle expiry, activity renewal, account/role revocation, setup-link expiry/replacement/reuse, password-reset revocation, tampered/wrong-account tokens, lockout and mail failures. The contact regression tests use a fake email service; authentication tests capture fictional messages without SMTP. Plain `dotnet test` skips integration checks unless their explicit local test connections are supplied.

The latest step-8 run passes **131 backend tests (none skipped), 103 frontend tests and 5 production-worker integration tests**. The complete HTTP workflows include API host recreation using persisted test keys and the same PostgreSQL connection. Mail tests verify templates, MIME alternatives, private previews, recipient restrictions, invitation retries, reset expiry and the optional parent welcome/resend guide URL. The frontend production build succeeds within its bundle budgets. Earlier browser verification covered admin login, role routing, logout, password-help, the authorized VM SMTP reset request and the admin/teacher/parent screens. Actual iPhone/Android HTTPS installation checks remain pending; see [step 8](portal-step-8-testing.md).

The schema was previously verified to match the migrations, and migration reruns required no new migrations. The exported SQL also built an empty schema and reran successfully inside a transaction that was rolled back. One persisted user and no students remain after the tests.

The running API returned HTTP 200 and `ready` from its database health check, HTTP 200 for Swagger, and HTTP 400 for an empty contact submission at `/Email/SendEmail`. The invalid submission was rejected before email sending.

## Initial admin: completed locally

The original admin is `anrich@knowledgevinemontessori.co.za`, active, email-confirmed and assigned Admin. Identity verified the user's supplied password against the stored hash. No plaintext password was added to configuration, documentation or source.

Run from the repository root:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project KnowledgeVineAPI/KnowledgeVineAPI --no-launch-profile --artifacts-path /tmp/knowledge-vine-portal-build -- --bootstrap-admin
```

The command prompts twice without echoing the password during first creation, then verifies the stored hash and role before committing. Defaults are the original email and display fields `Anrich` / `Admin`; override names with `--first-name` and `--last-name` if desired. An existing admin returns without requesting a password. Password command arguments are rejected. Protected redirected input can provide the two password lines; never put the password in shell arguments/history. The command uses the runtime KnowledgeVine connection and does not start a web server or send email.

The [bootstrap implementation](../KnowledgeVineAPI/KnowledgeVineAPI/Data/PortalAdminBootstrap.cs) creates the account atomically through Identity. Password input happens before the transaction; a database-scoped advisory lock and a second account check prevent competing bootstrap commands from creating separate first admins. A repeat run was verified to preserve every persisted account field, including hash and security stamp. Normal application startup does not invoke bootstrap.

An existing admin blocks creation of another bootstrap admin, and application startup never resets credentials. Additional admins may be added later through controlled account/role management; authorization must use the Admin role rather than restrict every admin operation to the original email. Parent/teacher invitations retain their normal 24-hour setup links.

## Next development and deployment work

Local steps 1–7 are implemented with backups deferred at the user's request. Step 8's local verification and two-page guide draft are complete. The remaining device checks need a phone-reachable HTTPS preview; the final PDF and its welcome-email link stay unpublished until those checks and native captures are complete. See [the step-8 results and remaining work](portal-step-8-testing.md). Production setup remains step 9.

Steps 1–7 run in the Oracle VirtualBox VM after the user's separate deployment authorizations, with a separate database, credentials, key volume and original admin. Teacher/parent/PWA workflows and phone-width layouts were checked in earlier phases; the original admin and existing student were preserved and disposable test data removed. Open [the VM login](http://127.0.0.1:8803/portal/login); use its current password and this host instead of `localhost` to keep VM cookies separate from the laptop preview. Teacher/parent invitations are private test previews, while admin resets retain SMTP. The step-8 PWA navigation fix has not been deployed. Future VM deployments need explicit authorization. See [the VM deployment guide](../deploy/vm/README.md). Production remains unchanged.

Before production rollout, provision KnowledgeVine's dedicated PostgreSQL service, persistent storage and protection-key volume, and implement/test scoped database backups and restoration. Account-session and retention policies remain the decisions recorded in the plan.

The [backup/restore plan](portal-implementation-plan.md#backup-and-restore-plan) is deferred until going live. No backup scripts, dumps, restore containers or scheduled jobs have been created. Backup storage, scheduling and recovery verification will be settled during release preparation, rather than blocking local development now.

The API and tests now target **.NET 10**, matching the local SDK/runtime. The Linux x64 publish profile was updated as well and remains self-contained. A framework-dependent deployment needs .NET 10 in KnowledgeVine's own API runtime image; a self-contained deployment bundles its runtime and needs the appropriate executable launch and OS dependencies. Verify the KnowledgeVine Dockerfile/entry point before selecting the release mode. No server, container or live-site changes were made during local setup.

GOTR keeps its current images, containers and .NET version. Build/recreate only KnowledgeVine services; preserve KnowledgeVine's previous image for rollback. This release does not replace the host's .NET installation, restart shared Docker services or modify GOTR/shared server configuration. See the implementation plan's .NET deployment boundary for details.

Step 6 was subsequently deployed/tested after explicit authorization. Its guarded parent-feed migration and API/frontend were deployed without replacing the database container or configuration/keys. Parent API/SQL/browser, revocation and phone-layout checks passed; all temporary records and credentials were removed, and preexisting rows match the baseline exactly. See [step-6 VM verification](portal-step-6-testing.md#vm-deployment-and-verification--15-october-2026).

## PWA development and verification

Step 7 is deployed after explicit authorization. The worker is disabled during `ng serve`; build the production app for caching/update checks. `CHROME_BIN=/usr/bin/google-chrome npm run test:pwa` (after `npm run build`, from the frontend folder) now runs five real-worker checks on a loopback fake server with a disposable browser, without the database or SMTP. All 103 frontend unit tests also pass. Step 8 fixed a scope-relative navigation pattern and added a fresh-navigation/server-failure regression check with an outside-scope control. Private APIs still bypass the worker, changes are never queued and private responses are not cached. This new fix is local only. See [step-8 results and the guide draft](portal-step-8-testing.md); [step 7](portal-step-7-testing.md) records the earlier VM deployment.
