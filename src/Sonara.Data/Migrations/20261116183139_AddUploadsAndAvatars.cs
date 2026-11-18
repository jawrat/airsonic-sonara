using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonara.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadsAndAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarId",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "CustomAvatar",
                table: "Users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowUploads",
                table: "MusicFolders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CustomAvatar",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AllowUploads",
                table: "MusicFolders");
        }
    }
}
