# Step 5: teacher daily updates

Implemented locally and **deployed/tested on the VM** on 15 October 2026, after the user explicitly authorized this deployment. Steps 1–5 now run in both environments. Future VM deployments still require the user's explicit go-ahead.

Teachers now have an assigned-student worklist and a daily-note editor. The editor supports private drafts, sharing, corrections, a 2,000-character counter and version conflicts. No schema migration or new dependency is required. The parent feed is step 6; sharing currently saves the shared state for that upcoming feed. It sends no email or push notification.

## Prepare a fictional teacher locally or on the VM

Use [the local login](http://localhost:4200/portal/login), with the local API on port 5222 and Angular on 4200, or [the VM login](http://127.0.0.1:8803/portal/login). Each environment has its own accounts and passwords; use the current password for that environment. The admin can create/invite a fictional Teacher in the chosen environment, then test the steps below. An Admin-only account does not have Teacher access.

1. Sign in as the local admin and use **Accounts → Add teacher** with a unique fictional email such as `demo.teacher@example.invalid`.
2. Open **Preview invitation**. Complete that teacher's password setup in another browser/private window so the admin remains signed in. The teacher chooses their own password; use the existing setup rules (12–128 characters, uppercase, lowercase, number and symbol).
3. Refresh Accounts. The teacher should show **Ready**. Create a fictional student and assign that teacher. Add another student if testing search and status totals.
4. Sign into the teacher account at the same local login. Accounts with additional roles can choose **Teacher view** from their other portal view; Admin-only accounts cannot open the teacher feature.

Local and VM teacher/parent invitations remain private previews. Do not send real invitations or use actual child information for testing.

## Daily workflow

1. **Your students:** only enrolled children assigned to this teacher appear. The school date defaults to the API's current date in Africa/Johannesburg. The selector accepts today and earlier dates; future dates are rejected by the UI and API. Search by first/last name. Each child shows **Not started**, **Draft** or **Shared** for that date.
2. **Write a note:** enter roughly three short paragraphs. Fewer paragraphs are fine. The counter and maximum are 2,000 characters. Select **Save draft**; expect the Draft badge, saved time and All changes saved. An empty draft is allowed, but a shared note cannot be blank.
3. Return to the list or reopen the same child/date. The saved draft and date-specific status should persist. Another teacher, a parent and an Admin-only account cannot read or write this teacher's notes through these endpoints.
4. Choose **Share with parents**, review the confirmation and select **Confirm sharing**. Keyboard focus moves to the confirmation button. Expect Shared and the first-share time. Draft controls become **Save correction**. The parent-reading screen follows in step 6.
5. Change the shared text and choose **Save correction**. It remains Shared, keeps its original author and first-share time, and shows the latest editor and Edited time in South African time. A newly assigned ready teacher may correct the child's existing note; the previous teacher loses read/write access after reassignment.
6. Change the date. Earlier days have independent notes; saving never moves a note between dates. Leaving school removes the child from the teacher worklist and blocks further note access; stored history remains.

## Failures and unsaved text

- **Connection/save error:** typed text and its original expected version stay in the editor. Retry after connectivity returns. A failed share does not report success.
- **Concurrent save:** open the same child/date in two windows. Save a change in the first, then try the second. The second is rejected with its typed text retained. **Compare latest saved note** displays the newer server text separately. **Use latest saved text** asks before replacing the unsaved copy; **Continue with my text** explicitly bases the teacher's text on the reviewed newer version. A later concurrent change still causes another conflict.
- **Session ended:** idle expiry or an API 401 returns to login, holding the unsaved note only in this tab's memory. Login displays a generic recovery message, without showing the child or note text. Signing into the same Teacher account returns to the same child/date and restores the text. If the saved version changed, compare before saving. Signing into another account clears the recovery copy.
- **Leaving/refreshing:** changing date or leaving an unsaved editor asks before discarding work. The browser also warns before closing/reloading an unsaved editor. Reloading or closing ultimately loses the unsaved copy. Save draft is the durable option. Explicit sign-out clears recovery memory. There is no private localStorage, sessionStorage, IndexedDB or offline-note cache.
- **Assignment/enrollment changed:** a failed read/write reveals no other child's note. An already typed unsaved note stays in the current editor, but Save is blocked after access loss. If access changed during reauthentication, a read-only recovery panel retains only that teacher's previously typed text, without fetching or revealing the student's saved record. The server rechecks the current teacher and enrollment under a student lock for every save.

## Verification performed

- **117 backend tests** pass, none skipped. Teacher HTTP tests cover role/CSRF/no-store boundaries, assigned/enrolled children, date-specific statuses, draft/share/correction metadata, conflicting creates/updates, blank/length limits, South African date boundaries, reassignment and expiry. They use session-local fictional tables with copies of the released note trigger functions. Existing rollback-only database checks still exercise the released public rules through the restricted runtime connection.
- **69 frontend tests** pass. These cover text/version preservation on failures, conflict review, same-account recovery, different-account clearing, idle expiry versus explicit logout, login return navigation, date/discard confirmation, limits, assignment loss and CSRF preparation.
- The production frontend build passes with no budget warnings. The API builds without warnings.
- Local browser verification used one disposable Teacher and two fictional children against the restricted local runtime connection. Verified login, worklist statuses, three-paragraph draft, sharing, correction metadata, two-window conflict comparison, explicit continuation, and return to the unsaved note after session revocation and normal sign-in. The exact 30-minute idle timer is covered by automated tests; the browser check revoked only the disposable teacher's session stamp to exercise recovery immediately.
- Checked the student list and editor at 390px, with no horizontal overflow. Actual iPhone/Android installation testing remains in the PWA/installation phases.
- Signed out and removed only that disposable teacher, both children, their three notes and the temporary credential file. Local data returned to the original admin, zero students and zero notes. No real email, VM/production/GOTR operation, schema change or backup work was performed.

Screenshots use those now-removed fictional records: [student worklist](portal-designs/teacher-step5-worklist.jpg), [daily editor](portal-designs/teacher-step5-editor.jpg), [phone worklist](portal-designs/teacher-step5-worklist-mobile.jpg), [phone editor](portal-designs/teacher-step5-editor-mobile.jpg).

## VM deployment and verification — 15 October 2026

Published the framework-dependent Linux x64 API and production Angular build, preserved the VM appsettings files, and rebuilt/recreated only `api` and `web` under `/home/anrich/KnowledgeVine`. There was no schema migration or admin bootstrap. The PostgreSQL container retained its original identity and stayed healthy; the authentication-key files and appsettings fingerprints are unchanged. Runtime is .NET/ASP.NET Core 10.0.13, nginx 1.31.7 and PostgreSQL 18.6. The existing nginx image tag resolved to its newer patch during this build; no host service or other project was changed.

The predeployment VM contained the original admin, one existing student and no notes. Recorded row fingerprints before deployment and compared them after testing/cleanup: both existing records are unchanged, including the admin's password/security metadata. The VM is back to one user, one student and zero notes.

Using three disposable accounts (two Teachers and one Parent) and three fictional children, verified:

- Normal teacher sign-in; only assigned/enrolled children appear, with independent statuses for different dates. The list excludes note text.
- Three-paragraph draft, sharing confirmation with keyboard focus, shared corrections, preserved first-share/original author and Edited metadata.
- Two-window stale-save rejection and comparison without losing typed text; explicit continuation based on the reviewed version.
- Session-revocation/login recovery restores the exact unsaved text. The 30-minute interval remains covered by automated tests; only the disposable teacher's session stamp was revoked to test recovery promptly.
- Reassignment during reauthentication blocks the old teacher's saved-record access and presents only their previously typed text as a read-only recovery copy. The newly assigned teacher can correct the shared note while preserving its original author, creation time and first-share time.
- Other-teacher read/write attempts return 404; Parent access to Teacher endpoints returns 403. Missing CSRF, overlong text, malformed/future dates are rejected. Private endpoints return no-store; anonymous requests return 401.
- Worklist and editor fit a 390px viewport without horizontal overflow. This does not replace later real-device PWA testing.

Browser testing used `knowledgevine-vm.localhost:8803` to isolate the disposable login from the normal `127.0.0.1:8803` preview. The user dismissed the test note's native leave-page confirmation when browser automation could not access that prompt; this did not require an application change. The normal VM login was reopened afterwards.

Removed only the three disposable accounts, three fictional children, their two dated notes and temporary VM test tools/credentials. No email was sent. Original VM accounts/student data, database volume, authentication keys and SMTP settings were preserved. No production/Erebor/GOTR or backup work was performed.

The served frontend index and API DLL match the laptop's new build hashes. nginx validation, database health, SPA deep links and unauthenticated API checks pass. Saved VM proof uses now-removed fictional data: [worklist](portal-designs/teacher-step5-vm-worklist.jpg), [draft](portal-designs/teacher-step5-vm-draft.jpg), [shared correction](portal-designs/teacher-step5-vm-editor.jpg), [conflict comparison](portal-designs/teacher-step5-vm-conflict.jpg), [phone editor](portal-designs/teacher-step5-vm-editor-mobile.jpg), [phone worklist](portal-designs/teacher-step5-vm-worklist-mobile.jpg).

## API surface

All routes require the Teacher policy and return no-store responses. Writes also require CSRF. The teacher ID comes from the authenticated cookie; it is never accepted from form input.

| Method | API route | Purpose |
| --- | --- | --- |
| GET | `/portal/teacher/students?date=YYYY-MM-DD` | Assigned/enrolled student names and daily status; defaults to the school date. No note text in this response. |
| GET | `/portal/teacher/students/{id}/note?date=YYYY-MM-DD` | Authorized student/date with its existing note or an empty editor. |
| PUT | `/portal/teacher/students/{id}/note` | Save `{updateDate, noteText, share, expectedVersion}`. Use null version for the first save, the returned version thereafter. |

The frontend uses the same-origin `/api` proxy prefix. Account/student locks serialize saves against reassignment, deactivation and competing first saves. Existing database triggers generate version/timestamps and preserve original authorship/first sharing. No parent feed endpoint is added in this step.
