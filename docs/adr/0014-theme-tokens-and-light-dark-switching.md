# 0014. Coral/peach theme tokens with a three-state light/dark/system switch

Date: 2026-07-30
Status: Accepted

## Context

The SPA shipped with two disjoint token systems in `global.css`: a hand-rolled set of brand
variables (`--ground`, `--ink`, `--panel`, `--accent`, …) that actually drives the ~600 lines of
component CSS, and a separate, unused set of shadcn `oklch` tokens (`--background`, `--foreground`,
`--primary`, …) wired only into the one shadcn-generated component (`button.tsx`) and a bare
`@layer base` block. Three different dark-mode mechanisms existed in the CSS
(`@media (prefers-color-scheme)`, `:root[data-theme]`, `.dark`) but none was reachable from the UI
— there was no toggle, no persisted preference, and no JavaScript reading or writing any of them.

A new brand palette (coral/peach, shadcn-token-shaped) needed to become the app's actual theme, and
the marketplace needed a real light/dark/system switch a user can operate, not just an OS-level
default.

## Decision

The new palette becomes the only token system: `:root` holds the light values, `.dark` the dark
values, matching the shadcn variable names (`--background`, `--primary`, `--card`, …) supplied for
the redesign. Rather than rewrite every hand-written component rule to the new names, `:root` also
defines the legacy brand names (`--ground`, `--ink`, `--panel`, `--line`, …) as aliases —
`--ground: var(--background);` and so on — so the existing `.card`, `.btn`, `.chat-*` rules repaint
under the new palette and under `.dark` with zero per-rule edits. This works because a custom
property's `var()` reference resolves against the *computed* value of that property on the element
it's used from, not the value at the point the alias itself was declared — so on an `<html>` element
carrying the `.dark` class, `--ground`'s `var(--background)` reference resolves to `.dark`'s
`--background`, even though `--ground` itself is only ever declared once, inside `:root`. The one
naming collision, `--accent` (peach in the new palette, the CTA red in the old one), is resolved by
repointing every hand-written usage at `--primary`/`--primary-foreground` instead; `BerryIcon.tsx`'s
`fill="var(--accent)"` is left alone and simply inherits the new peach accent.

Three previously-dead dark-mode mechanisms (the media query, the `data-theme` attribute selectors,
the old oklch `.dark` block) are removed in favor of the one `.dark` class already wired into
Tailwind's `@custom-variant dark`. A new `ThemeProvider` (matching the existing `ToastProvider`
context+hook shape, composed alongside it in `main.tsx`) owns a `'light' | 'dark' | 'system'` state,
persists explicit choices to `localStorage`, and toggles `.dark` on `document.documentElement`. It
subscribes to `matchMedia('(prefers-color-scheme: dark)')`'s `change` event only while in `'system'`
mode, so an OS theme flip is picked up live without a reload, but never overrides an explicit
light/dark choice. A `ThemeToggle` segmented control (three buttons — Sun/Moon/Monitor from the
already-installed but previously-unused `lucide-react`) is mounted in the header, visible regardless
of sign-in state since theme is a display preference, not an account setting. An inline script in
`index.html`, running before the stylesheet loads, reads `localStorage.theme` and adds `.dark`
pre-paint so dark mode doesn't flash light on load — something `ThemeProvider` cannot do on its own
since React only runs after first paint.

Product cards additionally gain an Aceternity-style `GlowingEffect` border (pointer-tracked
gradient ring), pulled in via the `motion` package for its angle-tweening `animate()` call. This is
the change that actually touches `package.json` and is why this ADR exists as a gate-required
document rather than a purely descriptive one.

## Consequences

Every existing component rule keeps working unmodified — the alias layer means the coral/peach
palette (and dark mode) apply app-wide from one token swap, not a file-by-file rewrite. The
`@theme inline` block that maps shadcn's color tokens to Tailwind utilities was already complete for
this palette's variable names and needed no changes.

`ThemeProvider` intentionally has exactly one consumer (`ThemeToggle`); there is no separate
`useTheme`-only settings page yet, so the provider is deliberately minimal rather than pre-built for
hypothetical future consumers.

`motion` (~30 kB) is a real bundle-size cost for a single angle-tweening animation that CSS's
`@property` + `transition` could do dependency-free. It was kept because the supplied component
explicitly specifies it and rewriting the pasted third-party component's internals would forfeit
being able to take upstream fixes; revisit if bundle size becomes a measured problem.

Vitest's jsdom test environment does not expose `window.localStorage` or `window.matchMedia` in
this project's setup (the jsdom `Storage` accessor doesn't survive Vitest's global-copy step, and
jsdom has no `matchMedia` implementation at all), so both needed stubs in `setupTests.ts` — a
minimal in-memory `Storage` polyfill and a default `matchMedia` mock — now that `ThemeProvider`
mounts on every `renderWithProviders` call across the whole suite.

Alternatives considered:

- **Rewrite every component rule to the new shadcn variable names directly** — rejected. Far larger
  diff for the same visual result, and the alias layer is the standard token-theming trick for
  exactly this kind of incremental migration.
- **A theme `<select>` or a light/dark toggle without a system option** — rejected; the requirement
  was explicitly three states (light, dark, system), and a segmented control makes all three choices
  visible and reachable in one click rather than requiring a cycle-and-guess interaction.
- **CSS-only `@property --start` animation instead of `motion`** — considered and documented above
  as the leaner alternative, not taken because the component was supplied as-is with `motion` as an
  explicit dependency.
