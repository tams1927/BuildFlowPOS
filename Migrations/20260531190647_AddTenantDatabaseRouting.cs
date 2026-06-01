using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDatabaseRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionString",
                table: "Tenants",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DatabaseMode",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DatabaseName",
                table: "Tenants",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DatabaseProvisionedAtUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DatabaseServer",
                table: "Tenants",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastDatabaseMigration",
                table: "Tenants",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionString",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DatabaseMode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DatabaseName",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DatabaseProvisionedAtUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DatabaseServer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastDatabaseMigration",
                table: "Tenants");
        }
    }
}
