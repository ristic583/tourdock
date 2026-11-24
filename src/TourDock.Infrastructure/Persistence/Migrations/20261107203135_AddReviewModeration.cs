using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agency_reply",
                table: "reviews",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "agency_reply_at",
                table: "reviews",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "edited_at",
                table: "reviews",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "travelled_at",
                table: "reviews",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Existing reviews: the trip date comes from the booking they belong to.
            migrationBuilder.Sql("UPDATE reviews AS r SET travelled_at = b.slot_starts_at FROM bookings AS b WHERE b.id = r.booking_id;");

            migrationBuilder.CreateTable(
                name: "admin_audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_admin_audit_log_asp_net_users_admin_id",
                        column: x => x.admin_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "review_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_reports_reviews_review_id",
                        column: x => x.review_id,
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_review_reports_users_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_agency_id_created_at",
                table: "reviews",
                columns: new[] { "agency_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_log_admin_id",
                table: "admin_audit_log",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_log_at",
                table: "admin_audit_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_log_target_type_target_id",
                table: "admin_audit_log",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_review_reports_open",
                table: "review_reports",
                column: "review_id",
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_review_reports_reporter_id",
                table: "review_reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_reports_review_id_reporter_id",
                table: "review_reports",
                columns: new[] { "review_id", "reporter_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audit_log");

            migrationBuilder.DropTable(
                name: "review_reports");

            migrationBuilder.DropIndex(
                name: "ix_reviews_agency_id_created_at",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "agency_reply",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "agency_reply_at",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "edited_at",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "travelled_at",
                table: "reviews");
        }
    }
}
