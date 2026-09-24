using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AS24Net.Entity.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__DataSeed",
                columns: table => new
                {
                    ProfileName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Version = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSeed", x => x.ProfileName);
                });

            migrationBuilder.CreateTable(
                name: "ApiTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastUsed = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    InboxWebhookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WebhookSecret = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Base64Data = table.Column<string>(type: "text", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    HasPrivateKey = table.Column<bool>(type: "boolean", nullable: false),
                    Thumbprint = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Password = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalSettings",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false),
                    Json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalSettings", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "TransferEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PartnerId = table.Column<int>(type: "integer", nullable: true),
                    PartnerName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RemoteEndPoint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MessageId = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    FileName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    OutgoingMessageId = table.Column<int>(type: "integer", nullable: true),
                    ReceivedMessageId = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    HookParameters = table.Column<string>(type: "text", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastLogin = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Identities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    As2Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SigningCertificateId = table.Column<int>(type: "integer", nullable: true),
                    DecryptionCertificateId = table.Column<int>(type: "integer", nullable: true),
                    PreviousDecryptionCertificateId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identities_Certificates_DecryptionCertificateId",
                        column: x => x.DecryptionCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Identities_Certificates_PreviousDecryptionCertificateId",
                        column: x => x.PreviousDecryptionCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Identities_Certificates_SigningCertificateId",
                        column: x => x.SigningCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    As2Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultIdentityId = table.Column<int>(type: "integer", nullable: true),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_Partners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Partners_Certificates_EncryptionCertificateId",
                        column: x => x.EncryptionCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Partners_Certificates_PreviousSignatureCertificateId",
                        column: x => x.PreviousSignatureCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Partners_Certificates_SignatureCertificateId",
                        column: x => x.SignatureCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Partners_Certificates_TlsCertificateId",
                        column: x => x.TlsCertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Partners_Identities_DefaultIdentityId",
                        column: x => x.DefaultIdentityId,
                        principalTable: "Identities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CertificateChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartnerId = table.Column<int>(type: "integer", nullable: true),
                    PartnerName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CertificateId = table.Column<int>(type: "integer", nullable: true),
                    Usage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActivateAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificateChanges_Certificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CertificateChanges_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OutgoingMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Created = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PartnerId = table.Column<int>(type: "integer", nullable: false),
                    IdentityId = table.Column<int>(type: "integer", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    FileName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    NextRetry = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastErrorDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SentDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DeliveredDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    MdnMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Mic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReceivedMic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MdnDisposition = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MdnText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MdnSigned = table.Column<bool>(type: "boolean", nullable: false),
                    MdnMessageId = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Signed = table.Column<bool>(type: "boolean", nullable: false),
                    Encrypted = table.Column<bool>(type: "boolean", nullable: false),
                    Compressed = table.Column<bool>(type: "boolean", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WebhookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WebhookSecret = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingMessages_Identities_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "Identities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutgoingMessages_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReceivedMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Created = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PartnerId = table.Column<int>(type: "integer", nullable: true),
                    IdentityId = table.Column<int>(type: "integer", nullable: true),
                    MessageId = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    As2From = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    As2To = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FileName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    FilePath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Signed = table.Column<bool>(type: "boolean", nullable: false),
                    Encrypted = table.Column<bool>(type: "boolean", nullable: false),
                    Compressed = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Mic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MdnDisposition = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MdnStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MdnUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MdnSignedRequested = table.Column<bool>(type: "boolean", nullable: false),
                    MdnMicAlgorithm = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    MdnRetryCount = table.Column<int>(type: "integer", nullable: false),
                    MdnNextRetry = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    MdnSentDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    MdnLastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RemoteAddress = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FetchedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivedMessages_Identities_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "Identities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReceivedMessages_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_TokenHash",
                table: "ApiTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CertificateChanges_CertificateId",
                table: "CertificateChanges",
                column: "CertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateChanges_PartnerId",
                table: "CertificateChanges",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateChanges_Status_ActivateAt",
                table: "CertificateChanges",
                columns: new[] { "Status", "ActivateAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Identities_As2Id",
                table: "Identities",
                column: "As2Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Identities_DecryptionCertificateId",
                table: "Identities",
                column: "DecryptionCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Identities_PreviousDecryptionCertificateId",
                table: "Identities",
                column: "PreviousDecryptionCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Identities_SigningCertificateId",
                table: "Identities",
                column: "SigningCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_IdentityId",
                table: "OutgoingMessages",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_MessageId",
                table: "OutgoingMessages",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_PartnerId",
                table: "OutgoingMessages",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_Status_NextRetry",
                table: "OutgoingMessages",
                columns: new[] { "Status", "NextRetry" });

            migrationBuilder.CreateIndex(
                name: "IX_Partners_As2Id",
                table: "Partners",
                column: "As2Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_DefaultIdentityId",
                table: "Partners",
                column: "DefaultIdentityId");

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

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedMessages_As2From_MessageId",
                table: "ReceivedMessages",
                columns: new[] { "As2From", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedMessages_IdentityId",
                table: "ReceivedMessages",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedMessages_MdnStatus_MdnNextRetry",
                table: "ReceivedMessages",
                columns: new[] { "MdnStatus", "MdnNextRetry" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedMessages_PartnerId",
                table: "ReceivedMessages",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferEvents_Category_IsArchived_Timestamp",
                table: "TransferEvents",
                columns: new[] { "Category", "IsArchived", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferEvents_IsArchived_Timestamp",
                table: "TransferEvents",
                columns: new[] { "IsArchived", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferEvents_Timestamp_Id",
                table: "TransferEvents",
                columns: new[] { "Timestamp", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__DataSeed");

            migrationBuilder.DropTable(
                name: "ApiTokens");

            migrationBuilder.DropTable(
                name: "CertificateChanges");

            migrationBuilder.DropTable(
                name: "GlobalSettings");

            migrationBuilder.DropTable(
                name: "OutgoingMessages");

            migrationBuilder.DropTable(
                name: "ReceivedMessages");

            migrationBuilder.DropTable(
                name: "TransferEvents");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropTable(
                name: "Identities");

            migrationBuilder.DropTable(
                name: "Certificates");
        }
    }
}
