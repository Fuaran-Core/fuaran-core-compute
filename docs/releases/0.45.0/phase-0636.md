### The incomplete beta function keeps its accuracy at a large parameter, and `Special` publishes its complement and that complement's inverse (Phase 636, `DECISIONS.md` D81) — additive

The draft already carries breaking moves, so this change RIDES `0.45.0`.

**What changed.** `Fuaran.Compute.Elementary`'s `Special` gains `regularizedBetaComplement a b x`, which is
`1 − I_x(a, b)` computed and not subtracted where it is small, and `inverseRegularizedBetaComplement a b q`, the `x` at
which it is `q`; each `float`s in and a `float` out, each with a `Pinned` row in `ElementaryOperations.exactness`.
`ElementaryLaws.laws` gains one law over them. The package's baseline, `api/Fuaran.Compute.Elementary.txt`, gains those
two functions and loses nothing; `api/Fuaran.Compute.Distributions.txt` and every other file under `api/` and
`api/wire/` are unchanged.

**What answers other bits.** `Special.regularizedBeta` and `Special.inverseRegularizedBeta` where the two parameters
sum to `128` or more: the continued fraction is read in another form there, which is what the repair is. Below that
sum both answer the bits they answered. `StudentT.cdf`, `StudentT.quantile`, `FisherF.cdf` and `FisherF.quantile` answer
other bits wherever they read the incomplete beta function at such parameters (degrees of freedom from about 255 for
the t, and summing to 256 for the F), and, at any degrees of freedom, where they now read the complement or its
inverse in place of the forms `0.45.0`'s draft had: the t's tail and the F's upper probabilities where the argument
is the small one, and the t's quantile below `p = ¼` and the F's where the root is above one half. `Normal`,
`ChiSquare` and every other function of `Special` answer the bits they answered.

**The wire.** None. The packages encode nothing.

**The law vectors.** `conformance/laws/elementary-laws.json` gains a head member, `complements`, and 58 vectors after
the ones it held; five of those it held moved, each a `regularizedBeta` vector with a parameter sum of `128` or more
(`beta-inc-large-at-mean`, `beta-inc-large-below-mean`, `beta-inc-very-large-at-mean`, `beta-inc-very-large-skewed`,
`beta-inc-small-and-large`). `conformance/laws/distributions-laws.json` gains four vectors after the ones it held, and
34 of those it held moved: eleven of `StudentT.cdf` (`t-cdf-cauchy-minus-one`, the three at a thousand degrees of
freedom, the two at ten thousand, the two at forty thousand and the three at a million), fourteen of
`StudentT.quantile` (`t-quantile-thirty-95-percent`, `t-quantile-thirty-five-97.5-percent`,
`t-quantile-hundred-twenty-99.5-percent`, the four at a thousand, the two at ten thousand, the two at forty thousand
and the three at a million), five of `FisherF.cdf` (`f-cdf-hundred-two`, `f-cdf-thousand-two-small`,
`f-cdf-three-ten-thousand` and the two at ten thousand both) and four of `FisherF.quantile`
(`f-quantile-thousand-one-1e-10`, `f-quantile-two-tenth-30-percent`, `f-quantile-three-ten-thousand-95-percent`,
`f-quantile-ten-thousand-both-median`). D81 item 10 gives each one's distance from its reference before and after.
Every other vector of the two files is the line it was, but for the comma that now follows the last of each.
`dataframe-parity.json`, `linear-algebra-laws.json`, `regression-laws.json`, `solvers-laws.json` and
`transform-laws.json` are byte-identical: no `Predict` interval row of that file moved.

**What a consumer must do.** Nothing to compile. A caller that wants an upper tail of the beta distribution, or of
anything built on it, reads `Special.regularizedBetaComplement` in place of one minus the function, and
`Special.inverseRegularizedBetaComplement` where it holds an upper probability. A consumer that stores or compares a t
or F probability or quantile re-takes it at `0.45.0`: at degrees of freedom in the hundreds and beyond it moves by
the error the function had (a relative `3e-11` at a million degrees of freedom, `1e-13` at a thousand), and below
that by units in the last place. A consumer that stores a `Predict` interval re-takes it too: over degrees of
freedom of 1 to 130 and levels from one half to `0.999`, 580 of 1,040 quantiles answer other bits, by 34 units in the
last place at most and by 8 at most to thirty degrees of freedom. A host that transcribes `Special` transcribes the contracted fraction, its `λ` and
the small-parameter series beside the fraction it had, and the threshold between them (D81 items 2 to 5); one that
transcribes the distributions replaces the t's and the F's arms with the direct reads (D81 item 8). The accuracy is
stated by range in D81 item 9 and is a measurement: with one parameter large, up to a million, and the other below
one, the incomplete beta function and its complement are within 64 units in the last place of a fifty-digit
reference; the t's functions within 140 from a hundred degrees of freedom to a million.
