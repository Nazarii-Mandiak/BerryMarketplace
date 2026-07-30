namespace BerryExchange.AiCore;

public record ListingDraft(string BerryType, string FarmName, decimal? PricePerKg, decimal? QuantityAvailableKg, string? Note);
public record ComparableListing(string BerryType, string FarmName, decimal PricePerKg, decimal QuantityAvailableKg);
public record ListingCopySuggestion(string ImprovedDescription, decimal SuggestedPricePerKg, string Reasoning);

public interface IGenerativeAi
{
    bool IsEnabled { get; }
    Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
        IReadOnlyList<ComparableListing> comparables, CancellationToken ct);
    Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct);
}
