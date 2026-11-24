using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingTokenUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "embedding_token_usage",
                columns: table => new
                {
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tokens = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_embedding_token_usage", x => x.model);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "embedding_token_usage");
        }
    }
}
