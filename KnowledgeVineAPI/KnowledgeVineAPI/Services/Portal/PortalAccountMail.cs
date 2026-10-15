using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using KnowledgeVineAPI.Services.Mail;

namespace KnowledgeVineAPI.Services.Portal;

public sealed record PortalAccountMessage(string Recipient, Uri Link, bool InitialSetup,
    string FirstName = "", string? Role = null, bool IsResend = false);

public interface IPortalAccountMail
{
    bool IsAvailable { get; }
    Task SendAsync(PortalAccountMessage message, CancellationToken cancellationToken);
}

public sealed class PortalAccountMail(IConfiguration configuration, IHostEnvironment environment,
    IEmailTransport transport, PortalMailTemplates templates, PortalAccountPreviewMail preview) : IPortalAccountMail
{
    private string Mode => configuration["Portal:Mail:Mode"] ?? (environment.IsDevelopment() ? "Preview" : "Smtp");
    private bool PreviewMode => Mode.Equals("Preview", StringComparison.OrdinalIgnoreCase);
    private bool SmtpMode => Mode.Equals("Smtp", StringComparison.OrdinalIgnoreCase);
    public bool IsAvailable => PreviewMode ? preview.IsAvailable
        : SmtpMode && (preview.InvitationPreviewEnabled || (transport.IsAvailable
            && (!environment.IsDevelopment() || configuration.GetSection("Portal:Mail:AllowedRecipients").Get<string[]>()?.Length > 0)));

    public async Task SendAsync(PortalAccountMessage message, CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new InvalidOperationException("Portal account email delivery is not configured.");
        if (PreviewMode || (message.InitialSetup && preview.InvitationPreviewEnabled))
        {
            await preview.SendAsync(message, cancellationToken);
            return;
        }
        if (!transport.IsAvailable) throw new InvalidOperationException("SMTP account email delivery is not configured.");
        // The test VM permits only explicitly configured test recipients.
        var recipients = configuration.GetSection("Portal:Mail:AllowedRecipients").Get<string[]>() ?? [];
        if (environment.IsDevelopment() && recipients.Length == 0)
            throw new InvalidOperationException("Configure a test recipient before enabling development SMTP.");
        if (recipients.Length > 0 && !recipients.Contains(message.Recipient, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("This recipient is not enabled for test email delivery.");
        await transport.SendAsync(templates.Render(message), cancellationToken);
    }
}

public sealed class PortalAccountPreviewMail(IConfiguration configuration, IHostEnvironment environment,
    PortalMailTemplates templates) : IPortalAccountMail
{
    public bool IsAvailable => environment.IsDevelopment();
    public bool InvitationPreviewEnabled => environment.IsDevelopment() &&
        ((configuration["Portal:Mail:Mode"] ?? "Preview").Equals("Preview", StringComparison.OrdinalIgnoreCase)
            || ((configuration["Portal:Mail:Mode"] ?? "Preview").Equals("Smtp", StringComparison.OrdinalIgnoreCase)
                && configuration.GetValue<bool>("Portal:Mail:PreviewInvitations")));

    public sealed record InvitationPreview(string Text, string SetupUrl);
    public async Task<InvitationPreview?> LatestAsync(string email, CancellationToken ct)
    {
        if (!InvitationPreviewEnabled) return null;
        var directory = PreviewDirectory();
        if (!Directory.Exists(directory)) return null;
        var prefix = RecipientKey(email);
        var path = Directory.GetFiles(directory, prefix + "-*.json").OrderDescending().FirstOrDefault();
        return path is null ? null : JsonSerializer.Deserialize<InvitationPreview>(await File.ReadAllTextAsync(path, ct));
    }

    private string PreviewDirectory()
    {
        var keyPath = configuration["Portal:DataProtectionKeyPath"]
            ?? throw new InvalidOperationException("Configure the portal protection-key directory.");
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(keyPath))!, "mail-preview");
    }
    private static string RecipientKey(string email) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant())));

    public async Task SendAsync(PortalAccountMessage message, CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new InvalidOperationException("Local account previews are available only in Development.");
        var directory = PreviewDirectory();
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var content = templates.Render(message);
        var name = $"{RecipientKey(message.Recipient)}-{DateTime.UtcNow:yyyyMMdd-HHmmssfffffff}-{Guid.NewGuid():N}";
        await Write(Path.Combine(directory, name + ".html"), content.HtmlBody!, cancellationToken);
        await Write(Path.Combine(directory, name + ".txt"), content.TextBody, cancellationToken);
        if (message.InitialSetup)
            await Write(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new InvitationPreview(content.TextBody, message.Link.AbsoluteUri)), cancellationToken);
    }

    private static async Task Write(string path, string content, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
    }
}
