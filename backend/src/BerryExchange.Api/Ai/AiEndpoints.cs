using BerryExchange.AiCore;
using BerryExchange.Api.Listings;

namespace BerryExchange.Api.Ai;

public static class AiEndpoints
{
    public static void MapAiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ai");
        group.MapGet("/status", (IGenerativeAi ai) => Results.Ok(new { enabled = ai.IsEnabled }));

        group.MapPost("/listing-assist", async (ListingDraft draft, IGenerativeAi ai,
            ListingsService listings, CancellationToken ct) =>
        {
            if (!ai.IsEnabled)
            {
                return Results.Json(new { errors = new[] { "AI features are disabled: no Anthropic API key is configured." } },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var comparables = (await listings.GetAllAsync(ct))
                .Where(l => l.QuantityAvailable > 0)
                .OrderByDescending(l => string.Equals(l.BerryType, draft.BerryType, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(l => l.CreatedAt)
                .Take(10)
                .Select(l => new ComparableListing(l.BerryType, l.FarmName, l.PricePerPint, l.QuantityAvailable))
                .ToList();

            var suggestion = await ai.SuggestListingCopyAsync(draft, comparables, ct);
            return suggestion is null
                ? Results.Json(new { errors = new[] { "The assistant could not produce a suggestion. Please try again." } },
                    statusCode: StatusCodes.Status502BadGateway)
                : Results.Ok(suggestion);
        }).RequireAuthorization();
    }
}
