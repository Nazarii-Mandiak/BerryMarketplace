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
            NullLogger<EnrichingListingCreatedHandler>.Instance);

        var evt = new ListingCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "Blackberry", "Hedge Farm",
            6m, 8, "plump", DateTimeOffset.UtcNow);
        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Contains($"/api/internal/listings/{evt.ListingId}/enrichment", capturing.Request!.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(capturing.Body!);
        Assert.Equal(384, doc.RootElement.GetProperty("embedding").GetArrayLength());
    }
}
