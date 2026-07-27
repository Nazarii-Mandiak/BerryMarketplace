namespace BerryExchange.AiCore;

public sealed class DisabledGenerativeAi : IGenerativeAi
{
    public bool IsEnabled => false;
    public Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
        IReadOnlyList<ComparableListing> comparables, CancellationToken ct) =>
        Task.FromResult<ListingCopySuggestion?>(null);
    public Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
