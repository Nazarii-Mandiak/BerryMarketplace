# 0003. Use React + TypeScript + Vite for the frontend

Date: 2026-07-20
Status: Accepted

## Context

The existing `index.html` prototype establishes the intended UX: browse/filter/search listings, post a listing, reserve ("buy") a pint. That's a CRUD- and forms-heavy marketplace UI. The backend is .NET (see ADR-0001), and there's no existing team frontend preference — the choice was made purely on technical merit for a solo developer who may grow the team later.

## Decision

Build the frontend as a React + TypeScript single-page app, bundled with Vite. No server-side rendering framework (e.g. Next.js) — there's no SEO-critical public content at MVP scope, since selling requires an account.

## Consequences

The existing prototype's CSS custom-property design tokens and component-shaped markup translate directly into React components with minimal redesign. React's ecosystem and hiring pool are the largest available, which matters if this grows past a solo project. The cost accepted: a second language (TypeScript) alongside the C# backend, rather than a single-language stack.

Alternatives considered:
- **Blazor Server** — rejected. Requires a persistent SignalR connection per user, which complicates self-hosting.
- **Blazor WebAssembly** — rejected. Heavier cold-load payload for a public marketplace's first paint than a Vite-bundled SPA.
- **Vue 3 + TypeScript** — viable, simpler learning curve, but a smaller ecosystem/hiring pool than React; not chosen.
