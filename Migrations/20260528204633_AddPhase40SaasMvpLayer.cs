using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HardwareManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase40SaasMvpLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpirationDate",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxBranches",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxProducts",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxUsers",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Tenants",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionPlanId",
                table: "Tenants",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerName",
                table: "Branches",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Quotations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    QuotationNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    QuotationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotations_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Quotations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Quotations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MaxBranches = table.Column<int>(type: "int", nullable: false),
                    MaxUsers = table.Column<int>(type: "int", nullable: false),
                    MaxProducts = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuotationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuotationId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuotationItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QuotationItems_Quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "Quotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_SubscriptionPlanId",
                table: "Tenants",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationItems_ItemId",
                table: "QuotationItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationItems_QuotationId",
                table: "QuotationItems",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_BranchId",
                table: "Quotations",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_CustomerId",
                table: "Quotations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_QuotationNo",
                table: "Quotations",
                column: "QuotationNo");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_TenantId_BranchId_QuotationDate",
                table: "Quotations",
                columns: new[] { "TenantId", "BranchId", "QuotationDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_SubscriptionPlans_SubscriptionPlanId",
                table: "Tenants",
                column: "SubscriptionPlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_SubscriptionPlans_SubscriptionPlanId",
                table: "Tenants");

            migrationBuilder.DropTable(
                name: "QuotationItems");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropTable(
                name: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_SubscriptionPlanId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MaxBranches",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MaxProducts",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MaxUsers",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ManagerName",
                table: "Branches");
        }
    }
}
