using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace BerryExchange.McpServer;

[McpServerToolType]
public static class MarketplaceTools
{
    [McpServerTool, Description("Search Berry Exchange listings with a natural-language query. Returns JSON with a search mode and matching listings.")]
    public static Task<string> SearchListings(MarketplaceApiClient api,
        [Description("What to look for, e.g. 'sweet strawberries for jam'")] string query,
        CancellationToken ct) => api.SearchAsync(query, ct);

    [McpServerTool, Description("Get the full details of one listing by its GUID.")]
    public static Task<string> GetListing(MarketplaceApiClient api,
        [Description("The listing GUID")] Guid listingId, CancellationToken ct) =>
        api.GetListingAsync(listingId, ct);

    [McpServerTool, Description("Check how many pints remain for a listing.")]
    public static async Task<string> CheckAvailability(MarketplaceApiClient api,
        [Description("The listing GUID")] Guid listingId, CancellationToken ct)
    {
        var json = await api.GetListingAsync(listingId, ct);
        using var doc = JsonDocument.Parse(json);
        return $"{doc.RootElement.GetProperty("quantityAvailable").GetInt32()} pint(s) available.";
    }

    [McpServerTool, Description("Reserve one pint of a listing for the configured marketplace account. Ask the human for explicit confirmation before calling this.")]
    public static Task<string> CreateReservation(MarketplaceApiClient api,
        [Description("The listing GUID")] Guid listingId, CancellationToken ct) =>
        api.CreateReservationAsync(listingId, ct);
}
