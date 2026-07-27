# 0011. Claude-backed generative features via `IGenerativeAi`, with keyless graceful degradation

Date: 2026-07-28
Status: Accepted

## Context

Phase 4 of the "AI Engineer Showcase" plan adds the first *generative* AI feature to Berry Exchange: a listing assistant that suggests improved copy and a fair price for a draft listing, and an auto-generated one-sentence tasting note for a published listing. Everything AI-shaped so far in this codebase — `ITextEmbedder` (ADR-0010) — has been a local, network-free model with no external dependency and no API key. Generative copywriting is a different kind of task: it needs an actual large language model, and the only reasonable source for that is a hosted API call to Claude.

That introduces a constraint the rest of the codebase hasn't had to deal with yet: this repository's tests and CI have never had an `ANTHROPIC_API_KEY`, and won't have one in most environments this project runs in (a developer's laptop without the key configured, the CI runner, this very session). A design that makes `ANTHROPIC_API_KEY` a hard requirement would mean the test suite — and potentially the whole application — breaks the moment someone clones the repo without that secret. ADR-0009 already established the pattern for this shape of problem once: `IEventPublisher` has a real RabbitMQ-backed implementation and a `NullEventPublisher` no-op, selected by whether `RabbitMq:Host` is configured, so the marketplace works with or without a broker. Task 15 needs the same shape for Claude.

## Decision

Generative AI calls go through a new `IGenerativeAi` interface in `BerryExchange.AiCore` (the same shared library that already hosts `ITextEmbedder`, per ADR-0010 — both are "AI capability the API and worker can call," so they live together rather than splitting embeddings and generation into separate projects):

```csharp
public interface IGenerativeAi
{
    bool IsEnabled { get; }
    Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
        IReadOnlyList<ComparableListing> comparables, CancellationToken ct);
    Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct);
}
```

Two implementations, selected in `Program.cs` by whether `Anthropic:ApiKey` (config) or `ANTHROPIC_API_KEY` (environment variable fallback) is present:

- **`AnthropicGenerativeAi`** — the real implementation, calling Claude via the official `Anthropic` C# SDK (NuGet package `Anthropic`). Both methods call `client.Messages.Create` against model `claude-opus-5`. `SuggestListingCopyAsync` uses `output_config.format` (a JSON-schema structured output) rather than a prefill or free-text parsing, so the response is machine-consumed directly — Claude cannot return prose that breaks a downstream `JsonSerializer.Deserialize` call, and there's no assistant-turn prefill to maintain (prefill is unsupported on `claude-opus-5` per the current model migration guidance). `GenerateTastingNotesAsync` is plain text — it's already a single user-facing sentence, so a schema would add ceremony without adding safety.
- **`DisabledGenerativeAi`** — a no-op: `IsEnabled` is `false`, and both methods return `null` (`Task.FromResult<T?>(null)`) without making any network call.

Every generative call site is written to treat a `null` return as "no suggestion available," not as an error — the graceful-degradation behavior is pushed down into the type signature (`Task<ListingCopySuggestion?>`, `Task<string?>`) rather than left as a convention callers have to remember. `GET /api/ai/status` exposes `{ "enabled": bool }` so a caller (frontend, or a curious operator) can check which mode is active without inferring it from behavior.

The listing assistant (Tasks 16–17) runs synchronously inside the API, invoked from a user action (drafting a listing) where the buyer/seller is waiting on a response. Tasting-note generation (Task 18) runs asynchronously in the AI worker, triggered off the same `listing.created` event RabbitMQ already carries (ADR-0009) — nobody is blocked waiting for it, so there's no reason to put it on the request path. Both consume the same `IGenerativeAi` abstraction from `BerryExchange.AiCore`, exactly as `ITextEmbedder` is already shared between the API and the worker.

## Consequences

The marketplace, its test suite, and CI all continue to run with zero Claude-specific configuration — `DisabledGenerativeAi` is registered whenever no key is present, which is every environment this task was implemented and verified in. `GET /api/ai/status` returning `{"enabled": false}` here is not a placeholder pending a follow-up task; it is the correct, fully-tested behavior of the keyless path, and it's what Tasks 16–18 will build their own "no key configured" UI/worker behavior against.

The cost of that safety net is that the only code path exercised by this repository's automated tests is `DisabledGenerativeAi` — `AnthropicGenerativeAi` is compiled and type-checked, but its actual behavior against the live Claude API has not been (and, in most environments this codebase runs in, cannot be) exercised by an automated test. That's an accepted, structural gap for a project whose CI and default dev environment have no API key; the mitigating factor is that `AnthropicGenerativeAi` is a thin, mechanical translation layer (build request → call SDK → unwrap response) with no business logic of its own, so the surface area at risk of an untested bug is small. A real key, when someone has one, exercises it via the manual smoke test described below — this is intentionally a manual, not automated, verification step, so CI's behavior doesn't depend on a secret that may not exist.

Structured output for `SuggestListingCopyAsync` means the suggested price and description arrive already validated against a JSON schema before this code ever parses them — a schema violation surfaces as `stop_reason` behavior on the Claude side rather than as free-text that this code has to defensively regex apart. The tasting-note path deliberately skips that ceremony: it's one plain sentence, structured output would be pure overhead there, and the existing `JsonException`-guarded null-return in `SuggestListingCopyAsync` shows the fallback pattern this codebase uses when a generative response doesn't parse the way it's expected to, in case a future caller needs it.

Because `IGenerativeAi` is an interface behind a `Program.cs`-level conditional registration, adding a different backing implementation later (a different provider, a mock for a specific test, a cost-capped variant) is a new class and a registration change — it doesn't touch `AiEndpoints`, the listing assistant endpoints coming in Tasks 16–17, or the AI worker's tasting-note trigger.

Alternatives considered:

- **Require `ANTHROPIC_API_KEY` unconditionally, fail fast if missing** — rejected. This is the one alternative ADR-0009's `NullEventPublisher` precedent directly argues against: a required external secret that isn't configured in this project's CI or default dev environment would mean the application (and its test suite) simply doesn't start there. The whole point of Task 15 is that the rest of Phase 4 can be built and tested without anyone needing a Claude API key.
- **A feature flag instead of key-presence detection** — rejected as redundant. Key presence already is the signal that matters: there is no scenario where a key is configured but the feature should stay off, or vice versa, so a separate flag would be one more piece of configuration to keep in sync with no independent value.
- **Prefill / free-text parsing for `SuggestListingCopyAsync` instead of structured outputs** — rejected. Both a maintenance and a correctness concern: prefill is unsupported on `claude-opus-5`, and free-text parsing of "please return JSON" prompting is exactly the kind of brittle, regex-shaped code that structured outputs exist to eliminate. Given the response is consumed programmatically (its price populates a suggested-price field, not a chat bubble), constraining the schema server-side is strictly safer than validating after the fact.
- **Put tasting-note generation on the synchronous listing-creation path, alongside the listing assistant** — rejected. It has no interactive user waiting on it the way the listing assistant does, and ADR-0009 already exists specifically to keep listing creation fast and available independent of anything AI-shaped; adding a Claude call to that request path would be the same mistake ADR-0009 was written to avoid, just with a different AI feature.
