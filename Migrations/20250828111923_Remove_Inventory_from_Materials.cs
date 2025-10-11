using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MNBEMART.Migrations
{
    /// <inheritdoc />
    public partial class Remove_Inventory_from_Materials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_Warehouses_WarehouseId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "Inventory",
                table: "Materials");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_Warehouses_WarehouseId",
                table: "Materials",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_Warehouses_WarehouseId",
                table: "Materials");

            migrationBuilder.AddColumn<string>(
                name: "Inventory",
                table: "Materials",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_Warehouses_WarehouseId",
                table: "Materials",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id");
        }
    }
}
