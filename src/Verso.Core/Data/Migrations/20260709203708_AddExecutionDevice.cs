using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Verso.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Device",
                table: "Transcriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Device",
                table: "Transcriptions");
        }
    }
}
