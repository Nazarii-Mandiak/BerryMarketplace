using Pgvector;

namespace BerryExchange.Api.Listings;

public class Listing
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public string BerryType { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public decimal PricePerKg { get; set; }
    public decimal QuantityAvailableKg { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Vector? Embedding { get; set; }
    public string? AiTastingNotes { get; set; }
    public string? PhotoContentType { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
