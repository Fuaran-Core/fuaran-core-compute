### A refresh assembles its result from the prior one (Phase 448, `DECISIONS.md` D27) — none, `performance`

**What changed.** No public surface moves, and no answer: every `Incremental.refresh` returns the
table it returned before, and an encoded state is byte for byte the one it was. Underneath, in
`Fuaran.Compute.DataFrame`'s incremental seam:

- **A computed column of an in-place refresh through row-local steps** is the prior result's typed
  column, copied and written at the changed and appended rows, rather than packed from every row's
  cell.
- **A maintained group table** is the prior one, written at the recomputed groups and grown by the
  new ones.
- **A prefix-fold window's column** is read straight off the typed carrier the scan wrote. Its cells
  are read off the column only when the state is encoded.

Where the copy cannot be shown to equal the packed column (a carrier it does not handle, or a written
cell the carrier does not hold), the refresh packs the cells as before.

**Measured** (`docs/performance.md`, "A refresh assembles its result from the prior one"): on
`lines`, `window CumulSum` and `group-by high-card`, whose refresh alone had cost 2.1 to 3.4 times
the full evaluation since `0.39.0`'s substrate change, it now costs 0.13 to 0.96 of it on the Arm64
clock leg. No shape the seam accepted is declined.

**The class.** `none`: no signature, no answer and no wire byte moves. It rides the `0.39.0` draft.

**What a consumer does.** Nothing.
