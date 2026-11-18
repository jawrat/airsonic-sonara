using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sonara.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsRolesAndApiParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllFolders",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CommentRole",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CoverArtRole",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "DownloadRole",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxBitRate",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PlaylistRole",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PodcastRole",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SettingsRole",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShareRole",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StreamRole",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "UploadRole",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "InternetRadioStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StreamUrl = table.Column<string>(type: "text", nullable: false),
                    HomePageUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternetRadioStations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaRatings",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MediaId = table.Column<string>(type: "text", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaRatings", x => new { x.UserId, x.MediaId });
                    table.ForeignKey(
                        name: "FK_MediaRatings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaShares",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TrackIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastVisitedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VisitCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaShares_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NowPlayingEntries",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Client = table.Column<string>(type: "text", nullable: false),
                    TrackId = table.Column<int>(type: "integer", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NowPlayingEntries", x => new { x.UserId, x.Client });
                    table.ForeignKey(
                        name: "FK_NowPlayingEntries_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NowPlayingEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TrackId = table.Column<int>(type: "integer", nullable: false),
                    PlayedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayEvents_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "UserMusicFolders",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MusicFolderId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMusicFolders", x => new { x.UserId, x.MusicFolderId });
                    table.ForeignKey(
                        name: "FK_UserMusicFolders_MusicFolders_MusicFolderId",
                        column: x => x.MusicFolderId,
                        principalTable: "MusicFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserMusicFolders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_CreatedUtc",
                table: "ChatMessages",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_UserId",
                table: "ChatMessages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaShares_UserId",
                table: "MediaShares",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NowPlayingEntries_TrackId",
                table: "NowPlayingEntries",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_TrackId",
                table: "PlayEvents",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_UserId_PlayedUtc",
                table: "PlayEvents",
                columns: new[] { "UserId", "PlayedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserMusicFolders_MusicFolderId",
                table: "UserMusicFolders",
                column: "MusicFolderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "InternetRadioStations");

            migrationBuilder.DropTable(
                name: "MediaRatings");

            migrationBuilder.DropTable(
                name: "MediaShares");

            migrationBuilder.DropTable(
                name: "NowPlayingEntries");

            migrationBuilder.DropTable(
                name: "PlayEvents");

            migrationBuilder.DropTable(
                name: "ServerSettings");

            migrationBuilder.DropTable(
                name: "UserMusicFolders");

            migrationBuilder.DropColumn(
                name: "AllFolders",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CommentRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CoverArtRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DownloadRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MaxBitRate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PlaylistRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PodcastRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SettingsRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ShareRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StreamRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UploadRole",
                table: "Users");
        }
    }
}
