using KnowledgeVineAPI.Models;

namespace KnowledgeVineAPI.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(EmailRequest request, CancellationToken cancellationToken = default);
    }
}

