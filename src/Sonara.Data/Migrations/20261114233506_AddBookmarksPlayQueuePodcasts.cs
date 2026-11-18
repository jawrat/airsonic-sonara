using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sonara.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookmarksPlayQueuePodcasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayQueues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TrackIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    CurrentTrackId = table.Column<int>(type: "integer", nullable: true),
                    PositionMs = table.Column<long>(type: "bigint", nullable: false),
                    ChangedBy = table.Column<string>(type: "text", nullable: true),
                    ChangedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayQueues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayQueues_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PodcastChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    ImagePath = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    LastRefreshUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodcastChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PodcastEpisodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChannelId = table.Column<int>(type: "integer", nullable: false),
                    Guid = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PublishUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AudioUrl = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    FilePath = table.Column<string>(type: "text", nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: true),
                    Suffix = table.Column<string>(type: "text", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodcastEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PodcastEpisodes_PodcastChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "PodcastChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TrackId = table.Column<int>(type: "integer", nullable: true),
                    EpisodeId = table.Column<int>(type: "integer", nullable: true),
                    PositionMs = table.Column<long>(type: "bigint", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookmarks_PodcastEpisodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "PodcastEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_EpisodeId",
                table: "Bookmarks",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_TrackId",
                table: "Bookmarks",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_UserId_EpisodeId",
                table: "Bookmarks",
                columns: new[] { "UserId", "EpisodeId" },
                unique: true,
                filter: "\"EpisodeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_UserId_TrackId",
                table: "Bookmarks",
                columns: new[] { "UserId", "TrackId" },
                unique: true,
                filter: "\"TrackId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlayQueues_UserId",
                table: "PlayQueues",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastChannels_Url",
                table: "PodcastChannels",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodes_ChannelId_Guid",
                table: "PodcastEpisodes",
                columns: new[] { "ChannelId", "Guid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodes_PublishUtc",
                table: "PodcastEpisodes",
                column: "PublishUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookmarks");

            migrationBuilder.DropTable(
                name: "PlayQueues");

            migrationBuilder.DropTable(
                name: "PodcastEpisodes");

            migrationBuilder.DropTable(
                name: "PodcastChannels");
        }
    }
}
