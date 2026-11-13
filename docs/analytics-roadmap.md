# Analytics roadmap — the statistical capabilities Compute builds next, and in what order

A planning note, not a contract. It says which statistical, machine-learning, forecasting and
network capabilities `Fuaran.Compute` should add after the GLM (`DECISIONS.md` D53–D58), the
distributions (D79, D81) and the solvers (D77), why those and not others, and the order their
dependencies force. Each item becomes a phase with its own decision record when it is taken up;
nothing here binds a phase's design. Phase numbers already reserved in `DECISIONS.md` are named
where an item is meant to fill them. Other items are unnumbered, and the operator assigns their
numbers.

## The evidence

The demand signal is the all-time CRAN download ranking (datasciencemeta, read 2026-11-13),
restricted to packages that implement analytic methods. Data handling, plotting and plumbing are
left out. The twenty highest, with all-time downloads in millions:

| # | Package | What it does | M | # | Package | What it does | M |
|---|---|---|---:|---|---|---|---:|
| 1 | Hmisc | general statistics toolkit | 53.1 | 11 | tseries | time series | 24.5 |
| 2 | lme4 | mixed-effects models | 52.0 | 12 | caret | ML training framework | 22.7 |
| 3 | car | diagnostics, ANOVA | 46.9 | 13 | psych | psychometrics, factor analysis | 21.7 |
| 4 | igraph | networks | 45.6 | 14 | MASS | applied statistics | 21.0 |
| 5 | e1071 | SVM, naive Bayes, clustering | 42.3 | 15 | urca | unit roots, cointegration | 20.0 |
| 6 | lmtest | tests on regression models | 36.7 | 16 | rstan | Bayesian modelling | 18.9 |
| 7 | forecast | ARIMA, exponential smoothing | 29.0 | 17 | multcomp | multiple comparisons | 17.2 |
| 8 | quantmod | financial modelling | 27.5 | 18 | survival | survival analysis | 16.3 |
| 9 | TTR | technical indicators | 26.4 | 19 | pROC | ROC curves, AUC | 15.5 |
| 10 | sandwich | robust standard errors | 25.4 | 20 | nlme | (non)linear mixed models | 14.4 |

Just outside: mgcv, randomForest, kernlab and glmnet, at 12.7–13.5M each.

The ranking is biased in three ways, and the plan corrects for each:

1. **Dependencies inflate it.** forecast installs lmtest, tseries and urca, and quantmod installs
   TTR. lme4 and car install nloptr (44.8M), minqa, quantreg and pbkrtest, which users rarely
   call themselves. Those packages are counted here as **infrastructure demand** rather than as
   user demand. Their downloads show that the headline models need a multivariate optimiser and
   sparse matrices first. That is the strongest signal in the table for what has to be built
   beneath the models.
2. **Recommended packages are undercounted.** MASS, survival, nlme and mgcv ship with R, so their
   use is larger than their rank.
3. **Base R is invisible.** `stats` is never downloaded from CRAN, so `optim`, `nls`, `t.test`,
   `chisq.test`, `wilcox.test`, `cor.test`, `p.adjust`, `quantile`, `arima`, `loess` and
   `prcomp` do not appear. They are among the most used analytics in R, and the plan counts them
   with the packages whose territory they share.

## The rules every new capability keeps

These are already in force for the GLM. They are restated because they decide what an item costs.

- **Pinned or not admitted.** Every answer is byte-identical on .NET, under Fable and in every host
  that transcribes it: `+ − × ÷ √` and comparisons in one stated order, `Elementary`'s and
  `Special`'s functions instead of the host's, and no fused multiply-add. An iterative method
  states its start, its step, its tie rule and its `StoppingRule`. A cap reached is `NotConverged`,
  never a fit. A method is not admitted until it can be pinned this way.
- **Measured against a reference.** Each method is held to R's answer, or to a fifty-digit one
  where R's is the less accurate, with the agreement stated by range in its decision record.
  Each method has a method card (Phase 623).
- **A model answers tables.** Every model is a pipeline step whose outputs follow `Fit`'s shape:
  per-term rows (`Coefficients`), one summary row per group (`Summary`), and per-row output
  (`Fitted`, `Residuals`, scores). This is broom's tidy, glance and augment. Because the shape is
  shared, cross-validation, model comparison and reporting treat every family alike.
- **Randomness comes from a required seed.** Any draw reads `Rng` (D76) and nothing else.
  Splits, forests, MCMC and bootstraps replay bit for bit.
- **Refusals are named.** Separation, a singular design, a non-identifiable variance component,
  too few events, a non-stationary start: each is a typed refusal where R would warn.
- **Incremental where the algebra allows it.** An append-only method (a streaming fit, a
  Kaplan–Meier curve, a recursive filter, a running moment) continues from stored state. Every
  other method is a planner barrier, re-evaluated whole.

## Where Compute stands

| Capability | R equivalent | Compute |
|---|---|---|
| Least squares by streaming Givens QR, weights, offset, ridge | `lm` | `Regression`, `Fit` |
| GLMs: the full family and link table, negative binomial with θ estimated | `glm`, `MASS::glm.nb` | `Glm` (D55, D57, D58) |
| HC0–HC3 sandwich standard errors | `sandwich::vcovHC` | `robustStandardErrors` (D57) |
| Prediction with standard errors and intervals | `predict.lm`, `predict.glm` | `Predict` (D56, D79) |
| Normal, t, chi-square, F: cumulative distribution function and quantile | `pnorm`, `qt`, … | `Distributions` (D79, D81) |
| Gamma, beta, incomplete gamma and beta, polygamma | `lgamma`, `pbeta`, … | `Special` (D78, D81) |
| Root finding, golden section, damped iteration | `uniroot`, `optimize` | `Solvers` (D77) |
| Dense QR, pivoted QR, rank | `qr` | `LinearAlgebra` (D49) |
| k-means with k-means++, silhouette | `kmeans`, `cluster` | `KMeans` (D76) |
| Bootstrap with percentile intervals, clustered | `boot` | `Bootstrap` (D76) |
| Lag, lead, rolling and cumulative windows | `TTR::SMA`, `zoo` | `Window` |
| Monotone interpolation, user curves | `splinefun` | `Interpolate`, `Define` (D48) |

Measured against the list, this covers the base of lmtest and sandwich, MASS's GLM side, the
clustering in e1071 and the simple-moving-average part of TTR.

## The plan

Four stages. An item in one stage depends only on items in earlier stages, or in its own stage
where an arrow says so. Within a stage, the items are listed most valuable first.

### Stage 1 — finish what the fitted model already holds

These items need no new numerical primitive. They read the fit's `R`, its dispersion and degrees
of freedom, and the distributions already pinned. Together they are the largest share of user
demand for the least work.

**1.1 Inference on a fitted model** (car, lmtest, multcomp's simple half; `Regression`; the
Phase 619 slot).
- The coefficient covariance as a table (`vcov`).
- Wald tests of linear hypotheses `Lβ = c`, singly and jointly (F or chi-square by family).
- Log-likelihood, AIC, BIC and deviance tests between nested fits (`anova(m0, m1)`, `lrtest`).
- ANOVA tables of types I, II and III. Type III needs 1.2.
- Influence and collinearity: leverage, studentised residuals, Cook's distance, DFBETAS, VIF and
  GVIF.
- Residual tests: Breusch–Pagan, Durbin–Watson, Breusch–Godfrey and Ljung–Box (the chi-square is
  pinned already).
- More sandwich estimators: cluster-robust (CR0 to CR3), Newey–West HAC with a stated bandwidth
  rule.
- Multiplicity adjustment as a column function: Bonferroni, Holm, Hochberg, Hommel,
  Benjamini–Hochberg and Benjamini–Yekutieli (`p.adjust`).

Done when: each statistic matches `car`, `lmtest`, `sandwich` or `stats` on that package's own
examples, to a stated tolerance.

**1.2 Contrasts beyond treatment coding** (`DataFrame`'s term coding).
- Sum-to-zero, Helmert, orthogonal polynomial and a user-given contrast matrix for a categorical
  term, named in the `Fit` spec and on the wire.
- `Predict` codes new rows by the fit's own contrast.

**1.3 Densities and the discrete distributions** (`Distributions`; the Phase 629 slot).
- Densities for the four distributions already pinned.
- Binomial, Poisson, negative binomial, hypergeometric, geometric: probability mass function,
  cumulative distribution function and quantile.
- Gamma, beta, exponential, lognormal, Weibull, logistic, uniform: density, cumulative
  distribution function and quantile, from `Special`.
- The inverse incomplete gamma function lands in `Special`, as D78 item 1 anticipates.

**1.4 Classical tests and descriptives** (Hmisc, psych, base `stats`; a `Test` step in
`DataFrame`, answering one row per test per group). Needs 1.3.
- One- and two-sample t-tests, the Welch t-test and the paired t-test; the F test for variances.
- Wilcoxon signed-rank and rank-sum tests, with the exact null distribution by a pinned
  recursion below a stated size and the normal approximation above; Kruskal–Wallis.
- Contingency tables: Pearson chi-square with and without continuity correction, the G-test,
  Fisher's exact test on 2×2 tables (and r×c by network algorithm, later), McNemar's test.
- Proportions: exact (Clopper–Pearson) and Wilson intervals.
- Correlation: Pearson with Fisher-z interval, Spearman, Kendall's τ-b; a correlation-matrix
  step.
- Normality: Shapiro–Wilk (Royston's pinned coefficients), Anderson–Darling.
- Quantiles under a named rule (R's types 1–9, type 7 the default), weighted quantiles,
  weighted mean and variance aggregates, skewness and kurtosis, and a `describe` summary.

**1.5 Classification and regression metrics** (pROC, caret's metrics; `DataFrame`).
- ROC curve as a table, AUC with DeLong's variance and interval, two correlated AUCs compared.
- Confusion matrix and its derived rates at a threshold; log loss, Brier score, a calibration
  table by bins.
- RMSE, MAE, MAPE and R² as aggregates over a predicted and an observed column.

**1.6 Recursive filters and technical indicators** (TTR; a `Window` function).
- An exponential moving average with a stated α or span and a stated seed rule. It is the first
  window whose state is a recursion, and it is incremental on append.
- Indicators that follow from it: MACD, RSI by Wilder's smoothing, Bollinger bands, average true
  range.
- The same kernel is the geometric adstock of a response-curve model, which makes it a building
  block of marketing-mix work beside the Hill curve `Define` already expresses.
- The autocorrelation and partial autocorrelation functions as a table (`acf`, `pacf`).

**1.7 Spline basis terms** (mgcv's and Hmisc's simplest use; `Fit` term coding).
- B-spline, natural cubic and restricted cubic (`rcs`) basis expansions as `Fit` terms, with knots
  stated or placed at pinned quantiles.
- Together with the ridge penalty that exists, this gives penalised regression splines at a fixed
  smoothing parameter. Choosing that parameter is item 3.6.

### Stage 2 — the missing foundations

Every model family in stage 3 waits on one or more of these. The ranking shows it: lme4 cannot run
without nloptr and minqa.

**2.1 Symmetric factorisations** (`LinearAlgebra`; the Phase 622 slot).
- Cholesky, with a pivoted form that reports rank by D49's rule.
- The symmetric eigendecomposition and the singular value decomposition (D75 already reserves
  SymEig and SVD).
- Pinned by fixed sweep order (Jacobi, or Householder tridiagonalisation then implicit QL with a
  stated shift), with signs normalised so that vectors are reproducible.

**2.2 The multivariate optimiser** (`Solvers`). The single most important foundation.
- Nelder–Mead, with its simplex, coefficients and shrink rule stated.
- BFGS and L-BFGS-B, with box bounds, a pinned line search (More–Thuente or a stated Armijo
  backtracking) and a stated history length.
- A derivative-free trust-region method with bounds (BOBYQA-class). This is what lme4's default
  uses, and what a profiled REML deviance needs.
- Newton with a supplied Hessian, step-halving, for Cox regression and multinomial models.
- A finite-difference gradient and Hessian with a pinned step rule, for callers without an
  analytic one.
- Every method takes the existing `StoppingRule` and answers which arm stopped it.

**2.3 Sparse and block-structured matrices** (`LinearAlgebra`).
- A compressed-column sparse matrix, sparse Cholesky with a pinned fill-reducing ordering
  (approximate minimum degree), and block-diagonal products.
- Needed by mixed models with many levels (3.1) and by elastic net on wide designs (3.5).
  It could start as a block-diagonal special case if 3.1 comes first.

**2.4 Resampling and tuning** (caret's real value; a `DataFrame` step). Needs 1.5.
- Seeded splits: k-fold, repeated k-fold, stratified, grouped, leave-one-out, and the rolling
  origin for time series.
- A `CrossValidate` step that runs a `Fit`/`Predict` spec (or any model step that follows the
  table convention) per fold, and a `Tune` step over a grid of spec parameters. Both answer the
  metrics of 1.5 per fold, per setting, and a summary.
- Because the specs are data, this is a pipeline over pipelines, and it should come before more
  model types: every later family gets evaluated by it for free.

**2.5 New column semantics** (`DataFrame`; operator decisions before code).
- A **censored response**: time, event indicator, and optionally entry time (left truncation).
  Needed by 3.2.
- A **regular time index**: a frequency, gaps refused or filled by rule, seasonality period.
  Needed by 3.3.
- Both are additions to the schema contract and the wire codec, so they are the slowest items
  to decide, and should be decided early.

### Stage 3 — the headline model families

**3.1 Mixed-effects models** (lme4, nlme; #2 and #20). Needs 2.1, 2.2 and possibly 2.3.
- Linear mixed models by profiled REML and ML, with random intercepts and slopes, crossed and
  nested grouping, and the relative-covariance-factor parameterisation of lme4.
- Answers: fixed effects, variance components, conditional modes (BLUPs) and their variances,
  fitted values at the population and the group level.
- Inference: Satterthwaite or Kenward–Roger degrees of freedom (pbkrtest's territory), the
  likelihood-ratio test between nested fits, profile intervals.
- Then GLMMs by the Laplace approximation, reusing `Glm`'s families and links. Adaptive
  Gauss–Hermite quadrature later.
- nlme's residual correlation structures (AR(1), compound symmetry) and variance functions
  follow after that.

**3.2 Survival analysis** (survival). Needs 2.5's censored type; Cox needs 2.2.
- Kaplan–Meier with Greenwood's variance and pinned interval transforms; Nelson–Aalen;
  stratified curves. These are a sorted scan, so they can be incremental on append.
- The log-rank and the Peto–Peto tests (the chi-square is pinned).
- Cox proportional hazards by Newton on the partial likelihood, with Efron and Breslow ties,
  strata, robust standard errors, the Schoenfeld-residual proportionality test, and baseline
  hazard for prediction.
- Parametric survival regression (Weibull, exponential, log-normal) is a later addition.

**3.3 Time series and forecasting** (forecast, tseries, urca). Needs 2.5's time index and 2.2.
- A pinned Kalman filter and smoother over a state-space model, as the one kernel beneath both
  families.
- Exponential smoothing (ETS) with additive and multiplicative error, trend and season, fitted by
  likelihood, selected by AICc.
- ARIMA and seasonal ARIMA by exact likelihood; automatic order selection by a stated stepwise
  search.
- Forecasts with prediction intervals, as a `Predict`-shaped table.
- Unit-root and stationarity tests: augmented Dickey–Fuller, KPSS and Phillips–Perron, with
  MacKinnon's response-surface critical values pinned as data; Johansen cointegration later.
- GARCH(1,1) after the rest.

**3.4 Multivariate methods** (psych, MASS, e1071). Needs 2.1; factor analysis needs 2.2.
- Principal components on the covariance or correlation matrix: loadings, scores, variance
  explained.
- Factor analysis by maximum likelihood and minimum residual, with varimax and promax rotations.
- Linear and quadratic discriminant analysis.
- Mahalanobis distance; hierarchical clustering with stated linkage and tie rules.
- Cronbach's α and ω, the psychometric scale summaries.

**3.5 Penalised and robust regression** (glmnet, quantreg, MASS).
- Elastic net for the Gaussian and the binomial and Poisson families by coordinate descent, with
  a pinned cycle order, warm starts along a λ path and cross-validated λ through 2.4.
- Quantile regression by a pinned simplex or interior-point method (quantreg is already installed
  by car, so the demand is there).
- Huber and bisquare M-estimation (`MASS::rlm`), which reuses `Damped.iterate`.
- Nonlinear least squares (`nls`) by Gauss–Newton or Levenberg–Marquardt over a `Define`d
  function. This lets a Hill curve's parameters be fitted rather than given.
- Multinomial and ordinal (proportional-odds) logistic regression (`nnet::multinom`,
  `MASS::polr`).

**3.6 Smoothing-parameter selection** (mgcv). Needs 1.7 and 2.2.
- Generalised cross-validation and REML for the penalty on 1.7's bases, which turns them into
  additive models with automatic smoothness. A full GAM with tensor products comes later.
- LOESS with a pinned neighbour and tie rule.

### Stage 4 — further out

- **Trees and ensembles** (randomForest, rpart). CART with stated split, tie and pruning rules;
  random forests and gradient boosting, seeded per tree. Reproducible forests would be
  distinctive, but they cost a lot to pin and the gradient-boosted versions are better served
  elsewhere today.
- **Kernel methods** (e1071, kernlab). SVM by SMO with a pinned working-set choice; naive Bayes,
  which is nearly free from `GroupBy` and 1.3's densities and could move earlier.
- **Networks** (igraph). A graph package over edge-list tables: degree, shortest paths,
  connected components, PageRank and betweenness, then community detection (Louvain, seeded).
  It is a different data model, so it is a package of its own.
- **Bayesian modelling** (rstan). Conjugate updates and Laplace approximations first; HMC with
  NUTS much later. Seeded replay suits MCMC, but the build is large and serves few users of the
  current surface.
- **Simultaneous inference** (multcomp's full method). Tukey and Dunnett adjustments need the
  multivariate t integral (Genz's method, seeded); 1.1's step-down methods cover most use until
  then.

## The dependency graph

```
Stage 1   1.1 inference ── 1.2 contrasts          1.3 distributions ── 1.4 tests
          1.5 metrics      1.6 recursive filters  1.7 spline bases
                │                 │                      │
Stage 2   2.4 resampling   2.5 time index,        2.1 Cholesky, eigen, SVD
          (needs 1.5)          censored type      2.2 multivariate optimiser
                                                  2.3 sparse matrices
                │                 │                      │
Stage 3   3.5 elastic net  3.2 survival (2.5, 2.2) 3.1 mixed models (2.1, 2.2, 2.3)
          (2.4)            3.3 time series (2.5, 2.2, 1.6)
                                                  3.4 multivariate (2.1, 2.2)
                                                  3.6 smoothing (1.7, 2.2)
```

The packages stay where D74 and D75 put them: factorisations in `LinearAlgebra`, optimisers in
`Solvers`, distributions in `Distributions`, models in `Regression` (or new packages beside it
for survival, time series and multivariate methods, if the size of each argues for it), and the
steps a pipeline names in `DataFrame`.

## Decisions the operator owns

1. **Where the model families live.** One `Regression` package, or `Survival`, `TimeSeries` and
   `Multivariate` packages beside it. The graph above works either way. The question is how much
   a consumer of GLMs should carry.
2. **The censored and time-index column semantics** (2.5). These change the schema contract and
   the wire, so they need deciding before 3.2 and 3.3 start, not when they start.
3. **Whether the optimiser pins a derivative-free trust region** (2.2) or whether mixed models
   use L-BFGS-B on the profiled deviance and accept differing from lme4's path (but not its
   optimum) where the surface is flat.
4. **Which domain leads stage 3.** The ranking puts mixed models first. If the platform's lead
   use is response curves and marketing mix, nonlinear least squares, constrained optimisation
   and the adstock filter move ahead of mixed models and survival.
5. **What the platform does not attempt.** Gradient boosting and deep learning are left to other
   tools, and so is data acquisition (quantmod's other half).

## Out of scope

Data acquisition (quantmod's downloaders), plotting, report generation, and data handling already
owned by the verb set (data.table, zoo, xts and broom's territory). Spatial analysis (sf) is a
separate decision. It would rank first among analytic packages if counted (82.4M), but it needs a
geometry type and its own algebra, not a statistical method.
