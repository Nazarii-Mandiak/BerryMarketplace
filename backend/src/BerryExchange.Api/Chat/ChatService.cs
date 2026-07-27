using BerryExchange.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BerryExchange.Api.Chat;

public class ChatService
{
    private readonly BerryExchangeDbContext _db;
    public ChatService(BerryExchangeDbContext db) => _db = db;

    public async Task<ChatConversation> CreateConversationAsync(Guid userId, string? title, CancellationToken ct)
    {
        var conversation = new ChatConversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? "New conversation" : title.Trim()[..Math.Min(title.Trim().Length, 80)],
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.ChatConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    public Task<List<ChatConversation>> GetConversationsAsync(Guid userId, CancellationToken ct) =>
        _db.ChatConversations.Where(c => c.UserId == userId).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public Task<ChatConversation?> GetConversationAsync(Guid id, Guid userId, CancellationToken ct) =>
        _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

    public async Task<List<ChatMessage>?> GetMessagesAsync(Guid conversationId, Guid userId, CancellationToken ct)
    {
        var owned = await _db.ChatConversations.AnyAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (!owned) return null;
        return await _db.ChatMessages.Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt).ToListAsync(ct);
    }

    public async Task<ChatMessage> AppendMessageAsync(Guid conversationId, string role, string content, CancellationToken ct)
    {
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync(ct);
        return message;
    }
}
