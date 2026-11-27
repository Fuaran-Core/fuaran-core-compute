### The substrate at Core `1.0.0`: the compute frame is a view over the typed column, and the `Table` boundary converts nothing (Phase 423, `DECISIONS.md` D17 and D18) — BREAKING, `retype`

**The class, from the gate.** The surface family prints `Fuaran.Compute.DataFrame` **`retype`** (16
moves) and `Fuaran.Compute.PipelineQuery` **`retype`** (3 moves): every public signature that carried
a schema carries Core's `Field list` where it carried `(string * ColumnType) list` — `evalExprInRow`,
`typeOf`, `rowCompareBy`, `joinKeyIndices`, `windowStep`, `Incremental.planOver`, `Plan.explain`,
`Plan.isTotal`, `SchemaWalk`, and `PipelineQuery.Sources` with its constructor and `sources`. The
`Fuaran.Compute.ColumnOps` and `Fuaran.Compute.Conformance` surfaces are unmoved. The wire family
prints the `pipeline` and `pipelineQuery` documents **`additive`**: a schema entry may now carry
`unit`, `label`, `description` and `ext` (Core Phase 427), and one that carries none renders byte
for byte as before. The `columnOp` per-case documents re-rooted — a `ColumnType` is no longer
reachable through a `Column`, whose data is a typed vector, so the exemplar for each column type
reaches it through another case — with the codec's bytes for every op unchanged, as the `columnOp`
and `delta` law vectors show.

**What a consumer adopts.** `FuaranCoreVersion` `1.0.0` beside `Fuaran.Compute.*` `0.39.0`, and
Core's own `1.0.0` migration for the types this repository hands back and takes: `Schema = Field
list` (`Field.create name type`, with the metadata members), `Column = { Name; Data }` over the typed
`Vector` (`Column.ofCells` / `Column.toCells` where a cell list is wanted), `TimestampType` carrying
its unit, and the typed `ColumnError` cases. Nothing in this repository's own vocabulary — the
transforms, the expressions, the incremental seam, the registry — changed shape.

**What changed in behaviour, each a consequence of the column being typed.**

- *The `Table` boundary is a view.* `DataFrame.prepare` / `Frame.ofTable` borrow each column's
  storage (a slice is copied; an `AllValid` column has a mask built; under Fable a `bool` vector and
  every mask are read into bytes) and `Frame.toTable` adopts the frame's arrays: no cell is boxed on
  the typed path in either direction. `Frame.Origins`, `Frame.unpackFallback`, the `Chunked.Cells`
  memo, `Vec.packList` and `InternedCells` went with the conversions they served.
- *An int in a float or decimal column IS the float or decimal of its value* — on the way in, in a
  derived column, and in a delta: `Int 3` set into a float column reads back `Float 3.0`, and a
  source rebuilt with the float in its place is the same source.
- *A derived column — a `Derive`, an `Unpivot`'s value column, or a column of a `Union` — whose
  cells span two types no column type holds together is refused by name* (`typesBesideEachOther`;
  D18). The join type is taken where one exists.
- *`Cast(date, s)` and `Cast(timestamp, s)` refuse a text that is not the canonical form by name*,
  where they admitted any string (D18).
- *The compute codecs refuse a tag outside a closed vocabulary as `MalformedShape`* naming the tag
  and the admitted spellings, where they answered `UnknownType`; Core typed `UnknownType` to column
  types.
- *A projection renames a field keeping its metadata; a computed column is a fresh field.*

**The law vectors.** `conformance/laws/transform-laws.json` re-emitted from this tree moves one
vector's bytes: `column-op-6-setColumn-decimal` records `Decimal "12"` where it recorded `Int 12`.
Every other vector moves in the `kitVersion` stamp alone. The corpus copy follows the release (D6).

**Evidence.** The boundary law (`Frame`, "the boundary laws hold over every column type and a ragged
column") now asserts that a column of the table's length is a VIEW over its storage — the frame's
array is the column's — and that the round trip hands the storage back; `DECISIONS.md` D17 and D18
record the rulings; the suite's generators were rewritten to Core `1.0.0`'s truth where their premise
was a cell list (each case says so in its comment).
