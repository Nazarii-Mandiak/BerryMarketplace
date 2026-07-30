namespace BerryExchange.Api.Listings;

public record CreateListingRequest(string BerryType, string FarmName, decimal PricePerKg, decimal QuantityAvailableKg, string? Note);

public record ListingResponse(
    Guid Id, Guid SellerId, string BerryType, string FarmName,
    decimal PricePerKg, decimal QuantityAvailableKg, string? Note, DateTimeOffset CreatedAt,
    string? AiTastingNotes, bool HasPhoto)
{
    public static ListingResponse FromEntity(Listing l) =>
        new(l.Id, l.SellerId, l.BerryType, l.FarmName, l.PricePerKg, l.QuantityAvailableKg,
            l.Note, l.CreatedAt, l.AiTastingNotes, l.PhotoContentType != null);
}
