namespace BerryExchange.Api.Reservations;

public record ReserveRequest(decimal QuantityKg);

public record ReservationResponse(Guid Id, Guid ListingId, Guid BuyerId, decimal QuantityKg, string Status, DateTimeOffset ReservedAt)
{
    public static ReservationResponse FromEntity(Reservation r) =>
        new(r.Id, r.ListingId, r.BuyerId, r.QuantityKg, r.Status.ToString(), r.ReservedAt);
}

public record ReservationWithListingResponse(
    Guid Id, Guid ListingId, decimal QuantityKg, string Status, DateTimeOffset ReservedAt,
    string BerryType, string FarmName, decimal PricePerKg);
