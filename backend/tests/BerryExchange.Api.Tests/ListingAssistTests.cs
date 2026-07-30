using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BerryExchange.AiCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BerryExchange.Api.Tests;

public class ListingAssistTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ListingAssistTests(ApiTestFixture fixture) => _fixture = fixture;

    private sealed class FakeGenerativeAi : IGenerativeAi
    {
        public IReadOnlyList<ComparableListing>? SeenComparables;
        public bool IsEnabled => true;
        public Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
            IReadOnlyList<ComparableListing> comparables, CancellationToken ct)
        {
            SeenComparables = comparables;
            return Task.FromResult<ListingCopySuggestion?>(new("Juicy, jam-ready berries", 6.0m, "Priced with the market"));
        }
        public Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct) =>
            Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Assist_returns_503_when_ai_is_disabled()
    {
        var client = _fixture.CreateClient();
        await RegisterAsync(client);
        var response = await client.PostAsJsonAsync("/api/ai/listing-assist",
            new { BerryType = "Strawberry", FarmName = "F", PricePerKg = (decimal?)null, QuantityAvailableKg = (decimal?)null, Note = (string?)null });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Assist_requires_authentication()
    {
        var response = await _fixture.CreateClient().PostAsJsonAsync("/api/ai/listing-assist",
            new { BerryType = "Strawberry", FarmName = "F" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Assist_returns_suggestion_with_comparables_from_the_market()
    {
        var fake = new FakeGenerativeAi();
        var client = _fixture.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IGenerativeAi>();
            services.AddSingleton<IGenerativeAi>(fake);
        })).CreateClient();
        await RegisterAsync(client);
        (await client.PostAsJsonAsync("/api/listings",
            new { BerryType = "Strawberry", FarmName = "Comparable Farm", PricePerKg = 5.5m, QuantityAvailableKg = 3m, Note = (string?)null }))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/ai/listing-assist",
            new { BerryType = "Strawberry", FarmName = "My Farm", PricePerKg = (decimal?)null, QuantityAvailableKg = 4m, Note = "sweet" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Juicy, jam-ready berries", body.GetProperty("improvedDescription").GetString());
        Assert.NotNull(fake.SeenComparables);
        Assert.Contains(fake.SeenComparables!, c => c.FarmName == "Comparable Farm");
    }

    private static async Task RegisterAsync(HttpClient client) =>
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"a-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "A" }))
        .EnsureSuccessStatusCode();
}
