# 0015. Switch listings and reservations from pints to kilograms, with free-form fractional quantity

Date: 2026-07-30
Status: Accepted

## Context

Every listing and reservation in Berry Exchange was denominated in a single, fixed unit: one
pint, hardcoded. `Listing.PricePerPint`/`QuantityAvailable` and `Reservation.Quantity` were `int`
or price-per-fixed-unit; `POST /api/listings/{id}/reservations` took **no request body at all** —
`Quantity = 1` was a literal in `ReservationsService.ReserveAsync`. A buyer could not ask for more
or less than one pint, and the unit itself was baked into the schema, the JSON API, the RabbitMQ
`ListingCreatedEvent` contract, the chat agent's tool schemas, the MCP server's tool descriptions,
and the AI prompt text — around fifty files in total.

The product requirement is to sell and buy berries by weight instead, in kilograms, and to let a
buyer choose any amount — not a fixed step, not whole units only. `1.3` kg needs to be a valid
order.

## Decision

**Straight rename, no unit conversion.** `Listing.PricePerPint` → `PricePerKg`, `QuantityAvailable`
→ `QuantityAvailableKg`; `Reservation.Quantity` → `QuantityKg`. Existing rows are **not**
numerically converted — a price or quantity that was previously "per pint" is simply read as "per
kg" going forward. This is acceptable because the data is pre-launch/development data with no real
buyers to protect; a production migration with live listings would need an actual conversion
factor and likely a maintenance window, which this ADR deliberately does not attempt.

**Fractional weight throughout.** All three columns become `numeric(10,2)` (`QuantityAvailableKg`
and `QuantityKg` were `integer`). The migration hand-writes `RenameColumn` + `AlterColumn`
operations rather than accepting EF's auto-scaffolded diff — EF's default column-matching
heuristic, when a property is simultaneously renamed and retyped, treated the rename and the type
change as unrelated drops and adds, and in this case even cross-matched `PricePerPint` onto
`QuantityAvailableKg` by coincidence of ordinal position. The auto-scaffolded migration would have
silently zeroed every existing price and quantity. `RenameColumn` then `AlterColumn` (verified via
`dotnet ef migrations script`, which confirms the emitted SQL is a plain
`ALTER TABLE ... RENAME COLUMN` followed by `ALTER TABLE ... ALTER COLUMN ... TYPE numeric(10,2)`,
no `USING` clause needed since Postgres casts `integer → numeric` implicitly) preserves every
existing value exactly, just reinterpreted as kilograms and widened to 2 decimal places.

**The reservation endpoint now takes a body**, `record ReserveRequest(decimal QuantityKg)`,
validated at the boundary independently of the database constraint: `> 0`, at most 2 decimal
places (`decimal.Round(q, 2) == q`), and `≤ 1000` as a sanity cap. There is **deliberately no
0.25-multiple restriction** — `1.3` kg must be accepted as-is; that was the headline requirement.
`ReservationsService.ReserveAsync` gained a `decimal quantityKg` parameter, and the atomic oversell
guard changed from `QuantityAvailable - 1 WHERE QuantityAvailable > 0` to
`QuantityAvailableKg - {qty} WHERE QuantityAvailableKg >= {qty}` — still a single conditional
`UPDATE`, so two simultaneous buyers requesting overlapping fractional amounts still cannot both
succeed against the same stock (covered by
`ReservationsConcurrencyTests.Two_simultaneous_buyers_requesting_overlapping_fractional_weight_only_one_wins`).
`ReserveAsync` has three callers — the HTTP endpoint, `ChatToolExecutor` (the chat agent's
`create_reservation` tool), and the MCP server via HTTP — all three were updated together so none
of them can silently bypass the new validation.

**AI-facing surfaces were updated to match**: the chat agent's system prompt and `create_reservation`
tool schema now require a `quantity_kg` argument and describe prices as "USD per kilogram"; the MCP
server's `CreateReservation` tool gained the same parameter; `ListingDraft`/`ComparableListing`/
`ListingCopySuggestion` (AiCore) and the Anthropic prompt text were renamed in step. A stale-comment
sweep (`grep -ril pint`) confirms no live code, backend or frontend-facing contract, still says
"pint" anywhere except two coincidental substring matches (`MapInternal...Endpoints` contains the
literal characters "pInt") and the historical diagram-log entries in `docs/architecture/prompts.md`,
which intentionally record what was true when each diagram was created and are not rewritten.

## Consequences

Every write path that touches quantity or price had to change together — schema, DTOs, validation,
the atomic SQL guard, the RabbitMQ contract, and both AI-facing tool surfaces — because
`ReserveAsync`'s three callers all route through the same service method. Missing any one of them
would have left a path that could still oversell or reject a legitimate fractional order.

The lack of value conversion means this migration is only safe for non-production data. Re-running
this pattern against real listings would need a `KgPerPint` factor applied in the same migration
(a `UPDATE` after the rename/retype, not a separate step) and almost certainly a listing-frozen
maintenance window, since the atomic reservation guard cannot safely run concurrently with a
bulk price rewrite.

Alternatives considered:

- **Keep a fixed step (e.g. 0.25 kg only, no free-form entry)** — rejected; the explicit
  requirement was to accept any 2-decimal weight, not a discretized set. The frontend still nudges
  by 0.25 kg via `+`/`−` buttons for convenience, but the underlying value and the server-side
  validation accept anything at 2-decimal precision.
- **Convert existing values by a nominal pint→kg factor** — rejected for this pass; there is no
  real data to protect pre-launch, and inventing a conversion factor for a value nobody will
  actually consult adds risk (a wrong factor silently misprices every existing listing) for no
  benefit. Revisit if this pattern is ever needed against a live database.
