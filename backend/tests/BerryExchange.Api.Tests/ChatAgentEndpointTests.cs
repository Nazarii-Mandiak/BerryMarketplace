using System.Net.Http.Json;
using System.Text.Json;
using BerryExchange.Api.Chat.Agent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BerryExchange.AiCore;

namespace BerryExchange.Api.Tests;

public class ChatAgentEndpointTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ChatAgentEndpointTests(ApiTestFixture fixture) => _fixture = fixture;

    private sealed class ScriptedModel : IChatAgentModel
    {
        private int _calls;
        public Task<AgentTurn> NextTurnAsync(string systemPrompt, IReadOnlyList<AgentToolDefinition> tools,
            IReadOnlyList<AgentHistoryItem> history, CancellationToken ct)
        {
            _calls++;
            return Task.FromResult(_calls == 1
                ? new AgentTurn(null, [new AgentToolCall("t1", "search_listings", """{"query":"sweet"}""")])
                : new AgentTurn("Here are the sweetest berries on the market.", []));
        }
    }

    private sealed class EnabledAi : IGenerativeAi
    {
        public bool IsEnabled => true;
        public Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft d,
            IReadOnlyList<ComparableListing> c, CancellationToken ct) => Task.FromResult<ListingCopySuggestion?>(null);
        public Task<string?> GenerateTastingNotesAsync(string b, string f, string? n, CancellationToken ct) =>
            Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Sending_a_message_streams_tool_and_text_events_and_persists_the_reply()
    {
        var client = _fixture.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IChatAgentModel>();
            services.AddSingleton<IChatAgentModel>(new ScriptedModel());
            services.RemoveAll<IGenerativeAi>();
            services.AddSingleton<IGenerativeAi>(new EnabledAi());
        })).CreateClient();

        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"chat-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "C" })).EnsureSuccessStatusCode();
        var conversation = await (await client.PostAsJsonAsync("/api/chat/conversations", new { title = "hunt" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var conversationId = conversation.GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/messages",
            new { content = "what's sweet right now?" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"tool_call\"", body);
        Assert.Contains("Here are the sweetest berries on the market.", body);
        Assert.Contains("\"type\":\"done\"", body);

        var messages = await client.GetFromJsonAsync<JsonElement>($"/api/chat/conversations/{conversationId}/messages");
        var roles = messages.EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToList();
        Assert.Equal(["user", "assistant"], roles);
    }
}
