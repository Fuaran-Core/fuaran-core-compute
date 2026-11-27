### `Fuaran.Compute.GeometricAlgebraOps` is cut: geometric transforms as a parametric op algebra over a state of named objects (Phase 731, `DECISIONS.md` D85 item 9, D90) — additive

**What changed.** A new package, `Fuaran.Compute.GeometricAlgebraOps`, in the namespace of its id, referencing
`Fuaran.Compute.GeometricAlgebra`, `Fuaran.Core.OpStream`, `Fuaran.Core.Wire` and FSharp.Core: the only geometric
package that references the op-stream. Its baselines, `api/Fuaran.Compute.GeometricAlgebraOps.txt` and
`api/wire/Fuaran.Compute.GeometricAlgebraOps.txt`, are new; no other file under `api/` or `api/wire/` moves, and no other
package's surface or answers move.

- `Naming<'N>`: the caller's comparison of names and their spelling on the wire. The package invents neither.
- `Form`: an object in D85 item 4's parametric form — `Point`, `PointPair` (two points), `Line` (a point and a
  direction), `Circle` (a centre, a radius and a normal), `Plane` (a normal and a distance) and `Sphere` (a centre and
  a radius). `GeometricObject`: the algebra's typed blade a state holds.
- `GeometricOp<'N>`: D85 item 9's seven ops — `Add`, `Remove`, `Rotate`, `Translate`, `Scale`, `Reflect` and `Screw` —
  each carrying parameters and never a coefficient. `GeometricOpRefusal<'N>`: `UnknownName`, `NameAlreadyPresent`,
  `Geometry` (the algebra constructor's refusal, carried as it gave it), `NotANumber` (an object that would hold a
  NaN; a state never holds one, and an infinity is a value), `NotInvertible` and `InverseRefused` (an inverse the
  state after the op would refuse).
- `GeometricState<'N>`: an immutable set of named objects in ascending order of name under the caller's comparison,
  and the module `GeometricState`: `empty`, `toList`, `tryFind`, `count`.
- The module `GeometricOps`: `apply` and `canApply` (total; the name first and then the parameters in the order the
  op lists them; a refused op leaves the state as it was), `applyAll` (each op in turn, each refusal noted with its
  index), `invert` (guarded by `canApply` and by the state after the op; `Remove` has none), `compact` (a run of translations, or of rotations and
  screws about one axis given bit for bit, as one op; nothing else calls it), `encode` and `decode`, and
  `streamWitness`.
- `GeometricAlgebraOpsOperations.exactness` classifies every one of those functions, each `Pinned`, and
  `GeometricAlgebraOpsLaws.laws` is the law family a host runs: seventeen laws, ten exact and seven bounded.

**The wire.** `api/wire/Fuaran.Compute.GeometricAlgebraOps.txt` pins one document for each op, each form and each
float spelling a bit-pattern codec must keep. Every member is a parameter: an op is `{"$type": …, "name": …}` with its
parameters, a float sixteen lowercase hex digits of its IEEE-754 bit pattern and a triple three of them concatenated,
a NaN `7ff8000000000000` (`DECISIONS.md` D51 item 7's rule, which `LinAlgWire` and `RegressionWire` follow). No
coefficient travels (D85 item 10). A float on this wire is its bit pattern, not decimal text, because that is the
only spelling in the repository that replays a `-0.0` parameter to the same bits; it is not written by hand, and
D90 records that cost as an open point (G3).

**The law vectors.** `conformance/laws/geometric-algebra-ops-laws.json` is new: 244 vectors over the nine published
functions that compute, among them rotations and screws at the angles where the pinned sine and cosine are not
correctly rounded. A vector's expected state holds coefficients, as evidence: no stream carries one. Every other file
under `conformance/laws/` is byte-identical.

**What a consumer must do.** Nothing: no existing package changes. A caller that wants a history of transforms
references `Fuaran.Compute.GeometricAlgebraOps`, supplies a `Naming` for its names, and chains ops with
`OpStream.append` over `GeometricOps.streamWitness`. Three things to know before relying on it. Undo is truncate and
replay, which is exact; `invert` answers an op whose state comes back to rounding and, but for an `Add`'s, not bit
for bit, and no bound on that error is stated anywhere. A run that mixes axes is not compacted (D90 records why). And
each figure `GeometricAlgebraOpsLaws` holds is a pin of one seeded sample's largest error, with no margin: other
samples exceed it, and D90 records nine. The count of laws and of vectors is not on any baseline: this entry is where
it is stated.
