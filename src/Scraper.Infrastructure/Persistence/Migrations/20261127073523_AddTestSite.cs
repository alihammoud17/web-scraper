using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scraper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "sites",
                columns: new[] { "id", "allowed_path_prefixes", "base_url", "fetch_mode", "max_depth", "max_pages", "name", "requests_per_second", "seed_urls" },
                values: new object[] { "testsite", new string[0], "http://testsite:8080/", "Http", 3, 100, "Scraper Test Site", 1m, new[] { "http://testsite:8080/" } });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "sites",
                keyColumn: "id",
                keyValue: "testsite");
        }
    }
}
