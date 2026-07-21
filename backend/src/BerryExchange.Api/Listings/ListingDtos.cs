namespace BerryExchange.Api.Listings;

public record CreateListingRequest(string BerryType, string FarmName, decimal PricePerPint, int QuantityAvailable, string? Note);

public record ListingResponse(
    Guid Id, Guid SellerId, string BerryType, string FarmName,
    decimal PricePerPint, int QuantityAvailable, string? Note, DateTimeOffset CreatedAt)
{
    public static ListingResponse FromEntity(Listing l) =>
        new(l.Id, l.SellerId, l.BerryType, l.FarmName, l.PricePerPint, l.QuantityAvailable, l.Note, l.CreatedAt);
}
