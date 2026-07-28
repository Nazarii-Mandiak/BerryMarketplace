using BerryExchange.Api.Chat.Agent;

namespace BerryExchange.Api.Tests;

public class ChatAgentLoopTests
{
    private sealed class ScriptedModel : IChatAgentModel
    {
        private readonly Queue<AgentTurn> _turns;
        public List<IReadOnlyList<AgentHistoryItem>> SeenHistories { get; } = [];
        public ScriptedModel(params AgentTurn[] turns) => _turns = new Queue<AgentTurn>(turns);
        public Task<AgentTurn> NextTurnAsync(string systemPrompt, IReadOnlyList<AgentToolDefinition> tools,
            IReadOnlyList<AgentHistoryItem> history, CancellationToken ct)
        {
            SeenHistories.Add([.. history]);
            return Task.FromResult(_turns.Count > 0 ? _turns.Dequeue() : new AgentTurn("done", []));
        }
    }

    private sealed class EchoExecutor : IChatToolExecutor
    {
        public List<AgentToolCall> Executed { get; } = [];
        public Task<AgentToolResult> ExecuteAsync(Guid userId, AgentToolCall call, CancellationToken ct)
        {
            Executed.Add(call);
            return Task.FromResult(new AgentToolResult(call.Id, "ok"));
        }
    }

    [Fact]
    public async Task Loop_executes_tools_then_returns_final_text()
    {
        var model = new ScriptedModel(
            new AgentTurn(null, [new AgentToolCall("t1", "search_listings", """{"query":"sweet"}""")]),
            new AgentTurn("Here is what I found.", []));
        var executor = new EchoExecutor();
        var agent = new ChatAgent(model, executor);

        var events = new List<ChatAgentEvent>();
        await foreach (var evt in agent.RunAsync(Guid.NewGuid(), [new AgentUserMessage("any berries?")], CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Collection(events,
            e => Assert.Equal("search_listings", Assert.IsType<AgentToolCallEvent>(e).Name),
            e => Assert.Equal("Here is what I found.", Assert.IsType<AgentTextEvent>(e).Text));
        var call = Assert.Single(executor.Executed);
        Assert.Equal("t1", call.Id);
        // Second model call must see the assistant turn + tool results appended.
        Assert.Contains(model.SeenHistories[1], h => h is AgentToolResults);
    }

    [Fact]
    public async Task Loop_stops_after_max_iterations_of_tool_calls()
    {
        var endless = new AgentTurn(null, [new AgentToolCall("x", "check_stock", """{"listing_id":"00000000-0000-0000-0000-000000000000"}""")]);
        var model = new ScriptedModel(Enumerable.Repeat(endless, 20).ToArray());
        var agent = new ChatAgent(model, new EchoExecutor());

        var events = new List<ChatAgentEvent>();
        await foreach (var evt in agent.RunAsync(Guid.NewGuid(), [new AgentUserMessage("hi")], CancellationToken.None))
        {
            events.Add(evt);
        }

        var final = Assert.IsType<AgentTextEvent>(events[^1]);
        Assert.Contains("too many steps", final.Text);
    }
}
