using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class Phase46_InventoryIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaxStockLevel",
                table: "Items",
                type: "decimal(18,3)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxStockLevel",
                table: "Items");
        }
    }
}
