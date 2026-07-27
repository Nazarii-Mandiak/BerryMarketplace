using System.Net.Http.Json;

namespace BerryExchange.AiWorker;

public sealed class EnrichmentApiClient
{
    private readonly HttpClient _http;
    public EnrichmentApiClient(HttpClient http) => _http = http;

    public async Task SendAsync(Guid listingId, float[] embedding, string? tastingNotes, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"/api/internal/listings/{listingId}/enrichment",
            new { Embedding = embedding, TastingNotes = tastingNotes }, ct);
        response.EnsureSuccessStatusCode();
    }
}
