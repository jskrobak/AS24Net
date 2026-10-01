using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AS24Net.Entity.Migrations
{
    /// <inheritdoc />
    public partial class PartnerOwnCertificate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnCertificateId",
                table: "Partners",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_OwnCertificateId",
                table: "Partners",
                column: "OwnCertificateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Partners_Certificates_OwnCertificateId",
                table: "Partners",
                column: "OwnCertificateId",
                principalTable: "Certificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Partners_Certificates_OwnCertificateId",
                table: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Partners_OwnCertificateId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "OwnCertificateId",
                table: "Partners");
        }
    }
}
