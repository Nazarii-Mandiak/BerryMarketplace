# Berrow (Berry Exchange)

A berry marketplace with an AI core: growers list fresh berries with a photo,
edit or delete their listings, buyers browse, search semantically, chat with
an agent, and reserve any weight in kilograms — backed by a real API,
database, message broker, and an async AI enrichment pipeline.

## Architecture

An ASP.NET Core modular monolith (`Accounts`, `Listings`, `Reservations`, `Ai`,
`Chat`) sits in front of PostgreSQL with the `pgvector` extension. Creating a
listing publishes a `listing.created` event to RabbitMQ, which a separate AI
worker consumes to generate a tasting note and an embedding asynchronously —
the API and the browsing/reservation flow never block on AI calls. A standalone
MCP server exposes the marketplace as tools for agent clients (e.g. Claude
Code), and a React + TypeScript SPA is the primary UI. Shared event contracts
live in `BerryExchange.Contracts`; shared embeddings/generative-AI abstractions
live in `BerryExchange.AiCore`, used by both the API and the worker.

See `docs/architecture/*.mmd` for C4-style diagrams (context, container,
component-backend, component-frontend, data model, plus sequence diagrams for
the AI enrichment flow and the chat tool-calling loop) and `docs/adr/` for
every architectural decision, including why each of these pieces exists.

The root `vercel.json` deploys the SPA only (`frontend/`) to Vercel as a
static preview — there is no hosted backend behind it, so API-dependent
features won't work there; use the Docker or Kubernetes paths below for a
full working stack.

## Quickstart (Docker)

    export ANTHROPIC_API_KEY=sk-ant-...   # optional; AI features degrade gracefully without it
    export GOOGLE_CLIENT_ID=123...apps.googleusercontent.com   # optional; Google sign-in hides itself without it
    docker compose up --build

    # SPA:      http://localhost:5173
    # API:      http://localhost:5091
    # RabbitMQ: http://localhost:15672 (guest/guest)

This starts Postgres (with `pgvector`), RabbitMQ, the API, the AI worker, and
the frontend. `ANTHROPIC_API_KEY` is optional — without it the stack still
runs in full, AI features just report themselves as disabled (see below).

## AI features

- **Listing-copy assistant** (`POST /api/ai/listing-assist`) — drafts a
  polished title/description from a grower's rough notes before they publish.
- **Async tasting notes + embeddings** — the AI worker listens for
  `listing.created` events and generates a tasting note and a vector embedding
  for each new listing in the background, via `BerryExchange.AiCore`.
- **Semantic search** (`GET /api/listings/search`) — ranks listings by
  embedding similarity when they have one; falls back transparently to a
  keyword (`ILIKE`) search otherwise.
- **Agentic chat** (`POST /api/chat/conversations/{id}/messages`) — a
  streaming (SSE) conversational agent with a tool-calling loop over the
  marketplace (search, listing lookup, availability, reservations).

All of these degrade gracefully when there's no Anthropic key or no RabbitMQ
broker: the app falls back to `DisabledGenerativeAi`, a `NullEventPublisher`,
and keyword-only search, so the app and its full test suite run with zero
external configuration. Check `GET /api/ai/status` to see whether generative
AI is currently enabled.

## MCP server

`BerryExchange.McpServer` exposes the marketplace over stdio as four MCP
tools — `search_listings`, `get_listing`, `check_availability`, and
`create_reservation` — so any MCP-capable agent client can browse and act on
the marketplace directly. Register it with Claude Code:

    claude mcp add berry-exchange -- dotnet run --project <repo>/backend/src/BerryExchange.McpServer

`create_reservation` needs a dedicated marketplace account to act as: set the
`BerryMcp__Email` / `BerryMcp__Password` env vars, or the tool responds with a
"disabled" message instead of erroring.

## Development

Run each service directly (outside Docker), starting with the backend:

    cd backend
    dotnet run --project src/BerryExchange.Api --launch-profile http

    cd frontend
    npm install
    npm run dev

Visit `http://localhost:5173` — the dev server proxies `/api/*` to the backend
at `http://localhost:5091`.

Google sign-in needs the Client ID configured on both sides: the frontend
picks up `VITE_GOOGLE_CLIENT_ID` from `frontend/.env`, but outside Docker the
backend has no equivalent — set `export Authentication__Google__ClientId=<client id>`
before running `dotnet run`, or the button will show up but every sign-in
attempt will 401. Leaving it unset is also fine: the backend just treats
Google sign-in as a safe default of "off".

Run the tests:

    cd backend && dotnet test    # requires Docker running locally (Testcontainers-based Postgres)
    cd frontend && npm test && npm run lint && npm run build

Install the git hooks (enforces ADR + diagram freshness on architecture
commits, see ADR-0006):

    git config core.hooksPath scripts/git-hooks

## Kubernetes

`k8s/` holds plain Kustomize manifests for a local demo on a `kind` cluster:

    docker build -t berry-api:local -f backend/src/BerryExchange.Api/Dockerfile backend
    docker build -t berry-ai-worker:local -f backend/src/BerryExchange.AiWorker/Dockerfile backend
    docker build -t berry-frontend:local frontend
    kind load docker-image berry-api:local berry-ai-worker:local berry-frontend:local
    kubectl apply -k k8s/

Once every pod is `Running`/`Ready`, reach the frontend by port-forwarding its
Service (there's no Ingress in this demo setup):

    kubectl port-forward svc/frontend 8080:80

    # Visit http://localhost:8080

See `docs/adr/0008-containerization-and-ci.md` for the rationale.

## Branching

`development` is the standing integration branch; `feature/*` branches merge
into it per-phase; `main` only receives finished, reviewed work. See
`CONTRIBUTING.md` for the full workflow.
