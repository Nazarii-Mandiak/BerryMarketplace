namespace BerryExchange.Api.Reservations;

public record ReservationResponse(Guid Id, Guid ListingId, Guid BuyerId, int Quantity, string Status, DateTimeOffset ReservedAt)
{
    public static ReservationResponse FromEntity(Reservation r) =>
        new(r.Id, r.ListingId, r.BuyerId, r.Quantity, r.Status.ToString(), r.ReservedAt);
}
