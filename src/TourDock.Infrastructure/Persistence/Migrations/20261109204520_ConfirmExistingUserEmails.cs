using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmExistingUserEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Email confirmation starts with this release. Accounts created before it never got a link, so they count as
            // confirmed instead of suddenly being unable to book. New sign-ups confirm through the emailed link.
            migrationBuilder.Sql("UPDATE users SET email_confirmed = TRUE WHERE NOT email_confirmed;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: which accounts were unconfirmed before isn't recorded.
        }
    }
}
