using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignDemoTourMeetingPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only untouched demo locations qualify. Agency-edited meeting points and real agencies are preserved.
            migrationBuilder.Sql(
                """
                UPDATE tours AS t
                SET address = p.new_address, latitude = p.new_latitude, longitude = p.new_longitude
                FROM agencies AS a,
                (VALUES
                    ('captain.marin@example.com', 'Sunset Cruise along the Split Riviera', 'Split', 'Riva promenade, pier 3', 43.507600, 16.438100, 'Riva waterfront, Obala Hrvatskog narodnog preporoda', 43.507600, 16.438100),
                    ('captain.marin@example.com', 'Blue Cave & Five Islands', 'Split', 'Riva promenade, pier 5', 43.507000, 16.440000, 'Riva waterfront, Obala Hrvatskog narodnog preporoda', 43.507600, 16.438100),
                    ('captain.marin@example.com', 'Big Game Fishing Charter', 'Split', 'ACI marina', 43.502100, 16.429500, 'ACI Marina Split, Uvala Baluni', 43.501111, 16.429917),
                    ('skipper.luka@example.com', 'Morning Fishing Trip', 'Hvar', 'Hvar town harbour', 43.172900, 16.441100, 'Riva 21, Hvar town harbour', 43.171182, 16.441340),
                    ('skipper.luka@example.com', 'Pakleni Islands Snorkeling', 'Hvar', 'Hvar town harbour', 43.172000, 16.440000, 'Riva 21, Hvar town harbour', 43.171182, 16.441340),
                    ('guide.ana@example.com', 'Konavle Valley Jeep Safari', 'Dubrovnik', 'Pile Gate bus stop', 42.642000, 18.106000, 'Pile Gate, Brsalje', 42.641730, 18.106850),
                    ('guide.ana@example.com', 'Bay of Kotor Day Trip', 'Kotor', 'Old town main gate', 42.424700, 18.771200, 'Sea Gate (main entrance to the old town)', 42.424737, 18.769705)
                ) AS p(email, title, city, old_address, old_latitude, old_longitude, new_address, new_latitude, new_longitude)
                WHERE t.agency_id = a.id
                  AND a.contact_email = p.email
                  AND t.title = p.title AND t.city = p.city
                  AND t.address = p.old_address
                  AND t.latitude = p.old_latitude AND t.longitude = p.old_longitude;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data correction only. Preserve the corrected locations when rolling back application code.
        }
    }
}
