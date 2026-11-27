### A pipeline fits an OLS regression with the `Fit` verb (Phase 592, `DECISIONS.md` D53) — BREAKING, `union-widening`

**What changed.** `Transform` gains `Fit of FitSpec`: `FitSpec = { Response; Predictors: (string * ColExpr) list;
Intercept; GroupBy; Weights; Offset; Ridge: Slot<float> option; Family; Link; Control; Output }` with the new
public types `FitFamily` (`Gaussian | Poisson | Gamma | Tweedie of power | QuasiPoisson`), `FitLink` (`Identity |
Log`), `FitControl` (`{ MaxIter: Slot<int>; Tol: Slot<float> }`) and `FitOutput` (`Coefficients | Summary | Fitted |
Residuals`), and the module `FitSpec` (`ols`, `defaultControl`, `estimationResponse`, `exprs`, `mapExprs`,
`slotParams`, `substituteSlots`). Ordinary least squares (`Gaussian` under `Identity`) is evaluated per group by
`Fuaran.Compute.LinearAlgebra`'s streaming Givens QR — the DataFrame package now depends on it — and every other
pairing is refused by the new `EvalError.UnsupportedFit`; a spec or row the bridge refuses is the new
`EvalError.FitError`. The four outputs have closed schemas (D53 item 9), which the static walk states and
`PipelineQueryRegistry.register` admits. Incrementally, `StepIncrementality` gains `MaintainFits` and
`FallBackReason` gains `FitInputNotMaintained`; a fit last in a pipeline is maintained per group, an append
continuing the stored factorization byte for byte, and the state codec gains an optional `fit` member (emitted
only where a fit left state, so no existing state's bytes move). Wire: `{"$type":"fit",…}` (D53 item 13).

**The class.** Four published unions gain cases — `Transform`, `EvalError`, `StepIncrementality` and
`FallBackReason` — so an exhaustive `match` over any of them stops compiling (`union-widening`); every case is
appended, so no existing case's tag moves. The surface guard classes the move from the regenerated
`api/Fuaran.Compute.DataFrame.txt`. The wire baselines of `Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`
and `Fuaran.Compute.PipelineQuery` gain the `fit` document. **It RIDES `0.43.0`**, for the reason the Phase 596
entry gives: the slot is untagged, publicly unpinned, and already one minor past `v0.42.0`.

**The law vectors.** Thirteen `evalPipeline` vectors join `conformance/laws/transform-laws.json` after the function
and curve shapes, as iterations 117 to 129 (`iterations` reads `130`; every earlier vector is byte-identical),
over a fixed ten-row table: ordinary least squares, a grouped summary, a categorical term, an interaction and a
polynomial, a rank-deficient design (the aliased column named, its estimate null), two terms collinear to `1e-7`,
weights with a zero beside a ridge penalty, an offset without an intercept, fitted values over a listwise
deletion, a decimal response's residuals and summary (decimal cells), and two refusals naming their class
(`UnsupportedFit`, `FitError`). Every vector is pinned bytes; the node leg holds the same answers under Fable line
for line. A language twin (`fuaran-ts` follows) that does not yet fit reports these vectors as unsupported rather
than skipping them.

**What a consumer does.** Add the new arms to any exhaustive `match` over `Transform`, `EvalError`,
`StepIncrementality` or `FallBackReason`. A consumer that hands pipelines to this evaluator and matches none of
them changes nothing.
