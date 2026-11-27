### The pivot aggregates per pair through streams, and the Table boundary fills its selection with a loop (Phase 353) — none, `performance`

**What changed.** No public surface moves, and no answer. The pivot aggregates each (group, on-value)
pair through Phase 323's streams, one stream per pivot column, and falls back to `Column.aggregate`
exactly where the streams do. `Frame.physical` fills the identity selection with a counted loop. Under
node the pivot reads 0.42 to 0.44 of its Phase 267 row-frame estimate (it read 1.08 to 1.17 before), and
`lines` reads 0.24 to 0.35 of 8a39a42. The answers are byte-identical on node and .NET over the
transform-law vectors and the corpus (`benchmarks/results/2026-10-05-i7-9700-phase-353.md`).

**Nothing to adopt.** The Core half of the phase (`Column.aggregate` reading its aggregate and column
type by pattern rather than union equality) rides Core's 0.35.0 draft; compute's `Median`, `StdDev` and
`CountDistinct` pairs pick it up when the substrate pin rises to 0.35.0.
