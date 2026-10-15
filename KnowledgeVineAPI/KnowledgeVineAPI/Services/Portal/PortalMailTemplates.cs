using System.Net;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using KnowledgeVineAPI.Services.Mail;

namespace KnowledgeVineAPI.Services.Portal;

public sealed class PortalMailTemplates(IConfiguration configuration, IHostEnvironment environment)
{
    public Uri PublicOrigin()
    {
        var address = configuration["Portal:PublicUrl"];
        if (string.IsNullOrWhiteSpace(address) && environment.IsDevelopment()) address = "http://localhost:4200";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var origin) || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment)
            || (origin.Scheme != "https" && !(environment.IsDevelopment() && origin.Scheme == "http")))
            throw new InvalidOperationException("Configure the public KnowledgeVine portal address.");
        return origin;
    }

    public EmailContent Render(PortalAccountMessage message)
    {
        var origin = PublicOrigin();
        if (message.Link.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority)
            || message.Link.AbsolutePath != (message.InitialSetup ? "/portal/setup-password" : "/portal/reset-password")
            || !string.IsNullOrEmpty(message.Link.UserInfo) || !string.IsNullOrEmpty(message.Link.Query)
            || string.IsNullOrEmpty(message.Link.Fragment))
            throw new InvalidOperationException("Account links must use the configured school portal and a token fragment.");
        var teacher = message.Role == PortalRoles.Teacher;
        var subject = !message.InitialSetup ? "Reset your Knowledge Vine password"
            : message.IsResend ? "Your new Knowledge Vine password-setup link"
            : teacher ? "Welcome to your Knowledge Vine teacher portal" : "Welcome to the Knowledge Vine portal";
        var heading = !message.InitialSetup ? "Let’s get you signed in again"
            : message.IsResend ? "A fresh start, with a fresh link"
            : teacher ? "Welcome to your teacher portal" : "Welcome to the family";
        var intro = !message.InitialSetup ? "You requested a password reset for your Knowledge Vine portal account. Choose a new password using the link below."
            : message.IsResend ? "Here is a new link to finish setting up your portal account. Earlier password-setup links no longer work."
            : teacher ? "Your school portal account is ready to set up. It will be your place to share short daily updates with families."
            : "Your school portal account is ready to set up. It will be your place to see the daily updates shared for your linked child or children.";
        var button = message.InitialSetup ? "Set up your password" : "Reset your password";
        var greeting = string.IsNullOrWhiteSpace(message.FirstName) ? "Hello," : "Hello " + message.FirstName.Trim() + ",";
        var login = new Uri(origin, "/portal/login").AbsoluteUri;
        var support = configuration["Portal:Mail:SupportEmail"] ?? configuration["EmailSettings:FromEmail"] ?? "info@knowledgevinemontessori.co.za";
        if (!MimeKit.MailboxAddress.TryParse(support, out var supportAddress))
            throw new InvalidOperationException("Configure a valid school support email.");
        support = supportAddress.Address;
        var teacherNote = message.InitialSetup && teacher
            ? "The school can assign students to you after you have set your password. You do not need to sign in first for assignment to become available." : "";
        var safety = message.InitialSetup
            ? "After setting your password, you will return to the sign-in page. Sign in using the email address below."
            : "If you did not request this, you can ignore this email. Your password has not changed.";
        var guide = message.InitialSetup && !teacher ? InstallationGuide(origin) : null;
        var guideText = guide is not null && message.InitialSetup && !teacher
            ? "\nPhone installation guide: " + guide.AbsoluteUri + "\n" : "";
        var text = $"Knowledge Vine Montessori\n{heading}\n\n{greeting}\n{intro}\n\n{button}:\n{message.Link.AbsoluteUri}\n\n"
            + $"This link expires after 24 hours and can be used once.\n{safety}\n\nSign-in email: {message.Recipient}\nSign in: {login}\n"
            + (teacherNote.Length > 0 ? "\n" + teacherNote + "\n" : "") + guideText
            + $"\nNeed help? Contact {support}.\nDiscover. Explore. Grow.\n";
        var guideHtml = guide is not null && message.InitialSetup && !teacher
            ? $"<p style=\"margin:24px 0 0\"><a href=\"{H(guide.AbsoluteUri)}\" style=\"color:#184f36\">Phone installation guide</a></p>" : "";
        var html = $$"""
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="referrer" content="no-referrer"><title>{{H(subject)}}</title></head>
<body style="margin:0;padding:0;background:#faf8f2;color:#244634;font-family:Arial,Helvetica,sans-serif">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#faf8f2"><tr><td align="center" style="padding:32px 16px">
<table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;background:#fffdf8;border:1px solid #dce2d2;border-radius:16px">
<tr><td style="padding:28px 32px;background:#e8eee4;border-radius:16px 16px 0 0;border-bottom:1px solid #dce2d2">
<p style="margin:0;color:#184f36;font-family:Georgia,serif;font-size:27px">Knowledge Vine</p><p style="margin:7px 0 0;color:#5f705b;font-size:11px;letter-spacing:2px">MONTESSORI · SCHOOL PORTAL</p></td></tr>
<tr><td style="padding:32px;font-size:15px;line-height:1.7">
<h1 style="margin:0 0 24px;color:#184f36;font-family:Georgia,serif;font-weight:normal;font-size:31px;line-height:1.2">{{H(heading)}}</h1>
<p style="margin:0 0 12px">{{H(greeting)}}</p><p style="margin:0 0 26px">{{H(intro)}}</p>
<table role="presentation" cellpadding="0" cellspacing="0"><tr><td bgcolor="#184f36" style="border-radius:8px"><a href="{{H(message.Link.AbsoluteUri)}}" style="display:inline-block;padding:14px 24px;color:#ffffff;text-decoration:none;font-weight:bold;font-size:14px">{{H(button)}} →</a></td></tr></table>
<p style="margin:20px 0 8px;font-size:13px;color:#57634f">This link expires after <strong>24 hours</strong> and can be used once.</p>
<p style="margin:0 0 24px;font-size:13px;color:#57634f">{{H(safety)}}</p>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f2f4eb;border-radius:8px"><tr><td style="padding:16px;font-size:13px">
<strong>Sign-in email</strong><br>{{H(message.Recipient)}}<br><a href="{{H(login)}}" style="color:#184f36">Open the sign-in page</a></td></tr></table>
{{(teacherNote.Length > 0 ? "<p style=\"margin:22px 0 0;font-size:13px\">" + H(teacherNote) + "</p>" : "")}}{{guideHtml}}
<p style="margin:26px 0 8px;font-size:12px;color:#57634f">If the button does not open, copy this full link into your browser:</p>
<p style="margin:0;font-size:11px;line-height:1.6;overflow-wrap:anywhere;word-break:break-all"><a href="{{H(message.Link.AbsoluteUri)}}" style="color:#315c41">{{H(message.Link.AbsoluteUri)}}</a></p>
<p style="margin:28px 0 0;padding-top:22px;border-top:1px solid #dce2d2;font-size:13px">Need a hand? Email <a href="mailto:{{H(support)}}" style="color:#184f36">{{H(support)}}</a>.</p>
</td></tr><tr><td align="center" style="padding:0 24px 26px;color:#8c733e;font-family:Georgia,serif;font-size:18px">Discover. Explore. Grow.</td></tr>
</table></td></tr></table></body></html>
""";
        return new(message.Recipient, subject, text, html);
    }

    private Uri? InstallationGuide(Uri origin)
    {
        var configured = configuration["Portal:Mail:InstallationGuideUrl"];
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (!Uri.TryCreate(origin, configured, out var guide) || !string.IsNullOrEmpty(guide.UserInfo)
            || guide.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority)
            || !guide.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The installation guide must be a PDF on the school portal.");
        return guide;
    }

    private static string H(string text) => WebUtility.HtmlEncode(text);
}
