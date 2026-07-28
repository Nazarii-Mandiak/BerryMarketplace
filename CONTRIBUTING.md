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
