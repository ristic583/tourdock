using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "agencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    suspension_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agency_members",
                columns: table => new
                {
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agency_members", x => new { x.agency_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_agency_members_agencies_agency_id",
                        column: x => x.agency_id,
                        principalTable: "agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    max_guests = table.Column<int>(type: "integer", nullable: false),
                    booking_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cancellation_policy = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    address = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    price_per_person_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    price_per_person_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    private_price_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    private_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tours", x => x.id);
                    table.CheckConstraint("ck_tours_duration", "duration > interval '0'");
                    table.CheckConstraint("ck_tours_max_guests", "max_guests BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "fk_tours_agencies_agency_id",
                        column: x => x.agency_id,
                        principalTable: "agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicles", x => x.id);
                    table.CheckConstraint("ck_vehicles_capacity", "capacity BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "fk_vehicles_agencies_agency_id",
                        column: x => x.agency_id,
                        principalTable: "agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tour_seasonal_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tour_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    price_per_person_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    price_per_person_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    private_price_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    private_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tour_seasonal_prices", x => x.id);
                    table.CheckConstraint("ck_tour_seasonal_prices_dates", "valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_tour_seasonal_prices_tours_tour_id",
                        column: x => x.tour_id,
                        principalTable: "tours",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tour_schedule_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tour_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tour_schedule_rules", x => x.id);
                    table.CheckConstraint("ck_tour_schedule_rules_dates", "valid_until IS NULL OR valid_until >= valid_from");
                    table.CheckConstraint("ck_tour_schedule_rules_days", "days > 0");
                    table.ForeignKey(
                        name: "fk_tour_schedule_rules_tours_tour_id",
                        column: x => x.tour_id,
                        principalTable: "tours",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tour_schedule_rules_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tour_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    reserved_seats = table.Column<int>(type: "integer", nullable: false),
                    booking_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_privately_booked = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slots", x => x.id);
                    table.CheckConstraint("ck_slots_capacity", "capacity >= 1");
                    table.CheckConstraint("ck_slots_reserved_seats", "reserved_seats BETWEEN 0 AND capacity");
                    table.CheckConstraint("ck_slots_time_range", "ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_slots_agencies_agency_id",
                        column: x => x.agency_id,
                        principalTable: "agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_slots_tour_schedule_rules_schedule_rule_id",
                        column: x => x.schedule_rule_id,
                        principalTable: "tour_schedule_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_slots_tours_tour_id",
                        column: x => x.tour_id,
                        principalTable: "tours",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_slots_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tour_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    guests = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cancellation_policy = table.Column<string>(type: "jsonb", nullable: false),
                    slot_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    slot_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    hold_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_amount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    paid_amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    refunded_amount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    refunded_amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    total_price_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_guests", "guests >= 1");
                    table.ForeignKey(
                        name: "fk_bookings_agencies_agency_id",
                        column: x => x.agency_id,
                        principalTable: "agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_slots_slot_id",
                        column: x => x.slot_id,
                        principalTable: "slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_tours_tour_id",
                        column: x => x.tour_id,
                        principalTable: "tours",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tour_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    hidden_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_reviews_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_tours_tour_id",
                        column: x => x.tour_id,
                        principalTable: "tours",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agencies_status",
                table: "agencies",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_agency_members_user_id",
                table: "agency_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_agency_id_slot_starts_at",
                table: "bookings",
                columns: new[] { "agency_id", "slot_starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_id_slot_starts_at",
                table: "bookings",
                columns: new[] { "customer_id", "slot_starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hold_expires_at",
                table: "bookings",
                column: "hold_expires_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_slot_id",
                table: "bookings",
                column: "slot_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tour_id",
                table: "bookings",
                column: "tour_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_booking_id",
                table: "reviews",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_tour_id_created_at",
                table: "reviews",
                columns: new[] { "tour_id", "created_at" },
                filter: "is_hidden = false");

            migrationBuilder.CreateIndex(
                name: "ix_slots_agency_id_starts_at",
                table: "slots",
                columns: new[] { "agency_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_slots_schedule_rule_id_starts_at",
                table: "slots",
                columns: new[] { "schedule_rule_id", "starts_at" },
                unique: true,
                filter: "schedule_rule_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_slots_starts_at",
                table: "slots",
                column: "starts_at",
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_slots_tour_id_starts_at",
                table: "slots",
                columns: new[] { "tour_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_slots_vehicle_id",
                table: "slots",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_tour_schedule_rules_tour_id",
                table: "tour_schedule_rules",
                column: "tour_id");

            migrationBuilder.CreateIndex(
                name: "ix_tour_schedule_rules_vehicle_id",
                table: "tour_schedule_rules",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_tour_seasonal_prices_tour_id",
                table: "tour_seasonal_prices",
                column: "tour_id");

            migrationBuilder.CreateIndex(
                name: "ix_tours_agency_id",
                table: "tours",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "ix_tours_status",
                table: "tours",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_agency_id",
                table: "vehicles",
                column: "agency_id");

            // Not expressible in the EF model: enforced by PostgreSQL itself.
            // One vehicle can't be on two departures at the same time (cancelled departures free the vehicle).
            migrationBuilder.Sql("""
                ALTER TABLE slots ADD CONSTRAINT ex_slots_vehicle_no_overlap
                EXCLUDE USING gist (vehicle_id WITH =, tstzrange(starts_at, ends_at) WITH &&)
                WHERE (status <> 'Cancelled');
                """);

            // A tour can't have two seasonal prices covering the same day.
            migrationBuilder.Sql("""
                ALTER TABLE tour_seasonal_prices ADD CONSTRAINT ex_tour_seasonal_prices_no_overlap
                EXCLUDE USING gist (tour_id WITH =, daterange(valid_from, valid_to, '[]') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agency_members");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "tour_seasonal_prices");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "slots");

            migrationBuilder.DropTable(
                name: "tour_schedule_rules");

            migrationBuilder.DropTable(
                name: "tours");

            migrationBuilder.DropTable(
                name: "vehicles");

            migrationBuilder.DropTable(
                name: "agencies");
        }
    }
}
