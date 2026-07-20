# 0001. Adopt a modular monolith architecture

Date: 2026-07-20
Status: Accepted

## Context

Berry Exchange is a real MVP marketplace (not a throwaway prototype) built and operated by a solo developer, self-hosted today with a planned migration to Azure later. It's a reservation-only marketplace — no in-app payments. The architecture needs to be simple enough for one person to build, run, and debug, without foreclosing a path to splitting out services if the marketplace grows.

## Decision

Build Berry Exchange as a single ASP.NET Core Web API deployable unit with three internal modules — Listings, Reservations, Accounts — each owning its own endpoints, application service, and domain entities. Modules communicate only through service interfaces, never by reaching into another module's EF Core entities directly. One shared PostgreSQL database (see ADR-0002).

## Consequences

Single deployable and single database transaction scope make the concurrency-critical reservation flow (see `docs/architecture/reservation-flow.mmd`) straightforward, and self-hosting overhead stays low (one container, not several). The tradeoff: no independent scaling or deployment per module today — if one module later needs to scale independently or use a different datastore, the module-boundary discipline established now is what makes that split possible without a rewrite.

Alternatives considered:
- **Microservices** — rejected. Network calls, distributed transactions, and multi-service deploys aren't justified by current traffic or team size (one developer).
- **Serverless (Azure Functions + Cosmos DB)** — rejected. Poor fit for self-hosting today; cold-start and local-dev friction don't pay off at MVP scale.
