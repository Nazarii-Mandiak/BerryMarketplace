using System.Security.Claims;
using System.Text.Json;
using BerryExchange.Api.Chat.Agent;
using BerryExchange.AiCore;

namespace BerryExchange.Api.Chat;

public record CreateConversationRequest(string? Title);
public record SendChatMessageRequest(string Content);

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/chat").RequireAuthorization();

        group.MapGet("/conversations", async (HttpContext http, ChatService chat, CancellationToken ct) =>
        {
            var conversations = await chat.GetConversationsAsync(GetUserId(http), ct);
            return Results.Ok(conversations.Select(c => new { c.Id, c.Title, c.CreatedAt }));
        });

        group.MapPost("/conversations", async (CreateConversationRequest request, HttpContext http,
            ChatService chat, CancellationToken ct) =>
        {
            var conversation = await chat.CreateConversationAsync(GetUserId(http), request.Title, ct);
            return Results.Created($"/api/chat/conversations/{conversation.Id}",
                new { conversation.Id, conversation.Title, conversation.CreatedAt });
        });

        group.MapGet("/conversations/{conversationId:guid}/messages", async (Guid conversationId,
            HttpContext http, ChatService chat, CancellationToken ct) =>
        {
            var messages = await chat.GetMessagesAsync(conversationId, GetUserId(http), ct);
            return messages is null
                ? Results.NotFound()
                : Results.Ok(messages.Select(m => new { m.Id, m.Role, m.Content, m.CreatedAt }));
        });

        group.MapPost("/conversations/{conversationId:guid}/messages", async (Guid conversationId,
            SendChatMessageRequest request, HttpContext http, ChatService chat, ChatAgent agent,
            IGenerativeAi ai, CancellationToken ct) =>
        {
            if (!ai.IsEnabled)
            {
                return Results.Json(new { errors = new[] { "AI chat is disabled: no Anthropic API key is configured." } },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return Results.BadRequest(new { errors = new[] { "Content is required." } });
            }

            var userId = GetUserId(http);
            if (await chat.GetConversationAsync(conversationId, userId, ct) is null) return Results.NotFound();

            await chat.AppendMessageAsync(conversationId, "user", request.Content.Trim(), ct);
            // Text-only history replay (documented simplification in ADR-0011): tool traffic
            // from earlier turns is not persisted, so it is not replayed.
            var history = (await chat.GetMessagesAsync(conversationId, userId, ct))!
                .Select(m => m.Role == "user"
                    ? (AgentHistoryItem)new AgentUserMessage(m.Content)
                    : new AgentAssistantTurn(m.Content, []))
                .ToList();

            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";

            var assistantParts = new List<string>();
            await foreach (var evt in agent.RunAsync(userId, history, ct))
            {
                object payload = evt switch
                {
                    AgentTextEvent text => new { type = "text", text = text.Text },
                    AgentToolCallEvent tool => new { type = "tool_call", name = tool.Name },
                    _ => new { type = "unknown" },
                };
                if (evt is AgentTextEvent t) assistantParts.Add(t.Text);
                await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }

            if (assistantParts.Count > 0)
            {
                await chat.AppendMessageAsync(conversationId, "assistant", string.Join("\n\n", assistantParts), ct);
            }
            await http.Response.WriteAsync("data: {\"type\":\"done\"}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
            return Results.Empty;
        });
    }

    internal static Guid GetUserId(HttpContext http) =>
        Guid.Parse(http.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
