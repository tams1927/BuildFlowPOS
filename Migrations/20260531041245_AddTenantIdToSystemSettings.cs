using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdToSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SystemSettings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_TenantId",
                table: "SystemSettings",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_TenantId",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SystemSettings");
        }
    }
}
