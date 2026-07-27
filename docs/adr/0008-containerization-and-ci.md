# 0008. Containerize the API and frontend with docker-compose; run CI on GitHub Actions

Date: 2026-07-27
Status: Accepted

## Context

Berry Exchange has had a Dockerfile-per-deployable story on paper since ADR-0005 (self-host via Docker Compose, with a Caddy reverse proxy in front of the SPA and API), but until now the repo carried no actual `Dockerfile`s, no `docker-compose.yml`, and no automated build/test pipeline. The "AI Engineer Showcase" plan's Phase 1 closes that gap: a reviewer checking out the repo should be able to bring up the whole stack with one command and see it work, and every push should be verified by CI rather than by the developer remembering to run tests locally. Two deployables need containers — the ASP.NET Core API (`backend/src/BerryExchange.Api`) and the React SPA (`frontend`) — plus PostgreSQL, which the modular monolith already depends on (ADR-0001, ADR-0002).

A related, forward-looking constraint shaped the choice of Postgres image: ADR-0010 (semantic search, not yet written) is expected to need `pgvector`. Picking that image now, while it's a no-op today, avoids a disruptive base-image swap later.

This ADR covers the **local/demo compose topology** introduced in this task, which is deliberately narrower in scope than ADR-0005's **production self-host topology**. ADR-0005 fronts the SPA and API with a standalone Caddy container that terminates TLS and reverse-proxies `/api/*`. The compose stack introduced here has no such standalone proxy: the frontend container's own nginx does double duty, serving the built SPA and proxying `/api/` to the API container. That's a reasonable simplification for a stack meant to be run with `docker compose up` on a laptop or in CI — TLS and Caddy's other production concerns aren't relevant there. Nothing here supersedes or contradicts ADR-0005's production deployment decision; the two topologies serve different purposes and both diagrams now coexist in `docs/architecture/container.mmd`.

## Decision

Give each deployable its own multi-stage `Dockerfile`: the API's builds and publishes with the .NET SDK image and runs on the smaller ASP.NET runtime image; the frontend's builds the Vite bundle with a Node image and serves the static output with `nginx:alpine`, using `frontend/nginx.conf` to serve `index.html` for SPA routing and proxy `/api/` to the `api` service by its Compose DNS name.

Add one root-level `docker-compose.yml` that brings up the full local stack as three services — `postgres` (image `pgvector/pgvector:pg16`), `api`, and `frontend` — wired together by Compose's internal network, with `api` waiting on `postgres`'s healthcheck and `frontend` waiting on `api`. Database schema is applied automatically at container startup via the `Database:AutoMigrate` configuration switch added to `Program.cs`, which the compose file turns on (`Database__AutoMigrate: "true"`) but which stays off by default so existing local-dev and test behavior (running `dotnet ef database update` manually) is unaffected. RabbitMQ and a background worker service are intentionally out of scope for this compose file — Phase 2 adds them.

CI runs on GitHub Actions: the backend job runs `dotnet test`, using Testcontainers to spin up a real Postgres for integration tests rather than mocking the database; the frontend job runs lint, unit tests, and a production build. GitHub Actions was chosen simply because the repository already lives on GitHub — no separate CI platform account or integration is needed.

## Consequences

A reviewer or interviewer can clone the repo and run `docker compose up -d --build` to get a working stack — SPA on `:5173`, API on `:5091`, Postgres on `:5432` — without installing the .NET SDK, Node, or Postgres locally. The same Dockerfiles this compose file builds are reusable for CI (build-and-test in a container) and, per ADR-0005, for the eventual Azure Container Apps migration, so this task doesn't introduce a second, throwaway container definition alongside a "real" one. Choosing `pgvector/pgvector:pg16` over plain `postgres:16` now costs nothing (it's a superset image) and removes a future migration step once vector search lands.

The tradeoff is one more topology to keep mentally distinct: this compose file's nginx-does-everything shape is not the same as ADR-0005's Caddy-fronted production shape, and anyone updating one must check whether the other also needs updating. `docs/architecture/container.mmd` now documents both side by side to make that distinction visible rather than implicit.

A "Kubernetes" section will be appended to this ADR in Phase 7, once the showcase plan reaches container orchestration; nothing about that is decided here.

Alternatives considered:
- **Dev-only compose without the app images (Postgres only, apps run via `dotnet run` / `npm run dev`)** — rejected. It would still require dependency installation locally and wouldn't demonstrate the actual deployable artifacts, leaving nothing a reviewer could point to as "this is what runs in production."
- **Azure DevOps Pipelines for CI** — rejected. The repository lives on GitHub, so GitHub Actions avoids a second platform account and a cross-platform webhook/integration; the pipeline concepts (build, test, artifact) transfer directly if a future Azure migration ever warrants revisiting this.
