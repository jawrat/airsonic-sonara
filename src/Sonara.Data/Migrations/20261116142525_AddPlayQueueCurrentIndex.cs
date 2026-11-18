using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonara.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayQueueCurrentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentIndex",
                table: "PlayQueues",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentIndex",
                table: "PlayQueues");
        }
    }
}
