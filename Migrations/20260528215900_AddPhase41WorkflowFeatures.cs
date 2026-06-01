using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase41WorkflowFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConvertedToSaleId",
                table: "Quotations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Items",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliveryReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DRNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    DeliveryAddress = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SalesHeaderId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryReceipts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryReceipts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DeliveryReceipts_SalesHeaders_SalesHeaderId",
                        column: x => x.SalesHeaderId,
                        principalTable: "SalesHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DeliveryReceipts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryReceiptItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliveryReceiptId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryReceiptItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryReceiptItems_DeliveryReceipts_DeliveryReceiptId",
                        column: x => x.DeliveryReceiptId,
                        principalTable: "DeliveryReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryReceiptItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_ConvertedToSaleId",
                table: "Quotations",
                column: "ConvertedToSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReceiptItems_DeliveryReceiptId",
                table: "DeliveryReceiptItems",
                column: "DeliveryReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReceiptItems_ItemId",
                table: "DeliveryReceiptItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReceipts_BranchId",
                table: "DeliveryReceipts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReceipts_CustomerId",
                table: "DeliveryReceipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReceipts_DRNumber",
                table: "DeliveryReceipts",
                column: "DRNumber");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReceipts_SalesHeaderId",
                table: "DeliveryReceipts",
                column: "SalesHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReceipts_TenantId_BranchId_DeliveryDate",
                table: "DeliveryReceipts",
                columns: new[] { "TenantId", "BranchId", "DeliveryDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Quotations_SalesHeaders_ConvertedToSaleId",
                table: "Quotations",
                column: "ConvertedToSaleId",
                principalTable: "SalesHeaders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_SalesHeaders_ConvertedToSaleId",
                table: "Quotations");

            migrationBuilder.DropTable(
                name: "DeliveryReceiptItems");

            migrationBuilder.DropTable(
                name: "DeliveryReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_ConvertedToSaleId",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "ConvertedToSaleId",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "Items");
        }
    }
}
