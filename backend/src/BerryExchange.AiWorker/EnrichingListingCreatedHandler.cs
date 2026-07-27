using BerryExchange.AiCore;
using BerryExchange.Contracts;

namespace BerryExchange.AiWorker;

public sealed class EnrichingListingCreatedHandler : IListingCreatedHandler
{
    private readonly ITextEmbedder _embedder;
    private readonly EnrichmentApiClient _api;
    private readonly IGenerativeAi _ai;
    private readonly ILogger<EnrichingListingCreatedHandler> _logger;

    public EnrichingListingCreatedHandler(ITextEmbedder embedder, EnrichmentApiClient api, IGenerativeAi ai,
        ILogger<EnrichingListingCreatedHandler> logger)
    {
        _embedder = embedder;
        _api = api;
        _ai = ai;
        _logger = logger;
    }

    public async Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct)
    {
        var text = $"{evt.BerryType} from {evt.FarmName}. {evt.Note}".Trim();
        var embedding = _embedder.Embed(text);

        string? tastingNotes = null;
        if (_ai.IsEnabled)
        {
            try
            {
                tastingNotes = await _ai.GenerateTastingNotesAsync(evt.BerryType, evt.FarmName, evt.Note, ct);
            }
            catch (Exception ex)
            {
                // Notes are a nice-to-have; the embedding must land regardless.
                _logger.LogWarning(ex, "Tasting-notes generation failed for {ListingId}", evt.ListingId);
            }
        }

        await _api.SendAsync(evt.ListingId, embedding, tastingNotes, ct);
        _logger.LogInformation("Enriched listing {ListingId} (notes: {HasNotes})", evt.ListingId, tastingNotes is not null);
    }
}
