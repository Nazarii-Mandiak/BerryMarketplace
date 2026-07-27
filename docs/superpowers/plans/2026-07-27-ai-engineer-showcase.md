# AI Engineer Showcase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance Berry Exchange with RabbitMQ messaging, an AI enrichment microservice, semantic search (pgvector), a Claude-powered listing assistant, full agentic chat, an MCP server, Docker/compose, GitHub Actions CI, and k8s manifests — per `docs/superpowers/specs/2026-07-27-ai-engineer-showcase-design.md`.

**Architecture:** Keep the ASP.NET Core modular monolith (`BerryExchange.Api`) as the core; add `BerryExchange.Contracts` (integration events), `BerryExchange.AiCore` (shared embeddings + Claude client), `BerryExchange.AiWorker` (RabbitMQ consumer microservice), and `BerryExchange.McpServer` (MCP stdio server). Chat lives inside the API. The worker writes enrichment back through an internal API endpoint.

**Tech Stack:** .NET 10, EF Core + Npgsql + Pgvector, RabbitMQ.Client v7, Anthropic C# SDK (`Anthropic` NuGet), SmartComponents.LocalEmbeddings, ModelContextProtocol C# SDK, React 19 + Vite + vitest, Docker/compose, GitHub Actions, plain-YAML k8s.

## Global Constraints

- Target framework `net10.0`, `Nullable` enabled; follow the existing minimal-API module style (`<Module>/<Module>Endpoints.cs` + `<Module>Service.cs`).
- Claude model ID is exactly `claude-opus-5` — never a date-suffixed variant.
- Every LLM call goes through an interface (`IGenerativeAi` / `IChatAgentModel`); CI and all tests run with **no** `ANTHROPIC_API_KEY`.
- Postgres image is `pgvector/pgvector:pg16` everywhere (compose, test fixture, k8s).
- Embeddings are 384-dim from `SmartComponents.LocalEmbeddings` (prerelease package).
- Branching: each phase works on a `feature/<phase-name>` branch cut from `development`; phase ends with `git checkout development && git merge --no-ff feature/<name> && git push origin development`. `main` is only touched in the final task.
- Pre-commit hook (`scripts/git-hooks/pre-commit`): any commit staging files matching `scripts/git-hooks/architecture-paths.txt` (csproj, Program.cs, Dockerfile*, docker-compose*, appsettings*, Migrations/*) must also stage an ADR (`docs/adr/*.md`) and a diagram (`docs/architecture/*.mmd`). Each phase's *first* architecture-touching commit includes its ADR + diagram; follow-up commits in the same phase that merely re-touch already-documented files use `git commit --no-verify` (explicitly sanctioned by the hook's header comment).
- Use the repo skills `adr-update` and `architecture-diagram-update` when a step says "draft ADR" / "update diagram" — they know the house format (MADR-lite, C4 mermaid).
- No secrets in git: `ANTHROPIC_API_KEY` comes from env; the internal API key defaults are dev-only values in appsettings.Development.json/compose.
- Commit messages: imperative summary line; end body with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Run backend tests from `backend/`: `dotnet test` (requires Docker running, for Testcontainers). Run frontend tests from `frontend/`: `npm test`; lint: `npm run lint`.

---

## Phase 0 — Housekeeping & branching

### Task 1: Clean gitignore, untrack `.claude/`, document branching

**Files:**
- Modify: `.gitignore`
- Create: `docs/adr/0007-development-branching-strategy.md`
- Create: `CONTRIBUTING.md`
- Delete from index (not disk): everything under `.claude/`

**Interfaces:**
- Consumes: nothing.
- Produces: the `feature/*` → `development` → `main` workflow every later task follows.

- [ ] **Step 1: Create the phase branch**

```bash
git checkout development
git checkout -b feature/housekeeping
```

- [ ] **Step 2: Replace `.gitignore`**

```gitignore
# OS
.DS_Store

# AI tooling (personal, not part of the product)
.claude/
.superpowers/

# Node
node_modules/
dist/

# .NET
bin/
obj/
*.user

# Secrets / local env
.env
.env.*
```

- [ ] **Step 3: Untrack `.claude/` (files stay on disk)**

```bash
git rm -r --cached .claude
```

- [ ] **Step 4: Verify nothing else got untracked and `.superpowers/` is now ignored**

Run: `git status --short | head -30` — expect only `D` entries under `.claude/` plus `.gitignore`/new files; `git check-ignore .superpowers/sdd/progress.md` prints the path (meaning: ignored).

- [ ] **Step 5: Write `docs/adr/0007-development-branching-strategy.md`** (use the `adr-update` skill; content to cover)

Decision: introduce a long-lived `development` integration branch; feature branches (`feature/<topic>`) are cut from and merged back into `development` with `--no-ff`; `main` receives only the final reviewed merge from `development`. Context: solo-developer repo previously merging feature branches straight to `main`; the showcase project needs an integration line where multi-phase work accumulates before a single reviewed release to `main`. Consequences: `main` stays demo-ready at all times; slightly more branch ceremony.

- [ ] **Step 6: Write `CONTRIBUTING.md`**

```markdown
# Contributing

## Branching

- `main` — stable, demo-ready. Only receives reviewed merges from `development`.
- `development` — integration branch. All work lands here first.
- `feature/<topic>` — cut from `development`, merged back with `--no-ff` when the
  phase is complete and tests pass.

## Workflow

1. `git checkout development && git checkout -b feature/<topic>`
2. Commit in small steps; keep tests green (`cd backend && dotnet test`, `cd frontend && npm test`).
3. Architecture-relevant commits must include an ADR (`docs/adr/`) and a diagram
   (`docs/architecture/`) — enforced by `scripts/git-hooks/pre-commit`
   (see ADR-0006). Install hooks: `git config core.hooksPath scripts/git-hooks`.
4. Merge: `git checkout development && git merge --no-ff feature/<topic>`, then push.

See `docs/adr/0007-development-branching-strategy.md`.
```

- [ ] **Step 7: Commit and merge the phase**

```bash
git add -A
git commit -m "Clean up gitignore, untrack .claude, adopt development branching (ADR-0007)"
git checkout development
git merge --no-ff feature/housekeeping -m "Merge feature/housekeeping into development"
git push -u origin development
```

(The hook does not fire: no architecture-path files are staged.)

---

## Phase 1 — Docker + CI baseline

### Task 2: API Dockerfile + AutoMigrate switch

**Files:**
- Create: `backend/src/BerryExchange.Api/Dockerfile`
- Create: `backend/.dockerignore`
- Modify: `backend/src/BerryExchange.Api/Program.cs` (startup scope block, lines ~67-70)

**Interfaces:**
- Consumes: existing `BerryExchangeDbContext`.
- Produces: config flag `Database:AutoMigrate` (bool, default false) — compose and k8s set it `true`.

- [ ] **Step 1: Create the phase branch**

```bash
git checkout development && git checkout -b feature/docker-ci
```

- [ ] **Step 2: Create `backend/.dockerignore`**

```
**/bin/
**/obj/
**/*.user
```

- [ ] **Step 3: Create `backend/src/BerryExchange.Api/Dockerfile`** (build context is `backend/`)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/BerryExchange.Api/BerryExchange.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "BerryExchange.Api.dll"]
```

- [ ] **Step 4: Extend the startup scope in `Program.cs`**

Replace the body of the existing `using (var scope = app.Services.CreateScope())` block (keep its explanatory comment) with:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();
    // In containers (compose/k8s) the schema is applied at startup instead of by a
    // developer running `dotnet ef database update`. Off by default so tests and
    // local dev keep their existing behavior.
    if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
    {
        db.Database.Migrate();
    }
}
```

- [ ] **Step 5: Verify build + existing tests**

Run: `docker build -f backend/src/BerryExchange.Api/Dockerfile -t berry-api backend` — expect successful image build.
Run: `cd backend && dotnet test` — expect all existing tests PASS (AutoMigrate defaults false; fixture still calls `MigrateAsync` itself).

- [ ] **Step 6: Do NOT commit yet** — Task 4 commits the phase's architecture files together with ADR-0008 and the diagram (single hook-satisfying commit).

### Task 3: Frontend Dockerfile + nginx proxy

**Files:**
- Create: `frontend/Dockerfile`
- Create: `frontend/nginx.conf`
- Create: `frontend/.dockerignore`

**Interfaces:**
- Produces: frontend image serving the SPA on port 80, proxying `/api/` to host `api:8080` (the compose/k8s service name).

- [ ] **Step 1: Create `frontend/.dockerignore`**

```
node_modules/
dist/
```

- [ ] **Step 2: Create `frontend/nginx.conf`**

```nginx
server {
  listen 80;
  root /usr/share/nginx/html;
  index index.html;

  location /api/ {
    proxy_pass http://api:8080;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
  }

  location / {
    try_files $uri /index.html;
  }
}
```

- [ ] **Step 3: Create `frontend/Dockerfile`**

```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

- [ ] **Step 4: Verify**

Run: `docker build -t berry-frontend frontend` — expect successful build.

- [ ] **Step 5: No commit yet** (Task 4 commits the phase).

### Task 4: docker-compose + ADR-0008 + container diagram

**Files:**
- Create: `docker-compose.yml` (repo root)
- Create: `docs/adr/0008-containerization-and-ci.md`
- Modify: `docs/architecture/container.mmd`

**Interfaces:**
- Produces: compose services named `postgres`, `api`, `frontend` (RabbitMQ + worker are added in Phase 2); DB connection string convention `Host=postgres;Database=berryexchange;Username=berry;Password=berry`.

- [ ] **Step 1: Create `docker-compose.yml`**

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg16
    environment:
      POSTGRES_DB: berryexchange
      POSTGRES_USER: berry
      POSTGRES_PASSWORD: berry
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U berry -d berryexchange"]
      interval: 5s
      timeout: 3s
      retries: 10

  api:
    build:
      context: backend
      dockerfile: src/BerryExchange.Api/Dockerfile
    environment:
      ConnectionStrings__BerryExchangeDb: Host=postgres;Database=berryexchange;Username=berry;Password=berry
      Database__AutoMigrate: "true"
    ports:
      - "5091:8080"
    depends_on:
      postgres:
        condition: service_healthy

  frontend:
    build: frontend
    ports:
      - "5173:80"
    depends_on:
      - api

volumes:
  pgdata:
```

- [ ] **Step 2: Smoke-test the stack**

Run: `docker compose up -d --build`, wait ~20s, then:
`curl -s http://localhost:5091/api/listings` → expect `[]` (empty JSON array);
`curl -s http://localhost:5173 | head -3` → expect HTML.
Then `docker compose down`.

- [ ] **Step 3: Draft `docs/adr/0008-containerization-and-ci.md`** (adr-update skill; cover)

Decision: multi-stage Dockerfiles per deployable + one root docker-compose for the full local stack (Postgres uses the `pgvector/pgvector:pg16` image in anticipation of ADR-0010 semantic search); schema applied on container start via `Database:AutoMigrate`; CI on GitHub Actions (backend `dotnet test` with Testcontainers, frontend lint+test+build). Alternatives: dev-only compose without app images (rejected — no demoable artifact); Azure DevOps pipelines (rejected — repo lives on GitHub; concepts transfer). A "Kubernetes" section will be appended in Phase 7.

- [ ] **Step 4: Update `docs/architecture/container.mmd`** (architecture-diagram-update skill)

Add: a `Docker Compose` boundary containing the existing SPA/API/Postgres containers; note nginx serving the SPA and proxying `/api` to the API container.

- [ ] **Step 5: Commit (hook satisfied: ADR + diagram staged)**

```bash
git add backend/.dockerignore backend/src/BerryExchange.Api/Dockerfile backend/src/BerryExchange.Api/Program.cs \
        frontend/Dockerfile frontend/nginx.conf frontend/.dockerignore docker-compose.yml \
        docs/adr/0008-containerization-and-ci.md docs/architecture/container.mmd
git commit -m "Containerize API and frontend with docker-compose (ADR-0008)"
```

### Task 5: GitHub Actions CI

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: required-check workflow named `CI` with jobs `backend` and `frontend`, triggered on pushes/PRs to `development` and `main`.

- [ ] **Step 1: Create `.github/workflows/ci.yml`**

```yaml
name: CI

on:
  push:
    branches: [development, main]
  pull_request:
    branches: [development, main]

jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Test (Testcontainers uses the runner's Docker daemon)
        run: dotnet test backend/BerryExchange.slnx

  frontend:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: frontend
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: 22
          cache: npm
          cache-dependency-path: frontend/package-lock.json
      - run: npm ci
      - run: npm run lint
      - run: npm test
      - run: npm run build
```

- [ ] **Step 2: Commit, merge the phase, push, verify CI**

```bash
git add .github/workflows/ci.yml
git commit -m "Add GitHub Actions CI for backend and frontend"
git checkout development
git merge --no-ff feature/docker-ci -m "Merge feature/docker-ci into development"
git push origin development
```

Then run: `gh run watch --exit-status` (or `gh run list --branch development --limit 1`) — expect both jobs green. If the backend job fails on `dotnet test backend/BerryExchange.slnx`, fall back to `run: dotnet test` with `working-directory: backend`, commit the fix, and re-verify.

---

## Phase 2 — RabbitMQ eventing + AI worker (branch `feature/rabbitmq-worker`)

### Task 6: Contracts project + event publishing from the API

**Files:**
- Create: `backend/src/BerryExchange.Contracts/BerryExchange.Contracts.csproj`
- Create: `backend/src/BerryExchange.Contracts/MessagingConventions.cs`
- Create: `backend/src/BerryExchange.Contracts/ListingCreatedEvent.cs`
- Create: `backend/src/BerryExchange.Api/Infrastructure/Messaging/IEventPublisher.cs`
- Create: `backend/src/BerryExchange.Api/Infrastructure/Messaging/NullEventPublisher.cs`
- Modify: `backend/src/BerryExchange.Api/Listings/ListingsService.cs`, `backend/src/BerryExchange.Api/Program.cs`, `backend/BerryExchange.slnx`
- Test: `backend/tests/BerryExchange.Api.Tests/ListingEventPublishingTests.cs`
- Create: `docs/adr/0009-rabbitmq-eventing-and-ai-worker.md`; Modify: `docs/architecture/component-backend.mmd`

**Interfaces:**
- Produces: `IEventPublisher.PublishAsync<T>(string routingKey, T @event, CancellationToken ct)`; `ListingCreatedEvent(Guid ListingId, Guid SellerId, string BerryType, string FarmName, decimal PricePerPint, int QuantityAvailable, string? Note, DateTimeOffset CreatedAt)` with `ListingCreatedEvent.RoutingKey == "listing.created"`; `MessagingConventions.Exchange == "berry.events"`.

- [ ] **Step 1: Branch + scaffold the Contracts project**

```bash
git checkout development && git checkout -b feature/rabbitmq-worker
cd backend
dotnet new classlib -o src/BerryExchange.Contracts -n BerryExchange.Contracts
rm src/BerryExchange.Contracts/Class1.cs
dotnet sln BerryExchange.slnx add src/BerryExchange.Contracts/BerryExchange.Contracts.csproj
dotnet add src/BerryExchange.Api/BerryExchange.Api.csproj reference src/BerryExchange.Contracts/BerryExchange.Contracts.csproj
```

- [ ] **Step 2: Write the contracts**

`MessagingConventions.cs`:
```csharp
namespace BerryExchange.Contracts;

public static class MessagingConventions
{
    public const string Exchange = "berry.events";
}
```

`ListingCreatedEvent.cs`:
```csharp
namespace BerryExchange.Contracts;

public record ListingCreatedEvent(
    Guid ListingId, Guid SellerId, string BerryType, string FarmName,
    decimal PricePerPint, int QuantityAvailable, string? Note, DateTimeOffset CreatedAt)
{
    public const string RoutingKey = "listing.created";
}
```

- [ ] **Step 3: Write the failing test**

`ListingEventPublishingTests.cs`:
```csharp
using System.Collections.Concurrent;
using System.Net.Http.Json;
using BerryExchange.Api.Infrastructure.Messaging;
using BerryExchange.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BerryExchange.Api.Tests;

public sealed class RecordingEventPublisher : IEventPublisher
{
    public ConcurrentQueue<(string RoutingKey, object Event)> Published { get; } = new();
    public Task PublishAsync<T>(string routingKey, T @event, CancellationToken ct)
    {
        Published.Enqueue((routingKey, @event!));
        return Task.CompletedTask;
    }
}

public class ListingEventPublishingTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ListingEventPublishingTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Creating_a_listing_publishes_ListingCreatedEvent()
    {
        var recorder = new RecordingEventPublisher();
        var client = _fixture.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(recorder);
        })).CreateClient();

        var email = $"grower-{Guid.NewGuid():N}@test.dev";
        var register = await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = email, Password = "Password1!", DisplayName = "Grower" });
        register.EnsureSuccessStatusCode();

        var create = await client.PostAsJsonAsync("/api/listings",
            new { BerryType = "Strawberry", FarmName = "Sunny Acres", PricePerPint = 6.5m, QuantityAvailable = 10, Note = "sweet" });
        create.EnsureSuccessStatusCode();

        var (routingKey, evt) = Assert.Single(recorder.Published);
        Assert.Equal(ListingCreatedEvent.RoutingKey, routingKey);
        var typed = Assert.IsType<ListingCreatedEvent>(evt);
        Assert.Equal("Strawberry", typed.BerryType);
        Assert.Equal(10, typed.QuantityAvailable);
    }
}
```

- [ ] **Step 4: Run to verify it fails**

Run: `cd backend && dotnet test --filter ListingEventPublishingTests` — expect FAIL (compile error: `IEventPublisher` doesn't exist).

- [ ] **Step 5: Implement publisher abstraction + wire into ListingsService**

`Infrastructure/Messaging/IEventPublisher.cs`:
```csharp
namespace BerryExchange.Api.Infrastructure.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<T>(string routingKey, T @event, CancellationToken ct);
}
```

`Infrastructure/Messaging/NullEventPublisher.cs`:
```csharp
namespace BerryExchange.Api.Infrastructure.Messaging;

// Used when RabbitMq:Host is not configured (tests, bare local dev):
// the marketplace works fully without a broker; enrichment just doesn't happen.
public sealed class NullEventPublisher : IEventPublisher
{
    public Task PublishAsync<T>(string routingKey, T @event, CancellationToken ct) => Task.CompletedTask;
}
```

`ListingsService.cs` — new constructor and publish-after-commit in `CreateAsync` (replace the class header and end of `CreateAsync`):
```csharp
public class ListingsService
{
    private readonly BerryExchangeDbContext _db;
    private readonly IEventPublisher _events;
    private readonly ILogger<ListingsService> _logger;

    public ListingsService(BerryExchangeDbContext db, IEventPublisher events, ILogger<ListingsService> logger)
    {
        _db = db;
        _events = events;
        _logger = logger;
    }
    // ... existing methods unchanged ...

    public async Task<Listing> CreateAsync(Guid sellerId, CreateListingRequest request, CancellationToken ct)
    {
        // ... existing entity construction + SaveChangesAsync unchanged ...

        // Publish-after-commit, best effort (see ADR-0009): a broker outage must never
        // fail the user's request. The transactional-outbox pattern is the documented
        // evolution path if delivery guarantees are ever needed.
        try
        {
            await _events.PublishAsync(ListingCreatedEvent.RoutingKey, new ListingCreatedEvent(
                listing.Id, listing.SellerId, listing.BerryType, listing.FarmName,
                listing.PricePerPint, listing.QuantityAvailable, listing.Note, listing.CreatedAt), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish ListingCreatedEvent for listing {ListingId}", listing.Id);
        }

        return listing;
    }
}
```
Add `using BerryExchange.Api.Infrastructure.Messaging;` and `using BerryExchange.Contracts;`.

`Program.cs` — register before `builder.Build()`:
```csharp
builder.Services.AddSingleton<BerryExchange.Api.Infrastructure.Messaging.IEventPublisher,
    BerryExchange.Api.Infrastructure.Messaging.NullEventPublisher>();
```
(Task 7 makes this conditional on RabbitMq:Host.)

- [ ] **Step 6: Run the full backend suite**

Run: `cd backend && dotnet test` — expect ALL PASS (new test included; existing e2e unaffected because NullEventPublisher is the default).

- [ ] **Step 7: Draft ADR-0009 + update component diagram, then commit**

ADR `docs/adr/0009-rabbitmq-eventing-and-ai-worker.md` (adr-update skill; amends ADR-0001): integration events over a RabbitMQ topic exchange `berry.events`; contracts isolated in `BerryExchange.Contracts`; publish-after-commit best-effort (outbox documented as evolution); an out-of-process AI enrichment worker consumes `listing.created`. Diagram: `component-backend.mmd` gains a `Messaging (IEventPublisher)` component inside the API with an edge to the (external) broker.

```bash
git add backend/src/BerryExchange.Contracts backend/src/BerryExchange.Api backend/BerryExchange.slnx \
        backend/tests/BerryExchange.Api.Tests/ListingEventPublishingTests.cs \
        docs/adr/0009-rabbitmq-eventing-and-ai-worker.md docs/architecture/component-backend.mmd
git commit -m "Publish listing.created integration events via IEventPublisher (ADR-0009)"
```

### Task 7: RabbitMQ publisher implementation + broker in compose

**Files:**
- Create: `backend/src/BerryExchange.Api/Infrastructure/Messaging/RabbitMqEventPublisher.cs`
- Modify: `backend/src/BerryExchange.Api/Program.cs`, `backend/src/BerryExchange.Api/BerryExchange.Api.csproj`, `docker-compose.yml`
- Test: `backend/tests/BerryExchange.Api.Tests/RabbitMqEventPublisherTests.cs`

**Interfaces:**
- Consumes: `IEventPublisher`, `MessagingConventions.Exchange`.
- Produces: config keys `RabbitMq:Host`, `RabbitMq:Port` (default 5672), `RabbitMq:Username`/`RabbitMq:Password` (default `guest`).

- [ ] **Step 1: Add packages**

```bash
cd backend
dotnet add src/BerryExchange.Api/BerryExchange.Api.csproj package RabbitMQ.Client
dotnet add tests/BerryExchange.Api.Tests/BerryExchange.Api.Tests.csproj package Testcontainers.RabbitMq
```

- [ ] **Step 2: Write the failing integration test**

`RabbitMqEventPublisherTests.cs`:
```csharp
using System.Text.Json;
using BerryExchange.Api.Infrastructure.Messaging;
using BerryExchange.Contracts;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace BerryExchange.Api.Tests;

public class RabbitMqEventPublisherTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder().Build();

    public Task InitializeAsync() => _rabbit.StartAsync();
    public Task DisposeAsync() => _rabbit.DisposeAsync().AsTask();

    [Fact]
    public async Task PublishAsync_delivers_json_event_to_topic_exchange()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = _rabbit.Hostname,
            ["RabbitMq:Port"] = _rabbit.GetMappedPublicPort(5672).ToString(),
            ["RabbitMq:Username"] = "rabbitmq",
            ["RabbitMq:Password"] = "rabbitmq",
        }).Build();

        await using var publisher = new RabbitMqEventPublisher(config);

        // Consumer-side setup: bind a fresh queue to the exchange the publisher declares.
        var factory = new ConnectionFactory
        {
            HostName = _rabbit.Hostname,
            Port = _rabbit.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq",
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(MessagingConventions.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        var queue = (await channel.QueueDeclareAsync()).QueueName;
        await channel.QueueBindAsync(queue, MessagingConventions.Exchange, "listing.*");

        var evt = new ListingCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "Blueberry", "Hilltop",
            5.25m, 4, null, DateTimeOffset.UtcNow);
        await publisher.PublishAsync(ListingCreatedEvent.RoutingKey, evt, CancellationToken.None);

        BasicGetResult? delivery = null;
        for (var i = 0; i < 50 && delivery is null; i++)
        {
            delivery = await channel.BasicGetAsync(queue, autoAck: true);
            if (delivery is null) await Task.Delay(100);
        }

        Assert.NotNull(delivery);
        var roundTripped = JsonSerializer.Deserialize<ListingCreatedEvent>(delivery!.Body.ToArray());
        Assert.Equal(evt.ListingId, roundTripped!.ListingId);
        Assert.Equal("Blueberry", roundTripped.BerryType);
    }
}
```
(Note: `RabbitMqBuilder` defaults to username/password `rabbitmq`/`rabbitmq` — if the container rejects auth, read the actual values off `_rabbit.GetConnectionString()` and adjust both the config and the consumer factory.)

- [ ] **Step 3: Run to verify it fails**

Run: `cd backend && dotnet test --filter RabbitMqEventPublisherTests` — expect FAIL (compile: `RabbitMqEventPublisher` missing).

- [ ] **Step 4: Implement `RabbitMqEventPublisher`**

```csharp
using System.Text.Json;
using BerryExchange.Contracts;
using RabbitMQ.Client;

namespace BerryExchange.Api.Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqEventPublisher(IConfiguration config)
    {
        _host = config["RabbitMq:Host"] ?? throw new InvalidOperationException("Missing RabbitMq:Host");
        _port = int.TryParse(config["RabbitMq:Port"], out var p) ? p : 5672;
        _username = config["RabbitMq:Username"] ?? "guest";
        _password = config["RabbitMq:Password"] ?? "guest";
    }

    public async Task PublishAsync<T>(string routingKey, T @event, CancellationToken ct)
    {
        var channel = await GetChannelAsync(ct);
        var body = JsonSerializer.SerializeToUtf8Bytes(@event);
        var props = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
        await channel.BasicPublishAsync(MessagingConventions.Exchange, routingKey,
            mandatory: false, basicProperties: props, body: body, cancellationToken: ct);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true }) return _channel;
            var factory = new ConnectionFactory { HostName = _host, Port = _port, UserName = _username, Password = _password };
            _connection = await factory.CreateConnectionAsync(cancellationToken: ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);
            await _channel.ExchangeDeclareAsync(MessagingConventions.Exchange, ExchangeType.Topic,
                durable: true, autoDelete: false, cancellationToken: ct);
            return _channel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _initLock.Dispose();
    }
}
```
(If the installed `RabbitMQ.Client` major version differs and a member name doesn't compile, fix from the compiler error — the v7 API is async-first as shown.)

- [ ] **Step 5: Make registration conditional in `Program.cs`**

Replace the Task 6 registration with:
```csharp
if (!string.IsNullOrEmpty(builder.Configuration["RabbitMq:Host"]))
{
    builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
}
else
{
    builder.Services.AddSingleton<IEventPublisher, NullEventPublisher>();
}
```
(with `using BerryExchange.Api.Infrastructure.Messaging;` at the top.)

- [ ] **Step 6: Add the broker to `docker-compose.yml`**

Add service + wire the API:
```yaml
  rabbitmq:
    image: rabbitmq:4-management
    ports:
      - "5672:5672"
      - "15672:15672"
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 10s
      timeout: 5s
      retries: 10
```
And under `api.environment`: `RabbitMq__Host: rabbitmq` plus `api.depends_on`: `rabbitmq: { condition: service_healthy }`.

- [ ] **Step 7: Run tests + commit**

Run: `cd backend && dotnet test` — expect ALL PASS.

```bash
git add -A
git commit --no-verify -m "Add RabbitMQ event publisher and broker service (decision recorded in ADR-0009)"
```

### Task 8: AI worker skeleton (consume + log) + Dockerfile + diagrams

**Files:**
- Create: `backend/src/BerryExchange.AiWorker/` (`BerryExchange.AiWorker.csproj`, `Program.cs`, `IListingCreatedHandler.cs`, `LoggingListingCreatedHandler.cs`, `RabbitMqConsumerService.cs`, `Dockerfile`, `appsettings.json`)
- Create: `backend/tests/BerryExchange.AiWorker.Tests/` (`BerryExchange.AiWorker.Tests.csproj`, `ConsumerIntegrationTests.cs`)
- Modify: `backend/BerryExchange.slnx`, `docker-compose.yml`
- Create: `docs/architecture/ai-enrichment-flow.mmd`; Modify: `docs/architecture/container.mmd`, `docs/adr/0009-rabbitmq-eventing-and-ai-worker.md`

**Interfaces:**
- Produces: `IListingCreatedHandler.HandleAsync(ListingCreatedEvent evt, CancellationToken ct)` — Task 12 swaps the logging implementation for the real enrichment one. Queue name `ai-enrichment`, dead-letter exchange `berry.events.dlx`, dead-letter queue `ai-enrichment.dead`.

- [ ] **Step 1: Scaffold**

```bash
cd backend
dotnet new worker -o src/BerryExchange.AiWorker -n BerryExchange.AiWorker
rm src/BerryExchange.AiWorker/Worker.cs
dotnet sln BerryExchange.slnx add src/BerryExchange.AiWorker/BerryExchange.AiWorker.csproj
dotnet add src/BerryExchange.AiWorker/BerryExchange.AiWorker.csproj reference src/BerryExchange.Contracts/BerryExchange.Contracts.csproj
dotnet add src/BerryExchange.AiWorker/BerryExchange.AiWorker.csproj package RabbitMQ.Client
dotnet new xunit -o tests/BerryExchange.AiWorker.Tests -n BerryExchange.AiWorker.Tests
dotnet sln BerryExchange.slnx add tests/BerryExchange.AiWorker.Tests/BerryExchange.AiWorker.Tests.csproj
dotnet add tests/BerryExchange.AiWorker.Tests/BerryExchange.AiWorker.Tests.csproj reference src/BerryExchange.AiWorker/BerryExchange.AiWorker.csproj
dotnet add tests/BerryExchange.AiWorker.Tests/BerryExchange.AiWorker.Tests.csproj package Testcontainers.RabbitMq
```

- [ ] **Step 2: Write the failing integration test**

`ConsumerIntegrationTests.cs`:
```csharp
using System.Text.Json;
using BerryExchange.AiWorker;
using BerryExchange.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace BerryExchange.AiWorker.Tests;

public sealed class RecordingHandler : IListingCreatedHandler
{
    public TaskCompletionSource<ListingCreatedEvent> Received { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct)
    {
        Received.TrySetResult(evt);
        return Task.CompletedTask;
    }
}

public class ConsumerIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder().Build();

    public Task InitializeAsync() => _rabbit.StartAsync();
    public Task DisposeAsync() => _rabbit.DisposeAsync().AsTask();

    [Fact]
    public async Task Consumer_receives_published_listing_created_event()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = _rabbit.Hostname,
            ["RabbitMq:Port"] = _rabbit.GetMappedPublicPort(5672).ToString(),
            ["RabbitMq:Username"] = "rabbitmq",
            ["RabbitMq:Password"] = "rabbitmq",
        }).Build();

        var handler = new RecordingHandler();
        var consumer = new RabbitMqConsumerService(config, handler, NullLogger<RabbitMqConsumerService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await consumer.StartAsync(cts.Token);

        // Publish directly to the exchange the consumer declared.
        var factory = new ConnectionFactory
        {
            HostName = _rabbit.Hostname,
            Port = _rabbit.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq",
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var evt = new ListingCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "Raspberry", "Bramble Row",
            7m, 3, "tart", DateTimeOffset.UtcNow);
        await channel.BasicPublishAsync(MessagingConventions.Exchange, ListingCreatedEvent.RoutingKey,
            mandatory: false,
            basicProperties: new BasicProperties { ContentType = "application/json" },
            body: JsonSerializer.SerializeToUtf8Bytes(evt));

        var received = await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(evt.ListingId, received.ListingId);

        await consumer.StopAsync(CancellationToken.None);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd backend && dotnet test tests/BerryExchange.AiWorker.Tests` — expect FAIL (types missing).

- [ ] **Step 4: Implement the worker**

`IListingCreatedHandler.cs`:
```csharp
using BerryExchange.Contracts;

namespace BerryExchange.AiWorker;

public interface IListingCreatedHandler
{
    Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct);
}
```

`LoggingListingCreatedHandler.cs`:
```csharp
using BerryExchange.Contracts;

namespace BerryExchange.AiWorker;

// Phase-2 placeholder behavior: proves the pipeline end to end.
// Task 12 replaces this registration with the enrichment handler.
public sealed class LoggingListingCreatedHandler : IListingCreatedHandler
{
    private readonly ILogger<LoggingListingCreatedHandler> _logger;
    public LoggingListingCreatedHandler(ILogger<LoggingListingCreatedHandler> logger) => _logger = logger;

    public Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct)
    {
        _logger.LogInformation("Received listing.created: {BerryType} from {FarmName} ({ListingId})",
            evt.BerryType, evt.FarmName, evt.ListingId);
        return Task.CompletedTask;
    }
}
```

`RabbitMqConsumerService.cs`:
```csharp
using System.Text.Json;
using BerryExchange.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BerryExchange.AiWorker;

public sealed class RabbitMqConsumerService : BackgroundService
{
    public const string QueueName = "ai-enrichment";
    public const string DeadLetterExchange = "berry.events.dlx";
    public const string DeadLetterQueue = "ai-enrichment.dead";

    private readonly IConfiguration _config;
    private readonly IListingCreatedHandler _handler;
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConsumerService(IConfiguration config, IListingCreatedHandler handler,
        ILogger<RabbitMqConsumerService> logger)
    {
        _config = config;
        _handler = handler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMq:Host"] ?? "localhost",
            Port = int.TryParse(_config["RabbitMq:Port"], out var p) ? p : 5672,
            UserName = _config["RabbitMq:Username"] ?? "guest",
            Password = _config["RabbitMq:Password"] ?? "guest",
        };

        // Startup retry: in compose/k8s the broker may come up after the worker.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _connection = await factory.CreateConnectionAsync(cancellationToken: stoppingToken);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ not reachable yet; retrying in 3s");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
        if (_connection is null) return;

        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(MessagingConventions.Exchange, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout,
            durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: "",
            cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange },
            cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(QueueName, MessagingConventions.Exchange,
            ListingCreatedEvent.RoutingKey, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var evt = JsonSerializer.Deserialize<ListingCreatedEvent>(ea.Body.Span)
                    ?? throw new JsonException("null event payload");
                await _handler.HandleAsync(evt, stoppingToken);
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process listing.created; dead-lettering");
                // requeue:false routes to the DLQ via x-dead-letter-exchange (single
                // delivery attempt; bounded-retry-by-DLQ documented in ADR-0009).
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            }
        };
        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
```

`Program.cs`:
```csharp
using BerryExchange.AiWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IListingCreatedHandler, LoggingListingCreatedHandler>();
builder.Services.AddHostedService<RabbitMqConsumerService>();

var host = builder.Build();
host.Run();
```

- [ ] **Step 5: Run tests**

Run: `cd backend && dotnet test` — expect ALL PASS.

- [ ] **Step 6: Dockerfile + compose service**

`backend/src/BerryExchange.AiWorker/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/BerryExchange.AiWorker/BerryExchange.AiWorker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "BerryExchange.AiWorker.dll"]
```

`docker-compose.yml` — add:
```yaml
  ai-worker:
    build:
      context: backend
      dockerfile: src/BerryExchange.AiWorker/Dockerfile
    environment:
      RabbitMq__Host: rabbitmq
    depends_on:
      rabbitmq:
        condition: service_healthy
```

- [ ] **Step 7: Diagrams + ADR touch-up, then commit and merge the phase**

Create `docs/architecture/ai-enrichment-flow.mmd` (architecture-diagram-update skill): sequence — Grower → API (`POST /api/listings`) → Postgres commit → publish `listing.created` → RabbitMQ → AiWorker (log for now; enrichment arrives in Phase 3/4). Update `container.mmd`: add RabbitMQ broker + AiWorker containers and their edges. Append one line to ADR-0009 noting the DLQ topology (`ai-enrichment` + `berry.events.dlx`/`ai-enrichment.dead`).

```bash
git add -A
git commit -m "Add AI worker skeleton consuming listing.created via RabbitMQ"
git checkout development
git merge --no-ff feature/rabbitmq-worker -m "Merge feature/rabbitmq-worker into development"
git push origin development
gh run watch --exit-status
```

Smoke check (optional but recommended): `docker compose up -d --build`, create a listing through the UI at `http://localhost:5173`, then `docker compose logs ai-worker | grep "Received listing.created"` — expect a hit. `docker compose down`.

---

## Phase 3 — pgvector + embeddings + semantic search (branch `feature/semantic-search`)

### Task 9: Shared AiCore library with local embeddings

**Files:**
- Create: `backend/src/BerryExchange.AiCore/BerryExchange.AiCore.csproj`, `backend/src/BerryExchange.AiCore/ITextEmbedder.cs`, `backend/src/BerryExchange.AiCore/LocalTextEmbedder.cs`
- Modify: `backend/BerryExchange.slnx`, `backend/src/BerryExchange.Api/BerryExchange.Api.csproj` (project ref)
- Test: `backend/tests/BerryExchange.Api.Tests/EmbeddingTests.cs`
- Create: `docs/adr/0010-pgvector-local-embeddings-semantic-search.md`; Modify: `docs/architecture/component-backend.mmd`

**Interfaces:**
- Produces: `ITextEmbedder { int Dimensions { get; } float[] Embed(string text); }`, `LocalTextEmbedder` (384-dim). Consumed by Tasks 12 (worker) and 13 (search).

- [ ] **Step 1: Branch + scaffold**

```bash
git checkout development && git checkout -b feature/semantic-search
cd backend
dotnet new classlib -o src/BerryExchange.AiCore -n BerryExchange.AiCore
rm src/BerryExchange.AiCore/Class1.cs
dotnet sln BerryExchange.slnx add src/BerryExchange.AiCore/BerryExchange.AiCore.csproj
dotnet add src/BerryExchange.AiCore/BerryExchange.AiCore.csproj package SmartComponents.LocalEmbeddings --prerelease
dotnet add src/BerryExchange.Api/BerryExchange.Api.csproj reference src/BerryExchange.AiCore/BerryExchange.AiCore.csproj
```
(If `SmartComponents.LocalEmbeddings` fails to restore or run on this machine, the fallback is `Microsoft.ML.OnnxRuntime` + the bundled all-MiniLM model — stop and surface the issue rather than silently switching.)

- [ ] **Step 2: Write the failing test**

`EmbeddingTests.cs`:
```csharp
using BerryExchange.AiCore;

namespace BerryExchange.Api.Tests;

public class EmbeddingTests
{
    [Fact]
    public void Embeddings_are_384_dimensional_and_rank_similar_text_closer()
    {
        using var embedder = new LocalTextEmbedder();
        var strawberries = embedder.Embed("sweet ripe strawberries for jam");
        var strawberries2 = embedder.Embed("fresh strawberry pints, very sweet");
        var tractors = embedder.Embed("used diesel tractor parts catalog");

        Assert.Equal(384, embedder.Dimensions);
        Assert.Equal(384, strawberries.Length);
        Assert.True(Cosine(strawberries, strawberries2) > Cosine(strawberries, tractors),
            "similar berry texts should be closer than unrelated text");
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd backend && dotnet test --filter EmbeddingTests` — expect FAIL (types missing).

- [ ] **Step 4: Implement**

`ITextEmbedder.cs`:
```csharp
namespace BerryExchange.AiCore;

public interface ITextEmbedder : IDisposable
{
    int Dimensions { get; }
    float[] Embed(string text);
}
```

`LocalTextEmbedder.cs`:
```csharp
using SmartComponents.LocalEmbeddings;

namespace BerryExchange.AiCore;

// Local ONNX embedding model (no network, no API key). The same class is used by
// the API (query-time) and the AiWorker (index-time) so vectors are comparable.
public sealed class LocalTextEmbedder : ITextEmbedder
{
    private readonly LocalEmbedder _embedder = new();
    public int Dimensions => 384;
    public float[] Embed(string text) => _embedder.Embed(text).Values.ToArray();
    public void Dispose() => _embedder.Dispose();
}
```
(If the package's embedding type exposes `.Values` as `ReadOnlyMemory<float>`, `ToArray()` is correct; fix member names from compiler errors, not guesses.)

- [ ] **Step 5: Run tests**

Run: `cd backend && dotnet test --filter EmbeddingTests` — expect PASS.

- [ ] **Step 6: ADR + diagram + commit**

Draft `docs/adr/0010-pgvector-local-embeddings-semantic-search.md` (adr-update skill): pgvector on the existing Postgres (vs a separate vector DB — rejected: operational overhead) with 384-dim vectors from a local ONNX MiniLM model shared by API and worker (vs a hosted embeddings API — rejected: cost/keys for a feature that runs on every listing; Anthropic offers no embeddings API); HNSW cosine index; query-time embedding in the API, index-time in the worker; keyword fallback. Update `component-backend.mmd`: add `AiCore (embeddings)` shared library node.

```bash
git add -A
git commit -m "Add AiCore shared library with local ONNX text embeddings (ADR-0010)"
```

### Task 10: pgvector EF Core setup + enrichment columns

**Files:**
- Modify: `backend/src/BerryExchange.Api/BerryExchange.Api.csproj`, `backend/src/BerryExchange.Api/Program.cs`, `backend/src/BerryExchange.Api/Listings/Listing.cs`, `backend/src/BerryExchange.Api/Listings/ListingDtos.cs`, `backend/src/BerryExchange.Api/Infrastructure/BerryExchangeDbContext.cs`, `backend/tests/BerryExchange.Api.Tests/ApiTestFixture.cs`
- Create: `backend/src/BerryExchange.Api/Infrastructure/Migrations/<timestamp>_AddListingEnrichment.cs` (generated)
- Test: `backend/tests/BerryExchange.Api.Tests/ListingEmbeddingPersistenceTests.cs`
- Modify: `docs/architecture/data-model.mmd`, `docs/adr/0010-pgvector-local-embeddings-semantic-search.md`

**Interfaces:**
- Produces: `Listing.Embedding` (`Pgvector.Vector?`, `vector(384)`), `Listing.AiTastingNotes` (`string?`, max 300); `ListingResponse` gains `string? AiTastingNotes`.

- [ ] **Step 1: Packages + fixture image**

```bash
cd backend
dotnet add src/BerryExchange.Api/BerryExchange.Api.csproj package Pgvector.EntityFrameworkCore
```
In `ApiTestFixture.cs` change the container line to:
```csharp
private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
```
(rest unchanged).

- [ ] **Step 2: Write the failing persistence test**

`ListingEmbeddingPersistenceTests.cs`:
```csharp
using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Listings;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;

namespace BerryExchange.Api.Tests;

public class ListingEmbeddingPersistenceTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ListingEmbeddingPersistenceTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Embedding_and_tasting_notes_round_trip_through_postgres()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();

        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = await SeedUserAsync(db),
            BerryType = "Gooseberry",
            FarmName = "Vector Farm",
            PricePerPint = 4m,
            QuantityAvailable = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            Embedding = new Vector(Enumerable.Repeat(0.1f, 384).ToArray()),
            AiTastingNotes = "Bright and tart."
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Listings.FindAsync(listing.Id);
        Assert.NotNull(loaded!.Embedding);
        Assert.Equal(384, loaded.Embedding!.ToArray().Length);
        Assert.Equal("Bright and tart.", loaded.AiTastingNotes);
    }

    private static async Task<Guid> SeedUserAsync(BerryExchangeDbContext db)
    {
        var user = new BerryExchange.Api.Accounts.ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"seed-{Guid.NewGuid():N}@test.dev",
            Email = $"seed-{Guid.NewGuid():N}@test.dev",
            DisplayName = "Seed"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd backend && dotnet test --filter ListingEmbeddingPersistenceTests` — expect FAIL (no `Embedding` member).

- [ ] **Step 4: Implement model + context + migration**

`Listing.cs` — add:
```csharp
using Pgvector;
// ...
public Vector? Embedding { get; set; }
public string? AiTastingNotes { get; set; }
```

`ListingDtos.cs` — extend the response record:
```csharp
public record ListingResponse(
    Guid Id, Guid SellerId, string BerryType, string FarmName,
    decimal PricePerPint, int QuantityAvailable, string? Note, DateTimeOffset CreatedAt,
    string? AiTastingNotes)
{
    public static ListingResponse FromEntity(Listing l) =>
        new(l.Id, l.SellerId, l.BerryType, l.FarmName, l.PricePerPint, l.QuantityAvailable,
            l.Note, l.CreatedAt, l.AiTastingNotes);
}
```

`BerryExchangeDbContext.cs` — in `OnModelCreating`, before the entity blocks add `builder.HasPostgresExtension("vector");` and inside the `Listing` block add:
```csharp
entity.Property(l => l.AiTastingNotes).HasMaxLength(300);
entity.Property(l => l.Embedding).HasColumnType("vector(384)");
entity.HasIndex(l => l.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
```

`Program.cs` — change the provider call to enable pgvector mapping:
```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseVector());
```
(add `using Pgvector.EntityFrameworkCore;`)

Generate the migration:
```bash
cd backend
dotnet tool install --global dotnet-ef 2>/dev/null || true
dotnet ef migrations add AddListingEnrichment --project src/BerryExchange.Api
```

- [ ] **Step 5: Run the full suite**

Run: `cd backend && dotnet test` — expect ALL PASS (fixture now runs pgvector image; migration applies the extension + columns + hnsw index).

- [ ] **Step 6: Diagram + ADR touch + commit**

Update `data-model.mmd`: `Listing` gains `Embedding vector(384)` and `AiTastingNotes`. Append a line to ADR-0010 noting the migration name and HNSW index.

```bash
git add -A
git commit -m "Add pgvector embedding and AI tasting notes columns to Listing"
```

### Task 11: Internal enrichment endpoint

**Files:**
- Create: `backend/src/BerryExchange.Api/Ai/InternalEnrichmentEndpoints.cs`
- Modify: `backend/src/BerryExchange.Api/Program.cs`, `backend/src/BerryExchange.Api/appsettings.Development.json`, `docker-compose.yml`
- Test: `backend/tests/BerryExchange.Api.Tests/InternalEnrichmentTests.cs`

**Interfaces:**
- Produces: `POST /api/internal/listings/{id}/enrichment` accepting `ListingEnrichmentRequest(float[] Embedding, string? TastingNotes)`, guarded by header `X-Internal-ApiKey` matching config `Internal:ApiKey`. Returns 204/401/404/400. Consumed by Task 12 (worker).

- [ ] **Step 1: Write the failing test**

`InternalEnrichmentTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using BerryExchange.AiCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BerryExchange.Api.Tests;

public class InternalEnrichmentTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public InternalEnrichmentTests(ApiTestFixture fixture) => _fixture = fixture;

    private HttpClient CreateClient() => _fixture.WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Internal:ApiKey"] = "test-internal-key" }))).CreateClient();

    [Fact]
    public async Task Enrichment_requires_internal_api_key_and_persists_notes()
    {
        var client = CreateClient();
        var email = $"grower-{Guid.NewGuid():N}@test.dev";
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = email, Password = "Password1!", DisplayName = "G" })).EnsureSuccessStatusCode();
        var created = await (await client.PostAsJsonAsync("/api/listings",
            new { BerryType = "Currant", FarmName = "Brook Farm", PricePerPint = 3.5m, QuantityAvailable = 5, Note = (string?)null }))
            .Content.ReadFromJsonAsync<ListingResponseDto>();

        using var embedder = new LocalTextEmbedder();
        var payload = new { Embedding = embedder.Embed("Currant from Brook Farm"), TastingNotes = "Jewel-bright and tangy." };

        // Without the header: rejected.
        var anonymous = await client.PostAsJsonAsync($"/api/internal/listings/{created!.Id}/enrichment", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // With the header: accepted and visible on the public listing.
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/listings/{created.Id}/enrichment")
        { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Internal-ApiKey", "test-internal-key");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);

        var listing = await client.GetFromJsonAsync<ListingResponseDto>($"/api/listings/{created.Id}");
        Assert.Equal("Jewel-bright and tangy.", listing!.AiTastingNotes);
    }

    public sealed record ListingResponseDto(Guid Id, string BerryType, string? AiTastingNotes);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend && dotnet test --filter InternalEnrichmentTests` — expect FAIL (404: endpoint missing).

- [ ] **Step 3: Implement the endpoint**

`Ai/InternalEnrichmentEndpoints.cs`:
```csharp
using BerryExchange.Api.Infrastructure;
using Pgvector;

namespace BerryExchange.Api.Ai;

public record ListingEnrichmentRequest(float[] Embedding, string? TastingNotes);

public static class InternalEnrichmentEndpoints
{
    public static void MapInternalEnrichmentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/internal/listings/{id:guid}/enrichment",
            async (Guid id, ListingEnrichmentRequest request, HttpContext http,
                   BerryExchangeDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            // Service-to-service auth: shared key, never the user cookie. An unset
            // Internal:ApiKey disables the endpoint entirely (fail closed).
            var expectedKey = config["Internal:ApiKey"];
            if (string.IsNullOrEmpty(expectedKey) ||
                !string.Equals(http.Request.Headers["X-Internal-ApiKey"], expectedKey, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }

            if (request.Embedding.Length != 384)
            {
                return Results.BadRequest(new { errors = new[] { "Embedding must have 384 dimensions." } });
            }

            var listing = await db.Listings.FindAsync([id], ct);
            if (listing is null) return Results.NotFound();

            listing.Embedding = new Vector(request.Embedding);
            listing.AiTastingNotes = request.TastingNotes is { Length: > 300 } notes ? notes[..300] : request.TastingNotes;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
```

Wire in `Program.cs` next to the other `Map*` calls: `app.MapInternalEnrichmentEndpoints();` (with `using BerryExchange.Api.Ai;`). Add to `appsettings.Development.json`: `"Internal": { "ApiKey": "dev-internal-key" }`. In `docker-compose.yml` add `Internal__ApiKey: dev-internal-key` under `api.environment`.

- [ ] **Step 4: Run tests, commit**

Run: `cd backend && dotnet test` — expect ALL PASS.

```bash
git add -A
git commit --no-verify -m "Add internal enrichment endpoint for worker write-back (decision in ADR-0010)"
```

### Task 12: Worker computes embeddings and calls the enrichment endpoint

**Files:**
- Create: `backend/src/BerryExchange.AiWorker/EnrichmentApiClient.cs`, `backend/src/BerryExchange.AiWorker/EnrichingListingCreatedHandler.cs`
- Modify: `backend/src/BerryExchange.AiWorker/BerryExchange.AiWorker.csproj` (ref AiCore), `backend/src/BerryExchange.AiWorker/Program.cs`, `docker-compose.yml`
- Test: `backend/tests/BerryExchange.AiWorker.Tests/EnrichingHandlerTests.cs`

**Interfaces:**
- Consumes: `IListingCreatedHandler` (Task 8), `ITextEmbedder` (Task 9), enrichment endpoint (Task 11).
- Produces: `EnrichmentApiClient.SendAsync(Guid listingId, float[] embedding, string? tastingNotes, CancellationToken ct)`; worker config keys `Api:BaseUrl`, `Internal:ApiKey`. Task 18 extends the handler with tasting notes.

- [ ] **Step 1: Add the reference**

```bash
cd backend
dotnet add src/BerryExchange.AiWorker/BerryExchange.AiWorker.csproj reference src/BerryExchange.AiCore/BerryExchange.AiCore.csproj
```

- [ ] **Step 2: Write the failing test**

`EnrichingHandlerTests.cs`:
```csharp
using System.Net;
using System.Text.Json;
using BerryExchange.AiCore;
using BerryExchange.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace BerryExchange.AiWorker.Tests;

public class EnrichingHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Handler_posts_384_dim_embedding_for_the_listing()
    {
        var capturing = new CapturingHandler();
        var http = new HttpClient(capturing) { BaseAddress = new Uri("http://api.test") };
        using var embedder = new LocalTextEmbedder();
        var handler = new EnrichingListingCreatedHandler(embedder, new EnrichmentApiClient(http),
            NullLogger<EnrichingListingCreatedHandler>.Instance);

        var evt = new ListingCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "Blackberry", "Hedge Farm",
            6m, 8, "plump", DateTimeOffset.UtcNow);
        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Contains($"/api/internal/listings/{evt.ListingId}/enrichment", capturing.Request!.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(capturing.Body!);
        Assert.Equal(384, doc.RootElement.GetProperty("embedding").GetArrayLength());
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd backend && dotnet test tests/BerryExchange.AiWorker.Tests` — expect FAIL (types missing).

- [ ] **Step 4: Implement**

`EnrichmentApiClient.cs`:
```csharp
using System.Net.Http.Json;

namespace BerryExchange.AiWorker;

public sealed class EnrichmentApiClient
{
    private readonly HttpClient _http;
    public EnrichmentApiClient(HttpClient http) => _http = http;

    public async Task SendAsync(Guid listingId, float[] embedding, string? tastingNotes, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"/api/internal/listings/{listingId}/enrichment",
            new { Embedding = embedding, TastingNotes = tastingNotes }, ct);
        response.EnsureSuccessStatusCode();
    }
}
```

`EnrichingListingCreatedHandler.cs`:
```csharp
using BerryExchange.AiCore;
using BerryExchange.Contracts;

namespace BerryExchange.AiWorker;

public sealed class EnrichingListingCreatedHandler : IListingCreatedHandler
{
    private readonly ITextEmbedder _embedder;
    private readonly EnrichmentApiClient _api;
    private readonly ILogger<EnrichingListingCreatedHandler> _logger;

    public EnrichingListingCreatedHandler(ITextEmbedder embedder, EnrichmentApiClient api,
        ILogger<EnrichingListingCreatedHandler> logger)
    {
        _embedder = embedder;
        _api = api;
        _logger = logger;
    }

    public async Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct)
    {
        var text = $"{evt.BerryType} from {evt.FarmName}. {evt.Note}".Trim();
        var embedding = _embedder.Embed(text);
        await _api.SendAsync(evt.ListingId, embedding, tastingNotes: null, ct); // notes: Task 18
        _logger.LogInformation("Enriched listing {ListingId}", evt.ListingId);
    }
}
```

`Program.cs` (worker) — replace the handler registration:
```csharp
builder.Services.AddSingleton<ITextEmbedder, LocalTextEmbedder>();
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var http = new HttpClient { BaseAddress = new Uri(config["Api:BaseUrl"] ?? "http://localhost:5091") };
    http.DefaultRequestHeaders.Add("X-Internal-ApiKey", config["Internal:ApiKey"] ?? "");
    return new EnrichmentApiClient(http);
});
builder.Services.AddSingleton<IListingCreatedHandler, EnrichingListingCreatedHandler>();
```
(add `using BerryExchange.AiCore;`; `LoggingListingCreatedHandler` can be deleted along with any reference to it).

`docker-compose.yml` — under `ai-worker.environment` add:
```yaml
      Api__BaseUrl: http://api:8080
      Internal__ApiKey: dev-internal-key
```

- [ ] **Step 5: Run tests, commit**

Run: `cd backend && dotnet test` — expect ALL PASS.

```bash
git add -A
git commit --no-verify -m "Enrich listings asynchronously: worker embeds text and writes back via internal API"
```

### Task 13: Semantic search endpoint

**Files:**
- Modify: `backend/src/BerryExchange.Api/Listings/ListingsService.cs`, `backend/src/BerryExchange.Api/Listings/ListingsEndpoints.cs`, `backend/src/BerryExchange.Api/Program.cs`
- Test: `backend/tests/BerryExchange.Api.Tests/SemanticSearchTests.cs`, `backend/tests/BerryExchange.Api.Tests/KeywordFallbackSearchTests.cs`

**Interfaces:**
- Produces: `GET /api/listings/search?q=<text>&limit=<n>` → `{ "mode": "semantic"|"keyword", "results": ListingResponse[] }`; `ListingsService.SearchAsync(string query, int limit, CancellationToken ct)` returning `(string Mode, List<Listing> Results)`.

- [ ] **Step 1: Write the failing tests**

`SemanticSearchTests.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using BerryExchange.AiCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BerryExchange.Api.Tests;

public class SemanticSearchTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public SemanticSearchTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Search_ranks_semantically_similar_listing_first()
    {
        var client = _fixture.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Internal:ApiKey"] = "k" }))).CreateClient();

        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"s-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "S" })).EnsureSuccessStatusCode();

        using var embedder = new LocalTextEmbedder();
        var strawberryId = await CreateEnrichedListingAsync(client, embedder, "Strawberry", "Sweet Fields", "very sweet, great for jam");
        await CreateEnrichedListingAsync(client, embedder, "Gooseberry", "Sour Acres", "extremely tart and firm");

        var response = await client.GetAsync("/api/listings/search?q=sweet%20strawberries%20for%20jam");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("semantic", doc.RootElement.GetProperty("mode").GetString());
        var first = doc.RootElement.GetProperty("results")[0];
        Assert.Equal(strawberryId, first.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CreateEnrichedListingAsync(HttpClient client, LocalTextEmbedder embedder,
        string berry, string farm, string note)
    {
        var created = await (await client.PostAsJsonAsync("/api/listings",
            new { BerryType = berry, FarmName = farm, PricePerPint = 5m, QuantityAvailable = 5, Note = note }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/listings/{id}/enrichment")
        { Content = JsonContent.Create(new { Embedding = embedder.Embed($"{berry} from {farm}. {note}"), TastingNotes = (string?)null }) };
        request.Headers.Add("X-Internal-ApiKey", "k");
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
        return id;
    }
}
```

`KeywordFallbackSearchTests.cs` (separate class ⇒ separate fixture ⇒ clean database with zero embeddings):
```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace BerryExchange.Api.Tests;

public class KeywordFallbackSearchTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public KeywordFallbackSearchTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Search_falls_back_to_keyword_matching_when_nothing_is_embedded()
    {
        var client = _fixture.CreateClient();
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"k-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "K" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/listings",
            new { BerryType = "Cloudberry", FarmName = "North Bog", PricePerPint = 9m, QuantityAvailable = 1, Note = (string?)null })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/listings/search?q=cloudberry");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("keyword", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal("Cloudberry", doc.RootElement.GetProperty("results")[0].GetProperty("berryType").GetString());
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd backend && dotnet test --filter "SemanticSearchTests|KeywordFallbackSearchTests"` — expect FAIL (404).

- [ ] **Step 3: Implement**

`ListingsService.cs` — inject `ITextEmbedder embedder` as a fourth constructor parameter (store as `_embedder`), add usings `BerryExchange.AiCore;`, `Pgvector;`, `Pgvector.EntityFrameworkCore;`, and add:
```csharp
public async Task<(string Mode, List<Listing> Results)> SearchAsync(string query, int limit, CancellationToken ct)
{
    var anyEmbedded = await _db.Listings.AnyAsync(l => l.Embedding != null, ct);
    if (anyEmbedded)
    {
        var queryVector = new Vector(_embedder.Embed(query));
        var results = await _db.Listings
            .Where(l => l.Embedding != null)
            .OrderBy(l => l.Embedding!.CosineDistance(queryVector))
            .Take(limit)
            .ToListAsync(ct);
        return ("semantic", results);
    }

    var pattern = $"%{query}%";
    var keyword = await _db.Listings
        .Where(l => EF.Functions.ILike(l.BerryType, pattern)
                 || EF.Functions.ILike(l.FarmName, pattern)
                 || (l.Note != null && EF.Functions.ILike(l.Note, pattern)))
        .OrderByDescending(l => l.CreatedAt)
        .Take(limit)
        .ToListAsync(ct);
    return ("keyword", keyword);
}
```

`ListingsEndpoints.cs` — add before the `/{id:guid}` route:
```csharp
group.MapGet("/search", async (string? q, int? limit, ListingsService service, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { errors = new[] { "q is required." } });
    }
    var (mode, results) = await service.SearchAsync(q.Trim(), Math.Clamp(limit ?? 10, 1, 50), ct);
    return Results.Ok(new { mode, results = results.Select(ListingResponse.FromEntity) });
});
```

`Program.cs` — register the embedder once (shared singleton; also used at query time):
```csharp
builder.Services.AddSingleton<BerryExchange.AiCore.ITextEmbedder, BerryExchange.AiCore.LocalTextEmbedder>();
```

- [ ] **Step 4: Run the full suite, commit**

Run: `cd backend && dotnet test` — expect ALL PASS.

```bash
git add -A
git commit --no-verify -m "Add semantic search over listings with keyword fallback"
```

### Task 14: Smart search + tasting notes in the frontend

**Files:**
- Modify: `frontend/src/api/types.ts`, `frontend/src/api/listings.ts`, `frontend/src/features/market/MarketPage.tsx`
- Test: `frontend/src/features/market/MarketPage.test.tsx` (extend)

**Interfaces:**
- Consumes: `GET /api/listings/search` (Task 13); `ListingResponse.aiTastingNotes` (Task 10).
- Produces: `searchListings(q: string): Promise<SearchListingsResponse>`.

- [ ] **Step 1: Extend the API layer**

`types.ts` — add `aiTastingNotes: string | null;` to `ListingResponse`, plus:
```typescript
export type SearchMode = 'semantic' | 'keyword';

export interface SearchListingsResponse {
  mode: SearchMode;
  results: ListingResponse[];
}
```

`listings.ts` — add:
```typescript
export function searchListings(q: string): Promise<SearchListingsResponse> {
  return apiRequest<SearchListingsResponse>(`/listings/search?q=${encodeURIComponent(q)}`);
}
```
(import the new type.)

- [ ] **Step 2: Write the failing test** (extend `MarketPage.test.tsx`, matching its existing mocking style for `getListings`)

```tsx
it('runs smart search and shows the semantic results with a mode badge', async () => {
  vi.mocked(searchListings).mockResolvedValue({
    mode: 'semantic',
    results: [makeListing({ berryType: 'Strawberry', farmName: 'Sweet Fields', aiTastingNotes: 'Candy-sweet.' })],
  });
  renderMarketPage();
  await userEvent.type(screen.getByRole('searchbox'), 'sweet berries for jam');
  await userEvent.click(screen.getByRole('button', { name: /smart search/i }));
  expect(await screen.findByText(/smart results · semantic/i)).toBeInTheDocument();
  expect(screen.getByText('Sweet Fields')).toBeInTheDocument();
  expect(screen.getByText('Candy-sweet.')).toBeInTheDocument();
});
```
Adapt helper names (`makeListing`, `renderMarketPage`, the search input's accessible role/label) to what the existing test file actually uses; add `searchListings` to the existing `vi.mock('../../api/listings', ...)` block; add `aiTastingNotes: null` to the existing listing factory so old tests still compile.

- [ ] **Step 3: Run to verify it fails**

Run: `cd frontend && npm test` — expect the new case FAIL (no smart search button).

- [ ] **Step 4: Implement in `MarketPage.tsx`**

Add state + handler alongside the existing search state:
```tsx
const [smartSearch, setSmartSearch] = useState<SearchListingsResponse | null>(null);

async function runSmartSearch() {
  const q = search.trim();
  if (!q) return;
  setSmartSearch(await searchListings(q));
}
```
Render a `Smart search` button next to the existing search input, and when `smartSearch` is non-null render a badge line `Smart results · {smartSearch.mode}` with a `Clear` button (`onClick={() => setSmartSearch(null)}`), showing `smartSearch.results` through the same listing-card markup the page already uses (extract the card into a local render helper if needed rather than duplicating markup). On every listing card, when `listing.aiTastingNotes` is set, render it as an italic line (e.g. `<p className="tasting-notes"><em>{listing.aiTastingNotes}</em></p>`). Match the page's existing styling patterns.

- [ ] **Step 5: Run tests + lint, commit, merge the phase**

Run: `cd frontend && npm test && npm run lint` — expect ALL PASS. Run `cd backend && dotnet test` once more (unchanged, sanity).

```bash
git add -A
git commit -m "Add smart search UI and AI tasting notes display"
git checkout development
git merge --no-ff feature/semantic-search -m "Merge feature/semantic-search into development"
git push origin development
gh run watch --exit-status
```

---

## Phase 4 — Claude integration + listing assistant (branch `feature/listing-assistant`)

### Task 15: `IGenerativeAi` + Anthropic implementation + status endpoint

**Files:**
- Create: `backend/src/BerryExchange.AiCore/IGenerativeAi.cs`, `backend/src/BerryExchange.AiCore/AnthropicGenerativeAi.cs`, `backend/src/BerryExchange.AiCore/DisabledGenerativeAi.cs`, `backend/src/BerryExchange.Api/Ai/AiEndpoints.cs`
- Modify: `backend/src/BerryExchange.AiCore/BerryExchange.AiCore.csproj` (package), `backend/src/BerryExchange.Api/Program.cs`, `docker-compose.yml`
- Test: `backend/tests/BerryExchange.Api.Tests/AiStatusTests.cs`
- Create: `docs/adr/0011-claude-api-generative-features.md`; Modify: `docs/architecture/component-backend.mmd`

**Interfaces:**
- Produces (in `BerryExchange.AiCore`):
```csharp
public record ListingDraft(string BerryType, string FarmName, decimal? PricePerPint, int? QuantityAvailable, string? Note);
public record ComparableListing(string BerryType, string FarmName, decimal PricePerPint, int QuantityAvailable);
public record ListingCopySuggestion(string ImprovedDescription, decimal SuggestedPricePerPint, string Reasoning);

public interface IGenerativeAi
{
    bool IsEnabled { get; }
    Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
        IReadOnlyList<ComparableListing> comparables, CancellationToken ct);
    Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct);
}
```
- Also produces `GET /api/ai/status` → `{ "enabled": bool }`. Consumed by Tasks 16, 17, 18, 21.

- [ ] **Step 1: Branch + package**

```bash
git checkout development && git checkout -b feature/listing-assistant
cd backend
dotnet add src/BerryExchange.AiCore/BerryExchange.AiCore.csproj package Anthropic
```

- [ ] **Step 2: Write the failing test**

`AiStatusTests.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace BerryExchange.Api.Tests;

public class AiStatusTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public AiStatusTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Status_reports_disabled_when_no_api_key_is_configured()
    {
        var status = await _fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/ai/status");
        Assert.False(status.GetProperty("enabled").GetBoolean());
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd backend && dotnet test --filter AiStatusTests` — expect FAIL (404).

- [ ] **Step 4: Implement the interface and both implementations**

`IGenerativeAi.cs`: the records + interface exactly as in **Interfaces** above (namespace `BerryExchange.AiCore`).

`DisabledGenerativeAi.cs`:
```csharp
namespace BerryExchange.AiCore;

public sealed class DisabledGenerativeAi : IGenerativeAi
{
    public bool IsEnabled => false;
    public Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
        IReadOnlyList<ComparableListing> comparables, CancellationToken ct) =>
        Task.FromResult<ListingCopySuggestion?>(null);
    public Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
```

`AnthropicGenerativeAi.cs`:
```csharp
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
                $"- {c.BerryType} from {c.FarmName}: ${c.PricePerPint}/pint, {c.QuantityAvailable} available"));

        var prompt = $"""
            A grower is drafting a berry marketplace listing.
            Draft: berry={draft.BerryType}; farm={draft.FarmName}; price=${draft.PricePerPint?.ToString() ?? "unset"}/pint; quantity={draft.QuantityAvailable?.ToString() ?? "unset"}; note={draft.Note ?? "(none)"}
            Comparable current listings:
            {comparablesText}
            Write an improved listing note (max 80 characters, warm and concrete) and suggest a fair
            price per pint grounded in the comparables.
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
                            suggestedPricePerPint = new { type = "number" },
                            reasoning = new { type = "string" },
                        }),
                        ["required"] = JsonSerializer.SerializeToElement(
                            new[] { "improvedDescription", "suggestedPricePerPint", "reasoning" }),
                        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
                    },
                },
            },
        });

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
        });
        return response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text?.Trim();
    }
}
```
(SDK member names come from the official `Anthropic` package; if one doesn't compile, fix from the compiler error rather than guessing alternatives.)

`Ai/AiEndpoints.cs`:
```csharp
using BerryExchange.AiCore;

namespace BerryExchange.Api.Ai;

public static class AiEndpoints
{
    public static void MapAiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ai");
        group.MapGet("/status", (IGenerativeAi ai) => Results.Ok(new { enabled = ai.IsEnabled }));
    }
}
```

`Program.cs` — registration + mapping:
```csharp
var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (!string.IsNullOrEmpty(anthropicApiKey))
{
    builder.Services.AddSingleton<BerryExchange.AiCore.IGenerativeAi>(
        new BerryExchange.AiCore.AnthropicGenerativeAi(anthropicApiKey));
}
else
{
    builder.Services.AddSingleton<BerryExchange.AiCore.IGenerativeAi,
        BerryExchange.AiCore.DisabledGenerativeAi>();
}
// ... next to the other Map* calls:
app.MapAiEndpoints();
```

`docker-compose.yml` — under `api.environment`: `Anthropic__ApiKey: ${ANTHROPIC_API_KEY:-}`.

- [ ] **Step 5: Run tests**

Run: `cd backend && dotnet test` — expect ALL PASS (fixture has no key ⇒ Disabled implementation).

- [ ] **Step 6: ADR + diagram + commit**

Draft `docs/adr/0011-claude-api-generative-features.md` (adr-update skill): Claude via the official C# SDK, model `claude-opus-5`; all generative calls behind `IGenerativeAi` with a Disabled fallback so the marketplace (and CI) runs keyless; structured outputs for machine-consumed responses; sync listing assistant in the API, async tasting notes in the worker. Update `component-backend.mmd`: `Ai` module + `AiCore` → Claude API edge.

```bash
git add -A
git commit -m "Add Claude-backed IGenerativeAi with graceful degradation (ADR-0011)"
```

- [ ] **Step 7 (optional, requires a real key): manual smoke**

Run: `cd backend && Anthropic__ApiKey=$ANTHROPIC_API_KEY dotnet run --project src/BerryExchange.Api --launch-profile http`, then `curl -s http://localhost:5091/api/ai/status` → `{"enabled":true}`.

### Task 16: Listing-assist endpoint

**Files:**
- Modify: `backend/src/BerryExchange.Api/Ai/AiEndpoints.cs`
- Test: `backend/tests/BerryExchange.Api.Tests/ListingAssistTests.cs`

**Interfaces:**
- Produces: `POST /api/ai/listing-assist` (auth required) accepting `ListingDraft`, returning `ListingCopySuggestion` (200), 503 when AI disabled, 502 when the model returns nothing usable.

- [ ] **Step 1: Write the failing test**

`ListingAssistTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BerryExchange.AiCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BerryExchange.Api.Tests;

public class ListingAssistTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ListingAssistTests(ApiTestFixture fixture) => _fixture = fixture;

    private sealed class FakeGenerativeAi : IGenerativeAi
    {
        public IReadOnlyList<ComparableListing>? SeenComparables;
        public bool IsEnabled => true;
        public Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
            IReadOnlyList<ComparableListing> comparables, CancellationToken ct)
        {
            SeenComparables = comparables;
            return Task.FromResult<ListingCopySuggestion?>(new("Juicy, jam-ready pints", 6.0m, "Priced with the market"));
        }
        public Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct) =>
            Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Assist_returns_503_when_ai_is_disabled()
    {
        var client = _fixture.CreateClient();
        await RegisterAsync(client);
        var response = await client.PostAsJsonAsync("/api/ai/listing-assist",
            new { BerryType = "Strawberry", FarmName = "F", PricePerPint = (decimal?)null, QuantityAvailable = (int?)null, Note = (string?)null });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Assist_requires_authentication()
    {
        var response = await _fixture.CreateClient().PostAsJsonAsync("/api/ai/listing-assist",
            new { BerryType = "Strawberry", FarmName = "F" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Assist_returns_suggestion_with_comparables_from_the_market()
    {
        var fake = new FakeGenerativeAi();
        var client = _fixture.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IGenerativeAi>();
            services.AddSingleton<IGenerativeAi>(fake);
        })).CreateClient();
        await RegisterAsync(client);
        (await client.PostAsJsonAsync("/api/listings",
            new { BerryType = "Strawberry", FarmName = "Comparable Farm", PricePerPint = 5.5m, QuantityAvailable = 3, Note = (string?)null }))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/ai/listing-assist",
            new { BerryType = "Strawberry", FarmName = "My Farm", PricePerPint = (decimal?)null, QuantityAvailable = 4, Note = "sweet" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Juicy, jam-ready pints", body.GetProperty("improvedDescription").GetString());
        Assert.NotNull(fake.SeenComparables);
        Assert.Contains(fake.SeenComparables!, c => c.FarmName == "Comparable Farm");
    }

    private static async Task RegisterAsync(HttpClient client) =>
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"a-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "A" }))
        .EnsureSuccessStatusCode();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend && dotnet test --filter ListingAssistTests` — expect FAIL (404 on `/api/ai/listing-assist`).

- [ ] **Step 3: Implement** (extend `AiEndpoints.cs`)

```csharp
using BerryExchange.Api.Listings;
// inside MapAiEndpoints, after /status:
group.MapPost("/listing-assist", async (ListingDraft draft, IGenerativeAi ai,
    ListingsService listings, CancellationToken ct) =>
{
    if (!ai.IsEnabled)
    {
        return Results.Json(new { errors = new[] { "AI features are disabled: no Anthropic API key is configured." } },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var comparables = (await listings.GetAllAsync(ct))
        .Where(l => l.QuantityAvailable > 0)
        .OrderByDescending(l => string.Equals(l.BerryType, draft.BerryType, StringComparison.OrdinalIgnoreCase))
        .ThenByDescending(l => l.CreatedAt)
        .Take(10)
        .Select(l => new ComparableListing(l.BerryType, l.FarmName, l.PricePerPint, l.QuantityAvailable))
        .ToList();

    var suggestion = await ai.SuggestListingCopyAsync(draft, comparables, ct);
    return suggestion is null
        ? Results.Json(new { errors = new[] { "The assistant could not produce a suggestion. Please try again." } },
            statusCode: StatusCodes.Status502BadGateway)
        : Results.Ok(suggestion);
}).RequireAuthorization();
```

- [ ] **Step 4: Run tests, commit**

Run: `cd backend && dotnet test` — expect ALL PASS.

```bash
git add -A
git commit -m "Add listing-assist endpoint grounding Claude suggestions in comparable listings"
```

### Task 17: "Improve with AI" in the Sell form

**Files:**
- Create: `frontend/src/api/ai.ts`
- Modify: `frontend/src/api/types.ts`, `frontend/src/features/sell/SellPage.tsx`
- Test: `frontend/src/features/sell/SellPage.test.tsx` (extend)

**Interfaces:**
- Consumes: `GET /api/ai/status`, `POST /api/ai/listing-assist`.
- Produces: `getAiStatus(): Promise<AiStatus>`, `suggestListing(draft): Promise<ListingCopySuggestion>`.

- [ ] **Step 1: API layer**

`types.ts` — add:
```typescript
export interface AiStatus {
  enabled: boolean;
}

export interface ListingDraft {
  berryType: string;
  farmName: string;
  pricePerPint: number | null;
  quantityAvailable: number | null;
  note: string | null;
}

export interface ListingCopySuggestion {
  improvedDescription: string;
  suggestedPricePerPint: number;
  reasoning: string;
}
```

`ai.ts`:
```typescript
import { apiRequest } from './client';
import type { AiStatus, ListingCopySuggestion, ListingDraft } from './types';

export function getAiStatus(): Promise<AiStatus> {
  return apiRequest<AiStatus>('/ai/status');
}

export function suggestListing(draft: ListingDraft): Promise<ListingCopySuggestion> {
  return apiRequest<ListingCopySuggestion>('/ai/listing-assist', {
    method: 'POST',
    body: JSON.stringify(draft),
  });
}
```

- [ ] **Step 2: Write the failing test** (extend `SellPage.test.tsx`)

```tsx
vi.mock('../../api/ai', () => ({
  getAiStatus: vi.fn().mockResolvedValue({ enabled: true }),
  suggestListing: vi.fn().mockResolvedValue({
    improvedDescription: 'Sun-ripened and jam-ready',
    suggestedPricePerPint: 6.25,
    reasoning: 'Comparable strawberries sell for $5.50-$7.00.',
  }),
}));

it('fills the form from the AI suggestion', async () => {
  renderSellPage();
  await userEvent.type(screen.getByLabelText(/berry/i), 'Strawberry');
  await userEvent.type(screen.getByLabelText(/farm/i), 'My Farm');
  await userEvent.click(await screen.findByRole('button', { name: /improve with ai/i }));
  expect(await screen.findByDisplayValue('Sun-ripened and jam-ready')).toBeInTheDocument();
  expect(screen.getByDisplayValue('6.25')).toBeInTheDocument();
  expect(screen.getByText(/comparable strawberries/i)).toBeInTheDocument();
});
```
Adapt label queries and render helper to the existing test file's conventions.

- [ ] **Step 3: Run to verify it fails**

Run: `cd frontend && npm test` — expect new case FAIL.

- [ ] **Step 4: Implement in `SellPage.tsx`**

- On mount, load `getAiStatus()` into `aiEnabled` state (default `false`; hide the button entirely when disabled or the call fails).
- Render an `Improve with AI` button inside the form (type `button`, disabled while a suggestion request is in flight or when berry/farm fields are empty).
- On click: build a `ListingDraft` from current form state (`pricePerPint`/`quantityAvailable` as numbers or `null`), call `suggestListing`, then set the note field to `improvedDescription`, the price field to `suggestedPricePerPint`, and store `reasoning` in state rendered as a small explanatory paragraph under the form. On `ApiError`, surface the message via the page's existing error/toast pattern.

- [ ] **Step 5: Run tests + lint, commit**

Run: `cd frontend && npm test && npm run lint` — expect ALL PASS.

```bash
git add -A
git commit -m "Add Improve-with-AI to the Sell form, gated on /api/ai/status"
```

### Task 18: Worker generates tasting notes via Claude

**Files:**
- Modify: `backend/src/BerryExchange.AiWorker/EnrichingListingCreatedHandler.cs`, `backend/src/BerryExchange.AiWorker/Program.cs`, `docker-compose.yml`
- Test: `backend/tests/BerryExchange.AiWorker.Tests/EnrichingHandlerTests.cs` (extend)

**Interfaces:**
- Consumes: `IGenerativeAi.GenerateTastingNotesAsync` (Task 15), `EnrichmentApiClient` (Task 12).

- [ ] **Step 1: Extend the failing test**

Add to `EnrichingHandlerTests.cs`:
```csharp
private sealed class FakeNotesAi : IGenerativeAi
{
    public bool IsEnabled => true;
    public Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft draft,
        IReadOnlyList<ComparableListing> comparables, CancellationToken ct) =>
        Task.FromResult<ListingCopySuggestion?>(null);
    public Task<string?> GenerateTastingNotesAsync(string berryType, string farmName, string? note, CancellationToken ct) =>
        Task.FromResult<string?>("Deep, bramble-sweet flavor.");
}

[Fact]
public async Task Handler_includes_generated_tasting_notes_when_ai_is_enabled()
{
    var capturing = new CapturingHandler();
    var http = new HttpClient(capturing) { BaseAddress = new Uri("http://api.test") };
    using var embedder = new LocalTextEmbedder();
    var handler = new EnrichingListingCreatedHandler(embedder, new EnrichmentApiClient(http),
        new FakeNotesAi(), NullLogger<EnrichingListingCreatedHandler>.Instance);

    await handler.HandleAsync(new ListingCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "Blackberry",
        "Hedge Farm", 6m, 8, "plump", DateTimeOffset.UtcNow), CancellationToken.None);

    using var doc = JsonDocument.Parse(capturing.Body!);
    Assert.Equal("Deep, bramble-sweet flavor.", doc.RootElement.GetProperty("tastingNotes").GetString());
}
```
Update the existing test in the file to pass `new DisabledGenerativeAi()` as the new constructor argument and assert `tastingNotes` is null in its payload.

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend && dotnet test tests/BerryExchange.AiWorker.Tests` — expect FAIL (constructor mismatch).

- [ ] **Step 3: Implement**

`EnrichingListingCreatedHandler.cs` — add `IGenerativeAi ai` as third constructor parameter (`_ai`), and change `HandleAsync`:
```csharp
public async Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct)
{
    var text = $"{evt.BerryType} from {evt.FarmName}. {evt.Note}".Trim();
    var embedding = _embedder.Embed(text);

    string? tastingNotes = null;
    if (_ai.IsEnabled)
    {
        try
        {
            tastingNotes = await _ai.GenerateTastingNotesAsync(evt.BerryType, evt.FarmName, evt.Note, ct);
        }
        catch (Exception ex)
        {
            // Notes are a nice-to-have; the embedding must land regardless.
            _logger.LogWarning(ex, "Tasting-notes generation failed for {ListingId}", evt.ListingId);
        }
    }

    await _api.SendAsync(evt.ListingId, embedding, tastingNotes, ct);
    _logger.LogInformation("Enriched listing {ListingId} (notes: {HasNotes})", evt.ListingId, tastingNotes is not null);
}
```

`Program.cs` (worker) — same conditional registration as the API:
```csharp
var workerAnthropicKey = builder.Configuration["Anthropic:ApiKey"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (!string.IsNullOrEmpty(workerAnthropicKey))
{
    builder.Services.AddSingleton<IGenerativeAi>(new AnthropicGenerativeAi(workerAnthropicKey));
}
else
{
    builder.Services.AddSingleton<IGenerativeAi, DisabledGenerativeAi>();
}
```

`docker-compose.yml` — under `ai-worker.environment`: `Anthropic__ApiKey: ${ANTHROPIC_API_KEY:-}`.

- [ ] **Step 4: Run all tests, commit, merge phase**

Run: `cd backend && dotnet test` — expect ALL PASS.

```bash
git add -A
git commit --no-verify -m "Generate AI tasting notes in the enrichment worker"
git checkout development
git merge --no-ff feature/listing-assistant -m "Merge feature/listing-assistant into development"
git push origin development
gh run watch --exit-status
```

---

## Phase 5 — Full agentic chat (branch `feature/agentic-chat`)

### Task 19: Chat persistence + conversation endpoints

**Files:**
- Create: `backend/src/BerryExchange.Api/Chat/ChatConversation.cs`, `backend/src/BerryExchange.Api/Chat/ChatMessage.cs`, `backend/src/BerryExchange.Api/Chat/ChatService.cs`, `backend/src/BerryExchange.Api/Chat/ChatEndpoints.cs`
- Modify: `backend/src/BerryExchange.Api/Infrastructure/BerryExchangeDbContext.cs`, `backend/src/BerryExchange.Api/Program.cs`
- Create: migration `AddChat` (generated)
- Test: `backend/tests/BerryExchange.Api.Tests/ChatConversationTests.cs`
- Modify: `docs/adr/0011-claude-api-generative-features.md` (chat section), `docs/architecture/data-model.mmd`

**Interfaces:**
- Produces: entities `ChatConversation { Guid Id; Guid UserId; string Title; DateTimeOffset CreatedAt; }`, `ChatMessage { Guid Id; Guid ConversationId; string Role; string Content; DateTimeOffset CreatedAt; }` (Role is `"user"` or `"assistant"`); `ChatService` with `CreateConversationAsync(Guid userId, string? title, CancellationToken ct)`, `GetConversationsAsync(Guid userId, ...)`, `GetConversationAsync(Guid id, Guid userId, ...)` (null unless owned), `GetMessagesAsync(Guid conversationId, Guid userId, ...)` (null unless owned), `AppendMessageAsync(Guid conversationId, string role, string content, ...)`. Endpoints (all `RequireAuthorization`): `GET /api/chat/conversations`, `POST /api/chat/conversations` body `{ "title": string? }`, `GET /api/chat/conversations/{id}/messages`.

- [ ] **Step 1: Branch + failing test**

```bash
git checkout development && git checkout -b feature/agentic-chat
```

`ChatConversationTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BerryExchange.Api.Tests;

public class ChatConversationTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ChatConversationTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Conversations_are_per_user_and_listable()
    {
        var client = _fixture.CreateClient();
        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"c-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "C" })).EnsureSuccessStatusCode();

        var created = await (await client.PostAsJsonAsync("/api/chat/conversations", new { title = "Berry hunt" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var conversationId = created.GetProperty("id").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/chat/conversations");
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("id").GetGuid() == conversationId);

        var messages = await client.GetFromJsonAsync<JsonElement>($"/api/chat/conversations/{conversationId}/messages");
        Assert.Empty(messages.EnumerateArray());

        // Another user cannot see it.
        var other = _fixture.CreateClient();
        (await other.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"o-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "O" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/chat/conversations/{conversationId}/messages")).StatusCode);
    }
}
```

Run: `cd backend && dotnet test --filter ChatConversationTests` — expect FAIL (404).

- [ ] **Step 2: Implement entities, service, endpoints, migration**

`Chat/ChatConversation.cs` and `Chat/ChatMessage.cs`: plain classes exactly as in **Interfaces** (all properties `{ get; set; }`, strings default `string.Empty`).

`BerryExchangeDbContext.cs`: add `public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();` and `public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();` plus model config:
```csharp
builder.Entity<ChatConversation>(entity =>
{
    entity.Property(c => c.Title).HasMaxLength(80).IsRequired();
    entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId);
});
builder.Entity<ChatMessage>(entity =>
{
    entity.Property(m => m.Role).HasMaxLength(16).IsRequired();
    entity.Property(m => m.Content).IsRequired();
    entity.HasOne<ChatConversation>().WithMany().HasForeignKey(m => m.ConversationId);
    entity.HasIndex(m => new { m.ConversationId, m.CreatedAt });
});
```

`Chat/ChatService.cs`:
```csharp
using BerryExchange.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BerryExchange.Api.Chat;

public class ChatService
{
    private readonly BerryExchangeDbContext _db;
    public ChatService(BerryExchangeDbContext db) => _db = db;

    public async Task<ChatConversation> CreateConversationAsync(Guid userId, string? title, CancellationToken ct)
    {
        var conversation = new ChatConversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? "New conversation" : title.Trim()[..Math.Min(title.Trim().Length, 80)],
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.ChatConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    public Task<List<ChatConversation>> GetConversationsAsync(Guid userId, CancellationToken ct) =>
        _db.ChatConversations.Where(c => c.UserId == userId).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public Task<ChatConversation?> GetConversationAsync(Guid id, Guid userId, CancellationToken ct) =>
        _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

    public async Task<List<ChatMessage>?> GetMessagesAsync(Guid conversationId, Guid userId, CancellationToken ct)
    {
        var owned = await _db.ChatConversations.AnyAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (!owned) return null;
        return await _db.ChatMessages.Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt).ToListAsync(ct);
    }

    public async Task<ChatMessage> AppendMessageAsync(Guid conversationId, string role, string content, CancellationToken ct)
    {
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync(ct);
        return message;
    }
}
```

`Chat/ChatEndpoints.cs`:
```csharp
using System.Security.Claims;

namespace BerryExchange.Api.Chat;

public record CreateConversationRequest(string? Title);

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/chat").RequireAuthorization();

        group.MapGet("/conversations", async (HttpContext http, ChatService chat, CancellationToken ct) =>
        {
            var conversations = await chat.GetConversationsAsync(GetUserId(http), ct);
            return Results.Ok(conversations.Select(c => new { c.Id, c.Title, c.CreatedAt }));
        });

        group.MapPost("/conversations", async (CreateConversationRequest request, HttpContext http,
            ChatService chat, CancellationToken ct) =>
        {
            var conversation = await chat.CreateConversationAsync(GetUserId(http), request.Title, ct);
            return Results.Created($"/api/chat/conversations/{conversation.Id}",
                new { conversation.Id, conversation.Title, conversation.CreatedAt });
        });

        group.MapGet("/conversations/{conversationId:guid}/messages", async (Guid conversationId,
            HttpContext http, ChatService chat, CancellationToken ct) =>
        {
            var messages = await chat.GetMessagesAsync(conversationId, GetUserId(http), ct);
            return messages is null
                ? Results.NotFound()
                : Results.Ok(messages.Select(m => new { m.Id, m.Role, m.Content, m.CreatedAt }));
        });
    }

    internal static Guid GetUserId(HttpContext http) =>
        Guid.Parse(http.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
```

`Program.cs`: `builder.Services.AddScoped<BerryExchange.Api.Chat.ChatService>();` and `app.MapChatEndpoints();`.

Generate migration: `cd backend && dotnet ef migrations add AddChat --project src/BerryExchange.Api`.

- [ ] **Step 3: Run the full suite**

Run: `cd backend && dotnet test` — expect ALL PASS.

- [ ] **Step 4: ADR/diagram + commit**

Append a "Agentic chat" section to ADR-0011 (persisted per-user conversations; assistant runs a tool-calling loop server-side; text-only history replay — tool traffic is not persisted, a documented simplification). Update `data-model.mmd` with the two chat tables.

```bash
git add -A
git commit -m "Add chat conversations and messages with per-user endpoints"
```

### Task 20: Agent model abstraction + marketplace tool executor

**Files:**
- Create: `backend/src/BerryExchange.Api/Chat/Agent/AgentModels.cs`, `backend/src/BerryExchange.Api/Chat/Agent/ToolCatalog.cs`, `backend/src/BerryExchange.Api/Chat/Agent/ChatToolExecutor.cs`
- Test: `backend/tests/BerryExchange.Api.Tests/ChatToolExecutorTests.cs`

**Interfaces:**
- Produces (`namespace BerryExchange.Api.Chat.Agent`):
```csharp
public record AgentToolDefinition(string Name, string Description, string InputSchemaJson);
public abstract record AgentHistoryItem;
public sealed record AgentUserMessage(string Text) : AgentHistoryItem;
public sealed record AgentAssistantTurn(string? Text, IReadOnlyList<AgentToolCall> ToolCalls) : AgentHistoryItem;
public sealed record AgentToolResults(IReadOnlyList<AgentToolResult> Results) : AgentHistoryItem;
public sealed record AgentToolCall(string Id, string Name, string InputJson);
public sealed record AgentToolResult(string ToolCallId, string Content, bool IsError = false);
public sealed record AgentTurn(string? Text, IReadOnlyList<AgentToolCall> ToolCalls);

public interface IChatAgentModel
{
    Task<AgentTurn> NextTurnAsync(string systemPrompt, IReadOnlyList<AgentToolDefinition> tools,
        IReadOnlyList<AgentHistoryItem> history, CancellationToken ct);
}

public interface IChatToolExecutor
{
    Task<AgentToolResult> ExecuteAsync(Guid userId, AgentToolCall call, CancellationToken ct);
}
```
- Also `ToolCatalog.SystemPrompt` (string const) and `ToolCatalog.Definitions` (the four tools). Consumed by Task 21.

- [ ] **Step 1: Write `AgentModels.cs`** — exactly the records/interfaces above, in one file.

- [ ] **Step 2: Write `ToolCatalog.cs`**

```csharp
namespace BerryExchange.Api.Chat.Agent;

public static class ToolCatalog
{
    public const string SystemPrompt = """
        You are Berry, the assistant for the Berry Exchange marketplace, where growers list
        fresh berries and buyers reserve pints. Prices are USD per pint.
        Rules:
        - Answer questions about listings by calling tools; never invent listings or prices.
        - Before calling create_reservation you MUST have asked the user and received an explicit
          "yes" for that exact listing in this conversation; only then call it with user_confirmed=true.
        - Keep answers short, concrete, and friendly. If a tool errors, tell the user plainly.
        """;

    public static readonly IReadOnlyList<AgentToolDefinition> Definitions =
    [
        new("search_listings",
            "Search berry listings with a free-text query. Call this whenever the user asks what is available.",
            """{"type":"object","properties":{"query":{"type":"string","description":"What the user is looking for"}},"required":["query"]}"""),
        new("get_listing",
            "Get the full details of a single listing by its id.",
            """{"type":"object","properties":{"listing_id":{"type":"string","description":"The listing GUID"}},"required":["listing_id"]}"""),
        new("check_stock",
            "Check how many pints are still available for a listing.",
            """{"type":"object","properties":{"listing_id":{"type":"string","description":"The listing GUID"}},"required":["listing_id"]}"""),
        new("create_reservation",
            "Reserve one pint of a listing for the current user. Only call after the user explicitly confirmed this listing in the conversation.",
            """{"type":"object","properties":{"listing_id":{"type":"string","description":"The listing GUID"},"user_confirmed":{"type":"boolean","description":"true only if the user explicitly said yes to reserving this exact listing"}},"required":["listing_id","user_confirmed"]}"""),
    ];
}
```

- [ ] **Step 3: Write the failing test**

`ChatToolExecutorTests.cs`:
```csharp
using System.Text.Json;
using BerryExchange.Api.Chat.Agent;
using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
using Microsoft.Extensions.DependencyInjection;

namespace BerryExchange.Api.Tests;

public class ChatToolExecutorTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ChatToolExecutorTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Unconfirmed_reservation_is_refused_and_confirmed_reservation_decrements_stock()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();
        var executor = new ChatToolExecutor(
            scope.ServiceProvider.GetRequiredService<ListingsService>(),
            scope.ServiceProvider.GetRequiredService<ReservationsService>());

        var seller = new BerryExchange.Api.Accounts.ApplicationUser
        { Id = Guid.NewGuid(), UserName = $"s-{Guid.NewGuid():N}@t.dev", Email = $"s-{Guid.NewGuid():N}@t.dev", DisplayName = "S" };
        var buyer = new BerryExchange.Api.Accounts.ApplicationUser
        { Id = Guid.NewGuid(), UserName = $"b-{Guid.NewGuid():N}@t.dev", Email = $"b-{Guid.NewGuid():N}@t.dev", DisplayName = "B" };
        db.Users.AddRange(seller, buyer);
        var listing = new Listing
        {
            Id = Guid.NewGuid(), SellerId = seller.Id, BerryType = "Mulberry", FarmName = "Silk Farm",
            PricePerPint = 4m, QuantityAvailable = 2, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        var unconfirmed = await executor.ExecuteAsync(buyer.Id,
            new AgentToolCall("t1", "create_reservation",
                JsonSerializer.Serialize(new { listing_id = listing.Id, user_confirmed = false })),
            CancellationToken.None);
        Assert.True(unconfirmed.IsError);

        var confirmed = await executor.ExecuteAsync(buyer.Id,
            new AgentToolCall("t2", "create_reservation",
                JsonSerializer.Serialize(new { listing_id = listing.Id, user_confirmed = true })),
            CancellationToken.None);
        Assert.False(confirmed.IsError);

        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.Listings.FindAsync(listing.Id))!.QuantityAvailable);

        var stock = await executor.ExecuteAsync(buyer.Id,
            new AgentToolCall("t3", "check_stock",
                JsonSerializer.Serialize(new { listing_id = listing.Id })), CancellationToken.None);
        Assert.Contains("1", stock.Content);
    }
}
```

Run: `cd backend && dotnet test --filter ChatToolExecutorTests` — expect FAIL (no `ChatToolExecutor`).

- [ ] **Step 4: Implement `ChatToolExecutor.cs`**

```csharp
using System.Text.Json;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;

namespace BerryExchange.Api.Chat.Agent;

public sealed class ChatToolExecutor : IChatToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ListingsService _listings;
    private readonly ReservationsService _reservations;

    public ChatToolExecutor(ListingsService listings, ReservationsService reservations)
    {
        _listings = listings;
        _reservations = reservations;
    }

    public async Task<AgentToolResult> ExecuteAsync(Guid userId, AgentToolCall call, CancellationToken ct)
    {
        try
        {
            using var input = JsonDocument.Parse(call.InputJson);
            var (content, isError) = call.Name switch
            {
                "search_listings" => await SearchAsync(input.RootElement, ct),
                "get_listing" => await GetListingAsync(input.RootElement, ct),
                "check_stock" => await CheckStockAsync(input.RootElement, ct),
                "create_reservation" => await CreateReservationAsync(userId, input.RootElement, ct),
                _ => ($"Unknown tool: {call.Name}", true),
            };
            return new AgentToolResult(call.Id, content, isError);
        }
        catch (Exception ex)
        {
            return new AgentToolResult(call.Id, $"Tool failed: {ex.Message}", IsError: true);
        }
    }

    private async Task<(string, bool)> SearchAsync(JsonElement input, CancellationToken ct)
    {
        var query = input.GetProperty("query").GetString() ?? "";
        var (mode, results) = await _listings.SearchAsync(query, limit: 5, ct);
        var payload = results.Select(l => new
        {
            id = l.Id, berryType = l.BerryType, farmName = l.FarmName,
            pricePerPint = l.PricePerPint, quantityAvailable = l.QuantityAvailable,
            aiTastingNotes = l.AiTastingNotes,
        });
        return (JsonSerializer.Serialize(new { mode, results = payload }, JsonOptions), false);
    }

    private async Task<(string, bool)> GetListingAsync(JsonElement input, CancellationToken ct)
    {
        var listing = await _listings.GetByIdAsync(ParseId(input), ct);
        return listing is null
            ? ("No listing with that id.", true)
            : (JsonSerializer.Serialize(ListingResponse.FromEntity(listing), JsonOptions), false);
    }

    private async Task<(string, bool)> CheckStockAsync(JsonElement input, CancellationToken ct)
    {
        var listing = await _listings.GetByIdAsync(ParseId(input), ct);
        return listing is null
            ? ("No listing with that id.", true)
            : ($"{listing.QuantityAvailable} pint(s) available.", false);
    }

    private async Task<(string, bool)> CreateReservationAsync(Guid userId, JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("user_confirmed", out var confirmed) || !confirmed.GetBoolean())
        {
            return ("The user has not confirmed this reservation. Ask them to confirm the exact listing first.", true);
        }
        var result = await _reservations.ReserveAsync(ParseId(input), userId, ct);
        return result.Succeeded
            ? ($"Reserved one pint. Reservation id: {result.Reservation!.Id}.", false)
            : ("This listing is sold out.", true);
    }

    private static Guid ParseId(JsonElement input) => Guid.Parse(input.GetProperty("listing_id").GetString()!);
}
```

- [ ] **Step 5: Run tests, commit**

Run: `cd backend && dotnet test` — expect ALL PASS.

```bash
git add -A
git commit -m "Add agent tool abstractions and marketplace tool executor"
```

### Task 21: Agent loop, Anthropic model adapter, SSE endpoint

**Files:**
- Create: `backend/src/BerryExchange.Api/Chat/Agent/ChatAgent.cs`, `backend/src/BerryExchange.Api/Chat/Agent/AnthropicChatAgentModel.cs`
- Modify: `backend/src/BerryExchange.Api/Chat/ChatEndpoints.cs`, `backend/src/BerryExchange.Api/Program.cs`
- Test: `backend/tests/BerryExchange.Api.Tests/ChatAgentLoopTests.cs`, `backend/tests/BerryExchange.Api.Tests/ChatAgentEndpointTests.cs`

**Interfaces:**
- Produces: `ChatAgent.RunAsync(Guid userId, IReadOnlyList<AgentHistoryItem> history, CancellationToken ct)` → `IAsyncEnumerable<ChatAgentEvent>` with events `AgentTextEvent(string Text)`, `AgentToolCallEvent(string Name)`; SSE endpoint `POST /api/chat/conversations/{id}/messages` body `{ "content": string }` emitting `data: {"type":"text"|"tool_call"|"done", ...}` frames. Consumed by Task 22 (widget).

- [ ] **Step 1: Write the failing loop test**

`ChatAgentLoopTests.cs`:
```csharp
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
```

Run: `cd backend && dotnet test --filter ChatAgentLoopTests` — expect FAIL (`ChatAgent` missing).

- [ ] **Step 2: Implement `ChatAgent.cs`**

```csharp
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
```

Run: `cd backend && dotnet test --filter ChatAgentLoopTests` — expect PASS.

- [ ] **Step 3: Implement `AnthropicChatAgentModel.cs`** (maps our records ↔ the Anthropic SDK; no automated test — covered by the interface + optional manual smoke)

```csharp
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
            Model = "claude-opus-5",
            MaxTokens = 4096,
            System = new List<TextBlockParam> { new() { Text = systemPrompt } },
            Tools = BuildTools(tools),
            Messages = BuildMessages(history),
        });

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
```
(As before: SDK member names per the official package; fix any mismatch from compiler errors.)

- [ ] **Step 4: Write the failing SSE endpoint test**

`ChatAgentEndpointTests.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using BerryExchange.Api.Chat.Agent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BerryExchange.AiCore;

namespace BerryExchange.Api.Tests;

public class ChatAgentEndpointTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ChatAgentEndpointTests(ApiTestFixture fixture) => _fixture = fixture;

    private sealed class ScriptedModel : IChatAgentModel
    {
        private int _calls;
        public Task<AgentTurn> NextTurnAsync(string systemPrompt, IReadOnlyList<AgentToolDefinition> tools,
            IReadOnlyList<AgentHistoryItem> history, CancellationToken ct)
        {
            _calls++;
            return Task.FromResult(_calls == 1
                ? new AgentTurn(null, [new AgentToolCall("t1", "search_listings", """{"query":"sweet"}""")])
                : new AgentTurn("Here are the sweetest berries on the market.", []));
        }
    }

    private sealed class EnabledAi : IGenerativeAi
    {
        public bool IsEnabled => true;
        public Task<ListingCopySuggestion?> SuggestListingCopyAsync(ListingDraft d,
            IReadOnlyList<ComparableListing> c, CancellationToken ct) => Task.FromResult<ListingCopySuggestion?>(null);
        public Task<string?> GenerateTastingNotesAsync(string b, string f, string? n, CancellationToken ct) =>
            Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Sending_a_message_streams_tool_and_text_events_and_persists_the_reply()
    {
        var client = _fixture.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IChatAgentModel>();
            services.AddSingleton<IChatAgentModel>(new ScriptedModel());
            services.RemoveAll<IGenerativeAi>();
            services.AddSingleton<IGenerativeAi>(new EnabledAi());
        })).CreateClient();

        (await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = $"chat-{Guid.NewGuid():N}@test.dev", Password = "Password1!", DisplayName = "C" })).EnsureSuccessStatusCode();
        var conversation = await (await client.PostAsJsonAsync("/api/chat/conversations", new { title = "hunt" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var conversationId = conversation.GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/messages",
            new { content = "what's sweet right now?" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"tool_call\"", body);
        Assert.Contains("Here are the sweetest berries on the market.", body);
        Assert.Contains("\"type\":\"done\"", body);

        var messages = await client.GetFromJsonAsync<JsonElement>($"/api/chat/conversations/{conversationId}/messages");
        var roles = messages.EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToList();
        Assert.Equal(["user", "assistant"], roles);
    }
}
```

Run: `cd backend && dotnet test --filter ChatAgentEndpointTests` — expect FAIL (404 on POST messages).

- [ ] **Step 5: Implement the SSE endpoint + DI**

Extend `ChatEndpoints.cs` (add `using System.Text.Json;`, `using BerryExchange.Api.Chat.Agent;`, `using BerryExchange.AiCore;`; add record `public record SendChatMessageRequest(string Content);`):
```csharp
group.MapPost("/conversations/{conversationId:guid}/messages", async (Guid conversationId,
    SendChatMessageRequest request, HttpContext http, ChatService chat, ChatAgent agent,
    IGenerativeAi ai, CancellationToken ct) =>
{
    if (!ai.IsEnabled)
    {
        return Results.Json(new { errors = new[] { "AI chat is disabled: no Anthropic API key is configured." } },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    if (string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest(new { errors = new[] { "Content is required." } });
    }

    var userId = GetUserId(http);
    if (await chat.GetConversationAsync(conversationId, userId, ct) is null) return Results.NotFound();

    await chat.AppendMessageAsync(conversationId, "user", request.Content.Trim(), ct);
    // Text-only history replay (documented simplification in ADR-0011): tool traffic
    // from earlier turns is not persisted, so it is not replayed.
    var history = (await chat.GetMessagesAsync(conversationId, userId, ct))!
        .Select(m => m.Role == "user"
            ? (AgentHistoryItem)new AgentUserMessage(m.Content)
            : new AgentAssistantTurn(m.Content, []))
        .ToList();

    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";

    var assistantParts = new List<string>();
    await foreach (var evt in agent.RunAsync(userId, history, ct))
    {
        object payload = evt switch
        {
            AgentTextEvent text => new { type = "text", text = text.Text },
            AgentToolCallEvent tool => new { type = "tool_call", name = tool.Name },
            _ => new { type = "unknown" },
        };
        if (evt is AgentTextEvent t) assistantParts.Add(t.Text);
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    if (assistantParts.Count > 0)
    {
        await chat.AppendMessageAsync(conversationId, "assistant", string.Join("\n\n", assistantParts), ct);
    }
    await http.Response.WriteAsync("data: {\"type\":\"done\"}\n\n", ct);
    await http.Response.Body.FlushAsync(ct);
    return Results.Empty;
});
```

`Program.cs` — after the `IGenerativeAi` registration:
```csharp
builder.Services.AddScoped<BerryExchange.Api.Chat.Agent.IChatToolExecutor,
    BerryExchange.Api.Chat.Agent.ChatToolExecutor>();
builder.Services.AddScoped<BerryExchange.Api.Chat.Agent.ChatAgent>(sp => new(
    sp.GetRequiredService<BerryExchange.Api.Chat.Agent.IChatAgentModel>(),
    sp.GetRequiredService<BerryExchange.Api.Chat.Agent.IChatToolExecutor>()));
if (!string.IsNullOrEmpty(anthropicApiKey))
{
    builder.Services.AddSingleton<BerryExchange.Api.Chat.Agent.IChatAgentModel>(
        new BerryExchange.Api.Chat.Agent.AnthropicChatAgentModel(anthropicApiKey));
}
else
{
    // Endpoint 503s before resolving the agent when AI is disabled, but DI still
    // needs a registration for test overrides to Replace.
    builder.Services.AddSingleton<BerryExchange.Api.Chat.Agent.IChatAgentModel>(
        new BerryExchange.Api.Chat.Agent.ThrowingChatAgentModel());
}
```
Add to `AgentModels.cs`:
```csharp
public sealed class ThrowingChatAgentModel : IChatAgentModel
{
    public Task<AgentTurn> NextTurnAsync(string systemPrompt, IReadOnlyList<AgentToolDefinition> tools,
        IReadOnlyList<AgentHistoryItem> history, CancellationToken ct) =>
        throw new InvalidOperationException("AI chat is disabled: no Anthropic API key configured.");
}
```

- [ ] **Step 6: Chat-loop diagram, run all tests, commit**

Create `docs/architecture/chat-tool-loop.mmd` (architecture-diagram-update skill): sequence — Buyer → SPA widget → `POST .../messages` (SSE) → ChatAgent loop → Claude (`IChatAgentModel`) ↔ ChatToolExecutor (search/get/check/reserve against module services) → streamed `tool_call`/`text`/`done` events → persisted assistant message.

Run: `cd backend && dotnet test` — expect ALL PASS.

```bash
git add -A
git commit --no-verify -m "Add agentic chat loop with Claude tool-calling and SSE streaming"
```

### Task 22: Chat widget in the frontend

**Files:**
- Create: `frontend/src/api/chat.ts`, `frontend/src/features/chat/ChatWidget.tsx`, `frontend/src/features/chat/sse.ts`
- Modify: `frontend/src/components/Layout.tsx` (mount widget), `frontend/src/api/types.ts`
- Test: `frontend/src/features/chat/sse.test.ts`, `frontend/src/features/chat/ChatWidget.test.tsx`

**Interfaces:**
- Consumes: chat endpoints (Tasks 19/21), `getAiStatus` (Task 17), the auth state hook in `features/auth`.
- Produces: `extractSseEvents(buffer: string): { events: ChatStreamEvent[]; rest: string }`.

- [ ] **Step 1: SSE parsing module + failing test**

`sse.ts`:
```typescript
export interface ChatStreamEvent {
  type: 'text' | 'tool_call' | 'done';
  text?: string;
  name?: string;
}

// Pull complete `data: {...}\n\n` frames off the front of the buffer;
// return whatever partial frame remains as `rest`.
export function extractSseEvents(buffer: string): { events: ChatStreamEvent[]; rest: string } {
  const events: ChatStreamEvent[] = [];
  let rest = buffer;
  let idx: number;
  while ((idx = rest.indexOf('\n\n')) >= 0) {
    const frame = rest.slice(0, idx);
    rest = rest.slice(idx + 2);
    const dataLine = frame.split('\n').find((line) => line.startsWith('data: '));
    if (dataLine) {
      events.push(JSON.parse(dataLine.slice(6)) as ChatStreamEvent);
    }
  }
  return { events, rest };
}
```

`sse.test.ts`:
```typescript
import { describe, expect, it } from 'vitest';
import { extractSseEvents } from './sse';

describe('extractSseEvents', () => {
  it('parses complete frames and keeps the partial tail', () => {
    const { events, rest } = extractSseEvents(
      'data: {"type":"tool_call","name":"search_listings"}\n\ndata: {"type":"text","text":"Hi"}\n\ndata: {"ty',
    );
    expect(events).toEqual([
      { type: 'tool_call', name: 'search_listings' },
      { type: 'text', text: 'Hi' },
    ]);
    expect(rest).toBe('data: {"ty');
  });
});
```

Run: `cd frontend && npm test` — the sse test should PASS immediately (pure function written first here); keep it as the regression anchor.

- [ ] **Step 2: Chat API module**

`types.ts` — add:
```typescript
export interface ChatConversation {
  id: string;
  title: string;
  createdAt: string;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  createdAt: string;
}
```

`chat.ts`:
```typescript
import { ApiError, apiRequest } from './client';
import type { ChatConversation, ChatMessage } from './types';
import { extractSseEvents, type ChatStreamEvent } from '../features/chat/sse';

export function getConversations(): Promise<ChatConversation[]> {
  return apiRequest<ChatConversation[]>('/chat/conversations');
}

export function createConversation(title?: string): Promise<ChatConversation> {
  return apiRequest<ChatConversation>('/chat/conversations', {
    method: 'POST',
    body: JSON.stringify({ title: title ?? null }),
  });
}

export function getMessages(conversationId: string): Promise<ChatMessage[]> {
  return apiRequest<ChatMessage[]>(`/chat/conversations/${conversationId}/messages`);
}

export async function streamChatMessage(
  conversationId: string,
  content: string,
  onEvent: (event: ChatStreamEvent) => void,
): Promise<void> {
  const response = await fetch(`/api/chat/conversations/${conversationId}/messages`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ content }),
  });
  if (!response.ok || !response.body) {
    throw new ApiError(response.status, ['Chat request failed']);
  }
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const { events, rest } = extractSseEvents(buffer);
    buffer = rest;
    events.forEach(onEvent);
  }
}
```

- [ ] **Step 3: Failing widget test**

`ChatWidget.test.tsx`:
```tsx
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ChatWidget } from './ChatWidget';
import type { ChatStreamEvent } from './sse';

vi.mock('../../api/chat', () => ({
  getConversations: vi.fn().mockResolvedValue([]),
  createConversation: vi.fn().mockResolvedValue({ id: 'conv-1', title: 'New conversation', createdAt: '' }),
  getMessages: vi.fn().mockResolvedValue([]),
  streamChatMessage: vi.fn(async (_id: string, _content: string, onEvent: (e: ChatStreamEvent) => void) => {
    onEvent({ type: 'tool_call', name: 'search_listings' });
    onEvent({ type: 'text', text: 'Two strawberry listings look great today.' });
    onEvent({ type: 'done' });
  }),
}));
vi.mock('../../api/ai', () => ({
  getAiStatus: vi.fn().mockResolvedValue({ enabled: true }),
}));

describe('ChatWidget', () => {
  it('sends a message and renders streamed assistant text', async () => {
    render(<ChatWidget isAuthenticated />);
    await userEvent.click(await screen.findByRole('button', { name: /chat with berry/i }));
    await userEvent.type(screen.getByPlaceholderText(/ask about berries/i), 'anything sweet?');
    await userEvent.click(screen.getByRole('button', { name: /send/i }));
    expect(await screen.findByText('Two strawberry listings look great today.')).toBeInTheDocument();
    expect(screen.getByText('anything sweet?')).toBeInTheDocument();
  });
});
```

Run: `cd frontend && npm test` — expect FAIL (no ChatWidget).

- [ ] **Step 4: Implement `ChatWidget.tsx`**

```tsx
import { useEffect, useRef, useState } from 'react';
import { getAiStatus } from '../../api/ai';
import { createConversation, streamChatMessage } from '../../api/chat';

interface DisplayMessage {
  role: 'user' | 'assistant' | 'status';
  content: string;
}

export function ChatWidget({ isAuthenticated }: { isAuthenticated: boolean }) {
  const [enabled, setEnabled] = useState(false);
  const [open, setOpen] = useState(false);
  const [messages, setMessages] = useState<DisplayMessage[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const conversationIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (!isAuthenticated) return;
    getAiStatus().then((s) => setEnabled(s.enabled)).catch(() => setEnabled(false));
  }, [isAuthenticated]);

  if (!isAuthenticated || !enabled) return null;

  async function send() {
    const content = input.trim();
    if (!content || busy) return;
    setInput('');
    setBusy(true);
    setMessages((m) => [...m, { role: 'user', content }]);
    try {
      conversationIdRef.current ??= (await createConversation()).id;
      await streamChatMessage(conversationIdRef.current, content, (event) => {
        if (event.type === 'tool_call') {
          setMessages((m) => [...m, { role: 'status', content: `Using ${event.name}…` }]);
        } else if (event.type === 'text' && event.text) {
          setMessages((m) => [...m, { role: 'assistant', content: event.text! }]);
        }
      });
    } catch {
      setMessages((m) => [...m, { role: 'status', content: 'Something went wrong. Try again.' }]);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="chat-widget">
      {open && (
        <div className="chat-panel">
          <div className="chat-messages">
            {messages.map((message, i) => (
              <p key={i} className={`chat-message chat-message--${message.role}`}>
                {message.role === 'status' ? <em>{message.content}</em> : message.content}
              </p>
            ))}
          </div>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void send();
            }}
          >
            <input
              placeholder="Ask about berries…"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              disabled={busy}
            />
            <button type="submit" disabled={busy}>
              Send
            </button>
          </form>
        </div>
      )}
      <button type="button" className="chat-toggle" onClick={() => setOpen((o) => !o)}>
        Chat with Berry
      </button>
    </div>
  );
}
```
Add minimal styles for `.chat-widget` (fixed bottom-right), `.chat-panel`, `.chat-messages`, and the message variants in the shared styles directory, matching the existing token/style conventions. Mount in `Layout.tsx`: render `<ChatWidget isAuthenticated={...} />` using the same current-user source the Header uses (pass a boolean derived from the existing auth hook/context).

- [ ] **Step 5: Run tests + lint, commit, merge phase**

Run: `cd frontend && npm test && npm run lint` and `cd backend && dotnet test` — expect ALL PASS.

```bash
git add -A
git commit -m "Add floating chat widget streaming agent events"
git checkout development
git merge --no-ff feature/agentic-chat -m "Merge feature/agentic-chat into development"
git push origin development
gh run watch --exit-status
```

---

## Phase 6 — MCP server (branch `feature/mcp-server`)

### Task 23: `BerryExchange.McpServer`

**Files:**
- Create: `backend/src/BerryExchange.McpServer/BerryExchange.McpServer.csproj`, `Program.cs`, `MarketplaceApiClient.cs`, `MarketplaceTools.cs`, `appsettings.json`
- Create: `backend/tests/BerryExchange.McpServer.Tests/` (`.csproj`, `MarketplaceApiClientTests.cs`)
- Modify: `backend/BerryExchange.slnx`
- Create: `docs/adr/0012-mcp-server.md`; Modify: `docs/architecture/container.mmd`

**Interfaces:**
- Consumes: public API endpoints (`/api/listings/search`, `/api/listings/{id}`, `/api/accounts/login`, `/api/listings/{id}/reservations`).
- Produces: MCP tools `search_listings`, `get_listing`, `check_availability`, `create_reservation` over stdio. Config: `BerryMcp:ApiBaseUrl` (default `http://localhost:5091`), optional `BerryMcp:Email`/`BerryMcp:Password` (reservation account).

- [ ] **Step 1: Branch + scaffold**

```bash
git checkout development && git checkout -b feature/mcp-server
cd backend
dotnet new console -o src/BerryExchange.McpServer -n BerryExchange.McpServer
dotnet sln BerryExchange.slnx add src/BerryExchange.McpServer/BerryExchange.McpServer.csproj
dotnet add src/BerryExchange.McpServer/BerryExchange.McpServer.csproj package ModelContextProtocol --prerelease
dotnet add src/BerryExchange.McpServer/BerryExchange.McpServer.csproj package Microsoft.Extensions.Hosting
dotnet new xunit -o tests/BerryExchange.McpServer.Tests -n BerryExchange.McpServer.Tests
dotnet sln BerryExchange.slnx add tests/BerryExchange.McpServer.Tests/BerryExchange.McpServer.Tests.csproj
dotnet add tests/BerryExchange.McpServer.Tests/BerryExchange.McpServer.Tests.csproj reference src/BerryExchange.McpServer/BerryExchange.McpServer.csproj
```

- [ ] **Step 2: Write the failing test**

`MarketplaceApiClientTests.cs`:
```csharp
using System.Net;
using BerryExchange.McpServer;

namespace BerryExchange.McpServer.Tests;

public class MarketplaceApiClientTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/api/accounts/login" => """{"id":"00000000-0000-0000-0000-000000000001"}""",
                var p when p.StartsWith("/api/listings/search") => """{"mode":"semantic","results":[]}""",
                var p when p.EndsWith("/reservations") => "",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task Search_passes_query_through_and_reservation_logs_in_first()
    {
        var handler = new ScriptedHandler();
        var client = new MarketplaceApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://api.test") }, "mcp@test.dev", "Password1!");

        var search = await client.SearchAsync("sweet strawberries", CancellationToken.None);
        Assert.Contains("semantic", search);
        Assert.Contains("q=sweet%20strawberries", handler.Requests[0].RequestUri!.Query);

        await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/api/accounts/login");

        // Second reservation must not log in again.
        var loginCount = handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/api/accounts/login");
        await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(loginCount, handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/api/accounts/login"));
    }

    [Fact]
    public async Task Reservation_without_configured_account_returns_disabled_message()
    {
        var client = new MarketplaceApiClient(
            new HttpClient(new ScriptedHandler()) { BaseAddress = new Uri("http://api.test") }, null, null);
        var result = await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Contains("disabled", result, StringComparison.OrdinalIgnoreCase);
    }
}
```

Run: `cd backend && dotnet test tests/BerryExchange.McpServer.Tests` — expect FAIL.

- [ ] **Step 3: Implement**

`MarketplaceApiClient.cs`:
```csharp
using System.Net.Http.Json;

namespace BerryExchange.McpServer;

public sealed class MarketplaceApiClient
{
    private readonly HttpClient _http;
    private readonly string? _email;
    private readonly string? _password;
    private bool _loggedIn;

    public MarketplaceApiClient(HttpClient http, string? email, string? password)
    {
        _http = http;
        _email = email;
        _password = password;
    }

    public Task<string> SearchAsync(string query, CancellationToken ct) =>
        _http.GetStringAsync($"/api/listings/search?q={Uri.EscapeDataString(query)}", ct);

    public Task<string> GetListingAsync(Guid listingId, CancellationToken ct) =>
        _http.GetStringAsync($"/api/listings/{listingId}", ct);

    public async Task<string> CreateReservationAsync(Guid listingId, CancellationToken ct)
    {
        if (_email is null || _password is null)
        {
            return "Reservations are disabled: no marketplace account is configured for this MCP server "
                 + "(set BerryMcp:Email and BerryMcp:Password).";
        }
        await EnsureLoggedInAsync(ct);
        var response = await _http.PostAsync($"/api/listings/{listingId}/reservations", content: null, ct);
        return response.IsSuccessStatusCode
            ? "Reserved one pint."
            : $"Reservation failed with status {(int)response.StatusCode}.";
    }

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_loggedIn) return;
        var response = await _http.PostAsJsonAsync("/api/accounts/login",
            new { Email = _email, Password = _password }, ct);
        response.EnsureSuccessStatusCode();
        _loggedIn = true; // auth cookie now lives in the handler's CookieContainer
    }
}
```
`MarketplaceTools.cs`:
```csharp
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace BerryExchange.McpServer;

[McpServerToolType]
public static class MarketplaceTools
{
    [McpServerTool, Description("Search Berry Exchange listings with a natural-language query. Returns JSON with a search mode and matching listings.")]
    public static Task<string> SearchListings(MarketplaceApiClient api,
        [Description("What to look for, e.g. 'sweet strawberries for jam'")] string query,
        CancellationToken ct) => api.SearchAsync(query, ct);

    [McpServerTool, Description("Get the full details of one listing by its GUID.")]
    public static Task<string> GetListing(MarketplaceApiClient api,
        [Description("The listing GUID")] Guid listingId, CancellationToken ct) =>
        api.GetListingAsync(listingId, ct);

    [McpServerTool, Description("Check how many pints remain for a listing.")]
    public static async Task<string> CheckAvailability(MarketplaceApiClient api,
        [Description("The listing GUID")] Guid listingId, CancellationToken ct)
    {
        var json = await api.GetListingAsync(listingId, ct);
        using var doc = JsonDocument.Parse(json);
        return $"{doc.RootElement.GetProperty("quantityAvailable").GetInt32()} pint(s) available.";
    }

    [McpServerTool, Description("Reserve one pint of a listing for the configured marketplace account. Ask the human for explicit confirmation before calling this.")]
    public static Task<string> CreateReservation(MarketplaceApiClient api,
        [Description("The listing GUID")] Guid listingId, CancellationToken ct) =>
        api.CreateReservationAsync(listingId, ct);
}
```

`Program.cs`:
```csharp
using BerryExchange.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdio transport: stdout carries the MCP protocol, so every log line must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var http = new HttpClient(new HttpClientHandler { UseCookies = true })
    {
        BaseAddress = new Uri(config["BerryMcp:ApiBaseUrl"] ?? "http://localhost:5091"),
    };
    return new MarketplaceApiClient(http, config["BerryMcp:Email"], config["BerryMcp:Password"]);
});

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```
(`appsettings.json`: `{ "BerryMcp": { "ApiBaseUrl": "http://localhost:5091" } }`. If an `AddMcpServer`/attribute name doesn't compile against the installed `ModelContextProtocol` prerelease, fix from compiler errors / the package README — do not invent members.)

- [ ] **Step 4: Run tests**

Run: `cd backend && dotnet test` — expect ALL PASS.

- [ ] **Step 5: Manual smoke with the MCP Inspector** (backend API must be running: `dotnet run --project src/BerryExchange.Api --launch-profile http`)

Run: `npx @modelcontextprotocol/inspector dotnet run --project backend/src/BerryExchange.McpServer` — in the inspector UI, list tools (expect 4) and call `search_listings` with `query: "sweet"` — expect JSON from the live API. Also register for Claude Code users (document in README, Task 25): `claude mcp add berry-exchange -- dotnet run --project <repo>/backend/src/BerryExchange.McpServer`.

- [ ] **Step 6: ADR + diagram + commit + merge**

Draft `docs/adr/0012-mcp-server.md` (adr-update skill): stdio MCP server as a separate process using the official C# SDK; talks to the marketplace over the public HTTP API with a dedicated service account (cookie login) rather than sharing the DB — the API remains the single owner of marketplace invariants; reservation tool inert unless an account is configured. Update `container.mmd`: MCP server container + edge to API + external MCP-client actor.

```bash
git add -A
git commit -m "Add MCP server exposing marketplace tools over stdio (ADR-0012)"
git checkout development
git merge --no-ff feature/mcp-server -m "Merge feature/mcp-server into development"
git push origin development
gh run watch --exit-status
```

---

## Phase 7 — Kubernetes manifests (branch `feature/k8s`)

### Task 24: k8s manifests + kind smoke test

**Files:**
- Create: `k8s/config.yaml`, `k8s/infra.yaml`, `k8s/apps.yaml`, `k8s/kustomization.yaml`
- Modify: `docs/adr/0008-containerization-and-ci.md` (append Kubernetes section)

**Interfaces:**
- Consumes: the three images built from the repo Dockerfiles, tagged `berry-api:local`, `berry-ai-worker:local`, `berry-frontend:local`.

- [ ] **Step 1: Branch + write the manifests**

```bash
git checkout development && git checkout -b feature/k8s
```

`k8s/config.yaml`:
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: berry-config
data:
  RabbitMq__Host: rabbitmq
  Api__BaseUrl: http://api:8080
  Database__AutoMigrate: "true"
---
apiVersion: v1
kind: Secret
metadata:
  name: berry-secrets
type: Opaque
stringData:
  ConnectionStrings__BerryExchangeDb: Host=postgres;Database=berryexchange;Username=berry;Password=berry
  Internal__ApiKey: dev-internal-key
  Anthropic__ApiKey: ""
```

`k8s/infra.yaml`:
```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: pgdata
spec:
  accessModes: [ReadWriteOnce]
  resources:
    requests:
      storage: 1Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: postgres
spec:
  replicas: 1
  selector:
    matchLabels: { app: postgres }
  template:
    metadata:
      labels: { app: postgres }
    spec:
      containers:
        - name: postgres
          image: pgvector/pgvector:pg16
          env:
            - { name: POSTGRES_DB, value: berryexchange }
            - { name: POSTGRES_USER, value: berry }
            - { name: POSTGRES_PASSWORD, value: berry }
          ports: [{ containerPort: 5432 }]
          volumeMounts: [{ name: pgdata, mountPath: /var/lib/postgresql/data }]
      volumes:
        - name: pgdata
          persistentVolumeClaim: { claimName: pgdata }
---
apiVersion: v1
kind: Service
metadata:
  name: postgres
spec:
  selector: { app: postgres }
  ports: [{ port: 5432 }]
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: rabbitmq
spec:
  replicas: 1
  selector:
    matchLabels: { app: rabbitmq }
  template:
    metadata:
      labels: { app: rabbitmq }
    spec:
      containers:
        - name: rabbitmq
          image: rabbitmq:4-management
          ports: [{ containerPort: 5672 }, { containerPort: 15672 }]
---
apiVersion: v1
kind: Service
metadata:
  name: rabbitmq
spec:
  selector: { app: rabbitmq }
  ports:
    - { name: amqp, port: 5672 }
    - { name: management, port: 15672 }
```

`k8s/apps.yaml`:
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api
spec:
  replicas: 1
  selector:
    matchLabels: { app: api }
  template:
    metadata:
      labels: { app: api }
    spec:
      containers:
        - name: api
          image: berry-api:local
          imagePullPolicy: Never
          envFrom:
            - configMapRef: { name: berry-config }
            - secretRef: { name: berry-secrets }
          ports: [{ containerPort: 8080 }]
---
apiVersion: v1
kind: Service
metadata:
  name: api
spec:
  selector: { app: api }
  ports: [{ port: 8080 }]
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ai-worker
spec:
  replicas: 1
  selector:
    matchLabels: { app: ai-worker }
  template:
    metadata:
      labels: { app: ai-worker }
    spec:
      containers:
        - name: ai-worker
          image: berry-ai-worker:local
          imagePullPolicy: Never
          envFrom:
            - configMapRef: { name: berry-config }
            - secretRef: { name: berry-secrets }
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: frontend
spec:
  replicas: 1
  selector:
    matchLabels: { app: frontend }
  template:
    metadata:
      labels: { app: frontend }
    spec:
      containers:
        - name: frontend
          image: berry-frontend:local
          imagePullPolicy: Never
          ports: [{ containerPort: 80 }]
---
apiVersion: v1
kind: Service
metadata:
  name: frontend
spec:
  selector: { app: frontend }
  ports: [{ port: 80 }]
```

`k8s/kustomization.yaml`:
```yaml
apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization
resources:
  - config.yaml
  - infra.yaml
  - apps.yaml
```

- [ ] **Step 2: Smoke-test with kind** (requires `kind` installed; if unavailable, note it and validate with `kubectl apply -k k8s/ --dry-run=client` instead)

```bash
docker build -f backend/src/BerryExchange.Api/Dockerfile -t berry-api:local backend
docker build -f backend/src/BerryExchange.AiWorker/Dockerfile -t berry-ai-worker:local backend
docker build -t berry-frontend:local frontend
kind create cluster --name berry
kind load docker-image berry-api:local berry-ai-worker:local berry-frontend:local --name berry
kubectl apply -k k8s/
kubectl rollout status deployment/api --timeout=180s
kubectl port-forward svc/frontend 8080:80 &
curl -s http://localhost:8080 | head -3   # expect HTML
kill %1
kind delete cluster --name berry
```

- [ ] **Step 3: ADR + commit + merge**

Append a "Kubernetes" section to ADR-0008: plain manifests + kustomize (no Helm — one environment, no templating need); `imagePullPolicy: Never` + `kind load` for local clusters; single-replica stateful services acceptable for a showcase.

```bash
git add k8s docs/adr/0008-containerization-and-ci.md
git commit -m "Add Kubernetes manifests with kind smoke-test workflow"
git checkout development
git merge --no-ff feature/k8s -m "Merge feature/k8s into development"
git push origin development
gh run watch --exit-status
```

---

## Phase 8 — Docs polish + release (branch `feature/docs-polish`)

### Task 25: README overhaul, final sweep, merge to main

**Files:**
- Modify: `README.md`
- Verify: all ADRs 0007-0012 exist; all diagrams in `docs/architecture/` reflect the final topology

- [ ] **Step 1: Branch + rewrite `README.md`**

```bash
git checkout development && git checkout -b feature/docs-polish
```

New `README.md` structure (write real prose for each section, reusing the accurate bits of the current README):

```markdown
# Berrow (Berry Exchange)

A berry marketplace with an AI core: growers list fresh berries, buyers browse,
search semantically, chat with an agent, and reserve pints — backed by a real
API, database, message broker, and an async AI enrichment pipeline.

## Architecture

ASP.NET Core modular monolith (Accounts, Listings, Reservations, Ai, Chat) +
PostgreSQL/pgvector + RabbitMQ + an AI enrichment worker + an MCP server +
a React SPA. See docs/architecture/*.mmd (C4 diagrams) and docs/adr/ for every
decision. [1-paragraph summary + component list]

## Quickstart (Docker)

    export ANTHROPIC_API_KEY=sk-ant-...   # optional; AI features degrade gracefully without it
    docker compose up --build
    # SPA:      http://localhost:5173
    # API:      http://localhost:5091
    # RabbitMQ: http://localhost:15672 (guest/guest)

## AI features
[listing assistant, async tasting notes + embeddings, semantic search, agentic
chat — one paragraph each, notes on graceful degradation via /api/ai/status]

## MCP server
[what it is + registration:
`claude mcp add berry-exchange -- dotnet run --project <repo>/backend/src/BerryExchange.McpServer`
+ env vars BerryMcp__Email / BerryMcp__Password for the reservation tool]

## Development
[per-service run commands (existing content), test commands, hook installation
`git config core.hooksPath scripts/git-hooks`]

## Kubernetes
[kind workflow from Task 24, 5 lines]

## Branching
[summary + link to CONTRIBUTING.md]
```

- [ ] **Step 2: Final verification sweep**

Run all of:
```bash
cd backend && dotnet test
cd ../frontend && npm test && npm run lint && npm run build
cd .. && docker compose up -d --build && sleep 25 && curl -sf http://localhost:5091/api/listings > /dev/null && curl -sf http://localhost:5173 > /dev/null && docker compose down
```
All must succeed. Also verify: `ls docs/adr/` shows 0001-0012; `git log --oneline development | head -30` reads as a coherent story.

- [ ] **Step 3: Commit + merge to development**

```bash
git add README.md
git commit -m "Overhaul README for the AI-enhanced marketplace"
git checkout development
git merge --no-ff feature/docs-polish -m "Merge feature/docs-polish into development"
git push origin development
gh run watch --exit-status
```

- [ ] **Step 4: Release to main — ONLY with explicit user approval**

Stop and confirm with the user that the showcase is ready. Then:
```bash
git checkout main
git merge --no-ff development -m "Release: AI Engineer showcase (RabbitMQ, AI worker, semantic search, agentic chat, MCP, Docker/CI/k8s)"
git push origin main
gh run watch --exit-status
```



