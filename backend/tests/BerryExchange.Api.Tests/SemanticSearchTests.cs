using System.Net.Http.Json;
using System.Text.Json;
using BerryExchange.AiCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BerryExchange.Api.Tests;

public class SemanticSearchTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public SemanticSearchTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Search_ranks_semantically_similar_listing_first()
    {
        var client = _fixture.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Internal:ApiKey"] = "k" }))).CreateClient();

        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"s-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "S" })).EnsureSuccessStatusCode();

        using var embedder = new LocalTextEmbedder();
        var strawberryId = await CreateEnrichedListingAsync(client, embedder, "Strawberry", "Sweet Fields", "very sweet, great for jam");
        await CreateEnrichedListingAsync(client, embedder, "Gooseberry", "Sour Acres", "extremely tart and firm");

        var response = await client.GetAsync("/api/listings/search?q=sweet%20strawberries%20for%20jam");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("semantic", doc.RootElement.GetProperty("mode").GetString());
        var first = doc.RootElement.GetProperty("results")[0];
        Assert.Equal(strawberryId, first.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CreateEnrichedListingAsync(HttpClient client, LocalTextEmbedder embedder,
        string berry, string farm, string note)
    {
        var created = await (await client.PostAsJsonAsync("/api/listings",
            new { BerryType = berry, FarmName = farm, PricePerPint = 5m, QuantityAvailable = 5, Note = note }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/listings/{id}/enrichment")
        { Content = JsonContent.Create(new { Embedding = embedder.Embed($"{berry} from {farm}. {note}"), TastingNotes = (string?)null }) };
        request.Headers.Add("X-Internal-ApiKey", "k");
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
        return id;
    }
}
