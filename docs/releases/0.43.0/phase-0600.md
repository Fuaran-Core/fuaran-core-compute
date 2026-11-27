### `Fit` estimates the general GLM — the binomial family and the full link set, with the same bits on every host (Phase 600, `DECISIONS.md` D58) — additive, within the draft's new types

**What changed.** In `Fuaran.Compute.LinearAlgebra`: `GlmFamily` gains, appended, `Binomial`, `QuasiBinomial`,
`InverseGaussian` and `NegativeBinomial of theta: float option` (`None` estimates θ by MASS's alternation); `GlmLink`
gains, appended, `Logit`, `Probit`, `Cloglog`, `Inverse`, `InverseSquare` and `Sqrt`; `LinAlgError` gains, appended,
`MeanOutOfRange of row`, `InvalidTheta of theta` and `ThetaNotConverged of alternation`; `GlmFit` gains `Theta`,
`ThetaStdError` and `Alternations`; `Glm` gains `admitted`, `defaultLink`, `linkFunction`, `inverseLink`,
`meanDerivative`, `validPredictor` and `responseInterval`; `Elementary` gains `erfc`, `normalCdf`, `normalDensity`
and `normalQuantile`, pinned (fdlibm's `s_erf.c`, Wichura's AS 241). `Glm.fit` fits twenty-nine family and link
pairs (`Glm.admitted`), Gaussian under a non-identity link by IRLS; separation of a binomial is refused by a
pinned rule. `LinearAlgebraLaws.laws` gains six laws. In `Fuaran.Compute.DataFrame`: `FitFamily` and `FitLink`
gain the same cases; the `Fit` verb evaluates every admitted pair, and a negative binomial's `Summary` table gains
`theta`, `theta_std_error` and `alternations`; `Predict` on the response scale maps through each link's inverse,
states `se_fit` by the delta method (**a behaviour change within the draft**: 597 stated it on the link scale) and
maps an interval's ends through `g⁻¹`, held inside the mean's range; a prediction interval is the identity link's
alone. Wire: the new family and link tags, an optional `theta` member for a fixed θ, and **a pair outside the
admitted table refused at decode** (it decoded before, and was refused at the step).

**The class.** Every type that moves is new in this draft — `GlmFamily`, `GlmLink`, `GlmFit`, `LinAlgError` and
`Glm` (Phases 590 to 598), `FitFamily`, `FitLink` and `PredictScale` (Phases 592 and 597) — so the surface guard,
reading against `v0.42.0`, sees additions only, though a case appended to a closed union would otherwise break
every exhaustive `match` over it. `api/Fuaran.Compute.LinearAlgebra.txt` and `api/Fuaran.Compute.DataFrame.txt`
gain the members above. The wire baselines of `Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps` and
`Fuaran.Compute.PipelineQuery` gain one document per new `FitFamily` and `FitLink` case; no existing document
moves. **It RIDES `0.43.0`.**

**The law vectors.** `conformance/laws/transform-laws.json` gains eighteen vectors after the family shapes
(iterations 163 to 180; `iterations` reads `181`): the binomial under logit, probit (summary) and cloglog
(fitted), quasi-binomial, a weighted proportion, Poisson under identity and square root, Gamma under inverse,
inverse Gaussian under inverse square, Gaussian under log, the negative binomial with θ fixed and estimated, two
response-scale predictions, and four refusals (separation as `OverflowError`; a mean out of range, a θ with no
estimate and a prediction with no mean as `FitError`). Two earlier vectors move: iteration 128's slot, which named
a Poisson identity fit as `UnsupportedFit`, is `fit-binomial-response-refused` (a pair outside the table can no
longer be carried by a vector at all), and `transform-142-predict-poisson-response`'s `se_fit` is the delta
method's. Every other earlier vector is byte-identical. `linear-algebra-laws.json` and `dataframe-parity.json` do
not move. The suite holds every admitted pair's estimates, standard errors, deviance and dispersion, and the
estimated θ and its error, to an independent eighty-digit reference computation, and the four special functions to
it in ulps (`tests/Fuaran.Core.Compute.Tests/fixtures/glm-reference/general.py`).

**What a consumer does.** Nothing to adopt; a pipeline naming a new pair gets the fit. An exhaustive `match` over
`GlmFamily`, `GlmLink`, `LinAlgError`, `FitFamily` or `FitLink` adds the new cases. A consumer reading `se_fit`
on the response scale reads the response's unit now. A language twin certifying the transform laws ports D58; the
eighteen new vectors and the two moved ones are red on it until it does.
