namespace BerryExchange.Api.Chat.Agent;

public static class ToolCatalog
{
    public const string SystemPrompt = """
        You are Berry, the assistant for the Berry Exchange marketplace, where growers list
        fresh berries and buyers reserve them by the kilogram. Prices are USD per kilogram.
        Rules:
        - Answer questions about listings by calling tools; never invent listings or prices.
        - Before calling create_reservation you MUST have asked the user and received an explicit
          "yes" for that exact listing and quantity in this conversation; only then call it with
          user_confirmed=true.
        - Keep answers short, concrete, and friendly. If a tool errors, tell the user plainly.
        """;

    public static readonly IReadOnlyList<AgentToolDefinition> Definitions =
    [
        new("search_listings",
            "Search berry listings with a free-text query. Call this whenever the user asks what is available.",
            """{"type":"object","properties":{"query":{"type":"string","description":"What the user is looking for"}},"required":["query"]}"""),
        new("get_listing",
            "Get the full details of a single listing by its id.",
            """{"type":"object","properties":{"listing_id":{"type":"string","description":"The listing GUID"}},"required":["listing_id"]}"""),
        new("check_stock",
            "Check how many kilograms are still available for a listing.",
            """{"type":"object","properties":{"listing_id":{"type":"string","description":"The listing GUID"}},"required":["listing_id"]}"""),
        new("create_reservation",
            "Reserve a given weight in kilograms of a listing for the current user. Only call after the user explicitly confirmed this listing and quantity in the conversation.",
            """{"type":"object","properties":{"listing_id":{"type":"string","description":"The listing GUID"},"quantity_kg":{"type":"number","description":"How many kilograms to reserve"},"user_confirmed":{"type":"boolean","description":"true only if the user explicitly said yes to reserving this exact listing and quantity"}},"required":["listing_id","quantity_kg","user_confirmed"]}"""),
    ];
}
