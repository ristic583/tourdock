using static TourDock.Application.Assistant.AgentToolSchemas;

namespace TourDock.Application.Assistant;

/// <summary>Account tools: what a signed-in traveler or agency member can let an agent do.</summary>
internal static class AccountToolSchemas
{
    public static readonly IReadOnlyList<AgentToolDefinition> Traveler =
    [
        new(TravelerTools.ListMyBookings,
            "The signed-in traveler's bookings, newest departure first: status, guests, what they paid and what they would get "
            + "back if they cancelled now.",
            Schema("""{ "type": "object", "properties": {}, "additionalProperties": false }"""),
            AgentScopes.Bookings),
        new(TravelerTools.StartBooking,
            "Hold seats (or a whole departure) for the signed-in traveler and get the checkout link. You never pay: give the "
            + "traveler the link, they pay on TourDock. The hold expires if they don't pay in time. Use a departure id from "
            + "get_departures, and only after the traveler confirmed the tour, date and group size. Calling it again with the "
            + "same details the same day returns the same booking.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "departure_id": { "type": "string", "description": "From get_departures." },
                    "guests": { "type": "integer", "minimum": 1, "maximum": 100 },
                    "booking_type": {
                      "type": "string", "enum": ["Shared", "Private"], "description": "Shared: seats. Private: the whole departure."
                    }
                  },
                  "required": ["departure_id", "guests", "booking_type"],
                  "additionalProperties": false
                }
                """),
            AgentScopes.Bookings),
        new(TravelerTools.CancelBooking,
            "Cancel one of the signed-in traveler's bookings. First call it with confirm false: it changes nothing and says "
            + "what they'd get back. Tell the traveler, and call it with confirm true only after they clearly said yes.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "booking_id": { "type": "string", "description": "From list_my_bookings." },
                    "confirm": { "type": "boolean", "description": "false: preview the refund. true: cancel for real." }
                  },
                  "required": ["booking_id", "confirm"],
                  "additionalProperties": false
                }
                """),
            AgentScopes.Bookings,
            Destructive: true),
    ];

    public static readonly IReadOnlyList<AgentToolDefinition> Agency =
    [
        new(AgencyTools.MyAgencies,
            "The agencies the signed-in user works for, with their ids and role. Call it first to get the agency_id.",
            Schema("""{ "type": "object", "properties": {}, "additionalProperties": false }"""),
            AgentScopes.AgencyRead),
        new(AgencyTools.AgencyBookings,
            "Bookings on the agency's departures in a date range (at most 31 days), with who booked.",
            Schema(DateRange),
            AgentScopes.AgencyRead),
        new(AgencyTools.AgencyStats,
            "The agency's numbers for a date range: departures, seats sold, occupancy and revenue.",
            Schema(DateRange),
            AgentScopes.AgencyRead),
        new(AgencyTools.AgencyReviews,
            "The newest reviews of the agency's tours, with any reply, 20 per page.",
            Schema("""
                {
                  "type": "object",
                  "properties": { "agency_id": { "type": "string" }, "page": { "type": "integer", "minimum": 1 } },
                  "required": ["agency_id"],
                  "additionalProperties": false
                }
                """),
            AgentScopes.AgencyRead),
        new(AgencyTools.ReplyToReview,
            "Post or replace the agency's public reply to a review. Show the reply to the user and post it only after they "
            + "approved the exact text.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "agency_id": { "type": "string" },
                    "review_id": { "type": "string", "description": "From agency_reviews." },
                    "text": { "type": "string", "maxLength": 1000 }
                  },
                  "required": ["agency_id", "review_id", "text"],
                  "additionalProperties": false
                }
                """),
            AgentScopes.AgencyWrite),
    ];

    private const string DateRange = """
        {
          "type": "object",
          "properties": {
            "agency_id": { "type": "string", "description": "From my_agencies." },
            "from": { "type": "string", "description": "First day, YYYY-MM-DD." },
            "to": { "type": "string", "description": "Last day, YYYY-MM-DD (at most 31 days after from)." }
          },
          "required": ["agency_id", "from", "to"],
          "additionalProperties": false
        }
        """;
}
