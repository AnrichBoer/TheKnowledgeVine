using KnowledgeVineAPI.Models;
using KnowledgeVineAPI.Services.Mail;

namespace KnowledgeVineAPI.Services;

public sealed class EmailService(IEmailTransport transport, IConfiguration configuration, ILogger<EmailService> logger) : IEmailService
{
    public async Task<bool> SendEmailAsync(EmailRequest request)
    {
        if (!configuration.GetValue("EmailSettings:ContactEnabled", true) || !transport.IsAvailable) return false;
        var content = new EmailContent(configuration["EmailSettings:FromEmail"]!, "New Contact Form Submission",
            $"Name: {request.Name}\nContact: {request.ContactNumber}\nEmail: {request.Email}\nMessage:\n{request.Message}",
            ReplyTo: request.Email.Trim());
        try
        {
            await transport.SendAsync(content, CancellationToken.None);
            return true;
        }
        catch (Exception error)
        {
            // SMTP responses and exception details can contain addresses or credentials.
            logger.LogWarning("Contact mail delivery failed ({ErrorType})", error.GetType().Name);
            return false;
        }
    }
}
