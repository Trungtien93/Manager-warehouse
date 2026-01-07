using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MNBEMART.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockTransferDetails_StockLots_LotId",
                table: "StockTransferDetails");

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_UserId_CreatedAt",
                table: "ChatMessages",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransferDetails_StockLots_LotId",
                table: "StockTransferDetails",
                column: "LotId",
                principalTable: "StockLots",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockTransferDetails_StockLots_LotId",
                table: "StockTransferDetails");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransferDetails_StockLots_LotId",
                table: "StockTransferDetails",
                column: "LotId",
                principalTable: "StockLots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
