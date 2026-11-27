### `Fit` fits Tweedie and quasi-Poisson and states robust standard errors HC0 to HC3 (Phase 598, `DECISIONS.md` D57) — additive, within the draft's new types

**What changed.** In `Fuaran.Compute.LinearAlgebra`: `GlmFamily` gains, appended, `Tweedie of power: float` and
`QuasiPoisson`, both fitted by `Glm.fit` under `Log` — quasi-Poisson as Poisson's iteration bit for bit with
Pearson's dispersion, Tweedie from R's `statmod` start with working weights `w·μ^(2−p)` through `Elementary.pow`,
its dispersion Pearson's; `LinAlgError` gains, appended, `InvalidPower of power` (a power strictly between zero and
one, or not finite); `GlmFit` gains `WorkingWeights` and `WorkingResiduals`; the new `RobustVariant` (`HC0 | HC1 |
HC2 | HC3`) names a sandwich, which `LeastSquares.robustStandardErrors` and `Glm.robustStandardErrors` state — one
pass over the rows of positive weight in a pinned order, the scores in the working weights and residuals, so the
errors are the same bits on every host. `LinearAlgebraLaws.laws` gains four laws. In `Fuaran.Compute.DataFrame`:
the `Fit` verb evaluates `Tweedie` and `QuasiPoisson` under `Log`, which `UnsupportedFit` refused; the new
`FitStdErrors` (`Classical | HC0 | HC1 | HC2 | HC3`, with `FitStdErrors.Robust` = `HC3`, the default variant) is a
new `FitSpec` field, `StdErrors`, classical in `FitSpec.ols`, which moves the `Coefficients` table's `std_error` and
`t` and nothing else. A Tweedie power outside the family, robust errors beside a positive ridge penalty, and a
`Predict` whose model's errors are robust are refused as `FitError`. Wire: the fit step's optional `stdErrors`
member — `hc0` … `hc3`, emitted only when not classical; `classical` and `robust` (which decodes to `hc3`) are
read too.

**The class.** Every type that moves is new in this draft: `GlmFamily`, `GlmFit` and `LinAlgError` (Phases 590
to 595) and `FitSpec` (Phase 592), so the surface guard, reading against `v0.42.0`, sees additions only — the new
`FitSpec` field included, which would otherwise be a `record-widening`. `api/Fuaran.Compute.LinearAlgebra.txt`
and `api/Fuaran.Compute.DataFrame.txt` gain the members above. The wire baselines of `Fuaran.Compute.DataFrame`,
`Fuaran.Compute.ColumnOps` and `Fuaran.Compute.PipelineQuery` gain one document per `FitStdErrors` case; no
existing document moves, because a classical spec emits no `stdErrors`. **It RIDES `0.43.0`.**

**The law vectors.** `conformance/laws/transform-laws.json` gains thirteen vectors after the predict shapes
(iterations 150 to 162; `iterations` reads `163`): a grouped quasi-Poisson summary, Tweedie at `p = 1.5` over
coefficients and a weighted summary, least squares under HC0, HC1 (grouped), HC2 (weighted, a zero weight) and
HC3 (a categorical term), Poisson and Tweedie coefficients with robust errors, and four refusals naming
`FitError`. Every earlier vector is byte-identical; the pooled node leg renders the new ones under Fable line for
line. `linear-algebra-laws.json` and `dataframe-parity.json` do not move. The suite holds the estimates,
dispersions, deviances and all five kinds of standard error to an independent sixty-digit reference computation
(`tests/Fuaran.Core.Compute.Tests/fixtures/glm-reference/`).

**What a consumer does.** Nothing to adopt; a pipeline that named Tweedie or quasi-Poisson under `Log` now gets the
fit, and robust errors are opt-in. A full-literal `FitSpec` construction adds `StdErrors =
FitStdErrors.Classical`. A language twin certifying the transform laws ports D57 items 1 to 4 and 7; the thirteen
new vectors are red on it until it does.
