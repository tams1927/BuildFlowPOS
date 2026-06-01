using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPilotRoutingFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DataMigrated",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DataMigratedAtUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RoutingEnabled",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataMigrated",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DataMigratedAtUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "RoutingEnabled",
                table: "Tenants");
        }
    }
}
