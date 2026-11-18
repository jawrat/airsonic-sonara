using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sonara.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TrackId = table.Column<int>(type: "integer", nullable: true),
                    AlbumId = table.Column<int>(type: "integer", nullable: true),
                    ArtistId = table.Column<int>(type: "integer", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stars_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Stars_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Stars_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Stars_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stars_AlbumId",
                table: "Stars",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_Stars_ArtistId",
                table: "Stars",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_Stars_TrackId",
                table: "Stars",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_Stars_UserId_AlbumId",
                table: "Stars",
                columns: new[] { "UserId", "AlbumId" },
                unique: true,
                filter: "\"AlbumId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stars_UserId_ArtistId",
                table: "Stars",
                columns: new[] { "UserId", "ArtistId" },
                unique: true,
                filter: "\"ArtistId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stars_UserId_TrackId",
                table: "Stars",
                columns: new[] { "UserId", "TrackId" },
                unique: true,
                filter: "\"TrackId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Stars");
        }
    }
}
