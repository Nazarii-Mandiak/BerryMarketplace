using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace BerryExchange.AiCore;

public sealed class AnthropicGenerativeAi : IGenerativeAi
{
    private const string Model = "claude-opus-5";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AnthropicClient _client;

    public AnthropicGenerativeAi(string apiKey) => _client = new AnthropicClient { ApiKey = apiKey };

    public bool IsEnabled => true;

    public async Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
        IReadOnlyList<ComparableListing> comparables, CancellationToken ct)
    {
        var comparablesText = comparables.Count == 0
            ? "(no comparable listings yet)"
            : string.Join("\n", comparables.Select(c =>
                $"- {c.BerryType} from {c.FarmName}: ${c.PricePerKg}/kg, {c.QuantityAvailableKg} kg available"));

        var prompt = $"""
            A grower is drafting a berry marketplace listing.
            Draft: berry={draft.BerryType}; farm={draft.FarmName}; price=${draft.PricePerKg?.ToString() ?? "unset"}/kg; quantity={draft.QuantityAvailableKg?.ToString() ?? "unset"} kg; note={draft.Note ?? "(none)"}
            Comparable current listings:
            {comparablesText}
            Write an improved listing note (max 80 characters, warm and concrete) and suggest a fair
            price per kilogram grounded in the comparables.
            """;

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 4096,
            System = new List<TextBlockParam>
            {
                new() { Text = "You help berry growers write marketplace listings. Be truthful; never invent qualities the draft does not support." },
            },
            Messages = [new() { Role = Role.User, Content = prompt }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat
                {
                    Schema = new Dictionary<string, JsonElement>
                    {
                        ["type"] = JsonSerializer.SerializeToElement("object"),
                        ["properties"] = JsonSerializer.SerializeToElement(new
                        {
                            improvedDescription = new { type = "string" },
                            suggestedPricePerKg = new { type = "number" },
                            reasoning = new { type = "string" },
                        }),
                        ["required"] = JsonSerializer.SerializeToElement(
                            new[] { "improvedDescription", "suggestedPricePerKg", "reasoning" }),
                        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
                    },
                },
            },
        }, ct);

        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
        if (text is null) return null;
        try
        {
            return JsonSerializer.Deserialize<ListingCopySuggestion>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null; // structured output should prevent this; treat as "no suggestion"
        }
    }

    public async Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 1024,
            System = new List<TextBlockParam>
            {
                new() { Text = "You write a single one-sentence tasting note for a berry listing (max 300 characters). Respond with the note only - no preamble, no quotes." },
            },
            Messages = [new() { Role = Role.User, Content = $"Berry: {berryType}. Farm: {farmName}. Grower note: {note ?? "(none)"}" }],
        }, ct);
        return response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text?.Trim();
    }
}
