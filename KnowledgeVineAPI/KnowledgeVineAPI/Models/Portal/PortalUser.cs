using Microsoft.AspNetCore.Identity;

namespace KnowledgeVineAPI.Models.Portal;

public sealed class PortalUser : IdentityUser<Guid>
{
    public PortalUser() => Id = Guid.NewGuid();

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
