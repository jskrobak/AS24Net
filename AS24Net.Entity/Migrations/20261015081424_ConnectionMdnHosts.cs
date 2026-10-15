using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AS24Net.Entity.Migrations
{
    /// <inheritdoc />
    public partial class ConnectionMdnHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MdnHosts",
                table: "Connections",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MdnHosts",
                table: "Connections");
        }
    }
}
