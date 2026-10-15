# Step 4: admin accounts and student links

Implemented locally and deployed to the testing VM on 14 October 2026. This step adds Students and Accounts screens, fixed-role teacher/parent creation, invitation resend, parent-child links, one ready teacher per child, account deactivation/reactivation and enrollment status. No schema migration is needed: the existing tables and database rules support these operations.

Verification: 109 backend and 50 frontend tests pass; the production frontend build passes its budgets. Local browser checks cover creation/linking, pending-teacher restrictions, private previews/resends and desktop/390px layouts. VM readiness/deep-link/unauthenticated-access checks pass. On 15 October 2026, the user signed into the VM with their current password and the authenticated browser checks below passed. No credential was reset.

[Parent form screenshot](portal-designs/admin-step4-add-parent-desktop.jpg) uses fictional local browser-test data.

## Authenticated VM verification — 15 October 2026

Using only fictional accounts and children, verified:

- Student creation without an assigned teacher; parent creation with a child already linked; adding a sibling and seeing the same links from the student editor.
- Teacher creation as Pending setup, with no password or child selector on the form and Assign students disabled until setup is completed.
- Private invitation previews with the VM setup URL and focus inside the dialog; resends, including a usable fresh preview after teacher deactivation/reactivation. No invitation emails were sent.
- Case-insensitive duplicate-email rejection across Parent/Teacher forms, with an Open existing account link and the entered values preserved.
- Teacher deactivation/reactivation and enrollment changes. Marking a child Left school retains the child record and parent links.
- Two-window stale-save rejection, with the second window's typed name retained and the first window's saved value preserved.
- Account cards at a 390px viewport without horizontal overflow. Default-width account and parent-detail views were also inspected. This is browser viewport testing; actual phone installation remains step 8.

Successful password setup, Ready-teacher assignment and session revocation remain covered by the passing backend integration tests; this browser pass did not choose a password for a fictional account.

Removed only the two fictional accounts, two fictional children, their links and fifteen private invitation-preview files after verification. The VM again has the original ready admin and no students. The user's signed-in session and password were preserved. No rebuild, deployment, real email, schema change, production/GOTR operation or backup work was performed during this verification pass.

Saved proof uses these now-removed fictional records: [VM accounts](portal-designs/admin-step4-vm-accounts.jpg), [390px account cards](portal-designs/admin-step4-vm-accounts-mobile.jpg), and [parent links](portal-designs/admin-step4-vm-parent-links.jpg).

## Try the workflow

Use the [VM login](http://127.0.0.1:8803/portal/login) on the laptop and sign in as `anrich@knowledgevinemontessori.co.za` with your **current VM portal password**. A password changed during step-3 testing remains changed; deployment never resets it. Local development uses [localhost:4200](http://localhost:4200/portal/login) and its separate account.

Use fictional names and unique email addresses such as `demo.teacher@example.invalid` and `demo.parent@example.invalid`. Do not reuse the admin email to invite a teacher or parent: an existing identity is shown for review and no duplicate account or extra role is silently created.

1. **Students → Add student:** enter fictional first/last names, leave the teacher unassigned and save. The enrolled student appears in the list.
2. **Accounts → Add parent:** enter names/email, select that child and choose **Send invitation**. Expect Pending setup and a saved child link. You may select siblings, or leave the selector empty and link children later.
3. **Accounts → Add teacher:** enter names/email and choose **Send invitation**. There is no password field or child selector. The account remains Pending setup, with **Assign students** disabled.
4. On either pending account, choose **Preview invitation**. This testing environment saves invitations privately instead of sending teacher/parent emails. The preview contains the actual setup link for that fictional account; it expires after 24 hours. Use **Open password setup** to choose a password and return to sign in. For these manual tests, open/copy the setup link into a separate browser/private window to keep the admin window signed in—completing setup signs out the browser that completes it. Do not share setup links or screenshots containing them.
5. Return to the admin Accounts page or refresh it. The completed account now shows **Ready**, without needing its first login. For the teacher, choose **Assign students**, open the student, review the preselected teacher and save. Only active, ready teachers appear in the selector.
6. In **Manage student**, add another parent link or remove one and save. In **Manage account** for a parent, link a second child and choose **Save child links**. Verify the same links from both screens. Multiple parents and siblings are supported; teacher assignments do not automatically create parent links for accounts with both roles.
7. On an active teacher account, choose **Deactivate account**, then confirm. The account becomes Inactive and its teacher assignments are cleared. Existing sessions and setup links are revoked; child links and notes remain stored. Reactivation restores eligibility after setup, but does not restore old teacher assignments automatically. A reactivated pending account requires **Resend invitation**.
8. Open **Manage student**, uncheck **Currently enrolled**, and save. Expect **Left school**, with the student record and links retained. Rechecking the box and saving enrolls the child again. The parent daily-feed access checks arrive in step 6; there are no daily-note screens to test yet.

## Resends and error handling

- **Resend invitation** is offered only for active pending accounts. It replaces the old setup link, including when delivery subsequently fails. Completed accounts use normal password recovery.
- A duplicate email returns **Open existing account**, with the entered form details retained. Open that account to review its role and links.
- Blank/whitespace names and invalid email addresses are rejected. Names have a 100-character limit.
- If invitation delivery fails, the account and saved child links remain; the admin sees a delivery failure and can resend. SMTP acceptance is reported as acceptance, not proof of inbox delivery.
- Save one student from two open browser windows. The second stale save must ask you to refresh and retain the unsaved form values. Parent-link edits also check the original selection, including changes made from the other management screen.
- Existing linked inactive parents/left-school children remain visible for review or unlinking. New student-side parent links require an active parent; pending setup is allowed.
- Administrator access cannot be deactivated through this first-version screen. Creating additional admins/role changes remains a separate controlled flow.

## Test delivery and deployment

Local development defaults to private previews. The VM uses Development mode with `Portal:Mail:PreviewInvitations=true`, so teacher/parent invitations and pending-account recovery are previews. Admin password recovery still uses the existing SMTP configuration and approved admin recipient. No additional real email was authorized or sent for step 4.

Invitation previews are available only through an authenticated Admin endpoint in Development when invitation preview mode is enabled. Files remain outside the webroot with private permissions; responses are no-store. Expired, replaced, inactive and completed setup links are not presented as usable previews. Production ignores the invitation-preview override, uses SMTP and returns no preview data. The PDF link remains deferred to step 8.

Future VM deployments still need the user's explicit go-ahead. This step's deployment was specifically authorized after the whole step was built and checked. Production/Erebor, GOTR, host services and backup/restore work remain outside this change.
