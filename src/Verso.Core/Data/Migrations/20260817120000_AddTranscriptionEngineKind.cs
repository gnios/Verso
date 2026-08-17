using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Verso.Core.Data;

#nullable disable

namespace Verso.Core.Data.Migrations
{
    [DbContext(typeof(VersoDbContext))]
    [Migration("20260817120000_AddTranscriptionEngineKind")]
    public partial class AddTranscriptionEngineKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultEngine",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultParakeetModel",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Engine",
                table: "Transcriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ParakeetModel",
                table: "Transcriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DefaultEngine", table: "UserSettings");
            migrationBuilder.DropColumn(name: "DefaultParakeetModel", table: "UserSettings");
            migrationBuilder.DropColumn(name: "Engine", table: "Transcriptions");
            migrationBuilder.DropColumn(name: "ParakeetModel", table: "Transcriptions");
        }
    }
}
