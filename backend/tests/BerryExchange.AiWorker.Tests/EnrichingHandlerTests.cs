using System.Net;
using System.Text.Json;
using BerryExchange.AiCore;
using BerryExchange.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace BerryExchange.AiWorker.Tests;

public class EnrichingHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Handler_posts_384_dim_embedding_for_the_listing()
    {
        var capturing = new CapturingHandler();
        var http = new HttpClient(capturing) { BaseAddress = new Uri("http://api.test") };
        using var embedder = new LocalTextEmbedder();
        var handler = new EnrichingListingCreatedHandler(embedder, new EnrichmentApiClient(http),
            new DisabledGenerativeAi(), NullLogger<EnrichingListingCreatedHandler>.Instance);

        var evt = new ListingCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "Blackberry", "Hedge Farm",
            6m, 8, "plump", DateTimeOffset.UtcNow);
        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Contains($"/api/internal/listings/{evt.ListingId}/enrichment", capturing.Request!.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(capturing.Body!);
        Assert.Equal(384, doc.RootElement.GetProperty("embedding").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("tastingNotes").ValueKind);
    }

    private sealed class FakeNotesAi : IGenerativeAi
    {
        public bool IsEnabled => true;
        public Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
            IReadOnlyList<ComparableListing> comparables, CancellationToken ct) =>
            Task.FromResult<ListingCopySuggestion?>(null);
        public Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct) =>
            Task.FromResult<string?>("Deep, bramble-sweet flavor.");
    }

    [Fact]
    public async Task Handler_includes_generated_tasting_notes_when_ai_is_enabled()
    {
        var capturing = new CapturingHandler();
        var http = new HttpClient(capturing) { BaseAddress = new Uri("http://api.test") };
        using var embedder = new LocalTextEmbedder();
        var handler = new EnrichingListingCreatedHandler(embedder, new EnrichmentApiClient(http),
            new FakeNotesAi(), NullLogger<EnrichingListingCreatedHandler>.Instance);

        await handler.HandleAsync(new ListingCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "Blackberry",
            "Hedge Farm", 6m, 8, "plump", DateTimeOffset.UtcNow), CancellationToken.None);

        using var doc = JsonDocument.Parse(capturing.Body!);
        Assert.Equal("Deep, bramble-sweet flavor.", doc.RootElement.GetProperty("tastingNotes").GetString());
    }
}
