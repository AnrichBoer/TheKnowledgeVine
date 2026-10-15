using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace KnowledgeVineAPI.Services.Mail;

public sealed record EmailContent(string Recipient, string Subject, string TextBody,
    string? HtmlBody = null, string? ReplyTo = null);

public interface IEmailTransport
{
    bool IsAvailable { get; }
    Task SendAsync(EmailContent content, CancellationToken cancellationToken);
}

public sealed class SmtpEmailTransport(IConfiguration configuration) : IEmailTransport
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(configuration["EmailSettings:SmtpHost"])
        && int.TryParse(configuration["EmailSettings:SmtpPort"], out var port) && port is > 0 and <= 65535
        && MailboxAddress.TryParse(configuration["EmailSettings:FromEmail"], out _)
        && !string.IsNullOrWhiteSpace(configuration["EmailSettings:Password"]);

    public MimeMessage CreateMessage(EmailContent content)
    {
        if (!IsAvailable) throw new InvalidOperationException("School SMTP delivery is not configured.");
        var sender = MailboxAddress.Parse(configuration["EmailSettings:FromEmail"]!);
        sender.Name = configuration["EmailSettings:FromName"] ?? "Knowledge Vine Montessori";
        var message = new MimeMessage();
        message.From.Add(sender);
        message.To.Add(MailboxAddress.Parse(content.Recipient));
        if (!string.IsNullOrWhiteSpace(content.ReplyTo))
            message.ReplyTo.Add(MailboxAddress.Parse(content.ReplyTo));
        message.Subject = content.Subject;
        message.Body = new BodyBuilder { TextBody = content.TextBody, HtmlBody = content.HtmlBody }.ToMessageBody();
        return message;
    }

    public async Task SendAsync(EmailContent content, CancellationToken cancellationToken)
    {
        using var message = CreateMessage(content);
        using var client = new SmtpClient { Timeout = 30000 };
        var host = configuration["EmailSettings:SmtpHost"]!;
        var port = int.Parse(configuration["EmailSettings:SmtpPort"]!);
        // Both modes require encryption and retain normal certificate verification.
        await client.ConnectAsync(host, port, port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
            cancellationToken);
        client.AuthenticationMechanisms.Remove("XOAUTH2");
        await client.AuthenticateAsync((configuration["EmailSettings:Username"] ?? configuration["EmailSettings:FromEmail"])!,
            configuration["EmailSettings:Password"]!, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        // DATA acceptance determines success; a later QUIT failure must not invite a duplicate send.
        try { await client.DisconnectAsync(true, cancellationToken); }
        catch (Exception error) when (error is IOException or MailKit.CommandException or MailKit.ProtocolException or OperationCanceledException) { }
    }
}
