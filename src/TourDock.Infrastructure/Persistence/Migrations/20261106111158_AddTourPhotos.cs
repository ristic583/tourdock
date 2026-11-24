using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTourPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tour_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tour_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_cover = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tour_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_tour_photos_tours_tour_id",
                        column: x => x.tour_id,
                        principalTable: "tours",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tour_photos_tour_id_sort_order",
                table: "tour_photos",
                columns: new[] { "tour_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_tour_photos_one_cover",
                table: "tour_photos",
                column: "tour_id",
                unique: true,
                filter: "is_cover");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tour_photos");
        }
    }
}
