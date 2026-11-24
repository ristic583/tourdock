using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSerbianAndMacedonianPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "search_alias",
                table: "cities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_cities_search_alias",
                table: "cities",
                column: "search_alias",
                filter: "search_alias IS NOT NULL")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            // Places in Serbia and North Macedonia. Same rules as the first seed: ids are UUIDv5s of "country:name",
            // search_name is PlaceSearch.Normalize(name); Beograd is also found as "Belgrade".
            migrationBuilder.Sql(
                """
                INSERT INTO cities (id, name, search_name, search_alias, country_code, latitude, longitude, population, time_zone_id) VALUES
                ('43b2ad65-1b27-57bc-9012-91b399333ce5', 'Beograd', 'beograd', 'belgrade', 'RS', 44.8176, 20.4569, 1200000, 'Europe/Belgrade'),
                ('f468fb0b-cb13-5463-beb2-c18ea590bca9', 'Novi Sad', 'novi sad', NULL, 'RS', 45.2671, 19.8335, 250000, 'Europe/Belgrade'),
                ('79abd830-e3e5-51ff-8fd3-85d6c3c274cf', 'Niš', 'nis', NULL, 'RS', 43.3209, 21.8958, 190000, 'Europe/Belgrade'),
                ('8671e95a-81cf-52ce-8729-51cfedc2277b', 'Kragujevac', 'kragujevac', NULL, 'RS', 44.0128, 20.9114, 150000, 'Europe/Belgrade'),
                ('768430a7-0e35-5e51-89ae-ad6ef876c31e', 'Subotica', 'subotica', NULL, 'RS', 46.1005, 19.6651, 95000, 'Europe/Belgrade'),
                ('93fe7e49-0cbb-519b-a0f1-7e25157df87a', 'Pančevo', 'pancevo', NULL, 'RS', 44.8708, 20.6403, 75000, 'Europe/Belgrade'),
                ('d3fe2d2b-7127-5612-892b-19229947d889', 'Zrenjanin', 'zrenjanin', NULL, 'RS', 45.3836, 20.3819, 70000, 'Europe/Belgrade'),
                ('da4100d3-8282-51f2-8253-e3cdd366f137', 'Novi Pazar', 'novi pazar', NULL, 'RS', 43.1367, 20.5122, 70000, 'Europe/Belgrade'),
                ('c5bc0877-9b8a-54ae-9756-6263881a433f', 'Čačak', 'cacak', NULL, 'RS', 43.8914, 20.3497, 70000, 'Europe/Belgrade'),
                ('97d217eb-f6ee-52a4-b680-047ff2b15b59', 'Kraljevo', 'kraljevo', NULL, 'RS', 43.7258, 20.6895, 60000, 'Europe/Belgrade'),
                ('1b291172-1e95-52b9-a25f-fab47d35c301', 'Smederevo', 'smederevo', NULL, 'RS', 44.6628, 20.9300, 60000, 'Europe/Belgrade'),
                ('415c93c1-b43c-5e01-9e1f-aed93c173897', 'Leskovac', 'leskovac', NULL, 'RS', 42.9981, 21.9461, 60000, 'Europe/Belgrade'),
                ('8def277a-fa14-533b-98c7-e86d95cd0104', 'Valjevo', 'valjevo', NULL, 'RS', 44.2751, 19.8982, 58000, 'Europe/Belgrade'),
                ('5169d349-fad0-5b48-8901-8df5ca4a2534', 'Kruševac', 'krusevac', NULL, 'RS', 43.5800, 21.3339, 58000, 'Europe/Belgrade'),
                ('f1822ab6-f64a-5cce-b4b8-a4fbfb79db38', 'Vranje', 'vranje', NULL, 'RS', 42.5514, 21.9003, 55000, 'Europe/Belgrade'),
                ('4014a6b5-7d92-510c-a8c7-bf4a41e75868', 'Šabac', 'sabac', NULL, 'RS', 44.7489, 19.6908, 52000, 'Europe/Belgrade'),
                ('f1c679b5-98cc-547b-b4db-835d68474581', 'Užice', 'uzice', NULL, 'RS', 43.8586, 19.8488, 50000, 'Europe/Belgrade'),
                ('ae4f9480-4f4f-557a-886c-b77ecbdc60c2', 'Sombor', 'sombor', NULL, 'RS', 45.7742, 19.1122, 45000, 'Europe/Belgrade'),
                ('cd9b9e35-d397-5c59-8924-59c9b547d068', 'Požarevac', 'pozarevac', NULL, 'RS', 44.6213, 21.1878, 44000, 'Europe/Belgrade'),
                ('4232f540-4931-52d3-a6e5-783f0d090e44', 'Pirot', 'pirot', NULL, 'RS', 43.1531, 22.5861, 38000, 'Europe/Belgrade'),
                ('b57138a2-9ca5-527d-b0ea-2fe7fc05cc05', 'Sremska Mitrovica', 'sremska mitrovica', NULL, 'RS', 44.9764, 19.6122, 38000, 'Europe/Belgrade'),
                ('7cf94b0e-200e-5e7e-8a0b-46d917543317', 'Jagodina', 'jagodina', NULL, 'RS', 43.9772, 21.2611, 37000, 'Europe/Belgrade'),
                ('119c2992-22f7-5a9d-8764-5554f6413e1e', 'Kikinda', 'kikinda', NULL, 'RS', 45.8297, 20.4653, 36000, 'Europe/Belgrade'),
                ('86a5bf5e-a631-565d-b9b8-3073ef625007', 'Zaječar', 'zajecar', NULL, 'RS', 43.9042, 22.2842, 35000, 'Europe/Belgrade'),
                ('d3021f59-98c7-5035-9259-31f5821107a9', 'Vršac', 'vrsac', NULL, 'RS', 45.1167, 21.3036, 35000, 'Europe/Belgrade'),
                ('843c768c-c358-5957-b1aa-489037f5cf0a', 'Vrnjačka Banja', 'vrnjacka banja', NULL, 'RS', 43.6236, 20.8967, 10000, 'Europe/Belgrade'),
                ('7955f814-11b1-5e3f-bfe0-c4775837b99c', 'Bajina Bašta', 'bajina basta', NULL, 'RS', 43.9708, 19.5675, 9000, 'Europe/Belgrade'),
                ('57002aba-7b37-5fea-b41e-ce45a04b5a42', 'Sremski Karlovci', 'sremski karlovci', NULL, 'RS', 45.2028, 19.9342, 8000, 'Europe/Belgrade'),
                ('16b7da8b-8dda-5681-afb5-c41f7e72ed4f', 'Kladovo', 'kladovo', NULL, 'RS', 44.6067, 22.6131, 8000, 'Europe/Belgrade'),
                ('20829031-f509-5c51-939e-bfb758c8b776', 'Palić', 'palic', NULL, 'RS', 46.0997, 19.7625, 7000, 'Europe/Belgrade'),
                ('b7d2e15d-52a1-5ef7-9fc2-52f0580bea8a', 'Sokobanja', 'sokobanja', NULL, 'RS', 43.6431, 21.8706, 7000, 'Europe/Belgrade'),
                ('6dd2a4b8-73d2-5d00-9dc1-147efc8938cb', 'Veliko Gradište', 'veliko gradiste', NULL, 'RS', 44.7639, 21.5203, 5000, 'Europe/Belgrade'),
                ('0bd6f003-5aa3-551e-a044-0a0cf2504971', 'Zlatibor', 'zlatibor', NULL, 'RS', 43.7290, 19.7006, 3000, 'Europe/Belgrade'),
                ('59eba7da-5c7e-54d2-9f24-2d8bbdfffc38', 'Donji Milanovac', 'donji milanovac', NULL, 'RS', 44.4650, 22.1517, 2400, 'Europe/Belgrade'),
                ('2af05496-b6a3-5f40-9d56-30726aa69dad', 'Golubac', 'golubac', NULL, 'RS', 44.6533, 21.6317, 1600, 'Europe/Belgrade'),
                ('e964dea5-6169-5955-b591-dd64c1415c5e', 'Mokra Gora', 'mokra gora', NULL, 'RS', 43.7906, 19.5061, 500, 'Europe/Belgrade'),
                ('4dff2227-1f5d-574d-af73-50d10e1efb98', 'Kopaonik', 'kopaonik', NULL, 'RS', 43.2856, 20.8125, 500, 'Europe/Belgrade'),
                ('5ea78dd0-ddab-5bee-be39-0d5f98e424d5', 'Skopje', 'skopje', NULL, 'MK', 41.9981, 21.4254, 530000, 'Europe/Skopje'),
                ('245f750c-c3d3-5f4d-a41c-0497435d28d4', 'Bitola', 'bitola', NULL, 'MK', 41.0314, 21.3347, 70000, 'Europe/Skopje'),
                ('13c52a0e-e867-53bc-96ff-82da239db366', 'Kumanovo', 'kumanovo', NULL, 'MK', 42.1322, 21.7144, 70000, 'Europe/Skopje'),
                ('082c3f09-063f-57a5-acb0-abdac85e9248', 'Prilep', 'prilep', NULL, 'MK', 41.3464, 21.5542, 66000, 'Europe/Skopje'),
                ('822d353b-257a-530c-9fa4-03915a303c9c', 'Tetovo', 'tetovo', NULL, 'MK', 42.0106, 20.9714, 50000, 'Europe/Skopje'),
                ('cd11fafb-0c01-559f-8e93-b5bb80f2e930', 'Ohrid', 'ohrid', NULL, 'MK', 41.1172, 20.8016, 40000, 'Europe/Skopje'),
                ('a2db3c83-bda8-5268-90e7-e2c648740c63', 'Struga', 'struga', NULL, 'MK', 41.1781, 20.6783, 17000, 'Europe/Skopje'),
                ('7821bf5e-2f48-521a-8a51-0a95ac7bc00f', 'Kruševo', 'krusevo', NULL, 'MK', 41.3700, 21.2483, 5000, 'Europe/Skopje'),
                ('7c28173b-9251-54bf-81e3-b497f4154334', 'Dojran', 'dojran', NULL, 'MK', 41.1856, 22.7203, 1000, 'Europe/Skopje');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM cities WHERE country_code IN ('RS', 'MK');");

            migrationBuilder.DropIndex(
                name: "ix_cities_search_alias",
                table: "cities");

            migrationBuilder.DropColumn(
                name: "search_alias",
                table: "cities");
        }
    }
}
