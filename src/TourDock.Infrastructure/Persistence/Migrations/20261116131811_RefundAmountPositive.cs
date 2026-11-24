using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefundAmountPositive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_refunds_amount_positive",
                table: "refunds",
                sql: "amount_amount > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refunds_amount_positive",
                table: "refunds");
        }
    }
}
