using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class Phase51_UnitConversionAndCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "SystemSettings",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyName",
                table: "SystemSettings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "BaseQuantity",
                table: "StockInDetails",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionQuantity",
                table: "StockInDetails",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedQuantity",
                table: "StockInDetails",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReceivedUnitId",
                table: "StockInDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseQuantity",
                table: "PurchaseOrderItems",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionQuantity",
                table: "PurchaseOrderItems",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderedQuantity",
                table: "PurchaseOrderItems",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "OrderedUnitId",
                table: "PurchaseOrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaseUnitId",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE Items SET BaseUnitId = UnitId WHERE BaseUnitId = 0 OR BaseUnitId IS NULL;");

            migrationBuilder.Sql(@"
                UPDATE SystemSettings SET CurrencyCode = 'PHP', CurrencyName = 'Philippine Peso'
                WHERE CurrencyCode = '' OR CurrencyCode IS NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE StockInDetails SET
                    BaseQuantity = Quantity,
                    ReceivedQuantity = Quantity,
                    ConversionQuantity = 1,
                    ReceivedUnitId = (SELECT i.BaseUnitId FROM Items i WHERE i.Id = StockInDetails.ItemId)
                WHERE BaseQuantity = 0;
            ");

            migrationBuilder.Sql(@"
                UPDATE PurchaseOrderItems SET
                    OrderedQuantity = Quantity,
                    BaseQuantity = Quantity,
                    ConversionQuantity = 1,
                    OrderedUnitId = (SELECT i.BaseUnitId FROM Items i WHERE i.Id = PurchaseOrderItems.ItemId)
                WHERE OrderedQuantity = 0;
            ");

            migrationBuilder.CreateTable(
                name: "ItemUnitConversions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    ConversionQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    IsDefaultPurchaseUnit = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUnitConversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemUnitConversions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemUnitConversions_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockInDetails_ReceivedUnitId",
                table: "StockInDetails",
                column: "ReceivedUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderItems_OrderedUnitId",
                table: "PurchaseOrderItems",
                column: "OrderedUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_BaseUnitId",
                table: "Items",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnitConversions_TenantId",
                table: "ItemUnitConversions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnitConversions_UnitId",
                table: "ItemUnitConversions",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "UX_ItemUnitConversions_ItemId_UnitId",
                table: "ItemUnitConversions",
                columns: new[] { "ItemId", "UnitId" },
                unique: true);

            migrationBuilder.Sql(@"
                INSERT INTO ItemUnitConversions (TenantId, ItemId, UnitId, ConversionQuantity, IsDefaultPurchaseUnit, IsActive, CreatedAtUtc, UpdatedAtUtc)
                SELECT i.TenantId, i.Id, i.BaseUnitId, 1, 1, 1, GETUTCDATE(), GETUTCDATE()
                FROM Items i
                WHERE NOT EXISTS (
                    SELECT 1 FROM ItemUnitConversions c WHERE c.ItemId = i.Id AND c.UnitId = i.BaseUnitId
                );
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Units_BaseUnitId",
                table: "Items",
                column: "BaseUnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderItems_Units_OrderedUnitId",
                table: "PurchaseOrderItems",
                column: "OrderedUnitId",
                principalTable: "Units",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StockInDetails_Units_ReceivedUnitId",
                table: "StockInDetails",
                column: "ReceivedUnitId",
                principalTable: "Units",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Units_BaseUnitId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderItems_Units_OrderedUnitId",
                table: "PurchaseOrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StockInDetails_Units_ReceivedUnitId",
                table: "StockInDetails");

            migrationBuilder.DropTable(
                name: "ItemUnitConversions");

            migrationBuilder.DropIndex(
                name: "IX_StockInDetails_ReceivedUnitId",
                table: "StockInDetails");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderItems_OrderedUnitId",
                table: "PurchaseOrderItems");

            migrationBuilder.DropIndex(
                name: "IX_Items_BaseUnitId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "CurrencyName",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BaseQuantity",
                table: "StockInDetails");

            migrationBuilder.DropColumn(
                name: "ConversionQuantity",
                table: "StockInDetails");

            migrationBuilder.DropColumn(
                name: "ReceivedQuantity",
                table: "StockInDetails");

            migrationBuilder.DropColumn(
                name: "ReceivedUnitId",
                table: "StockInDetails");

            migrationBuilder.DropColumn(
                name: "BaseQuantity",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "ConversionQuantity",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "OrderedQuantity",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "OrderedUnitId",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "BaseUnitId",
                table: "Items");
        }
    }
}
