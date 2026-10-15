namespace KnowledgeVineAPI.Models.Portal;

public sealed class DailyUpdate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentId { get; set; }
    public DateOnly UpdateDate { get; set; }
    public string NoteText { get; set; } = string.Empty;
    public string Status { get; set; } = DailyUpdateStatuses.Draft;
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SharedAt { get; set; }
    public int Version { get; set; }
}

public static class DailyUpdateStatuses
{
    public const string Draft = "draft";
    public const string Shared = "shared";
}
