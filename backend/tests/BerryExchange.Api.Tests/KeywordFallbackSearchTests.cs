using System.Net.Http.Json;
using System.Text.Json;

namespace BerryExchange.Api.Tests;

public class KeywordFallbackSearchTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public KeywordFallbackSearchTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Search_falls_back_to_keyword_matching_when_nothing_is_embedded()
    {
        var client = _fixture.CreateClient();
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"k-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "K" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/listings",
            new { BerryType = "Cloudberry", FarmName = "North Bog", PricePerKg = 9m, QuantityAvailableKg = 1m, Note = (string?)null })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/listings/search?q=cloudberry");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("keyword", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal("Cloudberry", doc.RootElement.GetProperty("results")[0].GetProperty("berryType").GetString());
    }
}
