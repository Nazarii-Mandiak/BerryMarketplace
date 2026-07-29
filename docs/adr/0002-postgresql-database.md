# 0002. Use PostgreSQL as the primary datastore

Date: 2026-07-20
Status: Accepted

## Context

Berry Exchange needs to self-host today (Docker Compose) and migrate to Azure later without a re-platform. It also needs solid transactional guarantees for one specific correctness-critical operation: atomically decrementing a listing's stock so two buyers can't both reserve the last of it (see `docs/architecture/reservation-flow.mmd`).

## Decision

Use PostgreSQL, accessed via the `Npgsql.EntityFrameworkCore.PostgreSQL` EF Core provider.

## Consequences

Free to self-host via Docker with no licensing cost. First-class EF Core support. Migrates cleanly to **Azure Database for PostgreSQL – Flexible Server** later — a connection-string and infrastructure change, not a data-access rewrite (see ADR-0005). Full ACID transactions support the atomic-decrement pattern the reservation flow depends on.

Alternatives considered:
- **SQL Server** — rejected. Native .NET integration is nice, but self-hosting outside the free Express tier (10 GB limit) introduces licensing cost that PostgreSQL avoids entirely.
- **SQLite** — rejected. Its concurrent-write story is too limited for a real multi-user MVP with simultaneous reservation attempts.
