using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase25BranchOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "StockAdjustmentHeaders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BranchTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransferNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FromBranchId = table.Column<int>(type: "int", nullable: false),
                    ToBranchId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchTransfers_Branches_FromBranchId",
                        column: x => x.FromBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BranchTransfers_Branches_ToBranchId",
                        column: x => x.ToBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BranchTransferItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchTransferId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchTransferItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchTransferItems_BranchTransfers_BranchTransferId",
                        column: x => x.BranchTransferId,
                        principalTable: "BranchTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BranchTransferItems_Items_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentHeaders_BranchId",
                table: "StockAdjustmentHeaders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchTransferItems_BranchTransferId",
                table: "BranchTransferItems",
                column: "BranchTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchTransferItems_ProductId",
                table: "BranchTransferItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchTransfers_FromBranchId",
                table: "BranchTransfers",
                column: "FromBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchTransfers_ToBranchId",
                table: "BranchTransfers",
                column: "ToBranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockAdjustmentHeaders_Branches_BranchId",
                table: "StockAdjustmentHeaders",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockAdjustmentHeaders_Branches_BranchId",
                table: "StockAdjustmentHeaders");

            migrationBuilder.DropTable(
                name: "BranchTransferItems");

            migrationBuilder.DropTable(
                name: "BranchTransfers");

            migrationBuilder.DropIndex(
                name: "IX_StockAdjustmentHeaders_BranchId",
                table: "StockAdjustmentHeaders");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "StockAdjustmentHeaders");
        }
    }
}
