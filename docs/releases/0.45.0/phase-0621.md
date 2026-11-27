### A pinned generator, `Rng`, and `KMeans` and `Bootstrap` steps seeded from it (Phase 621, `DECISIONS.md` D76) — `union-widening` on `Fuaran.Compute.DataFrame`, additive elsewhere

The draft already carries two breaking moves, so this change RIDES `0.45.0`.

**What changed.** `Fuaran.Compute.Elementary` gains the module `Rng` (6 additions to `api/Fuaran.Compute.Elementary.txt`,
nothing removed): `Rng.ofSeed`, `Rng.next`, `Rng.uniform53`, `Rng.below` and the stream type `Rng.Stream`, a pinned
SplitMix64 whose every draw is the same sixty-four bits on every host, with four rows in
`ElementaryOperations.exactness` and two laws in `ElementaryLaws.laws`. `Fuaran.Compute.DataFrame` gains two `Transform`
cases, `KMeans of KMeansSpec` and `Bootstrap of BootstrapSpec`, with `KMeansOutput`, `BootstrapOutput`, the modules
`KMeansSpec` and `BootstrapSpec`, and two `EvalError` cases, `KMeansError` and `BootstrapError` (56 additions to
`api/Fuaran.Compute.DataFrame.txt`, nothing removed or changed). `KMeans` clusters per group by k-means++ and Lloyd's
passes and answers the assignments, the centres or a summary; `Bootstrap` resamples per group, by row or by whole
cluster, and answers the replicates or a summary with a percentile interval. Both take a REQUIRED seed and answer the
same bytes on every host. Both are planner barriers, and the incremental seam re-evaluates them whole.

**The wire.** Two new step tags, `kmeans` and `bootstrap`; a literal seed is its decimal digits in a JSON string. Nine
documents are added to each of `api/wire/Fuaran.Compute.DataFrame.txt`, `api/wire/Fuaran.Compute.PipelineQuery.txt` and
`api/wire/Fuaran.Compute.ColumnOps.txt` (class `additive`); no document that was there moved. A pipeline encoded by
`0.44.0` decodes unchanged. A decoder of `0.44.0` refuses a pipeline that carries either new step, by its tag.

**The law vectors.** `elementary-laws.json` gains three `rng` vectors and a `generator` head member; the 110 vectors
that were there are byte-identical, the last gaining only the comma that separates it from the new rows.
`transform-laws.json` gains twenty vectors (`transform-181` to `transform-200`), a `seeded` head member, and its
`iterations` count moves from 181 to 201; the 194 vectors that were there are byte-identical. `dataframe-parity.json`,
`linear-algebra-laws.json` and `regression-laws.json` do not move.

**What a consumer must do.** An exhaustive `match` over `Transform` or over `EvalError` needs the new cases: that is the
`union-widening` class, and it is the only source change. A host that certifies against `transform-laws.json` reads 201
iterations and must implement both steps and the generator to pass the twenty new vectors; one that certifies against
`elementary-laws.json` reads the three `rng` vectors by their `op`. Nothing else moves: no existing pipeline's bytes, no
existing wire document, no existing answer.
