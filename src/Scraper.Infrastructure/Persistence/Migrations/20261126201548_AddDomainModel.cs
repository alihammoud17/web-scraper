using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Scraper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sites",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    seed_urls = table.Column<string[]>(type: "text[]", nullable: false),
                    allowed_path_prefixes = table.Column<string[]>(type: "text[]", nullable: false),
                    fetch_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requests_per_second = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    max_depth = table.Column<int>(type: "integer", nullable: false),
                    max_pages = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "crawl_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crawl_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_crawl_jobs_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status_code = table.Column<int>(type: "integer", nullable: true),
                    e_tag = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pages", x => x.id);
                    table.ForeignKey(
                        name: "fk_pages_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crawl_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    crawl_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    page_id = table.Column<Guid>(type: "uuid", nullable: true),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crawl_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_crawl_tasks_crawl_jobs_crawl_job_id",
                        column: x => x.crawl_job_id,
                        principalTable: "crawl_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_crawl_tasks_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_links_pages_source_page_id",
                        column: x => x.source_page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dead_letter_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    crawl_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    dead_lettered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dead_letter_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_dead_letter_entries_crawl_tasks_crawl_task_id",
                        column: x => x.crawl_task_id,
                        principalTable: "crawl_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dead_letter_entries_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "page_fetches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crawl_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fetch_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    headers = table.Column<string>(type: "jsonb", nullable: false),
                    e_tag = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    raw_html = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_fetches", x => x.id);
                    table.ForeignKey(
                        name: "fk_page_fetches_crawl_tasks_crawl_task_id",
                        column: x => x.crawl_task_id,
                        principalTable: "crawl_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_page_fetches_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "content_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    block_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    text = table.Column<string>(type: "text", nullable: true),
                    heading_level = table.Column<int>(type: "integer", nullable: true),
                    list_items = table.Column<string[]>(type: "text[]", nullable: true),
                    list_ordered = table.Column<bool>(type: "boolean", nullable: true),
                    code_language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    caption = table.Column<string>(type: "text", nullable: true),
                    header = table.Column<string>(type: "jsonb", nullable: true),
                    rows = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_blocks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    vehicle_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    site_fields = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_documents_document_versions_current_version_id",
                        column: x => x.current_version_id,
                        principalTable: "document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metrics_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crawl_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    downloads = table.Column<long>(type: "bigint", nullable: true),
                    likes = table.Column<long>(type: "bigint", nullable: true),
                    views = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_metrics_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_metrics_snapshots_crawl_tasks_crawl_task_id",
                        column: x => x.crawl_task_id,
                        principalTable: "crawl_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_metrics_snapshots_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "sites",
                columns: new[] { "id", "allowed_path_prefixes", "base_url", "fetch_mode", "max_depth", "max_pages", "name", "requests_per_second", "seed_urls" },
                values: new object[,]
                {
                    { "gta5mods", new[] { "/vehicles" }, "https://www.gta5-mods.com/vehicles", "Http", 2, 1000, "GTA5-Mods.com", 0.5m, new[] { "https://www.gta5-mods.com/vehicles" } },
                    { "gtacars", new string[0], "https://gtacars.net/", "Http", 3, 1000, "GTACars.net", 0.5m, new[] { "https://gtacars.net/" } },
                    { "newswire", new[] { "/newswire" }, "https://www.rockstargames.com/newswire", "Browser", 2, 200, "Rockstar Newswire", 0.2m, new[] { "https://www.rockstargames.com/newswire" } }
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_blocks_document_version_id_ordinal",
                table: "content_blocks",
                columns: new[] { "document_version_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_crawl_jobs_site_id",
                table: "crawl_jobs",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_crawl_tasks_crawl_job_id_status",
                table: "crawl_tasks",
                columns: new[] { "crawl_job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_crawl_tasks_crawl_job_id_url",
                table: "crawl_tasks",
                columns: new[] { "crawl_job_id", "url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_crawl_tasks_page_id",
                table: "crawl_tasks",
                column: "page_id");

            migrationBuilder.CreateIndex(
                name: "ix_dead_letter_entries_crawl_task_id",
                table: "dead_letter_entries",
                column: "crawl_task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dead_letter_entries_site_id",
                table: "dead_letter_entries",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_document_id_content_hash",
                table: "document_versions",
                columns: new[] { "document_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_vehicle_name",
                table: "document_versions",
                column: "vehicle_name");

            migrationBuilder.CreateIndex(
                name: "ix_documents_current_version_id",
                table: "documents",
                column: "current_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_page_id",
                table: "documents",
                column: "page_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_links_source_page_id_target_url",
                table: "links",
                columns: new[] { "source_page_id", "target_url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_metrics_snapshots_crawl_task_id",
                table: "metrics_snapshots",
                column: "crawl_task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_metrics_snapshots_document_id",
                table: "metrics_snapshots",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_page_fetches_crawl_task_id",
                table: "page_fetches",
                column: "crawl_task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_page_fetches_page_id",
                table: "page_fetches",
                column: "page_id");

            migrationBuilder.CreateIndex(
                name: "ix_pages_site_id_url",
                table: "pages",
                columns: new[] { "site_id", "url" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_content_blocks_document_versions_document_version_id",
                table: "content_blocks",
                column: "document_version_id",
                principalTable: "document_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_document_versions_documents_document_id",
                table: "document_versions",
                column: "document_id",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documents_document_versions_current_version_id",
                table: "documents");

            migrationBuilder.DropTable(
                name: "content_blocks");

            migrationBuilder.DropTable(
                name: "dead_letter_entries");

            migrationBuilder.DropTable(
                name: "links");

            migrationBuilder.DropTable(
                name: "metrics_snapshots");

            migrationBuilder.DropTable(
                name: "page_fetches");

            migrationBuilder.DropTable(
                name: "crawl_tasks");

            migrationBuilder.DropTable(
                name: "crawl_jobs");

            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "pages");

            migrationBuilder.DropTable(
                name: "sites");
        }
    }
}
