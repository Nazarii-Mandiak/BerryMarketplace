namespace BerryExchange.AiCore;

public record ListingDraft(string BerryType, string FarmName, decimal? PricePerPint, int? QuantityAvailable, string? Note);
public record ComparableListing(string BerryType, string FarmName, decimal PricePerPint, int QuantityAvailable);
public record ListingCopySuggestion(string ImprovedDescription, decimal SuggestedPricePerPint, string Reasoning);

public interface IGenerativeAi
{
    bool IsEnabled { get; }
    Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
        IReadOnlyList<ComparableListing> comparables, CancellationToken ct);
    Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct);
}
