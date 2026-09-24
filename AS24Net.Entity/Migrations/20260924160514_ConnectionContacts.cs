using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AS24Net.Entity.Migrations
{
    /// <summary>The one contact of a connection becomes a list of contacts; the existing contact is its first item.</summary>
    public partial class ConnectionContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Contacts",
                table: "Connections",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql("""
                UPDATE "Connections"
                SET "Contacts" = jsonb_build_array(jsonb_build_object(
                    'Name', coalesce("ContactName", ''),
                    'Description', NULL,
                    'Phones', '[]'::jsonb,
                    'Emails', CASE WHEN coalesce("ContactEmail", '') = '' THEN '[]'::jsonb ELSE jsonb_build_array("ContactEmail") END,
                    'Url', NULL))
                WHERE coalesce("ContactName", '') <> '' OR coalesce("ContactEmail", '') <> '';
                """);

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "Connections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Connections",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Connections",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Only the first contact fits into the columns.
            migrationBuilder.Sql("""
                UPDATE "Connections"
                SET "ContactName" = nullif(left("Contacts" -> 0 ->> 'Name', 200), ''),
                    "ContactEmail" = left("Contacts" -> 0 -> 'Emails' ->> 0, 200)
                WHERE jsonb_array_length("Contacts") > 0;
                """);

            migrationBuilder.DropColumn(
                name: "Contacts",
                table: "Connections");
        }
    }
}
