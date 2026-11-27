### The transform law vectors are compared under their stated tolerance on every host (Phase 559, `DECISIONS.md` D44) — additive

**What changed.** `Fuaran.Compute.Conformance` gains the module `VectorTolerance`: `ofExpected` reads a
vector's `expected.tolerance`, `tableAgrees tolerance expected actual` is the comparison a certifying host
applies to an `ok` vector's table, `floatsAgree` is its rule for two float cells of a host-rounded column,
and `ulpDistance` the measure (the difference of two doubles' bit patterns read as ordered integers). A
column the tolerance names agrees cell by cell — one type, one length, null in the same rows, two nonzero
finite floats of one sign within the bound, a zero, a non-finite value and every other cell exact — and
the rest of the table byte for byte. `transform-laws.json` re-emits with the rule stated in its
`description`; every vector, and every recorded answer, is byte-identical. No evaluator answer moves.

**The class.** Four functions in a new module; the `api/` baseline moves by additions only, so the change
RIDES the `0.41.0` draft.

**What a consumer does.** Nothing. A .NET host certifying against the transform law vectors may call
`VectorTolerance.tableAgrees` instead of comparing an `ok` vector's table byte for byte; a host in any other
language reads the same rule from the file's `description`.
