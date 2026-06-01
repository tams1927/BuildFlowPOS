using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class Phase421_SaaSStabilization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerLedgers_Customers_CustomerId",
                table: "CustomerLedgers");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Categories_CategoryId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Units_UnitId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesDetails_Items_ItemId",
                table: "SalesDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_StockAdjustmentDetails_Items_ItemId",
                table: "StockAdjustmentDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_StockInDetails_Items_ItemId",
                table: "StockInDetails");

            migrationBuilder.DropIndex(
                name: "IX_Branches_Code",
                table: "Branches");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SalesReturnHeaders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_StockInHeaders_TenantId_StockInNumber",
                table: "StockInHeaders",
                columns: new[] { "TenantId", "StockInNumber" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_StockAdjustmentHeaders_TenantId_AdjustmentNumber",
                table: "StockAdjustmentHeaders",
                columns: new[] { "TenantId", "AdjustmentNumber" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnHeaders_TenantId",
                table: "SalesReturnHeaders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UX_SalesReturnHeaders_TenantId_ReturnNumber",
                table: "SalesReturnHeaders",
                columns: new[] { "TenantId", "ReturnNumber" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_SalesHeaders_TenantId_SalesNumber",
                table: "SalesHeaders",
                columns: new[] { "TenantId", "SalesNumber" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Quotations_TenantId_QuotationNo",
                table: "Quotations",
                columns: new[] { "TenantId", "QuotationNo" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Expenses_TenantId_ExpenseNumber",
                table: "Expenses",
                columns: new[] { "TenantId", "ExpenseNumber" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_DeliveryReceipts_TenantId_DRNumber",
                table: "DeliveryReceipts",
                columns: new[] { "TenantId", "DRNumber" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_BranchTransfers_TenantId_TransferNumber",
                table: "BranchTransfers",
                columns: new[] { "TenantId", "TransferNumber" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Branches_TenantId_Code",
                table: "Branches",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerLedgers_Customers_CustomerId",
                table: "CustomerLedgers",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Categories_CategoryId",
                table: "Items",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Units_UnitId",
                table: "Items",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesDetails_Items_ItemId",
                table: "SalesDetails",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturnHeaders_Tenants_TenantId",
                table: "SalesReturnHeaders",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StockAdjustmentDetails_Items_ItemId",
                table: "StockAdjustmentDetails",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockInDetails_Items_ItemId",
                table: "StockInDetails",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerLedgers_Customers_CustomerId",
                table: "CustomerLedgers");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Categories_CategoryId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Units_UnitId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesDetails_Items_ItemId",
                table: "SalesDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturnHeaders_Tenants_TenantId",
                table: "SalesReturnHeaders");

            migrationBuilder.DropForeignKey(
                name: "FK_StockAdjustmentDetails_Items_ItemId",
                table: "StockAdjustmentDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_StockInDetails_Items_ItemId",
                table: "StockInDetails");

            migrationBuilder.DropIndex(
                name: "UX_StockInHeaders_TenantId_StockInNumber",
                table: "StockInHeaders");

            migrationBuilder.DropIndex(
                name: "UX_StockAdjustmentHeaders_TenantId_AdjustmentNumber",
                table: "StockAdjustmentHeaders");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturnHeaders_TenantId",
                table: "SalesReturnHeaders");

            migrationBuilder.DropIndex(
                name: "UX_SalesReturnHeaders_TenantId_ReturnNumber",
                table: "SalesReturnHeaders");

            migrationBuilder.DropIndex(
                name: "UX_SalesHeaders_TenantId_SalesNumber",
                table: "SalesHeaders");

            migrationBuilder.DropIndex(
                name: "UX_Quotations_TenantId_QuotationNo",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "UX_Expenses_TenantId_ExpenseNumber",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "UX_DeliveryReceipts_TenantId_DRNumber",
                table: "DeliveryReceipts");

            migrationBuilder.DropIndex(
                name: "UX_BranchTransfers_TenantId_TransferNumber",
                table: "BranchTransfers");

            migrationBuilder.DropIndex(
                name: "UX_Branches_TenantId_Code",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesReturnHeaders");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerLedgers_Customers_CustomerId",
                table: "CustomerLedgers",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Categories_CategoryId",
                table: "Items",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Units_UnitId",
                table: "Items",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesDetails_Items_ItemId",
                table: "SalesDetails",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StockAdjustmentDetails_Items_ItemId",
                table: "StockAdjustmentDetails",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StockInDetails_Items_ItemId",
                table: "StockInDetails",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
