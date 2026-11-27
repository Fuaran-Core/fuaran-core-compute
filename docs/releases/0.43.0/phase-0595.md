### A log-link GLM fits by IRLS with the same bits on every host (Phase 595, `DECISIONS.md` D55) — additive, within the new package and the new verb

**What changed.** `Fuaran.Compute.LinearAlgebra` gains `Glm`: `Glm.fit` (family, link, control, design,
response, prior weights, offset) fits `Gaussian` under `Identity` in one weighted least-squares solve and
`Poisson` and `Gamma` under `Log` by iteratively reweighted least squares over the streaming Givens QR, every
`exp` and `ln` through `Elementary`, so the coefficients, the iteration count and the convergence decision are
the same bits on every host; `Glm.nullDeviance` answers the null model's deviance. New types: `GlmFamily`
(`Gaussian | Poisson | Gamma`), `GlmLink` (`Identity | Log`), the opaque `GlmControl` (`GlmControl.create`,
`GlmControl.defaults`) and `GlmFit`. `LeastSquares.standardErrors` states the classical standard errors once.
`LinAlgError` gains, appended, `InvalidResponse of row`, `Separation of row` and `UnsupportedModel of family *
link`. `Operations.exactness` classifies the four new operations `Pinned`, and `LinearAlgebraLaws.laws` gains
five laws. In `Fuaran.Compute.DataFrame` the `Fit` verb evaluates `Poisson` and `Gamma` under `Log`, which
`UnsupportedFit` refused: `Summary` reports the iterations, the deviance, the null deviance and the dispersion
by family; under the log link a coefficient's unit is `1 / unit(x)` and the deviances are floats for any
response. A positive ridge penalty under a family but `Gaussian` is a `FitError`; a response outside its family
and a fit that does not converge are `FitError`s, and separation an `OverflowError`, each naming its row or
iteration. With an intercept and an offset, the null deviance is the intercept-only model's with that offset
(R's rule), for least squares too; without an offset it is unchanged to the bit.

**The class.** The surface guard reads `Fuaran.Compute.LinearAlgebra` as part of the first snapshot against
`v0.42.0`, which carries no baseline for it: everything added here, the `LinAlgError` cases included, is new in
this draft. `api/Fuaran.Compute.DataFrame.txt` and every wire baseline are byte-identical — the change in the
verb is internal, and the `Fit` step it changes is itself new in this draft (Phase 592), so no released consumer
saw the refusal lift or the null deviance move. **It RIDES `0.43.0`.**

**The law vectors.** `conformance/laws/transform-laws.json` gains nine vectors after the fit shapes (iterations
130 to 138): Poisson coefficients, a grouped Poisson summary, Poisson means under an `ln` offset, Gamma
coefficients over a categorical term, a weighted Gamma summary, and four refusals by class. Vector 128, which
pinned Poisson under `Log` as `UnsupportedFit`, is renamed `fit-unsupported-refused` and pins Poisson under
`Identity`. Every other vector is byte-identical. The suite holds the file fresh on .NET and the pooled node leg
renders every fit vector under Fable, line for line; `linear-algebra-laws.json` does not move.

**What a consumer does.** Nothing to adopt; a pipeline that named a log-link Poisson or Gamma fit now gets it. A
language twin certifying the transform laws ports `Glm` over its `Elementary` transcription (D55 items 2 to 6);
the nine new vectors are red on it until it does.
