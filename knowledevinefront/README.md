# Knowledge Vine Montessori frontend

Angular 20, Angular Material, and SCSS. The public pages are `/home`, `/about`, and `/contact`.

## Local preview

```bash
npm ci
npm start -- --host 127.0.0.1 --port 4200
```

Open http://localhost:4200. Contact and portal requests use the same-origin `/api` proxy to the local API. Viewing the preview does not send an enquiry. No production email credentials or hosting configuration were changed.

## Portal preview

The shared sign-in page is [http://localhost:4200/portal/login](http://localhost:4200/portal/login). Start the local .NET API on `127.0.0.1:5222` as described in [the portal development guide](../docs/portal-local-development.md). `proxy.conf.cjs` forwards same-origin `/api/**` portal requests to that API.

Step 2 includes login, initial password setup/reset, password help and protected Admin/Teacher/Parent entry screens. Steps 4–7 add account/student management, teacher notes, the parent feed and the PWA. Sessions expire after 30 minutes of inactivity, with no remembered login; private portal responses are not stored offline. Promotional banners and the open-day popup stay outside the portal.

Account recovery defaults to private local email previews during laptop development. Step 3 enables SMTP on the VM for the approved admin test recipient. See [email configuration](../docs/portal-email-delivery.md). The [implementation plan](../docs/portal-implementation-plan.md) records the current scope, completed checks and remaining phases.

Steps 1–7 are deployed to [the Ubuntu VM preview](http://127.0.0.1:8803/portal/login), with its own database and authentication keys. The public login role label remains removed as requested. See [the VM guide](../deploy/vm/README.md). Use `127.0.0.1` for the VM and `localhost` for this laptop preview to keep their cookies separate.

## Open day

Edit `src/app/open-day.config.ts` to update the event, enquiry message, timing, and dismissal period. Dates include the South African UTC offset explicitly.

The invitation opens after 1.8 seconds, defers while a visitor is typing in a form, and remembers dismissal on that browser for seven days. The banner reopens it at any time while the event is active. Both banner and dialog expire when the event ends. A distinct event ID gives a future promotion its own dismissal history.

The original flyer is preserved in `design/open-day-2026-10-31.png`. The popup links to its compressed web version and uses accessible HTML for the essential event information.

## Checks

Steps 1–7 are implemented locally and deployed/verified on the VM. See [admin testing](../docs/portal-step-4-testing.md), [teacher testing](../docs/portal-step-5-testing.md), [parent testing](../docs/portal-step-6-testing.md) and [PWA testing/screenshots](../docs/portal-step-7-testing.md). Current checks: **103 frontend unit tests and 4 production-worker integration tests pass**; the production build is within budget. The existing 127 backend tests passed in step 6; no API changes were needed for step 7.

The installable **Knowledge Vine Portal** launches `/portal/` in standalone mode. The service worker is enabled in production builds only, scoped to `/portal/`, and caches public app assets only. Private API calls explicitly bypass it and retain server no-store headers. Offline writes are not queued; parent feeds clear and teacher unsaved text stays only in the current tab. App updates wait for unfinished work. The VM loopback preview supports desktop PWA checks; actual iPhone/Android HTTPS installation and the PDF guide remain step 8. Future deployments need explicit authorization.

```bash
npm run build
CHROME_BIN=/usr/bin/google-chrome npm test -- --watch=false --browsers=ChromeHeadless
CHROME_BIN=/usr/bin/google-chrome npm run test:pwa
```

The production build leaves the existing Google Fonts links in place instead of downloading fonts during compilation. The frontend tests mock outgoing enquiries and do not send email.

## Images

The website loads compressed WebP assets. Originals are retained for future editing. To regenerate the web versions with Python and Pillow:

```bash
python3 scripts/optimize_images.py
```
