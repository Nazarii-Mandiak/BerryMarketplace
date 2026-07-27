using System.Security.Claims;

namespace BerryExchange.Api.Chat;

public record CreateConversationRequest(string? Title);

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
    }

    internal static Guid GetUserId(HttpContext http) =>
        Guid.Parse(http.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
