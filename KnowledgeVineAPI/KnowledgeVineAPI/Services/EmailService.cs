using System.Net;
using System.Net.Mail;
using KnowledgeVineAPI.Models;

namespace KnowledgeVineAPI.Services
{
    public class EmailService : IEmailService
    {
        private readonly string _connectionString;
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
            _connectionString = _config.GetValue<string>("ConnectionStrings:DefaultConnection");
        }

        public async Task<bool> SendEmailAsync(EmailRequest request)
        {
            try
            {
                var smtpHost = _config["EmailSettings:SmtpHost"];
                var smtpPort = int.Parse(_config["EmailSettings:SmtpPort"]);
                var fromEmail = _config["EmailSettings:FromEmail"];
                var password = _config["EmailSettings:Password"];

                var client = new SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new NetworkCredential(fromEmail, password),
                    EnableSsl = true
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail),
                    Subject = "New Contact Form Submission",
                    Body = $@"
                    Name: {request.Name}
                    Contact: {request.ContactNumber}
                    Email: {request.Email}

                    Message:
                    {request.Message}
            ",
                    IsBodyHtml = false
                };

                mailMessage.To.Add(fromEmail); // send to yourself

                await client.SendMailAsync(mailMessage);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
                return false;
            }
        }




    }
}
