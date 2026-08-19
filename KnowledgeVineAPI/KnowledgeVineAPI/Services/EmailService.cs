using System.Net;
using System.Net.Mail;
using KnowledgeVineAPI.Models;
using Microsoft.Extensions.Options;

namespace KnowledgeVineAPI.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailSettings> options, ILogger<EmailService> logger)
        {
            _settings = options.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(
            EmailRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
                {
                    Credentials = new NetworkCredential(_settings.FromEmail, _settings.Password),
                    EnableSsl = true
                };

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(_settings.FromEmail, "Knowledge Vine website"),
                    Subject = $"Website enquiry from {request.Name}",
                    Body = $@"Name: {request.Name}
Contact: {request.ContactNumber}
Email: {request.Email}

Message:
{request.Message}",
                    IsBodyHtml = false
                };

                mailMessage.To.Add(_settings.FromEmail);
                mailMessage.ReplyToList.Add(new MailAddress(request.Email, request.Name));

                await client.SendMailAsync(mailMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send a website enquiry email");
                throw;
            }
        }
    }
}
