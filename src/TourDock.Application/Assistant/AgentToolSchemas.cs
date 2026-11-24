using System.Text.Json;

namespace TourDock.Application.Assistant;

/// <summary>What the model reads about each tool. The descriptions say when to use a tool, not only what it does.</summary>
internal static class AgentToolSchemas
{
    public static readonly IReadOnlyList<AgentToolDefinition> All =
    [
        new(CatalogTools.SearchTours,
            "Search TourDock's published tours that have a bookable departure in the next 365 days. Use it whenever the traveler "
            + "asks what is available. Put what they want in their own words in `description` (activity, mood, who is coming), "
            + "and the hard facts in the other fields. Returns up to 6 tours, best match first when `description` is given, each "
            + "with its next departure that fits the group and its live seat count.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "description": {
                      "type": "string",
                      "maxLength": 200,
                      "description": "What the traveler wants, e.g. \"calm boat trip for a family\". Leave out place and dates."
                    },
                    "place": { "type": "string", "description": "City or town to search around (25 km), e.g. \"Split\" or \"Zlatibor\"." },
                    "country": { "type": "string", "description": "ISO country code, e.g. HR, RS, GR, ES." },
                    "date": { "type": "string", "description": "Departure date at the tour's location, YYYY-MM-DD." },
                    "guests": {
                      "type": "integer", "minimum": 1, "maximum": 100, "description": "Group size; only tours with that many free seats."
                    },
                    "max_price_per_person": { "type": "number", "minimum": 0, "description": "In EUR." },
                    "booking_type": { "type": "string", "enum": ["Shared", "Private"],
                      "description": "Shared: buy seats and join a group. Private: the whole departure for one group." }
                  },
                  "additionalProperties": false
                }
                """)),
        new(CatalogTools.GetTour,
            "Full details of one tour: description, meeting point, duration, prices, seasonal prices, cancellation policy and "
            + "rating. Use it before answering detailed questions about a tour (what is included, refunds, where to meet).",
            Schema("""
                {
                  "type": "object",
                  "properties": { "tour_id": { "type": "string", "description": "The tour's id from search_tours." } },
                  "required": ["tour_id"],
                  "additionalProperties": false
                }
                """)),
        new(CatalogTools.GetDepartures,
            "Departures of one tour in a date range (at most 31 days), with free seats and prices, for a given group size. Use it "
            + "when the traveler asks about specific dates or times.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "tour_id": { "type": "string", "description": "The tour's id from search_tours." },
                    "from": { "type": "string", "description": "First day, YYYY-MM-DD. Default: today." },
                    "to": { "type": "string", "description": "Last day, YYYY-MM-DD. Default: 14 days after from." },
                    "guests": { "type": "integer", "minimum": 1, "maximum": 100, "description": "Group size. Default: 1." }
                  },
                  "required": ["tour_id"],
                  "additionalProperties": false
                }
                """)),
    ];

    internal static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
