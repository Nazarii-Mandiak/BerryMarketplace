using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BerryExchange.Api.Tests;

public class RateLimitingTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public RateLimitingTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public void The_chat_and_ai_endpoint_groups_carry_the_llm_rate_limiting_policy()
    {
        var dataSource = _fixture.Services.GetRequiredService<EndpointDataSource>();
        var chatEndpoints = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } text && text.StartsWith("/api/chat"))
            .ToList();
        var aiEndpoints = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } text && text.StartsWith("/api/ai"))
            .ToList();

        Assert.NotEmpty(chatEndpoints);
        Assert.NotEmpty(aiEndpoints);
        Assert.All(chatEndpoints, e =>
            Assert.Equal("llm", e.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName));
        Assert.All(aiEndpoints, e =>
            Assert.Equal("llm", e.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName));
    }

    [Fact]
    public async Task Exceeding_the_rate_limit_on_the_chat_group_returns_429()
    {
        var client = _fixture.CreateClient();
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"rl-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "RL" }))
            .EnsureSuccessStatusCode();

        HttpStatusCode? tripped = null;
        for (var i = 0; i < 40 && tripped is null; i++)
        {
            var response = await client.GetAsync("/api/chat/conversations");
            if (response.StatusCode == HttpStatusCode.TooManyRequests) tripped = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, tripped);
    }

    [Fact]
    public async Task Sending_a_chat_message_over_the_length_cap_returns_400()
    {
        var client = _fixture.CreateClient();
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"rl-len-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "RL" }))
            .EnsureSuccessStatusCode();
        var conversation = await (await client.PostAsJsonAsync("/api/chat/conversations", new { title = "long" }))
            .Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var conversationId = conversation.GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/messages",
            new { content = new string('a', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Listing_assist_note_over_the_length_cap_returns_400()
    {
        var client = _fixture.CreateClient();
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"rl-note-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "RL" }))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/ai/listing-assist", new
        {
            BerryType = "Strawberry",
            FarmName = "F",
            PricePerKg = (decimal?)null,
            QuantityAvailableKg = (decimal?)null,
            Note = new string('a', 2001),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
