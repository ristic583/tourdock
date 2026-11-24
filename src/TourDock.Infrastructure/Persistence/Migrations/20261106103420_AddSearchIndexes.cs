using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateIndex(
                name: "ix_slots_local_date_tour_id",
                table: "slots",
                columns: new[] { "local_date", "tour_id" },
                filter: "status = 'Open'");

            // Trigram index: lets "city ILIKE 'spl%'" (and '%plit%') use an index instead of scanning all tours.
            migrationBuilder.Sql("CREATE INDEX ix_tours_city_trgm ON tours USING gin (city gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tours_city_trgm;");

            migrationBuilder.DropIndex(
                name: "ix_slots_local_date_tour_id",
                table: "slots");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
