using System.Net;
using System.Net.Http.Json;
using BerryExchange.AiCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BerryExchange.Api.Tests;

public class InternalEnrichmentTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public InternalEnrichmentTests(ApiTestFixture fixture) => _fixture = fixture;

    private HttpClient CreateClient() => _fixture.WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Internal:ApiKey"] = "test-internal-key" }))).CreateClient();

    [Fact]
    public async Task Enrichment_requires_internal_api_key_and_persists_notes()
    {
        var client = CreateClient();
        var email = $"grower-{Guid.NewGuid():N}@test.dev";
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = email, Password = "Password1!", DisplayName = "G" })).EnsureSuccessStatusCode();
        var created = await (await client.PostAsJsonAsync("/api/listings",
            new { BerryType = "Currant", FarmName = "Brook Farm", PricePerKg = 3.5m, QuantityAvailableKg = 5m, Note = (string?)null }))
            .Content.ReadFromJsonAsync<ListingResponseDto>();

        using var embedder = new LocalTextEmbedder();
        var payload = new { Embedding = embedder.Embed("Currant from Brook Farm"), TastingNotes = "Jewel-bright and tangy." };

        // Without the header: rejected.
        var anonymous = await client.PostAsJsonAsync($"/api/internal/listings/{created!.Id}/enrichment", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // With the header: accepted and visible on the public listing.
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/listings/{created.Id}/enrichment")
        { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Internal-ApiKey", "test-internal-key");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);

        var listing = await client.GetFromJsonAsync<ListingResponseDto>($"/api/listings/{created.Id}");
        Assert.Equal("Jewel-bright and tangy.", listing!.AiTastingNotes);
    }

    public sealed record ListingResponseDto(Guid Id, string BerryType, string? AiTastingNotes);
}
