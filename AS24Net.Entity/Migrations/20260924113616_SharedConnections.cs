using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AS24Net.Entity.Migrations
{
    /// <inheritdoc />
    public partial class SharedConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Partners that differ only in their AS2 name share a connection: its URL, the security settings and the
            // certificates move from the partner to it. Every partner gets a connection with its own settings so far;
            // partners with the same server can be moved to one connection afterwards.
            migrationBuilder.AddColumn<bool>(
                name: "AllowConfiguration",
                table: "ApiTokens",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SignMessages = table.Column<bool>(type: "boolean", nullable: false),
                    SignatureAlgorithm = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    EncryptMessages = table.Column<bool>(type: "boolean", nullable: false),
                    EncryptionAlgorithm = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    CompressMessages = table.Column<bool>(type: "boolean", nullable: false),
                    CompressBeforeSigning = table.Column<bool>(type: "boolean", nullable: false),
                    MdnMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RequestSignedMdn = table.Column<bool>(type: "boolean", nullable: false),
                    RequireSignedMessages = table.Column<bool>(type: "boolean", nullable: false),
                    RequireEncryptedMessages = table.Column<bool>(type: "boolean", nullable: false),
                    SignatureCertificateId = table.Column<int>(type: "integer", nullable: true),
                    PreviousSignatureCertificateId = table.Column<int>(type: "integer", nullable: true),
                    EncryptionCertificateId = table.Column<int>(type: "integer", nullable: true),
                    TlsCertificateId = table.Column<int>(type: "integer", nullable: true),
                    HttpUserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HttpPassword = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MdnTimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Connections_Certificates_EncryptionCertificateId",
                        column: x => x.EncryptionCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Connections_Certificates_PreviousSignatureCertificateId",
                        column: x => x.PreviousSignatureCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Connections_Certificates_SignatureCertificateId",
                        column: x => x.SignatureCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Connections_Certificates_TlsCertificateId",
                        column: x => x.TlsCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_EncryptionCertificateId",
                table: "Connections",
                column: "EncryptionCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_Name",
                table: "Connections",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Connections_PreviousSignatureCertificateId",
                table: "Connections",
                column: "PreviousSignatureCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_SignatureCertificateId",
                table: "Connections",
                column: "SignatureCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_TlsCertificateId",
                table: "Connections",
                column: "TlsCertificateId");

            // Remembers the partner a connection was made of until the partners point to their connections.
            migrationBuilder.AddColumn<int>(
                name: "PartnerId",
                table: "Connections",
                type: "integer",
                nullable: true);

            // The name of a connection is unique; partners of the same name are told apart by their id.
            migrationBuilder.Sql(@"
                INSERT INTO ""Connections"" (""Name"", ""Url"", ""SignMessages"", ""SignatureAlgorithm"", ""EncryptMessages"", ""EncryptionAlgorithm"", ""CompressMessages"", ""CompressBeforeSigning"", ""MdnMode"", ""RequestSignedMdn"", ""RequireSignedMessages"", ""RequireEncryptedMessages"", ""SignatureCertificateId"", ""PreviousSignatureCertificateId"", ""EncryptionCertificateId"", ""TlsCertificateId"", ""HttpUserName"", ""HttpPassword"", ""TimeoutSeconds"", ""MdnTimeoutMinutes"", ""ContactName"", ""ContactEmail"", ""PartnerId"")
                SELECT CASE WHEN (SELECT count(*) FROM ""Partners"" q WHERE q.""Name"" = p.""Name"") > 1
                            THEN left(p.""Name"", 40) || ' #' || p.""Id"" ELSE p.""Name"" END,
                       p.""Url"", p.""SignMessages"", p.""SignatureAlgorithm"", p.""EncryptMessages"", p.""EncryptionAlgorithm"", p.""CompressMessages"", p.""CompressBeforeSigning"", p.""MdnMode"", p.""RequestSignedMdn"", p.""RequireSignedMessages"", p.""RequireEncryptedMessages"", p.""SignatureCertificateId"", p.""PreviousSignatureCertificateId"", p.""EncryptionCertificateId"", p.""TlsCertificateId"", p.""HttpUserName"", p.""HttpPassword"", p.""TimeoutSeconds"", p.""MdnTimeoutMinutes"", p.""ContactName"", p.""ContactEmail"", p.""Id""
                FROM ""Partners"" p;");

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "Partners",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Partners"" p SET ""ConnectionId"" = c.""Id"" FROM ""Connections"" c WHERE c.""PartnerId"" = p.""Id"";");

            migrationBuilder.AlterColumn<int>(
                name: "ConnectionId",
                table: "Partners",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // Certificate changes belong to the connection of their partner.
            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "CertificateChanges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectionName",
                table: "CertificateChanges",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE ""CertificateChanges"" SET ""ConnectionName"" = ""PartnerName"";
                UPDATE ""CertificateChanges"" ch SET ""ConnectionId"" = c.""Id"", ""ConnectionName"" = c.""Name""
                FROM ""Connections"" c WHERE c.""PartnerId"" = ch.""PartnerId"";");

            migrationBuilder.DropForeignKey(
                name: "FK_CertificateChanges_Partners_PartnerId",
                table: "CertificateChanges");

            migrationBuilder.DropIndex(
                name: "IX_CertificateChanges_PartnerId",
                table: "CertificateChanges");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                table: "CertificateChanges");

            migrationBuilder.DropColumn(
                name: "PartnerName",
                table: "CertificateChanges");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                table: "Connections");

            migrationBuilder.DropForeignKey(
                name: "FK_Partners_Certificates_EncryptionCertificateId",
                table: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Partners_EncryptionCertificateId",
                table: "Partners");

            migrationBuilder.DropForeignKey(
                name: "FK_Partners_Certificates_PreviousSignatureCertificateId",
                table: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Partners_PreviousSignatureCertificateId",
                table: "Partners");

            migrationBuilder.DropForeignKey(
                name: "FK_Partners_Certificates_SignatureCertificateId",
                table: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Partners_SignatureCertificateId",
                table: "Partners");

            migrationBuilder.DropForeignKey(
                name: "FK_Partners_Certificates_TlsCertificateId",
                table: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Partners_TlsCertificateId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "Url",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "SignMessages",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "SignatureAlgorithm",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "EncryptMessages",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "EncryptionAlgorithm",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "CompressMessages",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "CompressBeforeSigning",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "MdnMode",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "RequestSignedMdn",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "RequireSignedMessages",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "RequireEncryptedMessages",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "SignatureCertificateId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "PreviousSignatureCertificateId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "EncryptionCertificateId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "TlsCertificateId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "HttpUserName",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "HttpPassword",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "TimeoutSeconds",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "MdnTimeoutMinutes",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Partners");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_ConnectionId",
                table: "Partners",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateChanges_ConnectionId",
                table: "CertificateChanges",
                column: "ConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_CertificateChanges_Connections_ConnectionId",
                table: "CertificateChanges",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Partners_Connections_ConnectionId",
                table: "Partners",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Every partner takes the settings of its connection back; the changes of a connection go to its first partner.
            migrationBuilder.DropForeignKey(
                name: "FK_CertificateChanges_Connections_ConnectionId",
                table: "CertificateChanges");

            migrationBuilder.DropForeignKey(
                name: "FK_Partners_Connections_ConnectionId",
                table: "Partners");

            migrationBuilder.AddColumn<bool>(
                name: "CompressBeforeSigning",
                table: "Partners",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CompressMessages",
                table: "Partners",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Partners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Partners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EncryptMessages",
                table: "Partners",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionAlgorithm",
                table: "Partners",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "EncryptionCertificateId",
                table: "Partners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HttpPassword",
                table: "Partners",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HttpUserName",
                table: "Partners",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MdnMode",
                table: "Partners",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MdnTimeoutMinutes",
                table: "Partners",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreviousSignatureCertificateId",
                table: "Partners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequestSignedMdn",
                table: "Partners",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireEncryptedMessages",
                table: "Partners",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireSignedMessages",
                table: "Partners",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SignMessages",
                table: "Partners",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SignatureAlgorithm",
                table: "Partners",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SignatureCertificateId",
                table: "Partners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TlsCertificateId",
                table: "Partners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Url",
                table: "Partners",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TimeoutSeconds",
                table: "Partners",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.Sql(@"
                UPDATE ""Partners"" p SET
                    ""Url"" = c.""Url"",
                    ""SignMessages"" = c.""SignMessages"",
                    ""SignatureAlgorithm"" = c.""SignatureAlgorithm"",
                    ""EncryptMessages"" = c.""EncryptMessages"",
                    ""EncryptionAlgorithm"" = c.""EncryptionAlgorithm"",
                    ""CompressMessages"" = c.""CompressMessages"",
                    ""CompressBeforeSigning"" = c.""CompressBeforeSigning"",
                    ""MdnMode"" = c.""MdnMode"",
                    ""RequestSignedMdn"" = c.""RequestSignedMdn"",
                    ""RequireSignedMessages"" = c.""RequireSignedMessages"",
                    ""RequireEncryptedMessages"" = c.""RequireEncryptedMessages"",
                    ""SignatureCertificateId"" = c.""SignatureCertificateId"",
                    ""PreviousSignatureCertificateId"" = c.""PreviousSignatureCertificateId"",
                    ""EncryptionCertificateId"" = c.""EncryptionCertificateId"",
                    ""TlsCertificateId"" = c.""TlsCertificateId"",
                    ""HttpUserName"" = c.""HttpUserName"",
                    ""HttpPassword"" = c.""HttpPassword"",
                    ""TimeoutSeconds"" = c.""TimeoutSeconds"",
                    ""MdnTimeoutMinutes"" = c.""MdnTimeoutMinutes"",
                    ""ContactName"" = c.""ContactName"",
                    ""ContactEmail"" = c.""ContactEmail""
                FROM ""Connections"" c WHERE c.""Id"" = p.""ConnectionId"";");

            migrationBuilder.AddColumn<int>(
                name: "PartnerId",
                table: "CertificateChanges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartnerName",
                table: "CertificateChanges",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE ""CertificateChanges"" SET ""PartnerName"" = ""ConnectionName"";
                UPDATE ""CertificateChanges"" ch SET ""PartnerId"" = p.""Id"", ""PartnerName"" = p.""Name""
                FROM ""Partners"" p
                WHERE p.""Id"" = (SELECT min(q.""Id"") FROM ""Partners"" q WHERE q.""ConnectionId"" = ch.""ConnectionId"");");

            migrationBuilder.DropIndex(
                name: "IX_CertificateChanges_ConnectionId",
                table: "CertificateChanges");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "CertificateChanges");

            migrationBuilder.DropColumn(
                name: "ConnectionName",
                table: "CertificateChanges");

            migrationBuilder.DropIndex(
                name: "IX_Partners_ConnectionId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "Partners");

            migrationBuilder.DropTable(
                name: "Connections");

            migrationBuilder.DropColumn(
                name: "AllowConfiguration",
                table: "ApiTokens");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateChanges_PartnerId",
                table: "CertificateChanges",
                column: "PartnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_CertificateChanges_Partners_PartnerId",
                table: "CertificateChanges",
                column: "PartnerId",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_EncryptionCertificateId",
                table: "Partners",
                column: "EncryptionCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_PreviousSignatureCertificateId",
                table: "Partners",
                column: "PreviousSignatureCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_SignatureCertificateId",
                table: "Partners",
                column: "SignatureCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_TlsCertificateId",
                table: "Partners",
                column: "TlsCertificateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Partners_Certificates_EncryptionCertificateId",
                table: "Partners",
                column: "EncryptionCertificateId",
                principalTable: "Certificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Partners_Certificates_PreviousSignatureCertificateId",
                table: "Partners",
                column: "PreviousSignatureCertificateId",
                principalTable: "Certificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Partners_Certificates_SignatureCertificateId",
                table: "Partners",
                column: "SignatureCertificateId",
                principalTable: "Certificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Partners_Certificates_TlsCertificateId",
                table: "Partners",
                column: "TlsCertificateId",
                principalTable: "Certificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
