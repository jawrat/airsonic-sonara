using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonara.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPodcastRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DownloadedUtc",
                table: "PodcastEpisodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Pinned",
                table: "PodcastEpisodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DownloadedUtc",
                table: "PodcastEpisodes");

            migrationBuilder.DropColumn(
                name: "Pinned",
                table: "PodcastEpisodes");
        }
    }
}
