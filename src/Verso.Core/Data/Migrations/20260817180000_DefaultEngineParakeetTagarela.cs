using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Verso.Core.Data;

#nullable disable

namespace Verso.Core.Data.Migrations
{
    [DbContext(typeof(VersoDbContext))]
    [Migration("20260817180000_DefaultEngineParakeetTagarela")]
    public partial class DefaultEngineParakeetTagarela : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE UserSettings
                SET DefaultEngine = 1,
                    DefaultParakeetModel = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE UserSettings
                SET DefaultEngine = 0,
                    DefaultParakeetModel = 0;
                """);
        }
    }
}
