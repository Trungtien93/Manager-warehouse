using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MNBEMART.Migrations
{
    /// <inheritdoc />
    public partial class Update_warehouse_material : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Inventory",
                table: "Materials",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "Materials",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_WarehouseId",
                table: "Materials",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_Warehouses_WarehouseId",
                table: "Materials",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_Warehouses_WarehouseId",
                table: "Materials");

            migrationBuilder.DropIndex(
                name: "IX_Materials_WarehouseId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Materials");

            migrationBuilder.AlterColumn<string>(
                name: "Inventory",
                table: "Materials",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
