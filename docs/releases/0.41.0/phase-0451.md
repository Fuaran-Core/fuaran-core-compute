### An append refresh allocates no per-row cache on the large-object heap (Phase 451, `DECISIONS.md` D42) — none, `performance`

**What changed.** No public surface moves, and no answer: every `Incremental.refresh` returns the table
it returned before, and an encoded state is byte for byte the one it was. Underneath, in
`Fuaran.Compute.DataFrame`'s incremental seam, the three caches a state keeps per source row (the row
tokens, each step's cells, the rows' groups) are held in chunks of 8,192 entries rather than one array
each. A refresh that appends shares every chunk of the prior state's caches and copies the one it
extends. One that edits copies the chunks it writes. Before, both re-allocated each cache whole, and at
20,000 rows each of those arrays was a large object. The encoder flattens the chunks, and the decoder
chunks a decoded state again.

**Measured** (`docs/performance.md`, "A 20,000-row append refresh is held against the full
evaluation"): the one-row append's refresh alone at 20,000 rows reads 0.35 to 0.40 of the full
evaluation on the small-group grouped nodes (Arm64 clock leg), where it read 0.8 to 2.5. The suite's
refresh-against-full case now times that size too.

**The class.** `none`: no signature, no answer and no wire byte moves. It rides the `0.41.0` draft.

**What a consumer does.** Nothing.
