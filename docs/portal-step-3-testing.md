# Step 3 manual testing

Step 3 provides account-email services. The existing admin's password-reset flow can be tested end to end on the VM now. Parent/teacher account creation and invitation buttons arrive in step 4; their current HTML/plain-text samples are for visual review and have fictional, nonfunctional links.

Use the laptop for these checks: the VM email links point to `http://127.0.0.1:8803`, which refers to the laptop when opened there. Opening the same address on an iPhone refers to the phone, so it cannot reach this VM through those links.

## Reset email and password

1. Open [VM sign in](http://127.0.0.1:8803/portal/login). If you want to check session revocation too, sign in with the current VM admin password in a separate browser or private window and leave it open.
2. In the test window, select **Forgot password?**, enter `anrich@knowledgevinemontessori.co.za`, then select **Send password instructions**. Expect: “If an active account uses this email, password instructions will be sent.” Requesting the email alone does not change the current password or end the existing session.
3. Check the inbox/spam folder for **Reset your Knowledge Vine password** from the school. Verify the name, readable school branding, reset button, fallback link, support address and the 24-hour/single-use instructions. The installation-PDF link is intentionally absent until step 8.
4. Open **Reset your password** from the fresh email on the laptop. Expect **Choose a new password**, with New password and Confirm new password fields. Merely opening the link does not consume it.
5. Try a password shorter than 12 characters: the form should show the requirements. Then try a compliant password with a different confirmation: it should say the passwords do not match. Correct both fields and submit. The required length is 12–128 characters, including uppercase, lowercase, a number and a symbol.
6. Expect a redirect to sign in and “Your password is set. Please sign in.” Log in with the new password: the admin view should open. One attempt with the old password should fail. This changes only the VM account; remember the new VM password. It does not change the local-development, production or Ubuntu/SSH password.
7. Refresh the previously signed-in separate browser/private window. It should require sign-in again after the completed reset.
8. Reopen the same original email link, enter a compliant password and submit again. Expect: “This link is invalid, expired or already used. Request a new link.” Opening a used link can still show the form; rejection occurs on submission. The password must remain unchanged.

The link's token is removed from the address bar after opening. If you refresh its password screen, reopen the full link from the email to load it again. Never share reset links or screenshots containing their full URLs.

## Optional edge checks

- Enter an invalid email format in password help: the form should ask for a valid address.
- Submit `nobody-step3@example.invalid`, which is not a registered account. Expect the same generic response as for the admin, with no email sent.
- Request a fresh link, leave it unused for more than 24 hours, then open it and submit a compliant password. Expect the invalid/expired/used-link message. Expiry is already covered by automated tests, so waiting is optional.

## Parent and teacher email review

Review these fictional samples for wording/layout; their buttons do not activate accounts:

- [Parent welcome](email-previews/parent-welcome.html)
- [Teacher welcome](email-previews/teacher-welcome.html)
- [Parent resend](email-previews/parent-resend.html)
- [Teacher resend](email-previews/teacher-resend.html)

Step 4 will let the admin create fictional test accounts, send setup instructions and verify Pending setup → Ready, setup-link replacement and teacher-assignment eligibility through the UI. Do not invite real families for these development checks. The current VM SMTP allowlist permits only the admin email; other recipients require separately authorized configuration/testing.

Automated checks already cover role-specific messages, plain-text alternatives, expired/reused/tampered links, resend replacement, SMTP failures, recipient restrictions and preservation of pending accounts. The last complete run passed 90 backend tests and 38 frontend tests. Manual checks above focus on the visible email/login experience. This checklist makes no VM changes or sends any email by itself.
