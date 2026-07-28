# 0007. Adopt a `development` integration branch with `--no-ff` feature branches

Date: 2026-07-27
Status: Accepted

## Context

Berry Exchange has so far been developed by a solo developer merging feature branches straight into `main` (see `a7aa7b3 Merge pull request #2 from Nazarii-Mandiak/frontend-implementation`). That workflow is fine for a single stream of small changes, but the "AI Engineer Showcase" enhancement work (design spec + 25-task, 9-phase plan, see `docs/adr/0006-architecture-docs-freshness.md` for related process tooling) is multi-phase and will land in a steady stream of commits over many sessions. Merging each in-progress phase straight to `main` would leave `main` in an intermediate, not-always-demo-ready state, which is a problem for a repo whose whole purpose is to be shown to interviewers.

## Decision

Introduce a long-lived `development` integration branch between feature work and `main`:

- `feature/<topic>` branches are cut from `development`, one per task or logical unit of work.
- Feature branches are merged back into `development` with `git merge --no-ff`, so each phase of work stays visible as a single merge commit in history even though the feature branch itself may contain several small commits.
- `main` only receives merges from `development`, and only once a batch of work is reviewed and verified — never a direct merge from a `feature/*` branch.

This repo's `development` branch already exists and has been carrying the design-spec and plan commits that precede this ADR; this decision formalizes the existing branch as the permanent integration line going forward, not a one-off measure.

## Consequences

`main` stays demo-ready at all times — an interviewer or reviewer checking out `main` never sees a partially finished phase. History on `development` is easy to scan because `--no-ff` merges keep each feature branch's commits grouped under one merge commit instead of interleaving them with unrelated work.

The tradeoff is a small amount of extra branch ceremony for a solo developer: every task now means a `feature/<topic>` branch, a merge into `development`, and eventually a separate merge into `main`, rather than committing straight to one branch. This is judged worth it given the showcase's stakes.

Alternatives considered:
- **Continue merging straight to `main`** — rejected. Leaves `main` in an intermediate state for the entire duration of a multi-week, multi-phase effort.
- **Trunk-based development with feature flags** — rejected. Adds flagging infrastructure that isn't justified for a solo-developer showcase repo with no concurrent users depending on `main`'s current state.
