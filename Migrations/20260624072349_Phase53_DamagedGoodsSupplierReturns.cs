using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class Phase53_DamagedGoodsSupplierReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DamagedStock",
                table: "Items",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DamagedStock",
                table: "BranchProductStocks",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "DamagedGoodsHeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    DamageNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DamageDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DamagedGoodsHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DamagedGoodsHeaders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DamagedGoodsHeaders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DamagedGoodsDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DamagedGoodsHeaderId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    ConversionQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DamagedGoodsDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DamagedGoodsDetails_DamagedGoodsHeaders_DamagedGoodsHeaderId",
                        column: x => x.DamagedGoodsHeaderId,
                        principalTable: "DamagedGoodsHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DamagedGoodsDetails_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DamagedGoodsDetails_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnHeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    ReturnNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReturnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LinkedDamagedGoodsId = table.Column<int>(type: "int", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnHeaders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupplierReturnHeaders_DamagedGoodsHeaders_LinkedDamagedGoodsId",
                        column: x => x.LinkedDamagedGoodsId,
                        principalTable: "DamagedGoodsHeaders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupplierReturnHeaders_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnHeaders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierReturnHeaderId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    ConversionQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDetails_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDetails_SupplierReturnHeaders_SupplierReturnHeaderId",
                        column: x => x.SupplierReturnHeaderId,
                        principalTable: "SupplierReturnHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDetails_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DamagedGoodsDetails_DamagedGoodsHeaderId",
                table: "DamagedGoodsDetails",
                column: "DamagedGoodsHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_DamagedGoodsDetails_ItemId",
                table: "DamagedGoodsDetails",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DamagedGoodsDetails_UnitId",
                table: "DamagedGoodsDetails",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_DamagedGoodsHeaders_BranchId",
                table: "DamagedGoodsHeaders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "UX_DamagedGoodsHeaders_TenantId_DamageNumber",
                table: "DamagedGoodsHeaders",
                columns: new[] { "TenantId", "DamageNumber" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDetails_ItemId",
                table: "SupplierReturnDetails",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDetails_SupplierReturnHeaderId",
                table: "SupplierReturnDetails",
                column: "SupplierReturnHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDetails_UnitId",
                table: "SupplierReturnDetails",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnHeaders_BranchId",
                table: "SupplierReturnHeaders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnHeaders_LinkedDamagedGoodsId",
                table: "SupplierReturnHeaders",
                column: "LinkedDamagedGoodsId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnHeaders_SupplierId",
                table: "SupplierReturnHeaders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "UX_SupplierReturnHeaders_TenantId_ReturnNumber",
                table: "SupplierReturnHeaders",
                columns: new[] { "TenantId", "ReturnNumber" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DamagedGoodsDetails");

            migrationBuilder.DropTable(
                name: "SupplierReturnDetails");

            migrationBuilder.DropTable(
                name: "SupplierReturnHeaders");

            migrationBuilder.DropTable(
                name: "DamagedGoodsHeaders");

            migrationBuilder.DropColumn(
                name: "DamagedStock",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DamagedStock",
                table: "BranchProductStocks");
        }
    }
}
