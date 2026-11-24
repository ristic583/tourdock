using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCitiesAndRadiusSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:cube", ",,")
                .Annotation("Npgsql:PostgresExtension:earthdistance", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "cities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    search_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    population = table.Column<int>(type: "integer", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cities", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cities_search_name",
                table: "cities",
                column: "search_name")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            // Radius search helpers. Plain SQL functions, so the planner inlines them: earth_box(...) @> ll_to_earth(...)
            // then matches the GiST index below, and only the rows inside the box get the exact distance check.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION td_within_radius(latitude double precision, longitude double precision,
                    center_latitude double precision, center_longitude double precision, meters double precision)
                RETURNS boolean LANGUAGE sql IMMUTABLE PARALLEL SAFE
                AS $$
                    SELECT earth_box(ll_to_earth(center_latitude, center_longitude), meters) @> ll_to_earth(latitude, longitude)
                       AND earth_distance(ll_to_earth(center_latitude, center_longitude), ll_to_earth(latitude, longitude)) <= meters
                $$;

                CREATE FUNCTION td_distance_m(latitude double precision, longitude double precision,
                    center_latitude double precision, center_longitude double precision)
                RETURNS double precision LANGUAGE sql IMMUTABLE PARALLEL SAFE
                AS $$
                    SELECT earth_distance(ll_to_earth(center_latitude, center_longitude), ll_to_earth(latitude, longitude))
                $$;

                CREATE INDEX ix_tours_location_earth ON tours USING gist (ll_to_earth(latitude, longitude));
                """);

            // Seed places (Croatia, Montenegro, Slovenia, Bosnia and Herzegovina, Albania). Ids are stable UUIDv5s of
            // "country:name"; search_name is PlaceSearch.Normalize(name).
            migrationBuilder.Sql(
                """
                INSERT INTO cities (id, name, search_name, country_code, latitude, longitude, population, time_zone_id) VALUES
                ('58b2c3b2-4795-5c88-b8df-80146bf61c49', 'Zagreb', 'zagreb', 'HR', 45.8150, 15.9819, 790000, 'Europe/Zagreb'),
                ('65f9b5e0-6a5b-5884-891e-630196c908d4', 'Split', 'split', 'HR', 43.5081, 16.4402, 160000, 'Europe/Zagreb'),
                ('f53d0aad-1308-5bf4-9d71-af09bb10d50a', 'Rijeka', 'rijeka', 'HR', 45.3271, 14.4422, 108000, 'Europe/Zagreb'),
                ('f64bcae6-fc1a-5fa4-9680-8d164b39e462', 'Osijek', 'osijek', 'HR', 45.5550, 18.6955, 96000, 'Europe/Zagreb'),
                ('ceee5b07-657f-589a-a150-c0b9d42cac49', 'Zadar', 'zadar', 'HR', 44.1194, 15.2314, 70000, 'Europe/Zagreb'),
                ('59e8fd1f-1e76-5026-995c-4a7e7a58d142', 'Velika Gorica', 'velika gorica', 'HR', 45.7125, 16.0756, 61000, 'Europe/Zagreb'),
                ('d887cfcc-2a32-508e-befe-615b22dbe763', 'Pula', 'pula', 'HR', 44.8666, 13.8496, 52000, 'Europe/Zagreb'),
                ('efb65c8b-a4b6-5dd9-9222-f7f20726e9e7', 'Slavonski Brod', 'slavonski brod', 'HR', 45.1603, 18.0156, 50000, 'Europe/Zagreb'),
                ('bc62e7db-3821-5ab0-96ea-c66759b92944', 'Karlovac', 'karlovac', 'HR', 45.4929, 15.5553, 46000, 'Europe/Zagreb'),
                ('aa225a76-7ba7-5a78-9af9-70e6c533566a', 'Varaždin', 'varazdin', 'HR', 46.3044, 16.3366, 43000, 'Europe/Zagreb'),
                ('5e178a65-edbc-5356-955f-b5880189e62b', 'Šibenik', 'sibenik', 'HR', 43.7350, 15.8952, 42000, 'Europe/Zagreb'),
                ('ac151c55-cf54-5f3b-a0b1-e9eaf95d562e', 'Dubrovnik', 'dubrovnik', 'HR', 42.6507, 18.0944, 41000, 'Europe/Zagreb'),
                ('a7aacbfd-f9c0-536d-a862-e3c6c884420c', 'Sisak', 'sisak', 'HR', 45.4660, 16.3780, 40000, 'Europe/Zagreb'),
                ('c5cd9232-1c91-5873-b3fb-1cf6f824bb3c', 'Kaštela', 'kastela', 'HR', 43.5469, 16.3800, 39000, 'Europe/Zagreb'),
                ('c91b0397-97ec-58c1-a899-d5c57021e741', 'Bjelovar', 'bjelovar', 'HR', 45.8986, 16.8423, 37000, 'Europe/Zagreb'),
                ('ea3cffa9-ad02-5dac-ac71-981993ed14e8', 'Samobor', 'samobor', 'HR', 45.8011, 15.7110, 37000, 'Europe/Zagreb'),
                ('1e4d37d8-4b22-57dd-a74a-5a0b4fb4d138', 'Vinkovci', 'vinkovci', 'HR', 45.2883, 18.8047, 30000, 'Europe/Zagreb'),
                ('1e55e6bf-81bf-538c-ad77-5ef8c1b0056f', 'Koprivnica', 'koprivnica', 'HR', 46.1628, 16.8275, 28000, 'Europe/Zagreb'),
                ('e6a48328-faaa-5010-a1e3-bcbabfc37c41', 'Čakovec', 'cakovec', 'HR', 46.3844, 16.4339, 27000, 'Europe/Zagreb'),
                ('e4e3b2f2-4bc6-54d4-a906-3ead0d615756', 'Đakovo', 'dakovo', 'HR', 45.3100, 18.4102, 25000, 'Europe/Zagreb'),
                ('8cce5867-33fe-5000-bbe7-cd1ea331bef3', 'Solin', 'solin', 'HR', 43.5428, 16.4925, 25000, 'Europe/Zagreb'),
                ('6d975a57-865f-5010-bc36-3e294aa0bd09', 'Sinj', 'sinj', 'HR', 43.7036, 16.6394, 24000, 'Europe/Zagreb'),
                ('3021d407-a9fe-51d4-b576-c6e12b0bf4c9', 'Vukovar', 'vukovar', 'HR', 45.3511, 19.0003, 23000, 'Europe/Zagreb'),
                ('56b046d0-623e-5c37-afae-9eb6d3ea2f6d', 'Požega', 'pozega', 'HR', 45.3400, 17.6853, 22000, 'Europe/Zagreb'),
                ('12bfb168-0826-53d3-bdf2-d0ea0d6e6170', 'Poreč', 'porec', 'HR', 45.2269, 13.5953, 16000, 'Europe/Zagreb'),
                ('08773a09-8a8c-5b1c-8ae3-9561bafd6216', 'Omiš', 'omis', 'HR', 43.4447, 16.6889, 15000, 'Europe/Zagreb'),
                ('b0712c19-efa2-581b-baab-516cc26214be', 'Metković', 'metkovic', 'HR', 43.0542, 17.6483, 15000, 'Europe/Zagreb'),
                ('e7eda7bf-da11-5a7b-b6ec-ea7e3b8ef0fe', 'Rovinj', 'rovinj', 'HR', 45.0812, 13.6387, 14000, 'Europe/Zagreb'),
                ('bf2ef851-f5ea-5958-9247-52f63fb1d55c', 'Trogir', 'trogir', 'HR', 43.5167, 16.2500, 13000, 'Europe/Zagreb'),
                ('7cde5afe-4b21-5eec-82ff-b13bc909276b', 'Umag', 'umag', 'HR', 45.4319, 13.5197, 13000, 'Europe/Zagreb'),
                ('00f39028-7694-5c19-9987-a4cdbf353402', 'Makarska', 'makarska', 'HR', 43.2969, 17.0178, 13000, 'Europe/Zagreb'),
                ('1bc74295-95ae-5f40-b08a-950998f69a77', 'Knin', 'knin', 'HR', 44.0406, 16.1969, 13000, 'Europe/Zagreb'),
                ('fe1163ed-3c10-5c82-9e32-86ae3aa0ab67', 'Gospić', 'gospic', 'HR', 44.5461, 15.3747, 12000, 'Europe/Zagreb'),
                ('58bb01a9-9f7f-5fb5-af45-267198f12eb8', 'Crikvenica', 'crikvenica', 'HR', 45.1772, 14.6928, 11000, 'Europe/Zagreb'),
                ('ef0fc8bc-92f3-55d1-b541-0934509d7a6d', 'Labin', 'labin', 'HR', 45.0950, 14.1197, 11000, 'Europe/Zagreb'),
                ('349c6f32-2f5c-5b2b-aa21-3864d3c0ff7c', 'Opatija', 'opatija', 'HR', 45.3377, 14.3053, 10000, 'Europe/Zagreb'),
                ('1052a0ff-5383-52d7-9c8b-218afea1ee4d', 'Imotski', 'imotski', 'HR', 43.4467, 17.2167, 10000, 'Europe/Zagreb'),
                ('1652d8b1-5c91-5452-b551-6e733e963b7d', 'Ploče', 'ploce', 'HR', 43.0547, 17.4331, 10000, 'Europe/Zagreb'),
                ('d72941b1-5f58-5355-b2ad-075b62496914', 'Benkovac', 'benkovac', 'HR', 44.0333, 15.6125, 10000, 'Europe/Zagreb'),
                ('747d1971-b6d0-5ae0-9b83-42f1bd3ef724', 'Otočac', 'otocac', 'HR', 44.8694, 15.2375, 9800, 'Europe/Zagreb'),
                ('0f413012-7209-566a-88df-320c196fce58', 'Vodice', 'vodice', 'HR', 43.7614, 15.7783, 8800, 'Europe/Zagreb'),
                ('6e7ff5c2-6f70-5b36-9f7a-8631f602f045', 'Pazin', 'pazin', 'HR', 45.2403, 13.9367, 8600, 'Europe/Zagreb'),
                ('0bd82cd9-b45d-53fd-878a-4e2dee78460e', 'Rab', 'rab', 'HR', 44.7569, 14.7600, 8000, 'Europe/Zagreb'),
                ('398d551b-7495-56fc-b0a2-d029a385be41', 'Senj', 'senj', 'HR', 44.9893, 14.9058, 7100, 'Europe/Zagreb'),
                ('1281c5af-00ce-54b7-98ad-62942b86a8d5', 'Drniš', 'drnis', 'HR', 43.8625, 16.1556, 7000, 'Europe/Zagreb'),
                ('1c59b79c-720d-5d3f-9de3-92fa10b42434', 'Krk', 'krk', 'HR', 45.0258, 14.5758, 6800, 'Europe/Zagreb'),
                ('33460eb3-5986-5dbf-958c-07e14a815e33', 'Medulin', 'medulin', 'HR', 44.8222, 13.9333, 6500, 'Europe/Zagreb'),
                ('cb551aeb-5198-5306-adb3-087ebcf63df7', 'Mali Lošinj', 'mali losinj', 'HR', 44.5306, 14.4686, 6300, 'Europe/Zagreb'),
                ('88a8c40f-d358-5f36-8a7c-dc9f2ef2d4e1', 'Biograd na Moru', 'biograd na moru', 'HR', 43.9433, 15.4519, 5600, 'Europe/Zagreb'),
                ('73513c1f-d1be-53e7-ae2e-1575078579ec', 'Korčula', 'korcula', 'HR', 42.9597, 17.1356, 5600, 'Europe/Zagreb'),
                ('1aab4c64-3a2d-5e1a-81a3-66daa5b891b2', 'Novi Vinodolski', 'novi vinodolski', 'HR', 45.1281, 14.7883, 5100, 'Europe/Zagreb'),
                ('aff007eb-63fd-534d-839f-ae790f07c171', 'Novigrad', 'novigrad', 'HR', 45.3167, 13.5667, 4300, 'Europe/Zagreb'),
                ('aeea3abc-521e-579d-831f-d512805f0c50', 'Hvar', 'hvar', 'HR', 43.1725, 16.4428, 4200, 'Europe/Zagreb'),
                ('12fff6f4-9fa5-57aa-98f9-bd823bf53982', 'Pakoštane', 'pakostane', 'HR', 43.9086, 15.5083, 4100, 'Europe/Zagreb'),
                ('9aca4716-77d0-50d9-a5d5-61a062ee7a13', 'Supetar', 'supetar', 'HR', 43.3833, 16.5500, 4000, 'Europe/Zagreb'),
                ('10a9dc78-2568-5e47-b187-5bb3e1c3e785', 'Orebić', 'orebic', 'HR', 42.9758, 17.1792, 4000, 'Europe/Zagreb'),
                ('a78b5767-4789-503c-9779-fb93a614dd77', 'Lovran', 'lovran', 'HR', 45.2917, 14.2742, 4000, 'Europe/Zagreb'),
                ('df3476ac-d0ff-579e-9985-dac7e31fbcd4', 'Plitvička Jezera', 'plitvicka jezera', 'HR', 44.8803, 15.6161, 4000, 'Europe/Zagreb'),
                ('01352b50-9b44-59c4-ba00-e51710c3cb6a', 'Skradin', 'skradin', 'HR', 43.8172, 15.9228, 3800, 'Europe/Zagreb'),
                ('a0f918cc-8aa8-594a-b779-b63e62f0be4f', 'Pag', 'pag', 'HR', 44.4436, 15.0583, 3800, 'Europe/Zagreb'),
                ('f1806a3c-301c-599b-97fc-a801ee7bbaa9', 'Jelsa', 'jelsa', 'HR', 43.1611, 16.6939, 3600, 'Europe/Zagreb'),
                ('0e1d907d-6165-5742-92d2-fe3e112d9423', 'Novalja', 'novalja', 'HR', 44.5578, 14.8853, 3600, 'Europe/Zagreb'),
                ('3379f9c1-fc89-578f-8d05-ced0f0b11431', 'Fažana', 'fazana', 'HR', 44.9272, 13.8031, 3600, 'Europe/Zagreb'),
                ('b713240b-fa9e-50a0-a246-b33d91b1adf5', 'Tisno', 'tisno', 'HR', 43.8000, 15.6500, 3000, 'Europe/Zagreb'),
                ('cc31055e-8b5d-53f6-83d9-54a5de902d92', 'Malinska', 'malinska', 'HR', 45.1239, 14.5283, 3000, 'Europe/Zagreb'),
                ('89f5e30f-65e5-5d48-b35f-11a2647c1f1b', 'Cres', 'cres', 'HR', 44.9603, 14.4086, 2900, 'Europe/Zagreb'),
                ('9da0acfa-0d64-5882-bb40-0e874d2a6366', 'Stari Grad', 'stari grad', 'HR', 43.1844, 16.5986, 2800, 'Europe/Zagreb'),
                ('67a55052-9364-5360-8892-e82e3b57a54b', 'Primošten', 'primosten', 'HR', 43.5864, 15.9239, 2800, 'Europe/Zagreb'),
                ('60c6c729-916f-5471-9b60-5230ebbfceb8', 'Baška Voda', 'baska voda', 'HR', 43.3583, 16.9500, 2800, 'Europe/Zagreb'),
                ('42b8292d-2423-598f-be4a-6eb7347ed641', 'Nin', 'nin', 'HR', 44.2417, 15.1789, 2700, 'Europe/Zagreb'),
                ('5177ef00-c5ed-5942-aa8f-2c208710b549', 'Ston', 'ston', 'HR', 42.8389, 17.6983, 2600, 'Europe/Zagreb'),
                ('9afa8e6e-2914-530a-a95c-ebc9ff45f788', 'Podgora', 'podgora', 'HR', 43.2433, 17.0742, 2500, 'Europe/Zagreb'),
                ('6e7a0bba-f6e2-5abd-a03d-3910dbd457df', 'Rogoznica', 'rogoznica', 'HR', 43.5306, 15.9686, 2300, 'Europe/Zagreb'),
                ('492d8d96-6d71-5722-b50c-f5a7801dfa63', 'Cavtat', 'cavtat', 'HR', 42.5806, 18.2186, 2200, 'Europe/Zagreb'),
                ('9a6c0cde-aec4-51a4-8258-9f891dea6970', 'Vrsar', 'vrsar', 'HR', 45.1500, 13.6056, 2100, 'Europe/Zagreb'),
                ('099b87ae-4fcb-508a-bb18-363674478c90', 'Murter', 'murter', 'HR', 43.8222, 15.5917, 2000, 'Europe/Zagreb'),
                ('76290318-2947-5db3-b4ff-38d859828fdd', 'Vis', 'vis', 'HR', 43.0619, 16.1831, 1900, 'Europe/Zagreb'),
                ('b0392000-402e-53bd-a671-67f1619a725a', 'Tučepi', 'tucepi', 'HR', 43.2675, 17.0556, 1900, 'Europe/Zagreb'),
                ('7b059a05-fdb7-5d4d-90ce-be7833497988', 'Starigrad', 'starigrad', 'HR', 44.2972, 15.4417, 1900, 'Europe/Zagreb'),
                ('597ee388-7296-5260-94ed-1412dd1f3617', 'Bol', 'bol', 'HR', 43.2617, 16.6550, 1700, 'Europe/Zagreb'),
                ('3ce842e2-ae5c-5040-9bcb-fbd4b2749c2e', 'Baška', 'baska', 'HR', 44.9706, 14.7536, 1700, 'Europe/Zagreb'),
                ('8d9c5837-5dae-5587-ac17-21b857d3390a', 'Brela', 'brela', 'HR', 43.3711, 16.9333, 1700, 'Europe/Zagreb'),
                ('135845ac-f04c-5e0f-9051-1491cde9bc96', 'Komiža', 'komiza', 'HR', 43.0436, 16.0917, 1500, 'Europe/Zagreb'),
                ('72b05da0-6d9c-5b64-8f0e-7bc8b2dde541', 'Motovun', 'motovun', 'HR', 45.3364, 13.8278, 1000, 'Europe/Zagreb'),
                ('edc0a15e-144c-5677-b1e3-7c93b1648f39', 'Lastovo', 'lastovo', 'HR', 42.7650, 16.8889, 800, 'Europe/Zagreb'),
                ('e7836503-ee94-5703-8920-de9d3247e11f', 'Slano', 'slano', 'HR', 42.7856, 17.8919, 600, 'Europe/Zagreb'),
                ('9d13d38f-4b36-5488-b223-b7953b31f5e0', 'Podgorica', 'podgorica', 'ME', 42.4411, 19.2636, 190000, 'Europe/Podgorica'),
                ('8645e086-4ea5-54dc-9c07-d95f42a01b70', 'Nikšić', 'niksic', 'ME', 42.7731, 18.9445, 56000, 'Europe/Podgorica'),
                ('7dc14fb1-c319-5c56-b315-3cdd1da6d767', 'Herceg Novi', 'herceg novi', 'ME', 42.4531, 18.5375, 30000, 'Europe/Podgorica'),
                ('53c75d4d-1200-5182-8e3c-f9a3376306b3', 'Budva', 'budva', 'ME', 42.2911, 18.8403, 19000, 'Europe/Podgorica'),
                ('945994e7-45e2-56f8-a513-775d0640e90c', 'Bar', 'bar', 'ME', 42.0931, 19.1003, 17000, 'Europe/Podgorica'),
                ('f65b7e69-3388-5698-8591-fe00a8487981', 'Tivat', 'tivat', 'ME', 42.4364, 18.6961, 14000, 'Europe/Podgorica'),
                ('3ecf995b-8e6c-5a84-8a7f-2189434f9c16', 'Kotor', 'kotor', 'ME', 42.4247, 18.7712, 13000, 'Europe/Podgorica'),
                ('3218d8c1-8c86-5004-a37d-dc69891ed948', 'Cetinje', 'cetinje', 'ME', 42.3906, 18.9142, 13000, 'Europe/Podgorica'),
                ('9483c122-82f1-5f04-9e23-d25244b45b02', 'Ulcinj', 'ulcinj', 'ME', 41.9294, 19.2244, 11000, 'Europe/Podgorica'),
                ('2a3a0f61-8556-5c8a-854a-0df67e7f94b0', 'Kolašin', 'kolasin', 'ME', 42.8222, 19.5167, 2700, 'Europe/Podgorica'),
                ('6b67afb4-3959-5b97-8b32-b8d988400264', 'Risan', 'risan', 'ME', 42.5156, 18.6956, 2000, 'Europe/Podgorica'),
                ('379a6d85-77bc-59ff-9a13-51153abaee0d', 'Žabljak', 'zabljak', 'ME', 43.1542, 19.1236, 1700, 'Europe/Podgorica'),
                ('fc908dab-3c7d-529e-855c-ab1181c7b5d6', 'Petrovac na Moru', 'petrovac na moru', 'ME', 42.2058, 18.9425, 1500, 'Europe/Podgorica'),
                ('3f1c7006-6f9d-53e5-ac42-78e36f9625c6', 'Sveti Stefan', 'sveti stefan', 'ME', 42.2556, 18.8917, 400, 'Europe/Podgorica'),
                ('59b7294d-af34-54fa-9bb5-dbd531eae9ee', 'Perast', 'perast', 'ME', 42.4864, 18.6997, 300, 'Europe/Podgorica'),
                ('888cd31c-9493-551c-a285-f90ec0a597d3', 'Ljubljana', 'ljubljana', 'SI', 46.0569, 14.5058, 285000, 'Europe/Ljubljana'),
                ('b2ac3a18-b51d-5dea-83d1-d4067347168c', 'Maribor', 'maribor', 'SI', 46.5547, 15.6459, 96000, 'Europe/Ljubljana'),
                ('17ac878f-6966-5c24-9874-2b2d79f93a6d', 'Celje', 'celje', 'SI', 46.2309, 15.2604, 38000, 'Europe/Ljubljana'),
                ('3fd5f160-81ad-50cd-a8f5-2f6ae11af4c6', 'Kranj', 'kranj', 'SI', 46.2389, 14.3556, 37000, 'Europe/Ljubljana'),
                ('8631830d-72c9-506b-ba91-2dd71a51d807', 'Koper', 'koper', 'SI', 45.5481, 13.7302, 26000, 'Europe/Ljubljana'),
                ('afab3dd5-68cc-5d1c-a311-56f76712790c', 'Nova Gorica', 'nova gorica', 'SI', 45.9558, 13.6483, 13000, 'Europe/Ljubljana'),
                ('726a2399-b745-5715-bd63-a75b371a20c8', 'Izola', 'izola', 'SI', 45.5383, 13.6619, 11000, 'Europe/Ljubljana'),
                ('a616d85c-9780-5870-9cd7-59be8ef89b88', 'Postojna', 'postojna', 'SI', 45.7744, 14.2153, 9700, 'Europe/Ljubljana'),
                ('d8d4fe48-22f3-5d3e-b0c3-8cc841e180dc', 'Bled', 'bled', 'SI', 46.3683, 14.1146, 5000, 'Europe/Ljubljana'),
                ('3c97b676-9791-5b3d-9bde-d473f19b9b0a', 'Piran', 'piran', 'SI', 45.5283, 13.5683, 4000, 'Europe/Ljubljana'),
                ('c0e740e6-f06e-5a49-a156-3f6f0b9fea02', 'Portorož', 'portoroz', 'SI', 45.5142, 13.5911, 2900, 'Europe/Ljubljana'),
                ('29ba2dab-0c88-5443-9765-688ea16c5e9b', 'Bohinjska Bistrica', 'bohinjska bistrica', 'SI', 46.2731, 13.9553, 1800, 'Europe/Ljubljana'),
                ('845a6693-bcce-5706-9d85-ccf578c84635', 'Bovec', 'bovec', 'SI', 46.3378, 13.5522, 1600, 'Europe/Ljubljana'),
                ('74cedee2-b71d-57ac-8480-d5e8cfbc2d6e', 'Sarajevo', 'sarajevo', 'BA', 43.8563, 18.4131, 275000, 'Europe/Sarajevo'),
                ('3073624c-b921-5c08-adf6-aff8a286f504', 'Banja Luka', 'banja luka', 'BA', 44.7722, 17.1910, 185000, 'Europe/Sarajevo'),
                ('20c3dd7c-2a09-5d53-8cc4-e37933bda2a0', 'Tuzla', 'tuzla', 'BA', 44.5384, 18.6671, 110000, 'Europe/Sarajevo'),
                ('1af470a4-0574-5d48-ba01-2d63f7b87eee', 'Zenica', 'zenica', 'BA', 44.2017, 17.9078, 70000, 'Europe/Sarajevo'),
                ('d53498f3-9b19-5e99-afe2-ad5d5550ce45', 'Mostar', 'mostar', 'BA', 43.3438, 17.8078, 60000, 'Europe/Sarajevo'),
                ('b91444d3-d5e0-5960-8276-8c29feb2d289', 'Bihać', 'bihac', 'BA', 44.8169, 15.8708, 39000, 'Europe/Sarajevo'),
                ('d102b9c6-ef01-5877-b768-aa97f57faa3b', 'Trebinje', 'trebinje', 'BA', 42.7119, 18.3439, 25000, 'Europe/Sarajevo'),
                ('fb414352-f738-5887-a103-c26ec5d82fee', 'Konjic', 'konjic', 'BA', 43.6533, 17.9611, 12000, 'Europe/Sarajevo'),
                ('dea69017-d714-57af-b207-087103364eb0', 'Jajce', 'jajce', 'BA', 44.3428, 17.2706, 7000, 'Europe/Sarajevo'),
                ('9530d410-9a14-5c6c-bf47-9cc99d36c29e', 'Neum', 'neum', 'BA', 42.9233, 17.6156, 4600, 'Europe/Sarajevo'),
                ('df505ed6-206b-53a5-9267-23ba95cfab16', 'Međugorje', 'medugorje', 'BA', 43.1906, 17.6778, 2300, 'Europe/Sarajevo'),
                ('1878c370-1189-5b13-8d9b-ef5851042bbe', 'Tirana', 'tirana', 'AL', 41.3275, 19.8187, 420000, 'Europe/Tirane'),
                ('f7b99a89-68ec-5713-937e-bd5097997513', 'Durrës', 'durres', 'AL', 41.3231, 19.4414, 115000, 'Europe/Tirane'),
                ('5b94e8c2-56eb-57bb-8ed1-54609d55e3f3', 'Vlorë', 'vlore', 'AL', 40.4667, 19.4833, 80000, 'Europe/Tirane'),
                ('0e5b2e3c-9d61-5228-8514-a34f2196d418', 'Shkodër', 'shkoder', 'AL', 42.0683, 19.5126, 77000, 'Europe/Tirane'),
                ('8f25269d-07cf-594a-bf56-cf31856105e3', 'Berat', 'berat', 'AL', 40.7058, 19.9522, 32000, 'Europe/Tirane'),
                ('129d7195-564f-5fc7-9782-a793aca4459c', 'Gjirokastër', 'gjirokaster', 'AL', 40.0758, 20.1389, 20000, 'Europe/Tirane'),
                ('e3c499fd-4719-5948-996e-f303ac8db36a', 'Sarandë', 'sarande', 'AL', 39.8756, 20.0053, 18000, 'Europe/Tirane'),
                ('b19b20d9-b9b5-5af8-97f0-e793f5ad5aac', 'Himarë', 'himare', 'AL', 40.1011, 19.7447, 3000, 'Europe/Tirane'),
                ('f57c6aaa-eac2-5570-a73f-9cea67d09ab4', 'Ksamil', 'ksamil', 'AL', 39.7686, 20.0003, 3000, 'Europe/Tirane');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS ix_tours_location_earth;
                DROP FUNCTION IF EXISTS td_distance_m(double precision, double precision, double precision, double precision);
                DROP FUNCTION IF EXISTS td_within_radius(double precision, double precision, double precision, double precision, double precision);
                """);

            migrationBuilder.DropTable(
                name: "cities");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:cube", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:earthdistance", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
