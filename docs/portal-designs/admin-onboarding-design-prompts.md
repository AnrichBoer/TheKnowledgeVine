# Admin onboarding design prompts

Generated with the built-in image generation tool. These are planning mockups, using the existing Knowledge Vine logo and original admin Students screen as visual references. Account names and example.com addresses are illustrative.

## Accounts overview

Use case: ui-mockup. Preview-only redesign of the Knowledge Vine Montessori ADMIN portal. Input image 1 is the exact existing logo; retain its wordmark and leaf. Input image 2 is the existing admin Students screen; match its cream, forest green and muted gold palette, header, Students/Accounts navigation, thin borders and readable serif headings with sans-serif form text. This is the same small product, not a new brand or a full school-management suite. Flat front-on desktop screen, landscape about 1800x1200, crisp readable interface, subtle neutral surround, no device props or browser chrome. Use fictional example names and reserved example.com emails only. Small outer footnote 'Design concept · Example accounts'. No real passwords, student photographs, charts, payments, attendance, marketing tools, technical database details or editable email-template builders.
Primary request: Show the Accounts overview of the revised admin portal, establishing the invitation and account-readiness workflow.
Header: supplied logo left, 'Admin portal' centre, 'School admin' and 'Sign out' right. Second navigation row: 'Students' and selected 'Accounts'.
Main heading 'Accounts'. Supporting text 'Invite teachers and parents, then manage their access.' Small account search field labelled 'Find an account'. Two clear top-right actions: outlined 'Add teacher' and filled green 'Add parent'.
A polished simple table has these exact column headings: 'Name', 'Email', 'Role', 'Setup status', 'Actions'. Four rows:
1. 'Thandi Dlamini', 'thandi.dlamini@example.com', 'Teacher', a soft green 'Ready' badge, enabled action 'Assign students'.
2. 'Nadia Patel', 'nadia.patel@example.com', 'Teacher', a muted gold 'Pending setup' badge, a text action 'Resend invitation' and a distinctly disabled grey 'Assign students' action.
3. 'Sam Jacobs', 'sam.jacobs@example.com', 'Parent', green 'Ready' badge, action 'Manage children'.
4. 'Jo Jacobs', 'jo.jacobs@example.com', 'Parent', muted gold 'Pending setup' badge, actions 'Resend invitation' and 'Manage children'.
Use small initials avatars, very readable text, modest row height and plenty of space between actions. A quiet informational strip underneath says exactly 'Teachers become available for student assignment after completing password setup.' A small status legend explains 'Ready: password set' and 'Pending setup: invitation not completed'. Do not imply that clicking or merely viewing an invitation makes an account ready. No teacher student assignment before password setup. No 'Invitation accepted' checkboxes and no actual password input on this overview.
This is a realistic implementable MVP with one overview and simple actions, not multiple overlapping mockup screens.

## Add teacher

Use case: ui-mockup. Preview-only redesign of the Knowledge Vine Montessori ADMIN portal. Input image 1 is the exact existing logo; retain its wordmark and leaf. Input image 2 is the existing admin Students screen; match its cream, forest green and muted gold palette, header, Students/Accounts navigation, thin borders and readable serif headings with sans-serif form text. This is the same small product, not a new brand or a full school-management suite. Flat front-on desktop screen, landscape about 1800x1200, crisp readable interface, subtle neutral surround, no device props or browser chrome. Use fictional example names and reserved example.com emails only. Small outer footnote 'Design concept · Example accounts'. No real passwords, student photographs, charts, payments, attendance, marketing tools, technical database details or editable email-template builders.
Primary request: Show the complete 'Add teacher' page in the revised admin portal. This is the form BEFORE a new teacher account is created and its invitation is sent. Keep it very simple and usable.
Same header and Students/Accounts tabs, Accounts active. A small back link 'Back to accounts'. Main heading 'Add teacher'. Supporting sentence 'Send an invitation so your teacher can create their password.'
Main area: one large form card on the left and a smaller quiet guidance panel on the right. Form fields ONLY:
'First name' with example value 'Nadia'.
'Last name' with example value 'Patel'.
'Email address' with example value 'nadia.patel@example.com'.
A small read-only label or chip 'Role: Teacher' rather than a role dropdown.
Below the fields show a discreet email icon and helper text 'An email with a password-setup link will be sent to this address.'
Bottom form actions: outlined 'Cancel' and prominent green 'Send invitation'. These are the only main buttons. No admin-entered password, confirm-password field, timetable or student picker.
The right guidance panel heading 'What happens next' with a small understated three-step list:
'1. Teacher receives the invitation.'
'2. Teacher sets their password.'
'3. You can then assign students.'
Add a small sage note below: 'Student assignment becomes available after password setup.' This is a workflow rule, not a switch admin can bypass. The teacher uses the same portal login as parents.
Do not show a success toast before the invitation has been sent. No actual email sending; this is a static design proposal. No open modal obscuring the form. Comfortable field widths, visible labels and clear focus on the single action.

## Add parent

Use case: ui-mockup. Preview-only redesign of the Knowledge Vine Montessori ADMIN portal. Input image 1 is the exact existing logo; retain its wordmark and leaf. Input image 2 is the existing admin Students screen; match its cream, forest green and muted gold palette, header, Students/Accounts navigation, thin borders and readable serif headings with sans-serif form text. This is the same small product, not a new brand or a full school-management suite. Flat front-on desktop screen, landscape about 1800x1200, crisp readable interface, subtle neutral surround, no device props or browser chrome. Use fictional example names and reserved example.com emails only. Small outer footnote 'Design concept · Example accounts'. No real passwords, student photographs, charts, payments, attendance, marketing tools, technical database details or editable email-template builders.
Primary request: Show the complete 'Add parent' page in the revised admin portal. This is the form BEFORE a new parent account and its child links are saved and the invitation is sent.
Same header and Students/Accounts tabs, Accounts active. Small back link 'Back to accounts'. Main heading 'Add parent'. Supporting sentence 'Create an account, link their children and send a welcome email.'
Main area: large left form card and smaller right guidance panel. Identity fields:
'First name' value 'Alex'.
'Last name' value 'Botha'.
'Email address' value 'alex.botha@example.com'.
Small read-only chip 'Role: Parent'.
Then a well-spaced section 'Link children'. One selected child row shows a simple initials circle 'LB', name 'Liam Botha' and a small remove icon. Beneath it a searchable picker with placeholder 'Find another child'. Helper text 'You can link more than one child to this parent.' Do not show birthdays, addresses, student IDs or photo uploads.
Under the form a short helper 'The welcome email includes password setup and the installation guide.'
Bottom actions: outlined 'Cancel' and filled forest-green 'Send invitation'.
Right guidance panel 'What the parent receives' with three concise labelled lines and simple line icons:
'A link to create their password'
'The shared portal login page'
'A PDF guide for iPhone and Android'
Quiet small note below 'After signing in, they see updates only for their linked children.'
Parents may have children linked before they finish account setup. This differs from teacher assignment, which is gated until the teacher completes setup. Do not add any teacher-selection or teacher-assignment fields to this parent form. No admin-entered passwords and no built-in PDF editing tool. Keep the design focused on simple account creation, child links and the welcome invitation.
