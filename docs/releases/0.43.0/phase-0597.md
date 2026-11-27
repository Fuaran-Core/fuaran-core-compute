### A fitted model scores rows with prediction intervals (Phase 597, `DECISIONS.md` D56) — BREAKING, `union-widening`

**What changed.** `Transform` gains `Predict of PredictSpec`: `PredictSpec = { Fit: FitSpec; Model: PredictModel;
Scale: PredictScale; Interval: PredictInterval option }` with the new public types `PredictModel` (`Training of
DataSource | Coefficients of DataSource`), `PredictScale` (`Link | Response`), `IntervalKind` (`Confidence |
Prediction`) and `PredictInterval` (`{ Kind: IntervalKind; Level: Slot<float> }`), and the module `PredictSpec`
(`ofTraining`, `mapExprs`, `slotParams`, `substituteSlots`). The step appends float columns to its input rows:
`predicted`, and from training rows `se_fit`, `se_pred` and, with an interval, `lower` and `upper` (D56 items 1 to
5). From training rows the step fits its spec exactly as `Fit` does, so a training row's prediction is its fitted
value bit for bit; the standard errors read the fit's `R` by one triangular solve; an interval's quantile is read
from a Student t table carried as data through `Interpolate`. A coefficient table scores point estimates over
numeric terms. Every refusal is `EvalError.FitError` with a `predict:` text naming what it refuses — no `EvalError`
case is added. The static walk states the appended columns (pinned), `PipelineQuery`'s parameter census and named
sources read the step, and the incremental seam falls back by name (`StepNotRowLocal "predict"`). Wire:
`{"$type":"predict","fit":{…},"model":{"from":…,"source":…},"scale":…,"interval":{"kind":…,"level":…}?}`.

**The class.** `Transform` gains a case, appended, so an exhaustive `match` over it stops compiling
(`union-widening`); no existing case's tag moves. The surface guard classes the move from the regenerated
`api/Fuaran.Compute.DataFrame.txt`. The wire baselines of `Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`
and `Fuaran.Compute.PipelineQuery` gain the `predict` document. **It RIDES `0.43.0`**, for the reason the Phase
596 entry gives: the slot is untagged, publicly unpinned, and already one minor past `v0.42.0`.

**The law vectors.** Eleven `evalPipeline` vectors join `conformance/laws/transform-laws.json` after the fit
shapes, as iterations 139 to 149 (`iterations` reads `150`; every earlier vector is byte-identical), over the fit
table, which is also each model's training rows: the training rows scored, a grouped model over a categorical
term with a prediction interval, a weighted confidence interval on the link scale, Poisson and Gamma means with
intervals from the normal and the t rows, a coefficient table's point estimates, and five refusals naming
`FitError`. Every vector is pinned bytes; the node leg holds the same answers under Fable line for line.

**What a consumer does.** Add the `Predict` arm to any exhaustive `match` over `Transform`. A consumer that hands
pipelines to this evaluator and matches none of them changes nothing.
