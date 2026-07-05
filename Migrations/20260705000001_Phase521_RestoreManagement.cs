using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class Phase521_RestoreManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RestoreRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BackupRecordId = table.Column<int>(type: "int", nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DatabaseType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RestoreMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RestoreTargetDatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SourceBackupPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreRecords_BackupRecords_BackupRecordId",
                        column: x => x.BackupRecordId,
                        principalTable: "BackupRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RestoreRecords_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestoreRecords_BackupRecordId",
                table: "RestoreRecords",
                column: "BackupRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreRecords_StartedAtUtc",
                table: "RestoreRecords",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreRecords_TenantId",
                table: "RestoreRecords",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestoreRecords");
        }
    }
}
