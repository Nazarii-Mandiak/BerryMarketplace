---
name: architecture-diagram-update
description: Generate or update a single Mermaid architecture diagram (.mmd) in docs/architecture/ for one C4 boundary (context, container, or component) or a supplementary diagram (data model, sequence flow). Use when a commit changes a module/container boundary and the matching diagram wasn't updated, when the pre-commit hook blocks a commit for a missing diagram, or when the user asks for a system/component diagram.
---

# Architecture Diagram Update

Regenerate exactly one `.mmd` file under `docs/architecture/` — the one whose boundary actually changed. Never redraw the whole architecture in response to a small change; scope creep is how diagrams start inventing relationships that aren't real.

## The five slots

Fill these for every diagram request, even when inferring them from a code change rather than being told directly:

1. **Diagram type** — context (C4 L1), container (C4 L2), component (C4 L3), sequence, or ER — pick the one file this change actually affects.
2. **Components** — the boxes: only what's real in the code right now, never invented for symmetry or completeness.
3. **Relationships** — the arrows: what actually calls/depends on what.
4. **Abstraction level / boundary** — which single container or component this diagram is scoped to. Don't cross into a different diagram's boundary.
5. **What to omit** — anything below this diagram's zoom level (e.g. a container diagram omits internal classes; a component diagram omits infrastructure like the reverse proxy).

## Steps

1. Identify which single `.mmd` file under `docs/architecture/` covers the boundary that changed. If none does, that's a signal a new file/boundary is needed — say so rather than cramming it into an existing diagram.
2. Read the current file's content, if it exists — preserve its style and layout, only touch what actually changed.
3. Regenerate the Mermaid source, filling the five slots above.
4. Keep node count reasonable (roughly under 30). Above that, decompose into two files rather than drawing one dense diagram.
5. Append one entry to `docs/architecture/prompts.md`: date, filename, one-line description of what changed and why — enough to regenerate or re-derive the diagram later without re-reading the whole conversation that produced it.
6. Stage the `.mmd` file and the `prompts.md` update alongside the code change, in the same commit.

## What NOT to do

- Don't touch a diagram whose boundary didn't change just because this skill is already running.
- Don't hand-edit a rendered image — there is no rendered image; the `.mmd` file is the only source of truth. Render at mermaid.live or via the Artifact tool only to eyeball-check, never as the place to fix something.
- Don't write an ADR here — if the change is decision-worthy (not just a picture update), that's the `adr-update` skill's job, run separately.
