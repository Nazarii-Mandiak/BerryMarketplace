# 0009. Publish integration events over RabbitMQ; consume them from an out-of-process AI worker

Date: 2026-07-27
Status: Accepted

## Context

Phase 2 of the "AI Engineer Showcase" plan adds an AI enrichment story: when a grower creates a listing, something should be able to look at it — summarize the note, tag the berry type, whatever the enrichment turns out to be — without that work happening synchronously inside the HTTP request that creates the listing. ADR-0001 committed Berry Exchange to a modular monolith: one deployable API process, modules talking through service interfaces, one shared database. That decision was about how the *request/response* marketplace (Listings, Reservations, Accounts) is structured, and nothing here revisits it — the API stays a single deployable, and the three modules still don't reach into each other's entities.

What ADR-0001 didn't need to address is asynchronous, one-to-many notification: telling an interested party "a listing was created" without the API knowing or caring who's listening, and without the AI enrichment worker's dependencies (an LLM client, its own retry/backoff behavior) becoming part of the API's request path or deployable. That's a different axis of the architecture — integration events crossing a process boundary, not domain logic crossing a module boundary — and it's the axis this ADR covers.

## Decision

The API publishes integration events to a RabbitMQ topic exchange named `berry.events` (see `MessagingConventions.Exchange`) and a separate, out-of-process AI worker service subscribes to routing keys it cares about (starting with `listing.created`) to perform enrichment. Event contracts — currently `ListingCreatedEvent`, routing key `listing.created` — live in their own class library, `BerryExchange.Contracts`, referenced by both the API (as publisher) and the worker (as consumer), so the two processes share a compiled schema instead of hand-copied DTOs or a wire format they have to keep in sync by hand.

Inside the API, publishing is reached through an `IEventPublisher` abstraction (`PublishAsync<T>(routingKey, event, ct)`) rather than a direct RabbitMQ client dependency in `ListingsService`. The default implementation registered today is `NullEventPublisher`, a no-op — this task wires the interface and the publish call site (`ListingsService.CreateAsync`, after `SaveChangesAsync`) but not the broker itself; the RabbitMQ-backed implementation is Task 7's work, registered conditionally once `RabbitMq:Host` is configured. Until then, and in any environment without a broker (tests, bare local dev), the marketplace works exactly as it does today — creating a listing just doesn't produce an event.

Publishing is best-effort and happens after the database commit, not inside the same transaction: `CreateAsync` saves the listing first, then tries to publish, and swallows (logs, doesn't rethrow) any publish failure. A broker outage must never turn into a failed listing creation for the seller. The cost of that choice is the classic dual-write gap — a listing can commit while its event is lost (broker down, process crash between the two steps) — which this ADR accepts for now and does not attempt to close.

## Consequences

Creating a listing stays fast and available even if RabbitMQ is down, slow, or not deployed at all, because `NullEventPublisher` and the try/catch around the publish call both fail toward "the marketplace still works." The AI worker can be developed, deployed, restarted, and scaled independently of the API — it's a separate process with its own lifecycle — without the API's `ListingsService` knowing anything about LLMs, prompts, or worker retry policy; it only knows it has an `IEventPublisher`. Adding a second consumer later (e.g., a search-index updater) costs a new queue bound to the same exchange, not a change to the API.

The tradeoff is the dual-write gap described above: at-most-once, best-effort delivery, with no guarantee an event actually reaches the broker. If a future requirement needs at-least-once delivery (e.g., billing-relevant events, or enrichment that must never silently skip a listing), the documented evolution path is the transactional outbox pattern — write the event to an outbox table in the same transaction as the listing, then a separate relay process publishes from the outbox with retry. That's a strictly additive change behind the existing `IEventPublisher` interface; nothing in this ADR forecloses it.

Alternatives considered:
- **Synchronous enrichment inside the request** (call the LLM directly from `ListingsService.CreateAsync`) — rejected. Ties listing creation latency and availability to an external LLM call, and pulls AI-provider dependencies into the core API deployable that ADR-0001 keeps deliberately small.
- **Transactional outbox from day one** — rejected for now. It's the right answer if delivery guarantees are ever required, but it adds a relay process and an outbox table before there's a concrete requirement that needs at-least-once delivery. The `IEventPublisher` abstraction is deliberately shaped so this is a swap-in later, not a redesign.
- **Direct RabbitMQ client call from `ListingsService`** (skip the `IEventPublisher` interface) — rejected. Would make every unit/integration test that exercises `CreateAsync` depend on a broker, and would hard-couple the API to RabbitMQ instead of leaving room for `NullEventPublisher`-style graceful degradation when no broker is configured.
