using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BerryExchange.Api.Tests;

public class ChatConversationTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ChatConversationTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Conversations_are_per_user_and_listable()
    {
        var client = _fixture.CreateClient();
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"c-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "C" })).EnsureSuccessStatusCode();

        var created = await (await client.PostAsJsonAsync("/api/chat/conversations", new { title = "Berry hunt" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var conversationId = created.GetProperty("id").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/chat/conversations");
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("id").GetGuid() == conversationId);

        var messages = await client.GetFromJsonAsync<JsonElement>($"/api/chat/conversations/{conversationId}/messages");
        Assert.Empty(messages.EnumerateArray());

        // Another user cannot see it.
        var other = _fixture.CreateClient();
        (await other.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"o-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "O" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/chat/conversations/{conversationId}/messages")).StatusCode);
    }
}
