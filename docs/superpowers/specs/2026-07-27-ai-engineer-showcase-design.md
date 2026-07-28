# Berry Exchange — AI Engineer Showcase Enhancement (Design)

Date: 2026-07-27
Status: Approved

## Purpose

Enhance Berry Exchange into an interview showcase aligned with the Conscensia **AI Engineer** vacancy (C#/.NET, EF Core, MSSQL, Angular, RabbitMQ, microservices, GitHub Copilot/AI-assisted workflow, Azure DevOps, Docker/Kubernetes, CI/CD, MCP-backed tooling). The project should read as a complete, complex, well-documented system built with a disciplined AI-assisted workflow.

## Scope decisions (settled with the owner)

| Decision | Choice | Rationale |
|---|---|---|
| Frontend framework | **Keep React** (no Angular rewrite) | Effort goes to AI/infra features; ADR-0003 documents the choice and the interview answer |
| Database | **Keep PostgreSQL** | ADR-0002 documents reasoning; EF Core makes an MSSQL provider swap trivial — a talking point, not a rewrite |
| AI features | **All four**: listing assistant, async enrichment, semantic search, full agentic chat + MCP server | Centerpiece of an AI Engineer showcase |
| LLM provider | **Claude API via official C# SDK** (`Anthropic` NuGet), model `claude-opus-5` | Best quality, production-style integration; requires `ANTHROPIC_API_KEY`; graceful degradation without it |
| Microservice + messaging | **AI enrichment worker** consuming RabbitMQ events | One service demonstrates microservices, RabbitMQ, and async AI pipelines together |
| DevOps artifacts | **Dockerfiles + docker-compose, GitHub Actions CI, k8s manifests** (no Azure DevOps YAML) | Runnable demos plus k8s literacy |
| Repo hygiene | **Untrack `.claude/`, ignore `.claude/` + `.superpowers/`** + standard build artifacts | Clean tree for an interviewer |
| Branching | **`development` branch**; `feature/<topic>` branches off it; merge to `main` only when the plan completes | Requested by owner |

## Target architecture

```
backend/
  src/
    BerryExchange.Api/          existing modular monolith (Accounts, Listings, Reservations)
                                + new modules: Ai (assistant, search, enrichment intake), Chat
    BerryExchange.Contracts/    NEW class library — integration event records
    BerryExchange.AiWorker/     NEW .NET Worker Service — RabbitMQ consumer → enrichment
    BerryExchange.McpServer/    NEW MCP stdio server (official ModelContextProtocol C# SDK)
  tests/
    BerryExchange.Api.Tests/    existing Testcontainers e2e + new module tests
    BerryExchange.AiWorker.Tests/  NEW — unit tests + one Testcontainers-RabbitMQ integration test
frontend/                       existing React SPA + chat widget, AI-assist in Sell, smart search
k8s/                            NEW plain-YAML manifests (kind-testable)
.github/workflows/ci.yml        NEW CI pipeline
docker-compose.yml              NEW full-stack local orchestration
```

Structural choices:

- **Chat lives inside the API** (a `Chat` module). It needs the user's cookie auth and direct access to module services; a separate chat service would force distributed auth for no story gain. The worker is the microservice showcase.
- **The worker writes enrichment back via an internal API endpoint** (API-key protected `POST /api/internal/listings/{id}/enrichment`), not direct DB writes — keeps "services own their data via APIs" honest. Fallback to direct DB write only if this proves obstructive, recorded in the ADR if so.

## Messaging (RabbitMQ)

- API publishes `listing.created` / `listing.updated` integration events to topic exchange `berry.events` after successful commit, using `RabbitMQ.Client`. Event contracts (records) live in `BerryExchange.Contracts`.
- Worker consumes with manual acks, bounded retry, and a dead-letter queue.
- Reliability: consciously ship **publish-after-commit** (best-effort) and document the transactional outbox as the evolution path in the ADR. Publish failures are logged and never fail the user request.

## AI features

### 1. Listing assistant (sync)
`POST /api/ai/listing-assist`: input = grower's draft (berry type, farm, price, quantity, note). The API fetches comparable listings from the DB, calls Claude (`claude-opus-5`) with **structured outputs** (JSON schema: improved description, suggested price, reasoning), validates, and returns. Sell form gets an "Improve with AI" button that fills the form fields (user can still edit). Timeout + friendly 503 when `ANTHROPIC_API_KEY` is absent; the UI hides AI affordances based on `GET /api/ai/status`.

### 2. Async enrichment (worker)
On `listing.created`, the worker:
1. Computes a **384-dim embedding** of the listing text with a local ONNX embedding model (all-MiniLM-L6-v2; exact packaging — SmartComponents.LocalEmbeddings or ONNX Runtime directly — verified at implementation time).
2. Generates short "AI tasting notes" via Claude.
3. POSTs both to the internal enrichment endpoint.

Embeddings are stored in a `vector(384)` column via **pgvector** (`Pgvector.EntityFrameworkCore`); compose/k8s Postgres switches to the `pgvector/pgvector` image. EF migration adds the column + HNSW index. Sequencing note: the embedding step ships in phase 3; the Claude-backed tasting-notes step ships in phase 4, once the shared Claude integration exists.

### 3. Semantic search
`GET /api/listings/search?q=…`: embed the query with the **same shared embedding library** used by the worker (consistency of model between index- and query-time), rank by cosine distance via pgvector, fall back to the existing keyword filter when embeddings are missing or the query is trivial. Frontend search box gains a natural-language "smart search" mode with visible indication of which mode served results.

### 4. Full agentic chat
`Chat` module in the API:
- Entities: `ChatConversation`, `ChatMessage` (per authenticated user), EF-persisted.
- `POST /api/chat/conversations/{id}/messages` streams the assistant response over **SSE**.
- Tool-calling loop via the C# SDK with tools: `search_listings`, `get_listing`, `check_stock`, `create_reservation`.
- `create_reservation` runs as the authenticated user and requires an explicit in-chat confirmation step before executing (the tool first returns a proposal; execution happens only after the user confirms).
- Frontend: floating chat widget, streamed rendering, conversation history.

### 5. MCP server
`BerryExchange.McpServer` — official `ModelContextProtocol` C# SDK, **stdio transport**, exposing `search_listings`, `get_listing`, `check_availability`, `create_reservation`. Talks to the API over HTTP with a configured API key (service account); reservation tool disabled unless the key maps to a user. README documents registration in Claude Desktop / Claude Code.

## DevOps

- **Dockerfiles**: multi-stage for API and worker; frontend built and served by nginx with `/api` proxy to the API container.
- **docker-compose.yml**: `postgres` (pgvector image), `rabbitmq` (management UI), `api`, `ai-worker`, `frontend`. One command runs the marketplace; `ANTHROPIC_API_KEY` passed through from the host env; healthchecks + `depends_on` conditions.
- **GitHub Actions CI** (`.github/workflows/ci.yml`): backend job (`dotnet test`, Testcontainers on hosted runner) + frontend job (`npm ci`, `oxlint`, `vitest`, build). Triggers: PRs and pushes to `development` and `main`. AI-dependent tests use fakes — no API key in CI.
- **K8s manifests** (`k8s/`): deployments + services for api, worker, frontend; postgres and rabbitmq with PVCs; Secret/ConfigMap for connection strings and API key; smoke-tested with `kind`.

## Documentation

- **New ADRs** (MADR-lite, numbered after 0006):
  1. RabbitMQ + AI-worker extraction (amends ADR-0001; publish-after-commit vs outbox trade-off)
  2. Claude API via C# SDK for generative features (incl. graceful degradation)
  3. pgvector + local ONNX embeddings for semantic search
  4. MCP server for marketplace tooling
  5. Containerization + CI (Docker/compose/GitHub Actions/k8s)
  6. Branching strategy (development + feature branches)
- **Diagrams** (`docs/architecture/`): update `container.mmd`, `component-backend.mmd`, `data-model.mmd`; add `ai-enrichment-flow.mmd` and `chat-tool-loop.mmd` sequence diagrams. ADR-0006's pre-commit freshness hook remains enforced.
- **README overhaul**: architecture summary, compose quickstart, AI setup (`ANTHROPIC_API_KEY`), MCP registration, branching strategy, per-service run instructions.

## Housekeeping & branching (first work item)

1. `git rm -r --cached .claude` (files stay on disk).
2. Root `.gitignore`: `.DS_Store`, `.claude/`, `.superpowers/`, `node_modules/`, `dist/`, `bin/`, `obj/`, `.env*`.
3. Create `development` from `main`. All subsequent work on `feature/<topic>` branches cut from `development`, merged back per phase. `development` → `main` only when the full plan is complete.

## Implementation phases

Each phase ends with tests green, ADRs/diagrams updated (hook-enforced), and a merge to `development`.

0. Housekeeping + branching setup
1. Docker/compose + CI baseline (existing app containerized; pipeline green)
2. RabbitMQ eventing + worker skeleton (consume + log)
3. pgvector + shared embedding library + semantic search (worker computes embeddings)
4. Claude integration (`IGenerativeAi` + C# SDK) → listing assistant (API + Sell form UI) + worker tasting notes
5. Agentic chat (backend loop + SSE + persistence, then widget)
6. MCP server
7. K8s manifests
8. Docs polish; merge `development` → `main`

## Testing strategy

- Claude calls sit behind a small internal interface (e.g. `IGenerativeAi`) so tests inject fakes; no API key needed in CI.
- Embedding library unit-tested against known vectors.
- Worker: unit tests with fake broker/LLM + one Testcontainers-RabbitMQ integration test.
- Chat loop: deterministic fake tool-runner tests (tool selection, confirmation gate).
- Existing Testcontainers e2e suite continues to run; new endpoints get e2e coverage where they don't need a live LLM.

## Error handling & degradation

- No `ANTHROPIC_API_KEY`: `GET /api/ai/status` reports disabled; UI hides AI affordances; AI endpoints return 503 with a clear message; core marketplace unaffected.
- RabbitMQ down: API logs publish failure and continues (documented trade-off); worker reconnects with backoff.
- LLM output invalid: structured-output validation rejects; endpoint returns a typed error; UI shows a retry affordance.

## Out of scope

- Angular rewrite, MSSQL migration, Azure DevOps pipelines, real payments/notifications, production deployment. Each exclusion is deliberate and documented for interview discussion.
