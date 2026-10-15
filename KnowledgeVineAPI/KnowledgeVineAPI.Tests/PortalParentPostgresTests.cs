namespace KnowledgeVineAPI.Tests;

public class PortalParentPostgresTests
{
    [PortalPostgresFact]
    public async Task ParentHelperKeepsHistoryButOnlyReturnsCurrentYearForEnrolledChildren()
    {
        await using var db = await PortalPostgresFixture.Create();
        var start = new DateOnly(db.SchoolDate.Year, 1, 1);
        foreach (var date in new[] { start.AddDays(-1), start, start.AddYears(1).AddDays(-1), start.AddYears(1) })
            await db.SaveNote(db.Child.Id, "Retained history.", share: true, date: date);
        await db.SaveNote(db.Sibling.Id, "Sibling's current note.", share: true);
        Assert.Equal(3, await db.FeedCount(db.Parent.Id));
        Assert.Equal(4, await db.Scalar<long>("SELECT count(*) FROM daily_updates WHERE student_id=@child", ("child", db.Child.Id)));
        await db.Execute("UPDATE students SET is_active=false WHERE id=@child", ("child", db.Child.Id));
        Assert.Equal(1, await db.FeedCount(db.Parent.Id));
        await db.ExpectFailure("42501", () => db.Scalar<long>("SELECT count(*) FROM portal_parent_updates(@parent,@child)", ("parent", db.Parent.Id), ("child", db.Child.Id)));
        Assert.Equal(4, await db.Scalar<long>("SELECT count(*) FROM daily_updates WHERE student_id=@child", ("child", db.Child.Id)));
        await db.Execute("UPDATE students SET is_active=true WHERE id=@child", ("child", db.Child.Id));
        await db.Execute("DELETE FROM parent_students WHERE parent_user_id=@parent AND student_id=@child", ("parent", db.Parent.Id), ("child", db.Child.Id));
        Assert.Equal(1, await db.FeedCount(db.Parent.Id));
        await db.ExpectFailure("42501", () => db.Scalar<long>("SELECT count(*) FROM portal_parent_updates(@parent,@child)", ("parent", db.Parent.Id), ("child", db.Child.Id)));
    }
}
