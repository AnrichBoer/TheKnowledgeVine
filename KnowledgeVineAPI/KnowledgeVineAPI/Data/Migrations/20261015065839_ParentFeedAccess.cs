using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeVineAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class ParentFeedAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PortalSql.ReadResource("portal_target_v1.sql"));
            migrationBuilder.Sql(PortalSql.ReadResource("parent_feed_v2.sql"));

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PortalSql.ReadResource("portal_target_v1.sql"));
            migrationBuilder.Sql(PortalSql.ReadResource("parent_feed_v2_down.sql"));

        }
    }
}
