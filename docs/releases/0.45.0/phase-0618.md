### The normal, t, chi-square and F distributions are pinned in `Fuaran.Compute.Distributions`, and `Predict` reads the t from it (Phase 618, `DECISIONS.md` D79) — breaking

The draft already carries two breaking moves, so this change RIDES `0.45.0`.

**What changed.** A new package, `Fuaran.Compute.Distributions`, over `Fuaran.Compute.Solvers` and
`Fuaran.Compute.Elementary` alone: `Normal.cdf`, `Normal.quantile`, `StudentT.cdf`, `StudentT.quantile`,
`ChiSquare.cdf`, `ChiSquare.quantile`, `FisherF.cdf`, `FisherF.quantile`, each `float`s in and a `float` out, with
`DistributionsOperations.exactness` (every one `Pinned`) and `DistributionsLaws.laws`. Its baseline is the new
`api/Fuaran.Compute.Distributions.txt`. `Normal.cdf` and `Normal.quantile` are `Elementary.normalCdf` and
`Elementary.normalQuantile` under another name, bit for bit; Elementary's surface does not move.
`Fuaran.Compute.DataFrame` now depends on the package. Its own public surface does not move
(`api/Fuaran.Compute.DataFrame.txt` and every other file under `api/` and `api/wire/` are unchanged): the t table
it carried was internal, and is deleted.

**What breaks: a `Predict` interval's bounds, and one refusal.** An interval's quantile was a row of a t table at
the tabled levels `0.5`, `0.8`, `0.9`, `0.95`, `0.98`, `0.99` and `0.999`, read linearly in `1/ν` between its rows.
It is now `StudentT.quantile` at the residual degrees of freedom, and `Normal.quantile` where the family fixes its
dispersion, read at the lower tail `(1 − level) / 2`. So:

- at a `ν` and level the table carried (`ν` from 1 to 30, 40, 50, 60, 80, 100, 120, 200, 500, 1000, and the normal
  row), `lower` and `upper` move by a few units in the last place: the table's entries were correctly rounded and
  the function is within 24 units of them;
- at a `ν` between the table's rows, or above 1000, they move in the fifth significant figure, from the
  interpolated reading to the quantile (at `ν = 35` and a level of `0.95` the multiplier falls from
  `2.03015984716` to `2.03010792825`);
- a level the table did not carry was refused as `FitError` (`the level 0.97 is not tabled`) and is now answered.
  A level that is not strictly between zero and one is refused as `FitError`
  (`the level 1 is not a coverage; a level lies strictly between 0 and 1`).

`predicted`, `se_fit` and `se_pred` do not move, and nothing moves in a `Predict` with no interval.

**The wire.** None of its own: the package encodes nothing, and is recorded so beside the other packages that
emit no document. The `Predict` step's wire form is unchanged; `level` was always a float slot.

**The law vectors.** `conformance/laws/distributions-laws.json` is new: 215 vectors over authored hard inputs
(the Cauchy distribution, degrees of freedom to a million, tails to `1e-10` and beyond, the arms' boundaries, the
special-value table), 143 of them with a fifty-digit reference and the function's distance from it. In
`conformance/laws/transform-laws.json` four rows and the head's `description` move and every other row is the line
it was, every id kept:

- `transform-142-predict-poisson-response`, `transform-143-predict-gamma-link` and
  `transform-175-predict-logit-response`: `lower` and `upper` by at most seven units in the last place;
- `transform-145-predict-level-refused`: its level is `1` where it was `0.97`, and it is still refused as
  `FitError`.

The two gaussian interval rows (`transform-140` and `transform-141`) answer the bits they answered.
`dataframe-parity.json`, `elementary-laws.json`, `linear-algebra-laws.json`, `regression-laws.json` and
`solvers-laws.json` are byte-identical.

**What a consumer must do.** A consumer that stores or compares `Predict` interval bounds (a forecast surface
that pins them, a snapshot test) re-takes them at `0.45.0`: they differ from `0.44.0`'s in the last digits at the
tabled degrees of freedom and in the fifth figure between them. A consumer that offered only the seven tabled
levels may offer any. A host that certifies against `transform-laws.json` takes the four rows above; one that
ports the distributions certifies against `distributions-laws.json`. A caller that wants a distribution function
opens `Fuaran.Compute.Distributions` and writes `StudentT.cdf`. The accuracy is stated by range in D79 item 9 and
is a measurement: tens of units in the last place at degrees of freedom below a hundred, and from there the
special functions' own error at a large parameter, which leaves a t tail good to fourteen digits at a thousand
degrees of freedom and eleven at a million.
