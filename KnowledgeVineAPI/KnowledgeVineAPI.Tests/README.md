# API, authentication and portal database tests

These tests use a fake email service; they do not connect to SMTP or send real enquiries. They cover successful sending, failed sending, safe error responses, required fields, email validation, and input length limits.

With the .NET 10 SDK/runtime, run the contact and database-target/model checks:

```bash
dotnet test KnowledgeVineAPI/KnowledgeVineAPI.sln --artifacts-path /tmp/knowledge-vine-api-checks
```

For all checks, including PostgreSQL integration tests against **knowledgevine_local** only, run from the repository root:

```bash
python3 scripts/test_portal_database.py
```

This runner reads the ignored local credentials without printing them. School-data tests use the runtime login and roll back their fictional records. Bootstrap and full HTTP authentication checks use the migration login to create connection-local temporary Identity tables, preserving persisted accounts; those tables disappear on connection close. Bootstrap tests cover password hashing, preserved reruns/password changes, refused promotion, atomic rollback and a competing-bootstrap recheck. Authentication tests exercise CSRF, cookie sessions, role checks, idle expiry, activity renewal, logout, account deactivation, setup/reset links, lockout and recovery failures. They capture fictional account messages without SMTP and use temporary protection keys that are removed after the test. A target guard rejects other databases/remote hosts before connecting. Plain `dotnet test` skips PostgreSQL checks unless the explicit local test connections are supplied.

The HTTP fixture finds the API source through `KNOWLEDGEVINE_PROJECT_ROOT`, supplied by the runner, or by walking up from the working directory. `TimeProvider` controls cookie expiry so inactivity tests do not wait 30 minutes.

Step-3 mail checks verify branded template variants, HTML encoding, optional guide links, MIME alternatives, SMTP recipient restrictions, private preview permissions and contact compatibility. They use fake transports/captures, never school SMTP. Invitation retry and expired-reset checks use the same temporary Identity-table fixture. Set `KNOWLEDGEVINE_MAIL_PREVIEW_DIR` to an output directory to export the five fictional HTML/plain-text samples for visual review. Real SMTP testing is a separately authorized operation; one admin reset test was performed through the VM UI.

The separate artifact path keeps verification output out of the repository's previously tracked build directories. See [local setup](../../docs/portal-local-development.md) for migration, health and credential-storage details.

Step-4 admin and step-5 teacher HTTP checks use session-local school tables as well as Identity tables. The fixture copies the released teacher-readiness/note-trigger functions into its temporary schema; it never replaces public functions. Teacher checks cover role/CSRF/no-store boundaries, assigned/enrolled worklists, date validation, drafts/sharing/corrections, author/first-share preservation, optimistic versions, reassignment and expiry. All 127 backend tests currently pass with none skipped through the scoped runner. See [teacher testing](../../docs/portal-step-5-testing.md).

Step-6 parent HTTP checks use those same temporary tables and cover role/read-only/no-store boundaries, family links, multiple parents/siblings, current-year filtering on feed and direct-note reads, retained history, enrollment/unlink revocation, corrections, expiry/deactivation/role changes, bounded cursor pagination and the exact Johannesburg New Year boundary via TimeProvider. Rolled-back runtime SQL checks exercise the newly migrated parent helper, including year boundaries and inactive/unlinked students. See [parent testing](../../docs/portal-step-6-testing.md). The test school date follows the database's current South African date, so year-filter checks remain valid in future years.
