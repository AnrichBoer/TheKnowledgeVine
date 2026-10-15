namespace KnowledgeVineAPI.Models.Portal;

public sealed class ParentStudent
{
    public Guid ParentUserId { get; set; }
    public Guid StudentId { get; set; }
    public Guid LinkedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
