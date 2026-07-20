# 0004. Cookie-based authentication via ASP.NET Core Identity

Date: 2026-07-20
Status: Accepted

## Context

Berry Exchange needs real user accounts (unlike the `localStorage`-only prototype) — any authenticated user can both list and reserve berries, there's no rigid buyer/seller role split. The SPA (ADR-0003) and API (ADR-0001) are served same-origin through a reverse proxy (ADR-0005).

## Decision

Use ASP.NET Core Identity with same-site session cookies for authentication, not JWT bearer tokens.

## Consequences

Same-origin serving lets the browser handle the session automatically via cookies, avoiding token-refresh logic and the XSS-exposed token storage that JWT-in-`localStorage` or JWT-in-JS-memory approaches carry. The constraint this accepts: the SPA and API must remain same-origin (enforced by the reverse-proxy layer). If a future requirement needs a fully decoupled cross-origin client (e.g. a separate mobile app calling the API directly), this decision would need revisiting toward token-based auth.
