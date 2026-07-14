using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class RC18_ReceivingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderId",
                table: "StockInHeaders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceivedBy",
                table: "StockInHeaders",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockInHeaders_PurchaseOrderId",
                table: "StockInHeaders",
                column: "PurchaseOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockInHeaders_PurchaseOrders_PurchaseOrderId",
                table: "StockInHeaders",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockInHeaders_PurchaseOrders_PurchaseOrderId",
                table: "StockInHeaders");

            migrationBuilder.DropIndex(
                name: "IX_StockInHeaders_PurchaseOrderId",
                table: "StockInHeaders");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "StockInHeaders");

            migrationBuilder.DropColumn(
                name: "ReceivedBy",
                table: "StockInHeaders");
        }
    }
}
