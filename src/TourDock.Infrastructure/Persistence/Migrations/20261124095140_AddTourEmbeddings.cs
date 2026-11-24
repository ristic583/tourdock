using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTourEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:cube", ",,")
                .Annotation("Npgsql:PostgresExtension:earthdistance", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:cube", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:earthdistance", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            // EF has no model for this table on purpose: only the embedding sync job writes it (raw SQL upsert) and search
            // reads it through td_semantic_distance, so no .NET type has to understand pgvector's vector type.
            migrationBuilder.Sql(
                """
                CREATE TABLE tour_embeddings (
                    tour_id uuid PRIMARY KEY REFERENCES tours (id) ON DELETE CASCADE,
                    model varchar(64) NOT NULL,
                    content_hash char(64) NOT NULL,
                    embedding vector(1024) NOT NULL,
                    updated_at timestamptz NOT NULL
                );

                -- Cosine distance between a tour's embedding and a query embedding from the same model; NULL when the
                -- tour has no embedding from that model yet (those tours sort last). No ANN index: search filters first
                -- (date, guests, place), and an exact scan over the filtered tours is fast and never drops results.
                CREATE FUNCTION td_semantic_distance(p_tour_id uuid, p_model text, p_query real[])
                RETURNS double precision LANGUAGE sql STABLE PARALLEL SAFE
                AS $$
                    SELECT e.embedding <=> p_query::vector
                    FROM tour_embeddings e
                    WHERE e.tour_id = p_tour_id AND e.model = p_model
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS td_semantic_distance(uuid, text, real[]);
                DROP TABLE IF EXISTS tour_embeddings;
                """);

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:cube", ",,")
                .Annotation("Npgsql:PostgresExtension:earthdistance", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:cube", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:earthdistance", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
