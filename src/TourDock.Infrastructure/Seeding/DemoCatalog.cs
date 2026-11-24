using TourDock.Domain.Tours;
using TourDock.Domain.Vehicles;

namespace TourDock.Infrastructure.Seeding;

/// <summary>When a departure runs. Windows are the same every year, so the seeded rules are stable from one start to the next.</summary>
internal enum Season
{
    YearRound,

    /// <summary>1 April – 31 October: most boat trips and day tours.</summary>
    Long,

    /// <summary>1 May – 15 October: sea days that need warm water.</summary>
    Summer,

    /// <summary>15 June – 30 September: high mountain roads and huts are open.</summary>
    Alpine,

    /// <summary>1 November – 31 March: the reduced winter timetable of year-round tours.</summary>
    OffSeason,

    /// <summary>15 December – 20 March: enough snow for snowshoes.</summary>
    Winter,

    /// <summary>15 September – 31 March: dark enough for the northern lights.</summary>
    Aurora,
}

internal sealed record Departure(DaysOfWeek Days, TimeOnly Time, Season Season = Season.YearRound);

/// <summary>A yearly price change, e.g. +20% in July and August. A window may cross New Year (December → January).</summary>
internal sealed record PriceSeason(int FromMonth, int FromDay, int ToMonth, int ToDay, int Percent);

internal sealed record DemoPlace(string Country, string City, string MeetingPoint, double Lat, double Lng, string TimeZone);

internal sealed record DemoTour(
    string Title,
    string PhotoKey,
    string Description,
    DemoPlace Place,
    double Hours,
    int MaxGuests,
    BookingMode Mode,
    decimal? PerPerson,
    decimal? Private,
    string Vehicle,
    Departure[] Schedule,
    PriceSeason[] Prices,
    RefundTier[] Refunds);

internal sealed record DemoVehicle(string Name, VehicleType Type, int Capacity);

/// <param name="RetiredTours">Tours an earlier version of the demo seeded for this agency and that are no longer offered.</param>
internal sealed record DemoAgency(string Name, string OwnerEmail, string OwnerName, DemoVehicle[] Vehicles, DemoTour[] Tours, string[] RetiredTours);

/// <summary>
/// The fictional demo catalog: invented agencies and people (example.com), real places, realistic prices and seasons.
/// The seeder owns these agencies' catalogs and brings them back to this state on every start.
/// </summary>
internal static class DemoCatalog
{
    private const DaysOfWeek Daily = DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday | DaysOfWeek.Thursday
        | DaysOfWeek.Friday | DaysOfWeek.Saturday | DaysOfWeek.Sunday;

    private const DaysOfWeek Mon = DaysOfWeek.Monday, Tue = DaysOfWeek.Tuesday, Wed = DaysOfWeek.Wednesday,
        Thu = DaysOfWeek.Thursday, Fri = DaysOfWeek.Friday, Sat = DaysOfWeek.Saturday, Sun = DaysOfWeek.Sunday;

    // Refund policies: hours before the start → percent refunded.
    private static readonly RefundTier[] Flexible = [new(24, 100)];
    private static readonly RefundTier[] Standard = [new(72, 100), new(24, 50)];
    private static readonly RefundTier[] Strict = [new(168, 100), new(72, 50)];

    // Price seasons.
    private static readonly PriceSeason HighSummer = new(7, 1, 8, 31, 20);
    private static readonly PriceSeason HighSummerMild = new(7, 1, 8, 31, 10);
    private static readonly PriceSeason WinterDiscount = new(11, 1, 3, 31, -10);
    private static readonly PriceSeason Holidays = new(12, 27, 1, 6, 15);

    private static TimeOnly At(int hour, int minute = 0) => new(hour, minute);

    public static DemoAgency[] Agencies() =>
    [
        // ── Croatia: kept small on purpose ────────────────────────────────────────────────────────────────────
        new("Adriatic Blue Tours", "captain.marin@example.com", "Marin K.",
            [new("Sea Breeze", VehicleType.Boat, 12), new("Blue Lagoon", VehicleType.Boat, 8)],
            [
                new("Sunset Cruise along the Split Riviera", "split-sunset",
                    """
                    Three slow hours on a traditional wooden gulet while the sun goes down behind the islands. We leave the Riva, round the pine-covered Marjan peninsula and anchor in a quiet bay near Šolta for a swim in the last warm light.

                    On the way back you get the best view of Split there is: Diocletian's Palace and the bell tower of St Domnius lit up against the hills.

                    Included: skipper and host, a glass of local wine (Pošip or Plavac Mali) or a soft drink, dried figs and almonds, towels and a swim ladder.
                    Bring: a light jacket for the return and swimwear if you want to swim.

                    Good to know: the boat has a shaded deck and a toilet. Children are welcome; life jackets in all sizes are on board.
                    """,
                    new("HR", "Split", "Riva waterfront, Obala Hrvatskog narodnog preporoda", 43.5076, 16.4381, "Europe/Zagreb"),
                    3, 12, BookingMode.Both, 45m, 420m, "Sea Breeze",
                    [new(Tue | Thu | Sat, At(18), Season.Long)], [HighSummer], Standard),
                new("Blue Cave & Five Islands", "blue-cave",
                    """
                    The classic Dalmatian speedboat day. We cross to Biševo for the Blue Cave, where sunlight coming through an underwater opening turns the water electric blue, then continue to Stiniva, a cove almost closed by cliffs.

                    Itinerary: Blue Cave (Biševo) → Stiniva cove → lunch break in Komiža or Vis town → Budikovac lagoon → the Pakleni Islands and Hvar town → back to Split around 17:30.

                    Included: fuel, skipper, snorkelling masks, bottled water.
                    Not included: the Blue Cave ticket (paid on site, set by the island authority) and lunch.

                    Good to know: the cave closes when the south wind raises the sea; on those days we swap it for the Green Cave on Ravnik and refund the difference. Not recommended in pregnancy or with back problems: the speedboat bounces in open water.
                    """,
                    new("HR", "Split", "Riva waterfront, Obala Hrvatskog narodnog preporoda", 43.5076, 16.4381, "Europe/Zagreb"),
                    10, 8, BookingMode.Shared, 110m, null, "Blue Lagoon",
                    [new(Mon | Wed | Fri | Sun, At(7, 30), Season.Summer)], [HighSummer], Standard),
            ],
            ["Big Game Fishing Charter"]),
        new("Hvar Fishing Co.", "skipper.luka@example.com", "Luka P.",
            [new("Marlin", VehicleType.Boat, 6)], [],
            ["Morning Fishing Trip", "Pakleni Islands Snorkeling"]),
        new("Dalmatian Jeep Safari", "guide.ana@example.com", "Ana R.",
            [new("Dune Rider", VehicleType.Jeep, 6), new("Coastal Coach", VehicleType.Bus, 20)],
            [
                new("Konavle Valley Jeep Safari", "konavle",
                    """
                    Off the coast road and into Konavle, the green valley behind Dubrovnik that most visitors never see. We take gravel tracks through vineyards and olive groves up to the Sokol Grad fortress, then down to the old water mills on the Ljuta river.

                    Lunch is at a family farm: home-made prosciutto, sheep's cheese, bread from the wood oven and a glass of Malvasija Dubrovačka, a white wine that grows almost nowhere else.

                    Included: pick-up at Pile Gate, open-top Land Rover with driver-guide, fortress entrance, lunch with wine and water.

                    Good to know: tracks are dusty in summer and muddy after rain; wear clothes that can take it. Vegetarian lunch on request.
                    """,
                    new("HR", "Dubrovnik", "Pile Gate, Brsalje", 42.641730, 18.106850, "Europe/Zagreb"),
                    5, 6, BookingMode.Both, 89m, 480m, "Dune Rider",
                    [new(Mon | Thu | Sat, At(9), Season.Long), new(Sat, At(10), Season.OffSeason)], [WinterDiscount], Standard),
                new("Bay of Kotor Day Trip", "kotor",
                    """
                    A full day around the Bay of Kotor, the fjord-like bay hemmed in by limestone mountains. We start in Kotor's old town (a UNESCO site) with a walk through its squares and the cathedral of St Tryphon.

                    Then along the shore to Perast, a baroque town of captains' palaces, and a short boat ride to Our Lady of the Rocks, an island church built by sailors who dropped stones on a reef for two centuries.

                    Included: guide, coach, the boat to the island and the island museum.
                    Not included: lunch (we stop at a family restaurant in Perast; mussels from the bay are the thing to order).

                    Good to know: the optional climb to the San Giovanni fortress above Kotor is 1,350 steps; we leave free time for it at the end.
                    """,
                    new("ME", "Kotor", "Sea Gate (main entrance to the old town)", 42.424737, 18.769705, "Europe/Podgorica"),
                    9, 20, BookingMode.Shared, 69m, null, "Coastal Coach",
                    [new(Wed | Sun, At(8), Season.Long), new(Sun, At(9), Season.OffSeason)], [WinterDiscount], Standard),
            ],
            []),
        new("Central Dalmatia Adventures", "guide.tomislav@example.com", "Tomislav B.",
            [new("Rib Speedy", VehicleType.Boat, 10), new("Adventure Van", VehicleType.Bus, 14)], [],
            ["Cetina River Rafting", "Trogir, Šolta & the Blue Lagoon", "Brač & Zlatni Rat Beach Day", "Krka Waterfalls Day Trip",
                "Biokovo Skywalk by Jeep", "Kornati National Park Cruise"]),

        // ── Montenegro, Slovenia, Bosnia and Herzegovina, North Macedonia ─────────────────────────────────────
        // (Kotor and Budva stay with the agencies that first offered them, so their booking history stays in one place.)
        new("Durmitor Outdoor", "guide.nikola@example.com", "Nikola S.",
            [new("Raft Shuttle", VehicleType.Bus, 16), new("Durmitor Van", VehicleType.Bus, 8)],
            [
                new("Tara Canyon Rafting", "tara-rafting",
                    """
                    Rafting through the Tara canyon, at up to 1,300 metres the deepest in Europe. From Žabljak we drive over the Đurđevića Tara bridge to the put-in at Brštanovica and paddle 18 km of clear green river to Šćepan Polje.

                    The rapids are grade II–III: lively in May and June with snowmelt, gentler and warmer from July. Licensed guides brief you before you get wet, and there is a swim stop under a waterfall halfway.

                    Included: transfer from Žabljak, wetsuit, helmet, life jacket, guide, lunch at the riverside camp.
                    Bring: swimwear, a towel, a change of clothes and shoes that can get wet.

                    Good to know: minimum age 12; you need to be able to swim. Montenegro and Bosnia share the river here, so bring your passport or ID card.
                    """,
                    new("ME", "Žabljak", "Žabljak bus station, main road", 43.1542, 19.1236, "Europe/Podgorica"),
                    9, 16, BookingMode.Shared, 75m, null, "Raft Shuttle",
                    [new(Mon | Wed | Fri | Sat, At(8), Season.Summer)], [HighSummerMild], Standard),
                new("Durmitor Snowshoe Walk to the Black Lake", "durmitor-snow",
                    """
                    Durmitor in winter is quiet and white. On snowshoes we walk through old spruce forest to the frozen Black Lake (Crno jezero) below Međed peak, about 8 km on gentle ground with 250 m of climbing.

                    On the way your guide shows you animal tracks in the snow and the best spots to see the peaks of the national park. Hot tea and a slice of local cheese pie at the lake.

                    Included: snowshoes, poles, gaiters, national park ticket, guide, hot drinks and a snack.
                    Bring: warm waterproof layers, gloves, a hat and winter boots (we can lend boots in common sizes).

                    Good to know: no experience needed; if you can walk for three hours you can snowshoe. Runs when there is enough snow, usually from mid-December.
                    """,
                    new("ME", "Žabljak", "Žabljak bus station, main road", 43.1542, 19.1236, "Europe/Podgorica"),
                    5, 8, BookingMode.Both, 55m, 320m, "Durmitor Van",
                    [new(Tue | Thu | Sat, At(9, 30), Season.Winter)], [Holidays], Flexible),
            ],
            []),
        new("Istria Coast & Kayak", "guide.iva@example.com", "Iva M.",
            [new("Kayak Trailer", VehicleType.Car, 10), new("Istra Sail", VehicleType.Boat, 10)],
            [
                new("Piran Sunset Sail", "piran",
                    """
                    Slovenia has only 47 km of coast, and this evening sail shows you the best of it. We leave Piran's marina, sail past the Venetian old town and Tartini Square, and head for the Strunjan cliffs, at about 80 metres the highest flysch wall on the Adriatic.

                    As the sun sets over the Gulf of Trieste we pour a glass of Malvazija, the local white wine, with olives and cheese from the Karst.

                    Included: skipper, wine or soft drinks, snacks, blankets for the return.

                    Good to know: under sail when there's wind, under engine when there isn't. The yacht takes ten people; book it privately for a celebration.
                    """,
                    new("SI", "Piran", "Piran marina, Cankarjevo nabrežje", 45.5283, 13.5683, "Europe/Ljubljana"),
                    3, 10, BookingMode.Both, 59m, 520m, "Istra Sail",
                    [new(Wed | Fri, At(18, 30), Season.Long)], [HighSummerMild], Standard),
            ],
            ["Rovinj Sea Kayak & Snorkel", "Lim Fjord Boat Trip", "Kamenjak Cape Kayak", "Brijuni Islands Sail"]),
        new("Lakes & Mountains", "guide.sara@example.com", "Sara L.",
            [new("Lake Boat", VehicleType.Boat, 12), new("Mini Coach", VehicleType.Bus, 16), new("Herzegovina Van", VehicleType.Bus, 16), new("Riviera Boat", VehicleType.Boat, 12)],
            [
                new("Lake Bled & Vintgar Gorge", "bled",
                    """
                    Slovenia's most famous view, done properly. We row across Lake Bled in a pletna, the flat-bottomed wooden boat that has carried visitors to the island since the 16th century, climb the 99 steps to the Church of the Assumption and ring the wishing bell.

                    After lunch we walk the Vintgar gorge: 1.6 km of wooden walkways bolted to the rock above the emerald Radovna river, ending at the 13-metre Šum waterfall.

                    Included: guide, transport, pletna boat, gorge ticket, and a slice of kremšnita, Bled's famous cream cake.

                    Good to know: Vintgar is open from April to November; the walkways are narrow and can be wet, so wear shoes with grip.
                    """,
                    new("SI", "Bled", "Bled bus station, Cesta svobode", 46.3683, 14.1146, "Europe/Ljubljana"),
                    6, 16, BookingMode.Shared, 75m, null, "Mini Coach",
                    [new(Tue | Fri, At(9), Season.Long)], [HighSummerMild], Standard),
                new("Lake Ohrid Boat to St. Naum", "ohrid",
                    """
                    Lake Ohrid is one of the oldest lakes in the world, and its southern shore is best seen from the water. We leave Ohrid's old harbour below the church of St John at Kaneo and cruise for about 90 minutes to the monastery of St Naum.

                    On the way: the Bay of Bones, a reconstructed Bronze Age village on stilts, and views of the Galičica mountains. At St Naum you visit the 10th-century monastery and take a rowing boat over the springs of the Black Drin, where the water is so clear you see every pebble.

                    Included: boat, guide, the rowing boat at the springs.
                    Not included: lunch (try the Ohrid trout at the monastery restaurant).
                    """,
                    new("MK", "Ohrid", "Ohrid old harbour", 41.1123, 20.7980, "Europe/Skopje"),
                    6, 12, BookingMode.Both, 35m, 300m, "Lake Boat",
                    [new(Mon | Wed | Sat, At(10), Season.Long)], [HighSummerMild], Standard),
                new("Mostar & Kravica Waterfalls", "mostar",
                    """
                    A day in Herzegovina. Morning in Mostar: the Old Bridge rebuilt after the war (with luck you'll see one of the divers jump 21 metres into the Neretva), the copper workshops of the Kujundžiluk bazaar and a Turkish coffee in an Ottoman house.

                    Afternoon at the Kravica waterfalls, a 25-metre-high amphitheatre of falls where you can swim from May to September. On the way back we stop at the dervish house in Blagaj, built against a cliff at the source of the Buna river.

                    Included: guide, transport, Kravica and Blagaj tickets.
                    Bring: swimwear in summer, a scarf for the dervish house.

                    Good to know: runs all year. In winter the falls are at their most powerful, but swimming isn't possible.
                    """,
                    new("BA", "Mostar", "Mostar bus station", 43.3438, 17.8078, "Europe/Sarajevo"),
                    9, 16, BookingMode.Shared, 59m, null, "Herzegovina Van",
                    [new(Thu | Sun, At(8), Season.Long), new(Sun, At(9), Season.OffSeason)], [WinterDiscount], Standard),
                new("Budva Riviera & Sveti Stefan", "budva",
                    """
                    Four hours by boat along the Budva riviera, the stretch of Montenegrin coast with the most beaches per kilometre. We pass the walled old town of Budva and the island of Sveti Nikola, then sail down to Sveti Stefan, the island village joined to the mainland by a sand spit.

                    Two swim stops in coves you can only reach from the sea, with time to snorkel off the rocks.

                    Included: skipper, snorkelling gear, fruit and soft drinks.

                    Good to know: the boat is shaded and has a toilet. In July and August we leave a little earlier to beat the crowds.
                    """,
                    new("ME", "Budva", "Budva marina", 42.2786, 18.8378, "Europe/Podgorica"),
                    4, 12, BookingMode.Both, 39m, 360m, "Riviera Boat",
                    [new(Tue | Fri | Sun, At(10), Season.Summer)], [HighSummer], Standard),
            ],
            []),

        // ── Serbia ────────────────────────────────────────────────────────────────────────────────────────────
        new("Danube & Highlands", "guide.milan@example.com", "Milan J.",
            [new("Danube Cruiser", VehicleType.Boat, 24), new("Highland Jeep", VehicleType.Jeep, 6), new("Vojvodina Van", VehicleType.Bus, 8)],
            [
                new("Belgrade Rivers Sunset Cruise", "belgrade",
                    """
                    Belgrade from the water, where the Sava meets the Danube. Two hours past the walls of Kalemegdan fortress, the floating river clubs, the wild Great War Island bird reserve and the old town of Zemun with its Gardoš tower.

                    Your host tells the city's story, from Roman Singidunum to today's Belgrade Waterfront, while the sun goes down over New Belgrade.

                    Included: host, a welcome drink (wine, rakija or a soft drink), heated indoor salon and an open top deck.

                    Good to know: runs all year; in winter the cruise leaves earlier and the salon is heated. Wheelchair access to the lower deck.
                    """,
                    new("RS", "Beograd", "Sava pier, Karađorđeva street", 44.8176, 20.4569, "Europe/Belgrade"),
                    2, 24, BookingMode.Both, 25m, 390m, "Danube Cruiser",
                    [new(Tue | Thu | Sat | Sun, At(19), Season.Long), new(Sat | Sun, At(16), Season.OffSeason)], [WinterDiscount], Flexible),
                new("Golubac Fortress & Iron Gates", "golubac",
                    """
                    The Danube at its most dramatic. From the quay at Golubac we sail past the medieval Golubac fortress, restored in 2019 and standing where the river enters the Iron Gates gorge, into Đerdap National Park.

                    The highlight is the Kazan gorge, where the Danube narrows to about 150 metres between cliffs 300 metres high. We pass the rock face of the Dacian king Decebalus carved on the Romanian bank and the Roman plaque Tabula Traiana, set in the cliff almost 2,000 years ago.

                    Included: boat, guide, fortress ticket, lunch on board (fish soup from the Danube or a vegetarian option).

                    Good to know: bring your passport. We stay in Serbian waters, but border police sometimes check.
                    """,
                    new("RS", "Golubac", "Golubac quay", 44.6533, 21.6317, "Europe/Belgrade"),
                    7, 24, BookingMode.Shared, 55m, null, "Danube Cruiser",
                    [new(Wed | Fri, At(9), Season.Long)], [], Standard),
                new("Zlatibor & Mokra Gora by Jeep", "zlatibor",
                    """
                    High meadows, pine forest and hill villages in western Serbia. We leave Zlatibor by jeep on forest roads to the Gostilje waterfall, then over the Šargan pass to Mokra Gora.

                    In season you ride the Šargan Eight, a narrow-gauge heritage railway that loops back on itself in a figure of eight to climb the mountain. All year we visit Drvengrad, the wooden village built for a film festival, and finish with lunch in a village household: kajmak, pršuta, fresh cheese and proja.

                    Included: jeep with driver-guide, waterfall ticket, train ticket (April–October), lunch.

                    Good to know: from November to March the train runs only on some days, so we drive the old railway route instead.
                    """,
                    new("RS", "Zlatibor", "Zlatibor centre, King's Square", 43.7290, 19.7006, "Europe/Belgrade"),
                    7, 6, BookingMode.Both, 65m, 360m, "Highland Jeep",
                    [new(Mon | Thu | Sat, At(9))], [WinterDiscount], Standard),
                new("Fruška Gora Wine Tour", "fruska",
                    """
                    The wine hills above Novi Sad, an hour from Belgrade. We start in Sremski Karlovci, a baroque town with the Patriarchate palace and the oldest high school in Serbia, and taste bermet, a spiced dessert wine made here since the 18th century.

                    Then into Fruška Gora National Park: the monastery of Krušedol with its frescoes, and two family wineries where the owners pour their own Graševina, Tamjanika and reds aged in local oak.

                    Included: driver-guide, two winery tastings with snacks, a bermet tasting, monastery visit.

                    Good to know: tastings are generous; we can stop for a proper lunch (not included) at a vineyard restaurant. Non-drinkers get home-made juices.
                    """,
                    new("RS", "Novi Sad", "Petrovaradin fortress, lower town gate", 45.2517, 19.8628, "Europe/Belgrade"),
                    6, 8, BookingMode.Both, 69m, 390m, "Vojvodina Van",
                    [new(Sun, At(10)), new(Wed, At(11))], [], Standard),
            ],
            []),
        new("Serbian Highlands Outdoor", "guide.jovana@example.com", "Jovana M.",
            [new("Uvac Boat", VehicleType.Boat, 10), new("Mountain Jeep", VehicleType.Jeep, 6),
                new("Highland Van", VehicleType.Bus, 8), new("Kopaonik Van", VehicleType.Bus, 12)],
            [
                new("Uvac Canyon Meanders & Griffon Vultures", "uvac",
                    """
                    The Uvac river winds through limestone hills in loops so tight that it almost touches itself, and above it fly some of the last griffon vultures in the Balkans. A reserve with a feeding station has brought the colony back from a handful of birds.

                    We take a small boat along the meanders to the Ledena Pećina ice cave, then walk 2 km up to the Molitva viewpoint for the famous view of the bends. With binoculars you'll see vultures, wingspan up to 2.8 m, gliding at eye level.

                    Included: boat, reserve ticket, cave entrance, binoculars, local guide, picnic lunch.
                    Bring: walking shoes and a jacket. The cave stays at about 8 °C all year.

                    Good to know: the walk to the viewpoint is steep in places; allow 45 minutes up.
                    """,
                    new("RS", "Nova Varoš", "Nova Varoš town square", 43.4597, 19.8139, "Europe/Belgrade"),
                    7, 10, BookingMode.Both, 49m, 420m, "Uvac Boat",
                    [new(Wed | Sat | Sun, At(9), Season.Long)], [HighSummerMild], Standard),
                new("Tara National Park & Drina Canyon", "tara-np",
                    """
                    Tara is Serbia's wildest national park: deep forest, bears, and the Drina river cutting a canyon a kilometre deep along the border with Bosnia.

                    By jeep we climb from Bajina Bašta to Banjska Stena, the cliff-top viewpoint above the canyon and Lake Perućac, and on to Zaovine lake. On the way back you see the little house on a rock in the middle of the Drina, the most photographed house in Serbia, and stop for lunch at a mountain lodge.

                    Included: jeep with driver-guide, national park ticket, lunch.

                    Good to know: runs all year; in winter the forest roads are snowy and the views are sharper. Warm layers recommended any time of year: Banjska Stena is at 1,065 m.
                    """,
                    new("RS", "Bajina Bašta", "Bajina Bašta town square", 43.9708, 19.5683, "Europe/Belgrade"),
                    6, 6, BookingMode.Both, 59m, 330m, "Mountain Jeep",
                    [new(Tue | Fri | Sun, At(9))], [WinterDiscount], Standard),
                new("Kopaonik Snowshoe to Pančić's Peak", "kopaonik",
                    """
                    Kopaonik is best known for skiing, but the quietest way up its highest point is on snowshoes. From the resort centre we walk through snow-heavy spruce to Pančić's Peak (2,017 m), with the mausoleum of the botanist Josif Pančić on top.

                    On a clear day you see across southern Serbia to Kosovo and the Šar mountains. On the way down we stop at a mountain hut for hot tea with honey and a slice of pita.

                    Included: snowshoes, poles, guide, hot drinks and a snack.
                    Bring: warm waterproof layers, gloves, a hat, sunglasses and winter boots.

                    Good to know: about 6 km with 250 m of climbing. No experience needed.
                    """,
                    new("RS", "Kopaonik", "Kopaonik resort centre, Sunčani vrhovi", 43.2856, 20.8131, "Europe/Belgrade"),
                    4, 12, BookingMode.Both, 39m, 240m, "Kopaonik Van",
                    [new(Mon | Wed | Fri | Sat, At(10), Season.Winter)], [Holidays], Flexible),
                new("Stara Planina Waterfalls by Jeep", "stara-planina",
                    """
                    Stara Planina, the Old Mountain, runs along the Bulgarian border and is one of the emptiest corners of Serbia. From Knjaževac we drive deep into the nature park on old shepherds' tracks, past stone villages where a few families still keep sheep.

                    We walk to two of the park's waterfalls: Pilj, one of the highest in Serbia at about 64 m, and the cascades of Kaluđerski skok. Lunch is in the village of Topli Do, cooked by our hosts.

                    Included: 4×4 transport with driver-guide, waterfall walks with a guide, lunch.
                    Bring: hiking shoes and water. The walk to Pilj is about 3 km each way with a steep finish.

                    Good to know: a long, full day. Mobile signal is patchy, which is half the charm.
                    """,
                    new("RS", "Knjaževac", "Knjaževac bus station", 43.5664, 22.2569, "Europe/Belgrade"),
                    9, 8, BookingMode.Both, 69m, 420m, "Highland Van",
                    [new(Thu | Sat, At(8, 30), Season.Summer)], [], Standard),
                new("Rtanj Sunrise Hike", "rtanj",
                    """
                    Rtanj is the pyramid-shaped mountain of eastern Serbia, full of local legends and famous for its mountain tea. We meet in Sokobanja before dawn and start walking from the village of Rtanj by headlamp.

                    You reach Šiljak, the 1,565 m summit with the ruins of a small chapel, as the sun comes up over the Timok valley. Breakfast on top, then down through meadows where you can pick Rtanj tea (wild savory).

                    Included: transfer from Sokobanja, guide, headlamps, breakfast pack.
                    Bring: hiking boots, a warm layer for the summit and water.

                    Good to know: about 10 km and 800 m of climbing; moderate fitness needed. Cancelled with a full refund if storms are forecast.
                    """,
                    new("RS", "Sokobanja", "Sokobanja town square", 43.6436, 21.8728, "Europe/Belgrade"),
                    6, 8, BookingMode.Both, 35m, 240m, "Highland Van",
                    [new(Sun, At(4, 30), Season.Long)], [], Flexible),
            ],
            []),

        // ── Albania ───────────────────────────────────────────────────────────────────────────────────────────
        new("Ionian Coast Boats", "skipper.ardit@example.com", "Ardit H.",
            [new("Kaltra", VehicleType.Boat, 12), new("Riviera Van", VehicleType.Bus, 8)],
            [
                new("Ksamil Islands & Hidden Bays", "ksamil",
                    """
                    The Albanian Riviera at its most turquoise. We leave the Ksamil waterfront and circle the four small Ksamil islands, then follow the coast south towards Butrint, stopping in bays with white pebbles and water clear to ten metres.

                    Lunch is grilled fish and Greek salad at a taverna on the water, then more swimming and snorkelling on the way back.

                    Included: skipper, snorkelling gear, lunch with a drink.

                    Good to know: Corfu is only a few kilometres away; you see it the whole time. On days with a strong north wind we switch to the sheltered bays.
                    """,
                    new("AL", "Ksamil", "Ksamil waterfront pier", 39.7686, 20.0003, "Europe/Tirane"),
                    5, 12, BookingMode.Both, 39m, 320m, "Kaltra",
                    [new(Daily, At(10), Season.Summer)], [HighSummer], Standard),
                new("Butrint & the Blue Eye", "butrint",
                    """
                    Two of southern Albania's best sights in one easy day. Butrint, a UNESCO site, was a city for 2,500 years: Greek theatre, Roman baths, a 6th-century baptistery with mosaic floors and a Venetian castle with views over the lagoon.

                    In the afternoon we drive inland to the Blue Eye (Syri i Kaltër), a spring where water rises from more than 50 metres down, deep blue in the centre and turquoise at the edges. The water stays at about 10 °C all year.

                    Included: driver-guide, Butrint ticket, Blue Eye parking and ticket.
                    Not included: lunch (stop in a village restaurant on the way).

                    Good to know: runs all year. Butrint is quietest and greenest in spring and autumn.
                    """,
                    new("AL", "Ksamil", "Ksamil main square", 39.7686, 20.0003, "Europe/Tirane"),
                    7, 8, BookingMode.Both, 49m, 290m, "Riviera Van",
                    [new(Tue | Thu | Sat, At(9))], [WinterDiscount], Standard),
            ],
            []),

        // ── Greece ────────────────────────────────────────────────────────────────────────────────────────────
        new("Aegean Sail Co.", "skipper.nikos@example.com", "Nikos P.",
            [new("Meltemi", VehicleType.Boat, 20), new("Kleftiko Star", VehicleType.Boat, 12)],
            [
                new("Santorini Caldera Sunset Catamaran", "santorini",
                    """
                    Five hours on a sailing catamaran inside Santorini's caldera, the flooded crater of the volcano that blew the island apart 3,600 years ago. We leave from Vlychada marina and sail under the cliffs to the Red Beach and the White Beach, with swim and snorkel stops.

                    Next stop is the hot springs by the volcano, where you swim into warm, iron-orange water. Dinner is a barbecue cooked on board while we sail north to watch the sunset under Oia.

                    Included: transfer from your hotel, swim and snorkel stops, BBQ dinner, unlimited wine, beer and soft drinks, towels.

                    Good to know: the sea can be choppy when the meltemi wind blows in July and August; tell us if you get seasick.
                    """,
                    new("GR", "Fira", "Vlychada marina", 36.3434, 25.4290, "Europe/Athens"),
                    5, 20, BookingMode.Both, 135m, 1900m, "Meltemi",
                    [new(Daily, At(15), Season.Long)], [HighSummer], Strict),
                new("Milos Kleftiko Sailing Day", "milos",
                    """
                    Kleftiko is a bay of white volcanic rock on Milos' south-west coast, full of arches and sea caves where pirates once hid. You can only reach it by sea, and a small boat is the best way in.

                    We sail from Adamantas past the coloured fishermen's houses of Klima to Kleftiko, then on to the Sykia cave, whose collapsed roof lets the sun light the water. Between them, snorkelling and swimming off the boat.

                    Included: skipper and crew, lunch cooked on board with Greek salad and grilled meat or fish, wine and soft drinks, snorkelling gear.

                    Good to know: the route changes with the wind. When the south-west coast is too rough we go to Polyaigos instead, an uninhabited island just as beautiful.
                    """,
                    new("GR", "Adamantas", "Adamantas port, main pier", 36.7258, 24.4472, "Europe/Athens"),
                    8, 12, BookingMode.Both, 115m, 1350m, "Kleftiko Star",
                    [new(Mon | Wed | Fri | Sun, At(9, 30), Season.Summer)], [HighSummerMild], Strict),
            ],
            []),
        new("Thessaly & Delphi Tours", "guide.eleni@example.com", "Eleni K.",
            [new("Meteora Van", VehicleType.Bus, 16), new("Delphi Coach", VehicleType.Bus, 20)],
            [
                new("Meteora Monasteries at Sunset", "meteora",
                    """
                    Six monasteries still in use stand on top of sandstone pillars rising 400 metres above the plain of Thessaly. Monks first climbed up here in the 14th century to get away from the world, and until the 1920s visitors were hauled up in nets.

                    We drive the panoramic road between the rocks, visit the inside of one monastery (Great Meteoron or Varlaam, depending on the day), and finish at a quiet viewpoint as the sun sets behind the Pindus mountains.

                    Included: guide, transport from Kalambaka, viewpoint stops.
                    Not included: monastery entrance (€5, paid on site).

                    Good to know: monasteries ask for covered shoulders and knees, and skirts for women (wraps are lent at the door). There are about 150 steps to the monastery.
                    """,
                    new("GR", "Kalambaka", "Kalambaka central square", 39.7056, 21.6264, "Europe/Athens"),
                    4, 16, BookingMode.Both, 39m, 280m, "Meteora Van",
                    [new(Mon | Wed | Fri | Sat | Sun, At(16, 30), Season.Long), new(Sat | Sun, At(14, 30), Season.OffSeason)], [WinterDiscount], Flexible),
                new("Delphi Day Trip from Athens", "delphi",
                    """
                    Delphi was the centre of the ancient Greek world, where people came to ask the oracle of Apollo about their future. It sits on the slopes of Mount Parnassus, above an olive valley that runs down to the sea.

                    With a licensed archaeologist-guide you walk the Sacred Way past the treasuries to the Temple of Apollo, the theatre and the stadium, then see the museum and its bronze Charioteer. Lunch is in the mountain village of Arachova.

                    Included: air-conditioned coach from central Athens, licensed guide, site and museum tickets.
                    Not included: lunch.

                    Good to know: about 2.5 hours' drive each way. The site is on a steep hillside; wear good shoes and bring water in summer.
                    """,
                    new("GR", "Athens", "Syntagma Square, Amalias Avenue side", 37.9755, 23.7348, "Europe/Athens"),
                    10, 20, BookingMode.Shared, 95m, null, "Delphi Coach",
                    [new(Tue | Thu | Sat, At(8))], [WinterDiscount], Standard),
            ],
            []),

        // ── Italy ─────────────────────────────────────────────────────────────────────────────────────────────
        new("Costiera Boats", "skipper.giulia@example.com", "Giulia R.",
            [new("Gozzo Sorrentino", VehicleType.Boat, 12), new("Capri Express", VehicleType.Boat, 12)],
            [
                new("Amalfi Coast & Positano by Boat", "amalfi",
                    """
                    The Amalfi Coast road is famous for its traffic jams; the sea has none. On a traditional wooden gozzo we leave Sorrento and round the cape to Li Galli, the islands of the sirens, then stop for 90 minutes in Positano, with its pastel houses stacked up the cliff.

                    Then on to Amalfi for two hours to see the cathedral and the paper mills, past the Furore fjord and with a swim stop in a cove on the way back. Limoncello on board for the return.

                    Included: skipper, swim stops, water, soft drinks, limoncello and snacks.
                    Not included: lunch ashore.

                    Good to know: boats can't sail in rough seas; if we cancel you get a full refund or another date.
                    """,
                    new("IT", "Sorrento", "Marina Piccola, Sorrento", 40.6291, 14.3781, "Europe/Rome"),
                    8, 12, BookingMode.Both, 145m, 1600m, "Gozzo Sorrentino",
                    [new(Tue | Thu | Sat, At(9), Season.Long)], [HighSummerMild], Standard),
                new("Capri Island & Blue Grotto", "capri",
                    """
                    A full day around and on Capri. By boat we circle the island: the Faraglioni stacks, the Green and White Grottoes, and the sheer cliffs below Villa Jovis, where the emperor Tiberius lived.

                    When the sea allows we stop at the Blue Grotto, entered lying back in a rowing boat through an opening one metre high. Inside, light coming through the water makes it glow blue. Then free time in Capri town and Anacapri.

                    Included: skipper, boat around the island, swim stop, drinks on board.
                    Not included: the Blue Grotto rowing boat and entrance (about €18, paid on site) and the funicular.

                    Good to know: the Blue Grotto closes at short notice when the sea is up, even on sunny days.
                    """,
                    new("IT", "Sorrento", "Marina Piccola, Sorrento", 40.6291, 14.3781, "Europe/Rome"),
                    8, 12, BookingMode.Both, 125m, 1450m, "Capri Express",
                    [new(Mon | Wed | Fri | Sun, At(9), Season.Long)], [HighSummerMild], Standard),
            ],
            []),
        new("Dolomiti Outdoor", "guide.matteo@example.com", "Matteo B.",
            [new("Alpine Van", VehicleType.Bus, 8)],
            [
                new("Tre Cime di Lavaredo Hike", "tre-cime",
                    """
                    The three rock towers of the Tre Cime are the symbol of the Dolomites, and the walk around them is one of the great day hikes in the Alps. From Cortina we drive up the toll road to Rifugio Auronzo at 2,320 m.

                    From there it's a 10 km loop on good paths: under the south faces, over the Forcella Lavaredo saddle, to Rifugio Locatelli for lunch facing the famous north walls, and back through meadows full of flowers in July. On the way down we stop at Lake Misurina.

                    Included: transfer from Cortina, toll road, mountain guide.
                    Not included: lunch at the hut.
                    Bring: hiking boots, a rain jacket, a warm layer, sun cream and 1.5 litres of water.

                    Good to know: about 400 m of climbing at altitude; moderate fitness needed. Only runs in summer, when the huts are open.
                    """,
                    new("IT", "Cortina d'Ampezzo", "Cortina bus station, Via G. Marconi", 46.5383, 12.1374, "Europe/Rome"),
                    8, 8, BookingMode.Both, 95m, 620m, "Alpine Van",
                    [new(Mon | Wed | Fri, At(8), Season.Alpine)], [], Standard),
                new("Dolomites Sunset Snowshoe", "dolomites-snow",
                    """
                    Late afternoon on snowshoes in the Dolomites, timed so the peaks turn pink at sunset. It's an effect called enrosadira, caused by the dolomite rock.

                    We walk through larch forest and open snowfields near Cortina to a viewpoint facing the Cinque Torri, then come down by headlamp to a mountain hut for hot chocolate or vin brulé.

                    Included: mountain guide, snowshoes, poles, headlamps, hot drink at the hut.
                    Bring: winter boots, warm layers, gloves and a hat.

                    Good to know: about 5 km with 250 m of climbing. No experience needed. The guide picks the route each day for avalanche safety.
                    """,
                    new("IT", "Cortina d'Ampezzo", "Cortina bus station, Via G. Marconi", 46.5383, 12.1374, "Europe/Rome"),
                    4, 8, BookingMode.Both, 75m, 420m, "Alpine Van",
                    [new(Tue | Thu | Sat, At(14), Season.Winter)], [Holidays], Flexible),
            ],
            []),

        // ── Portugal ──────────────────────────────────────────────────────────────────────────────────────────
        new("Algarve Coast Tours", "skipper.joao@example.com", "João M.",
            [new("Benagil Cruiser", VehicleType.Boat, 12), new("Golfinho", VehicleType.Boat, 12), new("Kayak Trailer", VehicleType.Car, 10)],
            [
                new("Benagil Caves Boat Trip", "benagil",
                    """
                    Two hours along the most beautiful stretch of the Algarve: golden limestone cliffs, arches and sea caves between Portimão and Benagil.

                    The highlight is the Benagil cave, a sea cave with a round hole in its roof that lights up the beach inside. On the way you pass the Marinha beach, the Elephant rock and the fishing village of Carvoeiro, and the skipper goes into the smaller caves when the sea is calm.

                    Included: skipper, life jackets, commentary in English and Portuguese.

                    Good to know: by law boats can't land on the beach inside Benagil, so you see it from the water. Runs all year; winter light on the cliffs is gorgeous.
                    """,
                    new("PT", "Portimão", "Portimão marina, pontoon B", 37.1268, -8.5276, "Europe/Lisbon"),
                    2, 12, BookingMode.Both, 35m, 320m, "Benagil Cruiser",
                    [new(Daily, At(10, 30))], [HighSummer, WinterDiscount], Flexible),
                new("Ponta da Piedade Kayak Tour", "ponta-kayak",
                    """
                    Paddle into the grottoes of Ponta da Piedade, Lagos' headland of golden rock pillars, arches and caves that big boats can't reach.

                    We launch from Batata beach. After a short lesson your guide leads the way along the cliffs, through rock arches, into grottoes and past Camilo beach, with a swim stop on a beach you can't reach on foot.

                    Included: sit-on-top kayak (single or double), paddle, life jacket, dry bag, guide.
                    Bring: swimwear, sun cream and a hat. Everything you wear will get wet.

                    Good to know: no experience needed; children from 8 in a double kayak with an adult.
                    """,
                    new("PT", "Lagos", "Batata beach, by the fort", 37.0998, -8.6705, "Europe/Lisbon"),
                    3, 10, BookingMode.Shared, 45m, null, "Kayak Trailer",
                    [new(Daily, At(9, 30), Season.Long)], [HighSummerMild], Flexible),
                new("Lagos Dolphin Watching", "dolphins",
                    """
                    Common and bottlenose dolphins feed off Lagos all year. A marine biologist joins every trip, explains what you're seeing and logs every sighting for a local research project.

                    On a fast rigid-hull boat we head out across the bay of Lagos, keeping our distance and our engines quiet once we find a group, as the code of conduct requires. On the way back we go along the Ponta da Piedade cliffs.

                    Included: marine biologist guide, life jackets, waterproof jackets in winter.

                    Good to know: we see dolphins on more than nine trips in ten. If we don't, you sail again for free. Not suitable for pregnant passengers or people with back problems.
                    """,
                    new("PT", "Lagos", "Lagos marina, reception pontoon", 37.1070, -8.6720, "Europe/Lisbon"),
                    2.5, 12, BookingMode.Both, 49m, 480m, "Golfinho",
                    [new(Tue | Thu | Sat | Sun, At(10))], [HighSummerMild], Flexible),
            ],
            []),

        // ── Spain ─────────────────────────────────────────────────────────────────────────────────────────────
        new("Mallorca Sea & Mountain", "guide.lucia@example.com", "Lucía F.",
            [new("Tramuntana 4x4", VehicleType.Jeep, 6), new("Cala Llaüt", VehicleType.Boat, 10)],
            [
                new("Serra de Tramuntana Jeep Safari", "tramuntana",
                    """
                    The mountains of northern Mallorca are a UNESCO cultural landscape: terraced olive groves, stone villages and limestone peaks over 1,400 m.

                    From Sóller we drive by 4×4 over the mountains past the Cúber reservoir and below Puig Major, the highest peak on the island, to the monastery of Lluc. Then down the snaking road to Sa Calobra, with its 270-degree hairpin, the Nus de sa Corbata. Off-road tracks on the way reach viewpoints the tour buses can't.

                    Included: 4×4 with driver-guide, lunch at a mountain finca with local wine.

                    Good to know: the Sa Calobra road has a lot of bends; take something for motion sickness if you need it.
                    """,
                    new("ES", "Sóller", "Plaça de la Constitució, Sóller", 39.7667, 2.7150, "Europe/Madrid"),
                    7, 6, BookingMode.Both, 95m, 520m, "Tramuntana 4x4",
                    [new(Mon | Wed | Fri, At(9))], [WinterDiscount], Standard),
                new("Formentor Coves by Llaüt", "mallorca",
                    """
                    Five hours on a llaüt, the traditional wooden Mallorcan fishing boat, around the Formentor peninsula. Pine forest runs down to coves with water so clear the anchor is visible eight metres down.

                    We swim at Cala Figuera and in front of Formentor beach, snorkel along the rocks, and stop for a long lunch on board: pa amb oli, local cheese, sobrassada and fruit.

                    Included: skipper, lunch, wine, water and soft drinks, snorkelling gear, paddle board.

                    Good to know: the cove stops depend on the wind. In a north wind we stay in the shelter of the bay of Pollença.
                    """,
                    new("ES", "Pollença", "Port de Pollença, fishermen's pier", 39.9070, 3.0850, "Europe/Madrid"),
                    5, 10, BookingMode.Both, 79m, 750m, "Cala Llaüt",
                    [new(Tue | Thu | Sat | Sun, At(10), Season.Summer)], [HighSummer], Standard),
            ],
            []),

        // ── Norway ────────────────────────────────────────────────────────────────────────────────────────────
        new("Western Fjords Guiding", "guide.ingrid@example.com", "Ingrid S.",
            [new("Fjord Electric", VehicleType.Boat, 30), new("Geiranger RIB", VehicleType.Boat, 12), new("Aurora Bus", VehicleType.Bus, 16)],
            [
                new("Nærøyfjord Silent Cruise from Flåm", "naeroy",
                    """
                    The Nærøyfjord is one of the narrowest fjords in the world, at one point just 250 metres wide between mountains 1,700 metres high, and it's a UNESCO World Heritage site. We cruise it on an electric boat, so the loudest thing you hear is the waterfalls.

                    From Flåm we sail up the Aurlandsfjord past the village of Undredal, known for its stave church and brown goat cheese, then into the Nærøyfjord. Watch for seals and porpoises.

                    Included: two hours on board, warm blankets, coffee or tea, commentary.

                    Good to know: runs all year. In winter the mountains are white and the boat's heated panorama salon is the place to be.
                    """,
                    new("NO", "Flåm", "Flåm harbour, main quay", 60.8628, 7.1137, "Europe/Oslo"),
                    2, 30, BookingMode.Shared, 69m, null, "Fjord Electric",
                    [new(Daily, At(10), Season.Long), new(Sat | Sun, At(11), Season.OffSeason)], [], Flexible),
                new("Geirangerfjord RIB & Seven Sisters", "geiranger",
                    """
                    Fast, close and wet: a rigid inflatable ride into the Geirangerfjord, past waterfalls a slow boat can't get near.

                    We stop below the Seven Sisters, seven falls dropping 250 metres side by side, and opposite the Suitor across the fjord. We also look up at the abandoned cliff-edge farms of Knivsflå and Skageflå, where children were once tied with ropes so they wouldn't fall.

                    Included: guide-driver, full-body survival suit, gloves, goggles.

                    Good to know: minimum age 6. Not suitable in pregnancy or with back or neck problems. Waterfalls are biggest in May and June.
                    """,
                    new("NO", "Geiranger", "Geiranger harbour, guest pontoon", 62.1008, 7.2059, "Europe/Oslo"),
                    1.5, 12, BookingMode.Both, 95m, 980m, "Geiranger RIB",
                    [new(Daily, At(11), Season.Summer)], [], Standard),
                new("Tromsø Northern Lights Chase", "tromso",
                    """
                    We leave Tromsø at 18:30 and follow the forecast. Your guide watches cloud maps and aurora data all evening and drives wherever the sky is clear: along the fjords, into the mountains, or as far as the Finnish border.

                    Once there we light a campfire, serve hot soup and cocoa, and help you set up your camera or phone to photograph the lights. Back in Tromsø between midnight and 2:00.

                    Included: guide, minibus, thermal suits and boots, hot meal and drinks, a tripod, and the photos your guide takes of your group.

                    Good to know: the northern lights are never guaranteed. If we see nothing, you can join another night at half price. The season runs from mid-September to the end of March.
                    """,
                    new("NO", "Tromsø", "Tromsø harbour, Prostneset bus terminal", 69.6489, 18.9606, "Europe/Oslo"),
                    7, 16, BookingMode.Both, 129m, 1650m, "Aurora Bus",
                    [new(Daily, At(18, 30), Season.Aurora)], [Holidays], Flexible),
            ],
            []),
    ];
}
