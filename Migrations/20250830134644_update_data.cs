using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MNBEMART.Migrations
{
    /// <inheritdoc />
    public partial class update_data : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountInWords",
                table: "StockReceipts");

            migrationBuilder.DropColumn(
                name: "CreditAccount",
                table: "StockReceipts");

            migrationBuilder.DropColumn(
                name: "DebitAccount",
                table: "StockReceipts");

            migrationBuilder.RenameColumn(
                name: "OriginalDocCount",
                table: "StockReceipts",
                newName: "ReceivedById");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAt",
                table: "StockReceipts",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "StockReceipts");

            migrationBuilder.RenameColumn(
                name: "ReceivedById",
                table: "StockReceipts",
                newName: "OriginalDocCount");

            migrationBuilder.AddColumn<string>(
                name: "AmountInWords",
                table: "StockReceipts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditAccount",
                table: "StockReceipts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DebitAccount",
                table: "StockReceipts",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
