using System.Net.Http.Json;
using System.Text.Json;

namespace BerryExchange.Api.Tests;

public class AiStatusTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public AiStatusTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Status_reports_disabled_when_no_api_key_is_configured()
    {
        var status = await _fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/ai/status");
        Assert.False(status.GetProperty("enabled").GetBoolean());
    }
}
