using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourDock.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only: places in Greece, Italy, Portugal, Spain and Norway, plus two Serbian mountain towns, so the wider demo
    /// catalog is reachable through place search and "near a place". Ids are UUIDv5s (URL namespace) of "country:name";
    /// search_name is PlaceSearch.Normalize(name) and search_alias the common English or island name.
    /// </summary>
    [DbContext(typeof(TourDockDbContext))]
    [Migration("20261114120000_AddEuropeanPlaces")]
    public partial class AddEuropeanPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO cities (id, name, search_name, search_alias, country_code, latitude, longitude, population, time_zone_id) VALUES
                ('c976cd18-d2f9-5a59-b4cd-8ecf65878d6f', 'Athens', 'athens', 'athina', 'GR', 37.9838, 23.7275, 664000, 'Europe/Athens'),
                ('cb7ef1c0-fb6c-5e1e-a185-7d1aaf94d440', 'Thessaloniki', 'thessaloniki', NULL, 'GR', 40.6401, 22.9444, 325000, 'Europe/Athens'),
                ('b71f8d5d-98c7-5381-a7b5-c1e5d606f233', 'Heraklion', 'heraklion', NULL, 'GR', 35.3387, 25.1442, 177000, 'Europe/Athens'),
                ('bcdd8fff-3d83-5a75-9c12-109c54b806d3', 'Chania', 'chania', NULL, 'GR', 35.5138, 24.0180, 108000, 'Europe/Athens'),
                ('bf8138ef-2819-5b2a-9d56-13f74f5e29f5', 'Rhodes', 'rhodes', NULL, 'GR', 36.4349, 28.2176, 50000, 'Europe/Athens'),
                ('c08d4377-5528-58b1-9bab-9a044b3b9994', 'Corfu', 'corfu', 'kerkyra', 'GR', 39.6243, 19.9217, 32000, 'Europe/Athens'),
                ('7fcc08bf-94ad-5a98-9abb-d5308800527d', 'Mykonos', 'mykonos', NULL, 'GR', 37.4467, 25.3289, 10000, 'Europe/Athens'),
                ('a5e579f9-c74d-5c5a-98b4-d62e5441ac2e', 'Kalambaka', 'kalambaka', 'meteora', 'GR', 39.7056, 21.6264, 12000, 'Europe/Athens'),
                ('46381264-2f5a-5090-88cf-c5a8287a5f78', 'Delphi', 'delphi', NULL, 'GR', 38.4824, 22.5010, 2500, 'Europe/Athens'),
                ('1c871d00-4060-5d89-aeda-d0331cd2be03', 'Fira', 'fira', 'santorini', 'GR', 36.4167, 25.4318, 2000, 'Europe/Athens'),
                ('afb73b9a-3b23-5d0b-bb3f-0cc6d3fb074f', 'Oia', 'oia', NULL, 'GR', 36.4618, 25.3753, 1500, 'Europe/Athens'),
                ('f265f473-e7cc-5d16-b9b4-bd9e5b277d52', 'Adamantas', 'adamantas', 'milos', 'GR', 36.7258, 24.4472, 1400, 'Europe/Athens'),
                ('9c4193d9-7c82-5058-9a86-c7c7e758a86a', 'Roma', 'roma', 'rome', 'IT', 41.9028, 12.4964, 2750000, 'Europe/Rome'),
                ('a77c9fbf-12e2-5c67-a6de-f75f5b5bb6d3', 'Milano', 'milano', 'milan', 'IT', 45.4642, 9.1900, 1370000, 'Europe/Rome'),
                ('d0a2cc44-bad1-5f6e-a839-4a1eaec205a8', 'Napoli', 'napoli', 'naples', 'IT', 40.8518, 14.2681, 910000, 'Europe/Rome'),
                ('fef87bcd-c17d-5cce-a02f-403685347187', 'Firenze', 'firenze', 'florence', 'IT', 43.7696, 11.2558, 360000, 'Europe/Rome'),
                ('90b5c308-3cd8-5d08-82bf-5fb53aad97cf', 'Venezia', 'venezia', 'venice', 'IT', 45.4408, 12.3155, 250000, 'Europe/Rome'),
                ('b1c19aeb-01f0-56c5-968b-6678bb7c6b75', 'Bolzano', 'bolzano', NULL, 'IT', 46.4983, 11.3548, 107000, 'Europe/Rome'),
                ('f8a81b91-5cb5-514e-af84-184132918c9c', 'Sorrento', 'sorrento', NULL, 'IT', 40.6263, 14.3758, 16000, 'Europe/Rome'),
                ('80d082cb-fecd-56a9-accc-02e0fd970aa3', 'Capri', 'capri', NULL, 'IT', 40.5532, 14.2222, 7000, 'Europe/Rome'),
                ('6aeea2cd-9dc6-5a53-824c-f126059e950f', 'Cortina d''Ampezzo', 'cortina d''ampezzo', 'cortina', 'IT', 46.5405, 12.1357, 5800, 'Europe/Rome'),
                ('e8dfbf20-1588-5205-a141-6095007b8cf8', 'Amalfi', 'amalfi', NULL, 'IT', 40.6340, 14.6027, 5000, 'Europe/Rome'),
                ('662d0ca6-6089-5ee3-81c5-4d262d6d6cba', 'Ortisei', 'ortisei', NULL, 'IT', 46.5745, 11.6716, 4800, 'Europe/Rome'),
                ('4f5f7040-12af-5108-99cc-1b7f72634877', 'Positano', 'positano', NULL, 'IT', 40.6281, 14.4850, 3900, 'Europe/Rome'),
                ('74f7ce04-60d3-558a-9a3e-4e6a04844c2e', 'Lisboa', 'lisboa', 'lisbon', 'PT', 38.7223, -9.1393, 545000, 'Europe/Lisbon'),
                ('b83f6975-72b2-5618-836c-3fa101e792d9', 'Porto', 'porto', NULL, 'PT', 41.1579, -8.6291, 232000, 'Europe/Lisbon'),
                ('f394bc77-d6f4-5109-ad69-84e9137b3fcd', 'Funchal', 'funchal', NULL, 'PT', 32.6669, -16.9241, 105000, 'Europe/Lisbon'),
                ('864c1bf1-3d31-510e-80d6-c2587f97b9a9', 'Faro', 'faro', NULL, 'PT', 37.0194, -7.9322, 65000, 'Europe/Lisbon'),
                ('b1f410ef-6633-51fe-9d5b-12ec4776ad55', 'Portimão', 'portimao', NULL, 'PT', 37.1386, -8.5370, 59000, 'Europe/Lisbon'),
                ('3621fd4c-3e04-5160-9ff9-05f632acb3c1', 'Albufeira', 'albufeira', NULL, 'PT', 37.0891, -8.2479, 42000, 'Europe/Lisbon'),
                ('b8ffe975-5d70-53e1-891e-304fe197cafd', 'Lagos', 'lagos', NULL, 'PT', 37.1028, -8.6730, 31000, 'Europe/Lisbon'),
                ('0f92c5ab-f158-53ec-a589-5b9ad89cf0da', 'Sintra', 'sintra', NULL, 'PT', 38.8029, -9.3817, 30000, 'Europe/Lisbon'),
                ('c7aff45a-3c60-5773-a6ae-18f6f1b6dfaa', 'Lagoa', 'lagoa', NULL, 'PT', 37.1350, -8.4536, 23000, 'Europe/Lisbon'),
                ('f921bbd1-dd27-5078-a8c3-4403c4a509fd', 'Madrid', 'madrid', NULL, 'ES', 40.4168, -3.7038, 3300000, 'Europe/Madrid'),
                ('011900df-6833-566d-9e64-fd53056d0d46', 'Barcelona', 'barcelona', NULL, 'ES', 41.3874, 2.1686, 1620000, 'Europe/Madrid'),
                ('562ce38c-a5fd-5710-b62f-78e619265b7f', 'Valencia', 'valencia', NULL, 'ES', 39.4699, -0.3763, 790000, 'Europe/Madrid'),
                ('62368f72-5d80-59c8-832b-c77ee1c07f53', 'Sevilla', 'sevilla', 'seville', 'ES', 37.3891, -5.9845, 685000, 'Europe/Madrid'),
                ('c7a875a6-61e6-5c16-9b2b-a85fd96cd11e', 'Málaga', 'malaga', NULL, 'ES', 36.7213, -4.4214, 580000, 'Europe/Madrid'),
                ('2babd003-5928-57fa-b835-04a1a79808eb', 'Palma', 'palma', 'palma de mallorca', 'ES', 39.5696, 2.6502, 420000, 'Europe/Madrid'),
                ('648b86f7-59d1-5984-b4c6-ff30c1dd68cd', 'Granada', 'granada', NULL, 'ES', 37.1773, -3.5986, 230000, 'Europe/Madrid'),
                ('af998e90-35cf-577c-a217-181a678a0d48', 'Pollença', 'pollenca', NULL, 'ES', 39.8770, 3.0160, 16000, 'Europe/Madrid'),
                ('001ee9c6-4ff0-5d6e-82c6-402f5f587c21', 'Sóller', 'soller', NULL, 'ES', 39.7667, 2.7150, 14000, 'Europe/Madrid'),
                ('7b5da5c4-a7d3-5a9b-9079-40e875ffb57f', 'Oslo', 'oslo', NULL, 'NO', 59.9139, 10.7522, 700000, 'Europe/Oslo'),
                ('69147b6b-0420-54cb-86aa-3f977df2ff12', 'Bergen', 'bergen', NULL, 'NO', 60.3913, 5.3221, 285000, 'Europe/Oslo'),
                ('618158ca-c33a-5a66-affa-189de42b1eba', 'Stavanger', 'stavanger', NULL, 'NO', 58.9700, 5.7331, 145000, 'Europe/Oslo'),
                ('15e4b295-59ec-5de6-b558-12e2c3856e87', 'Tromsø', 'tromso', NULL, 'NO', 69.6492, 18.9553, 77000, 'Europe/Oslo'),
                ('43ff8108-3203-5574-b7fa-61dff4b3e7d5', 'Ålesund', 'alesund', NULL, 'NO', 62.4722, 6.1495, 67000, 'Europe/Oslo'),
                ('680b8df3-cae6-5559-9275-36a0ea7baef0', 'Flåm', 'flam', NULL, 'NO', 60.8628, 7.1137, 350, 'Europe/Oslo'),
                ('030c75bb-9e41-5f6b-95fe-cad7f5462f47', 'Geiranger', 'geiranger', NULL, 'NO', 62.1008, 7.2059, 250, 'Europe/Oslo'),
                ('aec6981d-5e49-513d-a9e6-bd2ff7e1ae4d', 'Gudvangen', 'gudvangen', NULL, 'NO', 60.8774, 6.8432, 100, 'Europe/Oslo'),
                ('10365bf3-4622-5f37-84ab-4c8e1c47c162', 'Knjaževac', 'knjazevac', NULL, 'RS', 43.5664, 22.2569, 18000, 'Europe/Belgrade'),
                ('53760065-c08f-5c6c-b098-832c04c3a503', 'Nova Varoš', 'nova varos', NULL, 'RS', 43.4597, 19.8139, 9000, 'Europe/Belgrade');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM cities WHERE country_code IN ('GR', 'IT', 'PT', 'ES', 'NO')
                   OR (country_code = 'RS' AND name IN ('Knjaževac', 'Nova Varoš'));
                """);
        }
    }
}
