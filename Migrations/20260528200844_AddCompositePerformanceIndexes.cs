using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositePerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_SupplierId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerLedgers_CustomerId",
                table: "CustomerLedgers");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierId_PaymentDate",
                table: "SupplierPayments",
                columns: new[] { "SupplierId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockInHeaders_TenantId_DateReceived_PaymentStatus",
                table: "StockInHeaders",
                columns: new[] { "TenantId", "DateReceived", "PaymentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesHeaders_TenantId_BranchId_SalesDate_Status",
                table: "SalesHeaders",
                columns: new[] { "TenantId", "BranchId", "SalesDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_TenantId_CreatedAtUtc",
                table: "ImportBatches",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_BranchId_ExpenseDate",
                table: "Expenses",
                columns: new[] { "TenantId", "BranchId", "ExpenseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgers_CustomerId_Id",
                table: "CustomerLedgers",
                columns: new[] { "CustomerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchTransfers_TenantId_Status_CreatedAtUtc",
                table: "BranchTransfers",
                columns: new[] { "TenantId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchProductStocks_BranchId_TenantId_Quantity",
                table: "BranchProductStocks",
                columns: new[] { "BranchId", "TenantId", "Quantity" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditTrails_TenantId_CreatedAt",
                table: "AuditTrails",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_SupplierId_PaymentDate",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_StockInHeaders_TenantId_DateReceived_PaymentStatus",
                table: "StockInHeaders");

            migrationBuilder.DropIndex(
                name: "IX_SalesHeaders_TenantId_BranchId_SalesDate_Status",
                table: "SalesHeaders");

            migrationBuilder.DropIndex(
                name: "IX_ImportBatches_TenantId_CreatedAtUtc",
                table: "ImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_TenantId_BranchId_ExpenseDate",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_CustomerLedgers_CustomerId_Id",
                table: "CustomerLedgers");

            migrationBuilder.DropIndex(
                name: "IX_BranchTransfers_TenantId_Status_CreatedAtUtc",
                table: "BranchTransfers");

            migrationBuilder.DropIndex(
                name: "IX_BranchProductStocks_BranchId_TenantId_Quantity",
                table: "BranchProductStocks");

            migrationBuilder.DropIndex(
                name: "IX_AuditTrails_TenantId_CreatedAt",
                table: "AuditTrails");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierId",
                table: "SupplierPayments",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgers_CustomerId",
                table: "CustomerLedgers",
                column: "CustomerId");
        }
    }
}
