using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIsolationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Units",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Suppliers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockInHeaders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockAdjustmentHeaders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SalesHeaders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Items",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ImportBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Categories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "BranchTransfers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "BranchProductStocks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Branches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AuditTrails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Units_TenantId",
                table: "Units",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TenantId",
                table: "Suppliers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockInHeaders_TenantId",
                table: "StockInHeaders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentHeaders_TenantId",
                table: "StockAdjustmentHeaders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesHeaders_TenantId",
                table: "SalesHeaders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_TenantId",
                table: "Items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_TenantId",
                table: "ImportBatches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId",
                table: "Expenses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId",
                table: "Customers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_TenantId",
                table: "Categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchTransfers_TenantId",
                table: "BranchTransfers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchProductStocks_TenantId",
                table: "BranchProductStocks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_TenantId",
                table: "Branches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditTrails_TenantId",
                table: "AuditTrails",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Code",
                table: "Tenants",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_Tenants_TenantId",
                table: "Branches",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BranchProductStocks_Tenants_TenantId",
                table: "BranchProductStocks",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BranchTransfers_Tenants_TenantId",
                table: "BranchTransfers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Tenants_TenantId",
                table: "Categories",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Tenants_TenantId",
                table: "Customers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Tenants_TenantId",
                table: "Expenses",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatches_Tenants_TenantId",
                table: "ImportBatches",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Tenants_TenantId",
                table: "Items",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesHeaders_Tenants_TenantId",
                table: "SalesHeaders",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StockAdjustmentHeaders_Tenants_TenantId",
                table: "StockAdjustmentHeaders",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StockInHeaders_Tenants_TenantId",
                table: "StockInHeaders",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Tenants_TenantId",
                table: "Suppliers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Units_Tenants_TenantId",
                table: "Units",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Branches_Tenants_TenantId",
                table: "Branches");

            migrationBuilder.DropForeignKey(
                name: "FK_BranchProductStocks_Tenants_TenantId",
                table: "BranchProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_BranchTransfers_Tenants_TenantId",
                table: "BranchTransfers");

            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Tenants_TenantId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Tenants_TenantId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Tenants_TenantId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_ImportBatches_Tenants_TenantId",
                table: "ImportBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Tenants_TenantId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesHeaders_Tenants_TenantId",
                table: "SalesHeaders");

            migrationBuilder.DropForeignKey(
                name: "FK_StockAdjustmentHeaders_Tenants_TenantId",
                table: "StockAdjustmentHeaders");

            migrationBuilder.DropForeignKey(
                name: "FK_StockInHeaders_Tenants_TenantId",
                table: "StockInHeaders");

            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Tenants_TenantId",
                table: "Suppliers");

            migrationBuilder.DropForeignKey(
                name: "FK_Units_Tenants_TenantId",
                table: "Units");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Units_TenantId",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_TenantId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_StockInHeaders_TenantId",
                table: "StockInHeaders");

            migrationBuilder.DropIndex(
                name: "IX_StockAdjustmentHeaders_TenantId",
                table: "StockAdjustmentHeaders");

            migrationBuilder.DropIndex(
                name: "IX_SalesHeaders_TenantId",
                table: "SalesHeaders");

            migrationBuilder.DropIndex(
                name: "IX_Items_TenantId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_ImportBatches_TenantId",
                table: "ImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_TenantId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Categories_TenantId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_BranchTransfers_TenantId",
                table: "BranchTransfers");

            migrationBuilder.DropIndex(
                name: "IX_BranchProductStocks_TenantId",
                table: "BranchProductStocks");

            migrationBuilder.DropIndex(
                name: "IX_Branches_TenantId",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_AuditTrails_TenantId",
                table: "AuditTrails");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockInHeaders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockAdjustmentHeaders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesHeaders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BranchTransfers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BranchProductStocks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditTrails");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");
        }
    }
}
