# Portal account emails

Step 3 was implemented and deployed to the testing VM on 14 October 2026. It reuses the existing school SMTP details and provides parent/teacher welcome, parent/teacher resend and password-reset emails, each with HTML and plain-text content.

The user authorized one password-reset test email to `anrich@knowledgevinemontessori.co.za`. The VM request completed successfully after SMTP acceptance over verified TLS. Comparing the account's credential state before and after confirmed that the password hash and security stamp were unchanged. Inbox/spam placement is for the recipient to confirm. No real parent/teacher invitations were sent and no accounts/students were added.

## Configuration

Existing settings remain `EmailSettings:SmtpHost`, `SmtpPort`, `FromEmail` and `Password`. Do not copy their credential values into documentation or source. Optional `EmailSettings:Username` overrides the login username; otherwise it uses `FromEmail`. `EmailSettings:FromName` defaults to **Knowledge Vine Montessori**.

The shared [SMTP transport](../KnowledgeVineAPI/KnowledgeVineAPI/Services/Mail/EmailDelivery.cs) uses MailKit 4.18.1, with the cryptography dependency pinned to patched version 10.0.13. Port 465 uses TLS on connection; other ports require STARTTLS. Certificate validation remains enabled. SMTP failure/cancellation propagates to the caller; no automatic retry can duplicate an accepted message. A failure to close the connection after SMTP accepts the message does not change the send result. The contact service retains its school recipient and enquiry Reply-To address, using the same transport.

| Setting | Behaviour |
| --- | --- |
| `Portal:PublicUrl` | The portal origin used for setup/reset/sign-in links. HTTPS is required outside Development. Test VM: `http://127.0.0.1:8803`. |
| `Portal:Mail:Mode` | `Preview` or `Smtp`. Defaults to Preview in Development and Smtp otherwise. An unknown mode is unavailable. |
| `Portal:Mail:AllowedRecipients` | Optional SMTP recipient allowlist. Development SMTP requires an explicit nonempty list; the VM permits only the approved admin address. It never redirects a parent's/teacher's private link to another mailbox. |
| `Portal:Mail:PreviewInvitations` | Development-only override for private teacher/parent setup previews while retaining SMTP for completed-account password resets. Enabled on the step-4 VM test deployment; ignored outside Development. |
| `Portal:Mail:SupportEmail` | School support contact in templates; defaults to `EmailSettings:FromEmail`. |
| `Portal:Mail:InstallationGuideUrl` | Optional same-origin PDF URL. Included only in parent setup/resend mail when configured. Leave absent until phase 8 publishes and verifies the real guide at `/guides/knowledge-vine-phone-guide.pdf`. The current review draft is outside public assets; do not link it in invitations. |
| `EmailSettings:ContactEnabled` | Defaults to true; the VM sets false so contact-form testing cannot send through the live school mailbox. |

For a local SMTP test, explicitly select Smtp mode and the authorized recipient in protected configuration. Do not enable arbitrary development recipients. Previews are unavailable outside Development, and missing SMTP configuration returns an unavailable result rather than silently producing a preview.

The VM's copied SMTP configuration stays private. Compose now enables portal SMTP for the admin allowlist and disables only contact-form delivery. Changing the allowlist for another real test recipient requires the user's explicit sending instruction. Fake mailboxes in automated tests use captures/previews.

## Invitation integration for step 4

`PortalAccountLinks.SendInvitationAsync(userId, role, resend, cancellationToken)` loads the registered account and requires an active, pending Parent or Teacher with that role. It selects the registered recipient and name on the server, creates a fresh 24-hour setup token, and sends the role-appropriate welcome/resend template. Completed accounts use normal recovery; invitations cannot grant another role or silently replace an existing password.

The step-4 admin endpoints enforce Admin authorization and CSRF, validate roles/student links and commit the account and links before calling the email service. SMTP failure leaves the pending account and links in place so admin can retry. A resend locks the pending account, checks its version and persists the replacement token stamp before delivery, so earlier setup links stop working even if delivery subsequently fails. Merely opening a link does not consume it; successful password setup confirms the email, consumes the link and returns to login. Teacher assignment requires completed setup and an active Teacher role.

Recovery uses reset mail for a completed account and resend instructions for a pending one. Its public response remains generic. Setup and reset tokens both expire after 24 hours and are single-use; successful reset invalidates prior authenticated sessions. Requesting a reset alone does not change the password or invalidate an existing session.

Only account instructions are emailed. Daily notes and student data remain inside the portal. Account/student creation screens are now implemented in phase 4; the PWA/PDF remain later phases. Follow the [step-4 test guide](portal-step-4-testing.md) to use private setup previews for fictional accounts. Preview access is Admin-only, Development-only and verifies that the link is still usable; raw files are not served by nginx or Angular.

## Previews and verification

For user acceptance checks on the existing VM deployment, follow the [step-3 manual test checklist](portal-step-3-testing.md). Password reset is available end to end now; parent/teacher invitation UI testing follows in step 4.

Local previews use the same templates as SMTP and write owner-readable `.html` and `.txt` files into the private `mail-preview` directory beside the configured key directory. Real links contain authentication tokens; never expose these files through the web server or commit them.

Reviewable, fictional samples are in [email-previews](email-previews/parent-welcome.html). Their UUID/token values cannot activate or reset an account. Regenerate them from the repository root:

```bash
KNOWLEDGEVINE_MAIL_PREVIEW_DIR="$PWD/docs/email-previews" python3 scripts/test_portal_database.py
```

The current complete suite passes 109 backend and 50 frontend checks. Mail tests verify role-specific instructions, optional PDF handling, HTML encoding, same-origin fragment links, MIME alternatives, recipient restrictions, private preview permissions, failure/cancellation, supported delivery modes and contact compatibility. HTTP integration checks cover admin onboarding, invitation resend/retry and expired tokens using temporary fictional tables. The original step-3 SMTP test was performed through the VM's password-help screen; automated checks and step-4 invitation previews never send through school SMTP.

Browser previews verify the HTML layout, rather than every email client's rendering. The recipient can review the actual reset email in their mail app. The user-edited public role label is removed from the deployed VM build; their welcome-heading/intro comment was preserved.
