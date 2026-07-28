namespace BerryExchange.Api.Chat;

public class ChatConversation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
