using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MNBEMART.Migrations
{
    /// <inheritdoc />
    public partial class AddLotFieldsToStockTransferDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EstimatedCost",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "EstimatedDistance",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "TotalWeight",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "TransferMethod",
                table: "StockTransfers");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "StockTransferDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LotId",
                table: "StockTransferDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManufactureDate",
                table: "StockTransferDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsImportant",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Notifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DemandForecasts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ForecastDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ForecastedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ConfidenceLevel = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HistoricalAverage = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Trend = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandForecasts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemandForecasts_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DemandForecasts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSettings",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    EnableSound = table.Column<bool>(type: "bit", nullable: false),
                    EnableDesktopNotifications = table.Column<bool>(type: "bit", nullable: false),
                    EnableEmailNotifications = table.Column<bool>(type: "bit", nullable: false),
                    SoundType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "default"),
                    UpdateFrequency = table.Column<int>(type: "int", nullable: false, defaultValue: 30),
                    EnabledTypes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EmailDigestFrequency = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_NotificationSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferDetails_LotId",
                table: "StockTransferDetails",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IsDeleted_DeletedAt",
                table: "Notifications",
                columns: new[] { "IsDeleted", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsArchived",
                table: "Notifications",
                columns: new[] { "UserId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsImportant",
                table: "Notifications",
                columns: new[] { "UserId", "IsImportant" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DemandForecasts_MaterialId_WarehouseId_ForecastDate",
                table: "DemandForecasts",
                columns: new[] { "MaterialId", "WarehouseId", "ForecastDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemandForecasts_WarehouseId",
                table: "DemandForecasts",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransferDetails_StockLots_LotId",
                table: "StockTransferDetails",
                column: "LotId",
                principalTable: "StockLots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockTransferDetails_StockLots_LotId",
                table: "StockTransferDetails");

            migrationBuilder.DropTable(
                name: "DemandForecasts");

            migrationBuilder.DropTable(
                name: "NotificationSettings");

            migrationBuilder.DropIndex(
                name: "IX_StockTransferDetails_LotId",
                table: "StockTransferDetails");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_IsDeleted_DeletedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_IsArchived",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_IsImportant",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "StockTransferDetails");

            migrationBuilder.DropColumn(
                name: "LotId",
                table: "StockTransferDetails");

            migrationBuilder.DropColumn(
                name: "ManufactureDate",
                table: "StockTransferDetails");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsImportant",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Notifications");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCost",
                table: "StockTransfers",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedDistance",
                table: "StockTransfers",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalWeight",
                table: "StockTransfers",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferMethod",
                table: "StockTransfers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");
        }
    }
}
