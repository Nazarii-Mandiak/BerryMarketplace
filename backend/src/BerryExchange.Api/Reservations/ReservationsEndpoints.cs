using BerryExchange.Api.Listings;

namespace BerryExchange.Api.Reservations;

public static class ReservationsEndpoints
{
    public static void MapReservationsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/listings/{listingId:guid}/reservations", async (
            Guid listingId,
            ReserveRequest request,
            HttpContext http,
            ReservationsService reservationsService,
            CancellationToken ct) =>
        {
            var errors = ValidateReserveRequest(request);
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            var buyerId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            var result = await reservationsService.ReserveAsync(listingId, buyerId, request.QuantityKg, ct);
            return result.Outcome switch
            {
                ReserveOutcome.Success => Results.Created(
                    $"/api/reservations/{result.Reservation!.Id}", ReservationResponse.FromEntity(result.Reservation)),
                ReserveOutcome.NotFound => Results.NotFound(),
                ReserveOutcome.OwnListing => Results.BadRequest(new { error = "You cannot reserve your own listing." }),
                _ => Results.Conflict(new { error = "Sold out." }),
            };
        }).RequireAuthorization();

        app.MapGet("/api/reservations/mine", async (
            HttpContext http,
            ReservationsService reservationsService,
            ListingsService listingsService,
            CancellationToken ct) =>
        {
            var buyerId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            var reservations = await reservationsService.GetByBuyerAsync(buyerId, ct);
            var listings = await listingsService.GetByIdsAsync(reservations.Select(r => r.ListingId), ct);
            var listingsById = listings.ToDictionary(l => l.Id);

            var response = reservations.Select(r =>
            {
                var listing = listingsById[r.ListingId];
                return new ReservationWithListingResponse(
                    r.Id, r.ListingId, r.QuantityKg, r.Status.ToString(), r.ReservedAt,
                    listing.BerryType, listing.FarmName, listing.PricePerKg);
            });

            return Results.Ok(response);
        }).RequireAuthorization();
    }

    private static List<string> ValidateReserveRequest(ReserveRequest request)
    {
        var errors = new List<string>();

        if (request.QuantityKg <= 0)
        {
            errors.Add("QuantityKg must be greater than 0.");
        }
        else if (request.QuantityKg > 1000)
        {
            errors.Add("QuantityKg must be 1000 or less.");
        }
        else if (decimal.Round(request.QuantityKg, 2) != request.QuantityKg)
        {
            errors.Add("QuantityKg must have at most 2 decimal places.");
        }

        return errors;
    }
}
