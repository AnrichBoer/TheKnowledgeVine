using KnowledgeVineAPI.Models;

namespace KnowledgeVineAPI.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(EmailRequest request);

    }
}

