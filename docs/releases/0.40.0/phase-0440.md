### A decimal vector holds no boxed cells (Phase 440) — none, `performance`

**What changed.** No public surface moves, and no answer. Every pipeline over a decimal column returns
the table it returned before, cell for cell and wire byte for wire byte. Underneath, in
`Fuaran.Compute.DataFrame`'s frame, the scaled-integer decimal vector (Phase 280) carries the column's
decimal texts in place of a boxed `Cell` per row.

- Over a Core column, the texts are the column's own decimal vector, borrowed at the boundary in and
  adopted at the boundary out, as the other typed vectors have been since `0.39.0`'s substrate change.
- A cell is made from its row's text when it is read.

A column past the vector's width, or holding text outside the decimal grammar, takes the text path, as
before.

**Measured** (`docs/performance.md`, "A decimal vector holds no boxed cells"). At 100,000 rows a
prepared decimal column's frame holds 1.1 MB where it held 5.1 MB on .NET and 11.5 MB under node.
`prepare` costs 1.8 ms where it cost 8.2 ms on .NET, and 5.9 ms where it cost 10.4 ms under node. The
typed family's decimal filter, group-and-sum, sort and join each fell on both hosts.

**The class.** `none`: no signature, no answer and no wire byte moves. It rides the `0.40.0` draft.

**What a consumer does.** Nothing.
