using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class RC12_CostPerUnitClarity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostPerBaseUnit",
                table: "StockInDetails",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CostPerReceivedUnit",
                table: "StockInDetails",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CostPerBaseUnit",
                table: "PurchaseOrderItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CostPerOrderedUnit",
                table: "PurchaseOrderItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE StockInDetails
                SET CostPerBaseUnit = UnitCost,
                    CostPerReceivedUnit = CASE WHEN ConversionQuantity > 0 THEN UnitCost * ConversionQuantity ELSE UnitCost END
                WHERE UnitCost > 0 AND CostPerReceivedUnit = 0;

                UPDATE PurchaseOrderItems
                SET CostPerBaseUnit = UnitCost,
                    CostPerOrderedUnit = CASE WHEN ConversionQuantity > 0 THEN UnitCost * ConversionQuantity ELSE UnitCost END
                WHERE UnitCost > 0 AND CostPerOrderedUnit = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostPerBaseUnit",
                table: "StockInDetails");

            migrationBuilder.DropColumn(
                name: "CostPerReceivedUnit",
                table: "StockInDetails");

            migrationBuilder.DropColumn(
                name: "CostPerBaseUnit",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "CostPerOrderedUnit",
                table: "PurchaseOrderItems");
        }
    }
}
