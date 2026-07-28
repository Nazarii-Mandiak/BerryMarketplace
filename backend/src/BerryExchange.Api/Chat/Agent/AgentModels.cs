namespace BerryExchange.Api.Chat.Agent;

public record AgentToolDefinition(string Name, string Description, string InputSchemaJson);
public abstract record AgentHistoryItem;
public sealed record AgentUserMessage(string Text) : AgentHistoryItem;
public sealed record AgentAssistantTurn(string? Text, IReadOnlyList<AgentToolCall> ToolCalls) : AgentHistoryItem;
public sealed record AgentToolResults(IReadOnlyList<AgentToolResult> Results) : AgentHistoryItem;
public sealed record AgentToolCall(string Id, string Name, string InputJson);
public sealed record AgentToolResult(string ToolCallId, string Content, bool IsError = false);
public sealed record AgentTurn(string? Text, IReadOnlyList<AgentToolCall> ToolCalls);

public interface IChatAgentModel
{
    Task<AgentTurn> NextTurnAsync(string systemPrompt, IReadOnlyList<AgentToolDefinition> tools,
        IReadOnlyList<AgentHistoryItem> history, CancellationToken ct);
}

public interface IChatToolExecutor
{
    Task<AgentToolResult> ExecuteAsync(Guid userId, AgentToolCall call, CancellationToken ct);
}

public sealed class ThrowingChatAgentModel : IChatAgentModel
{
    public Task<AgentTurn> NextTurnAsync(string systemPrompt, IReadOnlyList<AgentToolDefinition> tools,
        IReadOnlyList<AgentHistoryItem> history, CancellationToken ct) =>
        throw new InvalidOperationException("AI chat is disabled: no Anthropic API key configured.");
}
