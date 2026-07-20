# 0006. Enforce architecture documentation freshness via a git pre-commit hook + Claude Code skills

Date: 2026-07-20
Status: Accepted

## Context

Architecture decisions (ADRs) and architecture diagrams rot silently when kept up to date only by developer memory or advisory instructions (e.g. a CLAUDE.md rule) — both are easy to forget, especially across long sessions or when someone other than the original author makes a change. A Claude Code skill or subagent only helps once someone remembers to invoke it, which has the same reliability gap. Diagrams (`docs/architecture/*.mmd`) and ADRs (`docs/adr/*.md`) are different artifacts with different lifecycles: a diagram is a living picture of the system *right now* and gets updated in place; an ADR records *why* a decision was made at a point in time and is superseded, never edited, when the decision changes.

## Decision

Install a **git pre-commit hook** (via `core.hooksPath`, pointing at `scripts/git-hooks/`, so it fires for any commit regardless of tool — Claude Code or a plain terminal). The hook inspects staged files against a maintained pattern list (`scripts/git-hooks/architecture-paths.txt`: `*.csproj`, `package.json`, `Program.cs`, `Startup.cs`, `docker-compose*.yml`, `Dockerfile*`, `appsettings*.json`, `**/Migrations/*.cs`). If any matched file is staged, it blocks the commit unless a corresponding ADR (`docs/adr/*.md`) and a corresponding diagram (`docs/architecture/*.mmd`) are also staged, or the developer deliberately bypasses with `git commit --no-verify`.

Two companion Claude Code skills do the authoring the hook can't:
- **`adr-update`** drafts a numbered MADR-format ADR.
- **`architecture-diagram-update`** regenerates exactly the one `.mmd` file whose boundary changed (never redrawing the whole system for a small change) and logs the change to `docs/architecture/prompts.md` for reproducibility.

## Consequences

Commits that touch architecture-relevant files always carry updated docs, or a deliberate, conscious bypass — never a silent omission. The hook is a heuristic, not a judge of quality or necessity: routine changes that happen to touch a matched file (e.g. adding an unrelated NuGet package) will trigger it too, and `--no-verify` is the intended, deliberate escape valve for those, not a workaround to feel guilty about. The hook also cannot catch architecturally-significant changes that don't match its file patterns — a periodic "drift detection" sweep (an agent comparing code, diagrams, and infra for semantic drift) is a reasonable future enhancement if that gap proves real, but isn't built now since nothing in the current workflow needs it yet.

Alternatives considered:
- **CLAUDE.md rule alone** — rejected as the sole mechanism. Purely advisory; nothing stops it from being silently skipped.
- **Skill alone (no hook)** — rejected as the sole mechanism. Solves *how* to write a good ADR/diagram once invoked, but not the "someone has to remember to invoke it" problem.
- **Subagent sweep alone** — rejected as the sole mechanism for now. Useful for periodic retrospective audits, but not real-time; may be added later as a complement, not a replacement.
