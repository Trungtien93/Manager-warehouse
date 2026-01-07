using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MNBEMART.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationSettings",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    EnableSound = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EnableDesktopNotifications = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EnableEmailNotifications = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    SoundType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "default"),
                    UpdateFrequency = table.Column<int>(type: "int", nullable: false, defaultValue: 30),
                    EnabledTypes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EmailDigestFrequency = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
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
                name: "IX_NotificationSettings_UserId",
                table: "NotificationSettings",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationSettings");
        }
    }
}



