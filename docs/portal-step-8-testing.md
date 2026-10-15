# Step 8: complete verification and phone installation guide

**15 October 2026: local verification complete; actual-phone verification and final publication pending.** The user confirmed there is no phone-accessible HTTPS preview yet and asked us to finish the local checks first. No VM deployment or configuration change was performed for this step.

**Subsequent decision:** the user will perform the actual-device checks on the first controlled live HTTPS deployment, before real-family onboarding. A separate HTTPS preview is no longer required before that deployment. The pending phone matrix and final screenshot/PDF/email checks below still apply. Step 9 is currently planning/read-only inspection; see [the Erebor findings](portal-production-inspection.md). No production deployment has occurred.

## Local results

| Check | Result |
| --- | --- |
| Backend | **131 passing, none skipped**, including three new complete HTTP workflows and parent welcome/resend guide-link checks. |
| Frontend unit tests | **103 passing**. |
| Production service worker | **5 passing**, including a fresh portal navigation during simulated server failure and proof that navigation outside `/portal/` remains uncontrolled. |
| Production frontend build | Passed without budget warnings; initial bundle **438.98 kB**. |
| Installation guide layout | Two-page branded **review draft**, rendered and visually inspected on both pages. Text, links, page count and absence of account-token/loopback links checked. |
| Preserved local state | All existing application/Identity rows, migration rows and authentication-key files match their pre-test fingerprints. One original admin, zero students, zero notes, three migrations remain. |

The backend workflows exercise the real HTTP controllers, Identity, PostgreSQL and released note triggers. Test rows live in connection-local temporary tables and disappear when their connection closes. Mail is captured in memory; **no real SMTP delivery occurs**. Existing database-rule tests use rollback-only transactions.

### Complete workflow coverage

The new `PortalWorkflowHttpTests` joins the previously tested individual features:

1. Admin creates a child, invites a teacher and invites a parent linked to that child. No password is assigned by the admin.
2. The teacher cannot log in or be assigned while Pending setup. Setup consumes the link, does not automatically sign in and rejects reuse.
3. The admin assigns the Ready teacher **before that teacher's first sign-in**. The parent completes their separate link and signs in.
4. The teacher saves a three-paragraph draft. The parent sees the child but neither the draft nor its direct-note URL.
5. Sharing makes the full text visible to that parent. A new HTTP host, service container and DbContexts then load the same saved note. The existing parent session still works with the persisted authentication keys.
6. The teacher corrects the shared note. The parent sees the correction, original author, preserved first-share time and edited status. Parent calls to teacher/admin routes are rejected.
7. The admin marks the child as having left. Parent feed/direct reads and teacher access stop; the stored note and family link remain.
8. A failed invitation leaves one pending account and its child link. After an API restart, resend succeeds. Its setup link survives another restart, completes once and permits login. No duplicate account/link is created. Logout rejects later private reads.
9. Two independently invited families and their teachers cannot read one another's children/notes. A parent feed read at minute 29 does not renew the session; a read after minute 30 is rejected.

These restart tests preserve the **same PostgreSQL connection and persisted test-key directory** while replacing the API host. They verify application restart compatibility; they do not claim a database process/container restart or backup/restore test. Container persistence was checked in the earlier VM phases. Backup/restore remains step 9.

The wider suite also checks wrong/tampered/expired/replaced setup links, resets and old-cookie revocation, lockout, CSRF, stale edits, reassignment, sibling/multiple-parent links, current South African year boundaries, retained older history, text limits and SMTP failure reporting.

### PWA issue found and fixed

The new production-worker navigation check initially failed: cached `/index.html` could be fetched directly, but a new `/portal/parent` navigation during server failure received the failure page. Angular 20.3.17's worker removes the registered `/portal/` scope before matching `navigationUrls`; the old `/portal/**` pattern therefore missed `/parent`.

`ngsw-config.json` now uses the standard `/**` navigation pattern, interpreted within the existing **`/portal/` registration scope**, with API/file/internal-path exclusions. A new portal navigation receives the cached **public application shell** while private API calls still fail instead of replaying notes. A navigation to `/index.html` outside the portal scope remains uncontrolled and receives the server failure. Existing private-canary/cache, no-queued-write and version-update checks all pass.

There are still no data cache groups, background sync or push subscriptions. This fix does not make student notes available offline. First use still needs a successful online visit. It is built locally and **has not been deployed to the VM**.

## Installation guide draft

Review artifact: [two-page phone guide draft](../output/pdf/knowledge-vine-phone-guide-draft.pdf).

The approved logo, green/cream/gold styling, account-setup reminder, four installation steps, support email and session/connectivity reminders are laid out on one iPhone/Safari page and one Android/Chrome page. Instructions were checked against the official Apple and Chrome references below.

Every page says **REVIEW DRAFT - NOT FOR PARENT DISTRIBUTION**. The illustration is an existing actual desktop-browser capture of portal help at phone width, expressly labeled as such. It is **not** a native iPhone/Android installation screenshot. No device versions or native results have been invented. The final phone-accessible address/QR and real installation captures are not yet included.

Rebuild the draft from the repository root with the bundled Python runtime, which provides ReportLab, Pillow and pypdf:

```bash
python3 scripts/build_portal_guide_draft.py
mkdir -p tmp/pdfs/portal-guide
pdftoppm -r 110 -png output/pdf/knowledge-vine-phone-guide-draft.pdf tmp/pdfs/portal-guide/page
```

The draft stays in `output/pdf`, **outside frontend public assets**. The script only produces a draft; it does not copy anything into the website or change mail settings. `Portal:Mail:InstallationGuideUrl` remains unset. Existing parent invitation emails therefore omit the guide link rather than send an unfinished document.

## Actual-phone checks remaining

Before these checks, agree and deploy a **phone-reachable HTTPS preview** within KnowledgeVine, with a trusted certificate, same-origin `/api` and account-link configuration for that exact origin. The current laptop/VM `127.0.0.1` URL points back to whichever device opens it and cannot be used from another phone. No public tunnel, certificate installation, DNS change, host service change or HTTPS deployment was made during local verification.

Use fictional children/accounts and private preview invitations. The user performs password creation/change on their phone. Do not send real parent invitations or include passwords, setup-token URLs, personal email inboxes or real student information in evidence captures.

For **each** platform, record phone model, OS version, browser version, preview origin, frontend build/manifest hash, date, tester, outcome and evidence paths. A desktop viewport is not a substitute.

| Physical-device check | iPhone / Safari | Android / Chrome |
| --- | --- | --- |
| Open HTTPS preview and complete the fictional welcome/setup/login flow | Pending | Pending |
| Install using the actual browser menu/prompt; correct school name and icon | Pending | Pending |
| Open from home screen into standalone `/portal/`; correct role/feed, sign in if required | Pending | Pending |
| Read the full note, switch linked siblings, refresh a correction; comfortable text/touch/keyboard/safe areas | Pending | Pending |
| Leave idle for over 30 minutes, including background/resume; private content clears and reauthentication is required | Pending | Pending |
| Sign out in the tested browser/app context; back/reopen does not reveal notes | Pending | Pending |
| After an online visit, disconnect and launch/refresh; show public shell/connection message without private notes; no automatic save/share on reconnect | Pending | Pending |
| Publish an explicitly authorized second preview build; prompt appears, dirty teacher text blocks reload, safe chosen update loads the new build | Pending | Pending |
| Confirm installation is optional and no push-permission prompt appears | Pending | Pending |

Capture the actual browser installation menu, Add/Install confirmation and standalone demo portal on both devices. Review the captures for private data and retain tested device/version details. Use those captures to replace the draft's browser illustration and confirm menu wording. Browser and installed-app sessions can differ; sign in or out in the context being tested rather than assume cookies transfer between them.

## Final PDF and email publication remaining

After the phone checks pass:

1. Finalize the two-page PDF using the verified native captures. Add the agreed phone-accessible portal address/QR, tested device/version information and remove draft notices. Render and inspect every final page.
2. Keep a downloadable school copy and publish the **final** artifact as `knowledevinefront/public/guides/knowledge-vine-phone-guide.pdf`, yielding the stable same-origin path `/guides/knowledge-vine-phone-guide.pdf` after the authorized frontend deployment.
3. Add a scoped nginx `/guides/` file location with `try_files $uri =404` before enabling the link, so a missing PDF cannot silently become the Angular HTML fallback. Check the signed-out URL returns HTTP 200, `application/pdf`, a `%PDF-` body and the expected file hash. A nonexistent guide must return 404.
4. Only then set `Portal:Mail:InstallationGuideUrl` to `/guides/knowledge-vine-phone-guide.pdf` in the agreed environment. Render a fictional parent welcome **and resend** preview, follow the download on both phones and confirm that teacher invitations and password resets have no guide link. The local template tests already check both MIME alternatives and both parent variants; actual download/delivery is still pending.
5. Keep launch messages paused until this published guide is verified. Any further real SMTP test needs a specifically authorized recipient. Step 8 is complete only after the remaining device, final-PDF and download/email checks are recorded.

## Re-run local verification

From the repository root:

```bash
python3 scripts/test_portal_database.py
```

From `knowledevinefront`:

```bash
CHROME_BIN=/usr/bin/google-chrome npm test -- --watch=false --browsers=ChromeHeadless
npm run build
CHROME_BIN=/usr/bin/google-chrome npm run test:pwa
```

No local migration, admin bootstrap, real email, VM/production/GOTR change, host runtime/service change or backup/restore operation was needed. The existing original admin and all stored local state were preserved.

## References

- [Apple: add a Safari website as an iPhone web app](https://support.apple.com/guide/iphone/open-as-web-app-iphea86e5236/ios)
- [Chrome: mobile installation menu and prompt](https://developer.chrome.com/blog/how_chrome_helps_users_install_the_apps_they_value)
- [Angular: navigation requests and service-worker configuration](https://angular.dev/ecosystem/service-workers/config#navigationurls)
