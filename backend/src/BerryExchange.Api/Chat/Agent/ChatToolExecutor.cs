using System.Text.Json;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;

namespace BerryExchange.Api.Chat.Agent;

public sealed class ChatToolExecutor : IChatToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ListingsService _listings;
    private readonly ReservationsService _reservations;

    public ChatToolExecutor(ListingsService listings, ReservationsService reservations)
    {
        _listings = listings;
        _reservations = reservations;
    }

    public async Task<AgentToolResult> ExecuteAsync(Guid userId, AgentToolCall call, CancellationToken ct)
    {
        try
        {
            using var input = JsonDocument.Parse(call.InputJson);
            var (content, isError) = call.Name switch
            {
                "search_listings" => await SearchAsync(input.RootElement, ct),
                "get_listing" => await GetListingAsync(input.RootElement, ct),
                "check_stock" => await CheckStockAsync(input.RootElement, ct),
                "create_reservation" => await CreateReservationAsync(userId, input.RootElement, ct),
                _ => ($"Unknown tool: {call.Name}", true),
            };
            return new AgentToolResult(call.Id, content, isError);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A real client-disconnect cancellation - propagate it instead of narrating
            // it to the model as a failed tool call.
            throw;
        }
        catch (Exception ex)
        {
            return new AgentToolResult(call.Id, $"Tool failed: {ex.Message}", IsError: true);
        }
    }

    private async Task<(string, bool)> SearchAsync(JsonElement input, CancellationToken ct)
    {
        var query = input.GetProperty("query").GetString() ?? "";
        var (mode, results) = await _listings.SearchAsync(query, limit: 5, ct);
        var payload = results.Select(l => new
        {
            id = l.Id, berryType = l.BerryType, farmName = l.FarmName,
            pricePerKg = l.PricePerKg, quantityAvailableKg = l.QuantityAvailableKg,
            aiTastingNotes = l.AiTastingNotes,
        });
        return (JsonSerializer.Serialize(new { mode, results = payload }, JsonOptions), false);
    }

    private async Task<(string, bool)> GetListingAsync(JsonElement input, CancellationToken ct)
    {
        var listing = await _listings.GetByIdAsync(ParseId(input), ct);
        return listing is null
            ? ("No listing with that id.", true)
            : (JsonSerializer.Serialize(ListingResponse.FromEntity(listing), JsonOptions), false);
    }

    private async Task<(string, bool)> CheckStockAsync(JsonElement input, CancellationToken ct)
    {
        var listing = await _listings.GetByIdAsync(ParseId(input), ct);
        return listing is null
            ? ("No listing with that id.", true)
            : ($"{listing.QuantityAvailableKg} kg available.", false);
    }

    private async Task<(string, bool)> CreateReservationAsync(Guid userId, JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("user_confirmed", out var confirmed) || !confirmed.GetBoolean())
        {
            return ("The user has not confirmed this reservation. Ask them to confirm the exact listing first.", true);
        }
        if (!input.TryGetProperty("quantity_kg", out var quantityElement) || !quantityElement.TryGetDecimal(out var quantityKg) || quantityKg <= 0)
        {
            return ("quantity_kg must be a positive number.", true);
        }
        var result = await _reservations.ReserveAsync(ParseId(input), userId, quantityKg, ct);
        return result.Outcome switch
        {
            ReserveOutcome.Success => ($"Reserved {quantityKg} kg. Reservation id: {result.Reservation!.Id}.", false),
            ReserveOutcome.NotFound => ("No listing with that id.", true),
            ReserveOutcome.OwnListing => ("You cannot reserve your own listing.", true),
            _ => ("This listing is sold out.", true),
        };
    }

    private static Guid ParseId(JsonElement input) => Guid.Parse(input.GetProperty("listing_id").GetString()!);
}
