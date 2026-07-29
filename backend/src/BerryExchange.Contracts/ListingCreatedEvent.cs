namespace BerryExchange.Contracts;

public record ListingCreatedEvent(
    Guid ListingId, Guid SellerId, string BerryType, string FarmName,
    decimal PricePerKg, decimal QuantityAvailableKg, string? Note, DateTimeOffset CreatedAt)
{
    public const string RoutingKey = "listing.created";
}
