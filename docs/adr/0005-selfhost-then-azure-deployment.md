# 0005. Self-host via Docker Compose now; migrate to Azure later

Date: 2026-07-20
Status: Accepted

## Context

The developer wants to self-host Berry Exchange today but expects to migrate to Azure once the marketplace has traction. Re-platforming later should be a config/infra change, not a rewrite.

## Decision

Containerize the API in a single Dockerfile from day one. Self-host via `docker-compose` with three containers: a reverse proxy (Caddy) serving the built SPA and forwarding `/api/*` to the API, the API itself, and PostgreSQL (ADR-0002). All environment-specific values (connection strings, CORS origins) are read from environment variables — never hardcoded. When migrating to Azure, the same container image runs on **Azure Container Apps**, the Postgres container is replaced by **Azure Database for PostgreSQL – Flexible Server** (via `pg_dump`/restore), and Caddy is replaced by **Azure Static Web Apps / Front Door**.

## Consequences

The Azure migration becomes a swap of infrastructure and configuration, not an application rewrite — provided the discipline of never hardcoding environment-specific values is maintained. Same-origin serving through the reverse proxy is also what makes cookie-based auth (ADR-0004) work without cross-origin cookie complications, in both environments.
