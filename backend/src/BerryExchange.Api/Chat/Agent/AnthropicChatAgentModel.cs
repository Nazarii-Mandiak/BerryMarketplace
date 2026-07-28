using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace BerryExchange.Api.Chat.Agent;

public sealed class AnthropicChatAgentModel : IChatAgentModel
{
    private readonly AnthropicClient _client;
    public AnthropicChatAgentModel(string apiKey) => _client = new AnthropicClient { ApiKey = apiKey };

    public async Task<AgentTurn> NextTurnAsync(string systemPrompt, IReadOnlyList<AgentToolDefinition> tools,
        IReadOnlyList<AgentHistoryItem> history, CancellationToken ct)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = "claude-haiku-4-5-20251001",
            MaxTokens = 4096,
            System = new List<TextBlockParam> { new() { Text = systemPrompt } },
            Tools = BuildTools(tools),
            Messages = BuildMessages(history),
        }, ct);

        string? text = null;
        var calls = new List<AgentToolCall>();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? textBlock))
            {
                text = text is null ? textBlock!.Text : $"{text}\n{textBlock!.Text}";
            }
            else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
            {
                calls.Add(new AgentToolCall(toolUse!.ID, toolUse.Name, JsonSerializer.Serialize(toolUse.Input)));
            }
        }
        return new AgentTurn(text, calls);
    }

    private static List<ToolUnion> BuildTools(IReadOnlyList<AgentToolDefinition> definitions)
    {
        var tools = new List<ToolUnion>();
        foreach (var definition in definitions)
        {
            using var schema = JsonDocument.Parse(definition.InputSchemaJson);
            var properties = new Dictionary<string, JsonElement>();
            foreach (var property in schema.RootElement.GetProperty("properties").EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }
            var required = schema.RootElement.TryGetProperty("required", out var requiredElement)
                ? requiredElement.EnumerateArray().Select(e => e.GetString()!).ToList()
                : [];
            tools.Add(new Tool
            {
                Name = definition.Name,
                Description = definition.Description,
                InputSchema = new() { Properties = properties, Required = required },
            });
        }
        return tools;
    }

    private static List<MessageParam> BuildMessages(IReadOnlyList<AgentHistoryItem> history)
    {
        var messages = new List<MessageParam>();
        foreach (var item in history)
        {
            switch (item)
            {
                case AgentUserMessage user:
                    messages.Add(new() { Role = Role.User, Content = user.Text });
                    break;
                case AgentAssistantTurn assistant:
                    var content = new List<ContentBlockParam>();
                    if (!string.IsNullOrEmpty(assistant.Text))
                    {
                        content.Add(new TextBlockParam { Text = assistant.Text });
                    }
                    foreach (var call in assistant.ToolCalls)
                    {
                        content.Add(new ToolUseBlockParam
                        {
                            ID = call.Id,
                            Name = call.Name,
                            Input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.InputJson)!,
                        });
                    }
                    messages.Add(new() { Role = Role.Assistant, Content = content });
                    break;
                case AgentToolResults toolResults:
                    messages.Add(new()
                    {
                        Role = Role.User,
                        Content = toolResults.Results.Select(result => (ContentBlockParam)new ToolResultBlockParam
                        {
                            ToolUseID = result.ToolCallId,
                            Content = result.Content,
                            IsError = result.IsError,
                        }).ToList(),
                    });
                    break;
            }
        }
        return messages;
    }
}
