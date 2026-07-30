namespace BerryExchange.Api.Reservations;

public enum ReservationStatus { Pending, Completed, Cancelled }

public class Reservation
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Guid BuyerId { get; set; }
    public decimal QuantityKg { get; set; }
    public ReservationStatus Status { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
}
