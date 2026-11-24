using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    history = table.Column<string>(type: "jsonb", nullable: false),
                    turns = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assistant_daily_usage",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    messages = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_daily_usage", x => x.day);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_conversations_expires_at",
                table: "assistant_conversations",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_conversations");

            migrationBuilder.DropTable(
                name: "assistant_daily_usage");
        }
    }
}
