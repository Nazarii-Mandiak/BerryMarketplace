using BerryExchange.Api.Listings;

namespace BerryExchange.Api.Reservations;

public static class ReservationsEndpoints
{
    public static void MapReservationsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/listings/{listingId:guid}/reservations", async (
            Guid listingId,
            HttpContext http,
            ReservationsService reservationsService,
            ListingsService listingsService,
            CancellationToken ct) =>
        {
            var buyerId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            var listing = await listingsService.GetByIdAsync(listingId, ct);
            if (listing is null)
            {
                return Results.NotFound();
            }
            if (listing.SellerId == buyerId)
            {
                return Results.BadRequest(new { error = "You cannot reserve your own listing." });
            }

            var result = await reservationsService.ReserveAsync(listingId, buyerId, ct);
            return result.Succeeded
                ? Results.Created($"/api/reservations/{result.Reservation!.Id}", ReservationResponse.FromEntity(result.Reservation))
                : Results.Conflict(new { error = "Sold out." });
        }).RequireAuthorization();
    }
}
