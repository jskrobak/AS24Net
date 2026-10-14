using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AS24Net.Entity.Migrations
{
    /// <inheritdoc />
    public partial class ApiTokenScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Scopes",
                table: "ApiTokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing tokens keep what they could do: Messages | Inbox | Certificates (7), and Configuration (8)
            // when they were allowed to change the configuration.
            migrationBuilder.Sql("""UPDATE "ApiTokens" SET "Scopes" = CASE WHEN "AllowConfiguration" THEN 15 ELSE 7 END;""");

            migrationBuilder.DropColumn(
                name: "AllowConfiguration",
                table: "ApiTokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowConfiguration",
                table: "ApiTokens",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""UPDATE "ApiTokens" SET "AllowConfiguration" = ("Scopes" & 8) <> 0;""");

            migrationBuilder.DropColumn(
                name: "Scopes",
                table: "ApiTokens");
        }
    }
}
