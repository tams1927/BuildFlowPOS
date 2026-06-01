using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiBranchSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "StockInHeaders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "SalesHeaders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ContactNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsMainBranch = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BranchProductStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ReorderLevel = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchProductStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchProductStocks_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BranchProductStocks_Items_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserBranches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockInHeaders_BranchId",
                table: "StockInHeaders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesHeaders_BranchId",
                table: "SalesHeaders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BranchId",
                table: "Expenses",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchProductStocks_BranchId_ProductId",
                table: "BranchProductStocks",
                columns: new[] { "BranchId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchProductStocks_ProductId",
                table: "BranchProductStocks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBranches_BranchId",
                table: "UserBranches",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Branches_BranchId",
                table: "Expenses",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesHeaders_Branches_BranchId",
                table: "SalesHeaders",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StockInHeaders_Branches_BranchId",
                table: "StockInHeaders",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Branches_BranchId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesHeaders_Branches_BranchId",
                table: "SalesHeaders");

            migrationBuilder.DropForeignKey(
                name: "FK_StockInHeaders_Branches_BranchId",
                table: "StockInHeaders");

            migrationBuilder.DropTable(
                name: "BranchProductStocks");

            migrationBuilder.DropTable(
                name: "UserBranches");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_StockInHeaders_BranchId",
                table: "StockInHeaders");

            migrationBuilder.DropIndex(
                name: "IX_SalesHeaders_BranchId",
                table: "SalesHeaders");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_BranchId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "StockInHeaders");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SalesHeaders");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Expenses");
        }
    }
}
