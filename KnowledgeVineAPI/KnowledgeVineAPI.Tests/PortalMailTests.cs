using KnowledgeVineAPI.Models;
using KnowledgeVineAPI.Services;
using KnowledgeVineAPI.Services.Mail;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace KnowledgeVineAPI.Tests;

public class PortalMailTests
{
    private static readonly Uri Origin = new("https://school.example.test");

    [Theory]
    [InlineData("Parent", false, "Welcome to the family")]
    [InlineData("Teacher", false, "Welcome to your teacher portal")]
    [InlineData("Parent", true, "A fresh start, with a fresh link")]
    [InlineData("Teacher", true, "A fresh start, with a fresh link")]
    public void SetupTemplatesIncludeTheCorrectInstructionsAndNoMissingGuide(string role, bool resend, string heading)
    {
        var template = new PortalMailTemplates(Config(), new Environment());
        var content = template.Render(Message(true, role, resend));
        Assert.Contains(heading, content.TextBody);
        Assert.Contains("24 hours", content.TextBody);
        Assert.Contains("can be used once", content.TextBody);
        Assert.Contains("return to the sign-in page", content.TextBody);
        Assert.Contains("parent@example.test", content.TextBody);
        Assert.Contains("https://school.example.test/portal/login", content.HtmlBody);
        Assert.DoesNotContain("installation guide", content.HtmlBody);
        Assert.DoesNotContain("installation guide", content.TextBody);
        Assert.Equal(role == "Teacher", content.TextBody.Contains("assign students"));
        Assert.Equal(resend, content.TextBody.Contains("Earlier password-setup links no longer work"));
    }

    [Fact]
    public void ResetTemplateIsGenericAndExplainsThatThePasswordHasNotChanged()
    {
        var content = new PortalMailTemplates(Config(), new Environment()).Render(Message(false, "Admin"));
        Assert.Equal("Reset your Knowledge Vine password", content.Subject);
        Assert.Contains("Your password has not changed", content.TextBody);
        Assert.Contains("ignore this email", content.HtmlBody);
        Assert.DoesNotContain("Admin", content.HtmlBody);
        Assert.DoesNotContain("student", content.TextBody);
    }

    [Fact]
    public void HtmlEncodesNamesAndLinkAttributes()
    {
        var content = new PortalMailTemplates(Config(), new Environment()).Render(Message(true) with { FirstName = "<script>&Sofia" });
        Assert.Contains("&lt;script&gt;&amp;Sofia", content.HtmlBody);
        Assert.Contains("&amp;token=fictional-token", content.HtmlBody);
        Assert.DoesNotContain("<script>", content.HtmlBody);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuideLinkIsOptionalAndAppearsOnlyInParentSetupMail(bool resend)
    {
        const string guide = "https://school.example.test/guides/knowledge-vine-phone-guide.pdf";
        var config = Config(new() { ["Portal:Mail:InstallationGuideUrl"] = "/guides/knowledge-vine-phone-guide.pdf" });
        var templates = new PortalMailTemplates(config, new Environment());
        var parent = templates.Render(Message(true, "Parent", resend));
        Assert.Contains(guide, parent.TextBody); Assert.Contains(guide, parent.HtmlBody);
        foreach (var message in new[] { Message(true, "Teacher", resend), Message(false) })
        {
            var content = templates.Render(message);
            Assert.DoesNotContain(guide, content.HtmlBody); Assert.DoesNotContain(guide, content.TextBody);
        }
    }

    [Theory]
    [InlineData("https://another.example.test/guide.pdf")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/guide.html")]
    public void InvalidGuideConfigurationCannotCreateUnsafeEmailLinks(string address)
    {
        var templates = new PortalMailTemplates(Config(new() { ["Portal:Mail:InstallationGuideUrl"] = address }), new Environment());
        Assert.Throws<InvalidOperationException>(() => templates.Render(Message(true)));
    }

    [Theory]
    [InlineData("https://other.example.test/portal/setup-password#token=fake")]
    [InlineData("https://school.example.test/portal/setup-password?token=fake")]
    [InlineData("https://school.example.test/other#token=fake")]
    public void TemplateRejectsWrongOriginWrongRouteOrQueryTokens(string address)
    {
        var templates = new PortalMailTemplates(Config(), new Environment());
        Assert.Throws<InvalidOperationException>(() => templates.Render(Message(true) with { Link = new Uri(address) }));
    }

    [Theory]
    [InlineData("http://school.example.test")]
    [InlineData("https://user:password@school.example.test")]
    [InlineData("https://school.example.test?token=secret")]
    public void ProductionRequiresASafeHttpsPortalAddress(string address)
    {
        var templates = new PortalMailTemplates(Config(new() { ["Portal:PublicUrl"] = address }), new Environment(Environments.Production));
        Assert.Throws<InvalidOperationException>(() => templates.PublicOrigin());
    }

    [Fact]
    public void SmtpMessageContainsBothMimeAlternativesAndOnlyTheRegisteredRecipient()
    {
        var config = Config();
        var content = new PortalMailTemplates(config, new Environment()).Render(Message(false));
        using var message = new SmtpEmailTransport(config).CreateMessage(content);
        Assert.Equal("Knowledge Vine Montessori", message.From.Mailboxes.Single().Name);
        Assert.Equal("school@example.test", message.From.Mailboxes.Single().Address);
        Assert.Equal("parent@example.test", message.To.Mailboxes.Single().Address);
        Assert.Empty(message.Cc); Assert.Empty(message.Bcc);
        Assert.IsType<MultipartAlternative>(message.Body);
        Assert.Equal(content.TextBody, message.TextBody);
        Assert.Equal(content.HtmlBody, message.HtmlBody);
    }

    [Fact]
    public async Task DevelopmentDefaultsToPrivatePreviewsWithoutCallingSmtp()
    {
        var keys = Path.Combine(Path.GetTempPath(), "knowledgevine-mail-test-" + Guid.NewGuid().ToString("N"), "keys");
        try
        {
            var config = Config(new() { ["Portal:DataProtectionKeyPath"] = keys });
            var transport = new FakeTransport();
            var environment = new Environment();
            var templates = new PortalMailTemplates(config, environment);
            var sender = Sender(config, environment, transport);
            Assert.True(sender.IsAvailable);
            await sender.SendAsync(Message(true), CancellationToken.None);
            Assert.Empty(transport.Messages);
            var directory = Path.Combine(Path.GetDirectoryName(keys)!, "mail-preview");
            Assert.Single(Directory.GetFiles(directory, "*.html"));
            Assert.Single(Directory.GetFiles(directory, "*.txt"));
            Assert.Contains("Welcome to the family", File.ReadAllText(Directory.GetFiles(directory, "*.html").Single()));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
                foreach (var path in Directory.GetFiles(directory))
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(keys))) Directory.Delete(Path.GetDirectoryName(keys)!, true); }
    }

    [Fact]
    public async Task DevelopmentSmtpNeedsAnExplicitRecipientAndRejectsOtherAddresses()
    {
        var transport = new FakeTransport();
        var config = Config(new() { ["Portal:Mail:Mode"] = "Smtp" });
        Assert.False(Sender(config, new Environment(), transport).IsAvailable);
        config = Config(new() { ["Portal:Mail:Mode"] = "Smtp", ["Portal:Mail:AllowedRecipients:0"] = "parent@example.test" });
        var sender = Sender(config, new Environment(), transport);
        await sender.SendAsync(Message(false), CancellationToken.None);
        Assert.Single(transport.Messages);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message(false) with { Recipient = "unapproved@example.test" }, CancellationToken.None));
        Assert.Single(transport.Messages);
    }

    [Fact]
    public async Task SmtpFailureAndCancellationAreNotReportedAsSuccessfulDelivery()
    {
        var transport = new FakeTransport { Fail = true };
        var sender = Sender(Config(), new Environment(Environments.Production), transport);
        await Assert.ThrowsAsync<IOException>(() => sender.SendAsync(Message(true), CancellationToken.None));
        transport.Fail = false;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(Message(true), cancellation.Token));
        Assert.Empty(transport.Messages);
    }

    [Fact]
    public async Task ProductionCannotSilentlyFallBackToPreviewOrMissingSmtp()
    {
        var transport = new FakeTransport { IsAvailable = false };
        Assert.False(Sender(Config(), new Environment(Environments.Production), transport).IsAvailable);
        var sender = Sender(Config(new() { ["Portal:Mail:Mode"] = "Preview" }), new Environment(Environments.Production), new FakeTransport());
        Assert.False(sender.IsAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message(true), CancellationToken.None));
        Assert.False(new SmtpEmailTransport(Config(new() { ["EmailSettings:Password"] = "" })).IsAvailable);
    }

    [Fact]
    public async Task ContactMailRetainsItsSchoolRecipientAndReplyToAndReportsFailures()
    {
        var transport = new FakeTransport();
        var service = new EmailService(transport, Config(), NullLogger<EmailService>.Instance);
        var request = new EmailRequest { Name = "Fictional Parent", ContactNumber = "0613041993", Email = "parent@example.test", Message = "A fictional enquiry." };
        Assert.True(await service.SendEmailAsync(request));
        var content = Assert.Single(transport.Messages);
        Assert.Equal("school@example.test", content.Recipient);
        Assert.Equal(request.Email, content.ReplyTo);
        Assert.Contains(request.Message, content.TextBody);
        using var message = new SmtpEmailTransport(Config()).CreateMessage(content);
        Assert.Equal(request.Email, message.ReplyTo.Mailboxes.Single().Address);
        transport.Fail = true;
        Assert.False(await service.SendEmailAsync(request));
        var disabled = new EmailService(transport, Config(new() { ["EmailSettings:ContactEnabled"] = "false" }), NullLogger<EmailService>.Instance);
        Assert.False(await disabled.SendEmailAsync(request));
        Assert.Single(transport.Messages);
    }

    // Explicit output enables visual review using fictional links, never account tokens.
    [Fact]
    public void ExportFictionalTemplatePreviewsWhenRequested()
    {
        var directory = System.Environment.GetEnvironmentVariable("KNOWLEDGEVINE_MAIL_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var templates = new PortalMailTemplates(Config(new() { ["Portal:PublicUrl"] = "http://127.0.0.1:8803" }), new Environment());
        foreach (var (name, initial, role, resend) in new[]
        {
            ("parent-welcome", true, "Parent", false), ("teacher-welcome", true, "Teacher", false),
            ("parent-resend", true, "Parent", true), ("teacher-resend", true, "Teacher", true),
            ("password-reset", false, "Admin", false)
        })
        {
            var content = templates.Render(Message(initial, role, resend) with
            { Link = new Uri($"http://127.0.0.1:8803/portal/{(initial ? "setup-password" : "reset-password")}#userId=00000000-0000-0000-0000-000000000000&token=fictional-token") });
            File.WriteAllText(Path.Combine(directory, name + ".html"), content.HtmlBody);
            File.WriteAllText(Path.Combine(directory, name + ".txt"), content.TextBody);
        }
        Assert.Equal(5, Directory.GetFiles(directory, "*.html").Length);
    }

    [Fact]
    public async Task ExplicitVmInvitationPreviewDoesNotChangeResetSmtpAndCannotEnableProductionPreviews()
    {
        var root = Path.Combine(Path.GetTempPath(), "knowledgevine-invitation-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = Config(new() { ["Portal:DataProtectionKeyPath"] = Path.Combine(root, "keys"),
                ["Portal:Mail:Mode"] = "Smtp", ["Portal:Mail:PreviewInvitations"] = "true",
                ["Portal:Mail:AllowedRecipients:0"] = "parent@example.test" });
            var transport = new FakeTransport(); var environment = new Environment();
            await Sender(config, environment, transport).SendAsync(Message(true), CancellationToken.None);
            Assert.Empty(transport.Messages);
            var previews = new PortalAccountPreviewMail(config, environment, new(config, environment));
            Assert.True(previews.InvitationPreviewEnabled);
            Assert.NotNull(await previews.LatestAsync("PARENT@example.test", CancellationToken.None));
            await Sender(config, environment, transport).SendAsync(Message(false), CancellationToken.None);
            Assert.Single(transport.Messages);
            environment.EnvironmentName = "Production";
            Assert.False(previews.InvitationPreviewEnabled);
            Assert.Null(await previews.LatestAsync("parent@example.test", CancellationToken.None));
            await Sender(config, environment, transport).SendAsync(Message(true), CancellationToken.None);
            Assert.Equal(2, transport.Messages.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public async Task InvitationPreviewOverrideCannotMakeAnUnknownMailModeAvailable(string mode)
    {
        var config = Config(new() { ["Portal:Mail:Mode"] = mode, ["Portal:Mail:PreviewInvitations"] = "true" });
        var environment = new Environment(); var transport = new FakeTransport();
        var sender = Sender(config, environment, transport);
        Assert.False(sender.IsAvailable);
        Assert.False(new PortalAccountPreviewMail(config, environment, new(config, environment)).InvitationPreviewEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message(true), CancellationToken.None));
        Assert.Empty(transport.Messages);
    }

    [Fact]
    public async Task InvitationPreviewDoesNotClaimAnUnconfiguredResetTransportIsUsable()
    {
        var config = Config(new() { ["Portal:Mail:Mode"] = "Smtp", ["Portal:Mail:PreviewInvitations"] = "true" });
        var transport = new FakeTransport { IsAvailable = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sender(config, new Environment(), transport).SendAsync(Message(false), CancellationToken.None));
        Assert.Empty(transport.Messages);
    }

    private static PortalAccountMessage Message(bool initial, string? role = "Parent", bool resend = false) =>
        new("parent@example.test", new Uri(Origin, $"/portal/{(initial ? "setup-password" : "reset-password")}#userId=00000000-0000-0000-0000-000000000000&token=fictional-token"), initial, "Sofia", role, resend);

    private static PortalAccountMail Sender(IConfiguration config, IHostEnvironment environment, IEmailTransport transport)
    {
        var templates = new PortalMailTemplates(config, environment);
        return new(config, environment, transport, templates, new(config, environment, templates));
    }

    private static IConfiguration Config(Dictionary<string, string?>? changes = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Portal:PublicUrl"] = Origin.AbsoluteUri, ["EmailSettings:SmtpHost"] = "smtp.example.test",
            ["EmailSettings:SmtpPort"] = "587", ["EmailSettings:FromEmail"] = "school@example.test",
            ["EmailSettings:Password"] = "fictional-smtp-test-value"
        };
        if (changes is not null) foreach (var (key, value) in changes) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class Environment(string name = "Development") : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "KnowledgeVineMailTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeTransport : IEmailTransport
    {
        public bool IsAvailable { get; set; } = true;
        public bool Fail { get; set; }
        public List<EmailContent> Messages { get; } = [];
        public Task SendAsync(EmailContent content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new IOException("Fictional delivery failure.");
            Messages.Add(content); return Task.CompletedTask;
        }
    }
}
