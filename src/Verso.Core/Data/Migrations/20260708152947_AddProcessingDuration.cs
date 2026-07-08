using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Verso.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ProcessingDurationSeconds",
                table: "Transcriptions",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessingDurationSeconds",
                table: "Transcriptions");
        }
    }
}
