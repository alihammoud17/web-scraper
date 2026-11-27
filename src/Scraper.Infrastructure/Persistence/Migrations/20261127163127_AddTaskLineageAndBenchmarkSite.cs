using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scraper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskLineageAndBenchmarkSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retried_at",
                table: "dead_letter_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "discovered_by_task_id",
                table: "crawl_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "sites",
                keyColumn: "id",
                keyValue: "testsite",
                columns: new[] { "max_pages", "requests_per_second" },
                values: new object[] { 1000, 5m });

            migrationBuilder.CreateIndex(
                name: "ix_crawl_tasks_discovered_by_task_id",
                table: "crawl_tasks",
                column: "discovered_by_task_id");

            migrationBuilder.AddForeignKey(
                name: "fk_crawl_tasks_crawl_tasks_discovered_by_task_id",
                table: "crawl_tasks",
                column: "discovered_by_task_id",
                principalTable: "crawl_tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_crawl_tasks_crawl_tasks_discovered_by_task_id",
                table: "crawl_tasks");

            migrationBuilder.DropIndex(
                name: "ix_crawl_tasks_discovered_by_task_id",
                table: "crawl_tasks");

            migrationBuilder.DropColumn(
                name: "retried_at",
                table: "dead_letter_entries");

            migrationBuilder.DropColumn(
                name: "discovered_by_task_id",
                table: "crawl_tasks");

            migrationBuilder.UpdateData(
                table: "sites",
                keyColumn: "id",
                keyValue: "testsite",
                columns: new[] { "max_pages", "requests_per_second" },
                values: new object[] { 100, 1m });
        }
    }
}
