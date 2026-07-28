using System.Runtime.CompilerServices;

namespace BerryExchange.Api.Chat.Agent;

public abstract record ChatAgentEvent;
public sealed record AgentTextEvent(string Text) : ChatAgentEvent;
public sealed record AgentToolCallEvent(string Name) : ChatAgentEvent;

public sealed class ChatAgent
{
    private const int MaxIterations = 8;
    private readonly IChatAgentModel _model;
    private readonly IChatToolExecutor _tools;

    public ChatAgent(IChatAgentModel model, IChatToolExecutor tools)
    {
        _model = model;
        _tools = tools;
    }

    public async IAsyncEnumerable<ChatAgentEvent> RunAsync(Guid userId,
        IReadOnlyList<AgentHistoryItem> history, [EnumeratorCancellation] CancellationToken ct)
    {
        var working = new List<AgentHistoryItem>(history);
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var turn = await _model.NextTurnAsync(ToolCatalog.SystemPrompt, ToolCatalog.Definitions, working, ct);
            working.Add(new AgentAssistantTurn(turn.Text, turn.ToolCalls));

            if (turn.ToolCalls.Count == 0)
            {
                if (!string.IsNullOrEmpty(turn.Text)) yield return new AgentTextEvent(turn.Text);
                yield break;
            }

            // Text alongside tool calls is interim narration - surface it too.
            if (!string.IsNullOrEmpty(turn.Text)) yield return new AgentTextEvent(turn.Text);

            var results = new List<AgentToolResult>();
            foreach (var call in turn.ToolCalls)
            {
                yield return new AgentToolCallEvent(call.Name);
                results.Add(await _tools.ExecuteAsync(userId, call, ct));
            }
            working.Add(new AgentToolResults(results));
        }
        yield return new AgentTextEvent(
            "I stopped because this took too many steps. Please try a more specific request.");
    }
}
