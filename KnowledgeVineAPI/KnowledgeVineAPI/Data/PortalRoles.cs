namespace KnowledgeVineAPI.Data;

public static class PortalRoles
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Parent = "Parent";

    public static readonly Guid AdminId = Guid.Parse("69e43dad-f3c5-4827-9d92-810501d66b01");
    public static readonly Guid TeacherId = Guid.Parse("69e43dad-f3c5-4827-9d92-810501d66b02");
    public static readonly Guid ParentId = Guid.Parse("69e43dad-f3c5-4827-9d92-810501d66b03");
}
