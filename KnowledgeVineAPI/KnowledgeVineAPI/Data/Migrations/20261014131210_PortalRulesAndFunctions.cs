using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeVineAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class PortalRulesAndFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PortalSql.ReadResource("portal_rules_v1.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PortalSql.ReadResource("portal_target_v1.sql"));
            migrationBuilder.Sql(PortalSql.ReadResource("portal_rules_v1_down.sql"));
        }
    }
}
