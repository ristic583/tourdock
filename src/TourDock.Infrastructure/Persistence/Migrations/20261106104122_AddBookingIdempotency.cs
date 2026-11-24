using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "bookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_slot_ends_at",
                table: "bookings",
                column: "slot_ends_at",
                filter: "status = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ux_bookings_customer_idempotency_key",
                table: "bookings",
                columns: new[] { "customer_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_slot_ends_at",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ux_bookings_customer_idempotency_key",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "bookings");
        }
    }
}
