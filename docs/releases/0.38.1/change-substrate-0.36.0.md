### The substrate pin moves to `Fuaran.Core.*` `0.36.0` — a dependency raise; no public surface moves

**What changed.** Every `Fuaran.Core.*` package this repository pins moves from `0.35.1` to the released
`0.36.0` (`FuaranCoreVersion` in `Directory.Packages.props`), so the four packages at `0.38.0` carry a
`0.36.0` floor on the substrate. The one substrate move that reached source here is the substrate's
`Query` record gaining `Where` and `OrderBy`: the two full `Query` literals (the reference pair in
`PipelineQueryConformance` and the suite's `query` helper) now name both, empty, which is the
declaration they described before. The substrate's `QueryError` gained an `UnknownColumn` case of the
same shape as `EvalError.UnknownColumn`; where both are in scope (the Fable compile of the node leg) the
three unqualified constructions in `Incremental` now name `EvalError.UnknownColumn`. No exhaustive match
here met a widened union (`QueryError`, `ResolveFault`, `PipelineEvalError`), no codec builder or
`Codec.write` call and no obsolete kit name is used, and the proof-leg kit and prover pin were re-copied
from the substrate as `copies.json` requires (the pin gains a Linux entry for the same prover release).
No `api/` baseline moved.

**The wire surface — `additive`.** A `pipelineQuery` document embeds the substrate's `query`, which now
carries `where` (with its `equalTo` discriminator) and `orderBy`; `api/wire/Fuaran.Compute.PipelineQuery.txt`
is regenerated and records the class `additive`. A reader of an older `pipelineQuery` document is
unaffected: both members are empty by default.

**Migrating.** A consumer raises `Fuaran.Core.*` to `0.36.0` with this version and takes the substrate's
own `0.36.0` source breaks (its `STABILITY.md`) for any substrate type it uses directly: a `PipelineQuery`
built from a full `Query` literal names `Where = []` and `OrderBy = []`.
