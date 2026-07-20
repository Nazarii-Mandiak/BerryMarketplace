---
name: adr-update
description: Draft or update an Architecture Decision Record in docs/adr/ using the MADR-lite format. Use when a commit changes architecture-relevant files (new dependencies, module wiring, infra config, migrations) and no matching ADR exists yet, when the pre-commit hook blocks a commit for a missing ADR, or when the user asks to record/update an architecture decision.
---

# ADR Update

Draft a new numbered Architecture Decision Record in `docs/adr/` capturing a decision that was just made or is being made right now. Never edit an already-**Accepted** ADR's Decision or Consequences to reflect a *new* choice — if a prior decision is being reversed, write a new ADR and mark the old one `Status: Superseded by NNNN`, linking to the new one.

## Steps

1. List `docs/adr/` to find the highest existing number. The new file is `NNNN-kebab-title.md`, zero-padded to 4 digits, one number higher.
2. Read `docs/adr/template.md` for the exact section layout — Title, Date, Status, Context, Decision, Consequences.
3. Fill in each section:
   - **Title**: short and decision-shaped ("Use PostgreSQL as the primary datastore", not "Database").
   - **Context**: the forces at play — constraints, requirements, prior state — written so someone with none of the current conversation's context understands why the question came up.
   - **Decision**: the one thing being decided, stated plainly, one sentence if possible.
   - **Consequences**: both what gets easier and the real tradeoff/cost being accepted, plus alternatives considered and why each was rejected.
4. If this decision supersedes an earlier ADR, edit that ADR's `Status` line to `Superseded by NNNN` and add a one-line pointer to the new one. Don't rewrite its Context/Decision/Consequences — the historical record stays intact.
5. Stage the new (and any superseded) ADR file(s) alongside the code change that prompted it, in the same commit.

## What NOT to do

- Don't write an ADR for a routine change with no real alternative considered (a typo fix, a patch version bump). The pre-commit hook's file-pattern match is a heuristic, not a mandate — if a blocked commit genuinely doesn't warrant an ADR, use `git commit --no-verify` instead of writing a hollow one.
- Don't restate diagram content. ADRs explain *why*; diagrams show *what* — link to the relevant file under `docs/architecture/` instead of duplicating it.
- Don't batch multiple unrelated decisions into one ADR. One decision per file.
