using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AS24Net.Entity.Migrations
{
    /// <inheritdoc />
    public partial class ReceivedMessageResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "ReceivedMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedBy",
                table: "ReceivedMessages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedDate",
                table: "ReceivedMessages",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "ReceivedMessages");

            migrationBuilder.DropColumn(
                name: "ResolvedBy",
                table: "ReceivedMessages");

            migrationBuilder.DropColumn(
                name: "ResolvedDate",
                table: "ReceivedMessages");
        }
    }
}
