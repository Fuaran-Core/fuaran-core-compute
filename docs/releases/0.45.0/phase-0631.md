### The pinned special functions, `Special`, in `Fuaran.Compute.Elementary` (Phase 631, `DECISIONS.md` D78) — additive

The draft already carries two breaking moves, so this change RIDES `0.45.0`.

**What changed.** `Fuaran.Compute.Elementary` gains the module `Special`: `gammaLn`, `gamma`, `betaLn`, `beta`,
`regularizedGammaP`, `regularizedGammaQ`, `regularizedBeta`, `inverseRegularizedBeta`, `polygamma`, `digamma` and
`trigamma`, each `float`s in and a `float` out, and each with a `Pinned` row in `ElementaryOperations.exactness`.
`ElementaryLaws.laws` gains six laws over them. The package's baseline, `api/Fuaran.Compute.Elementary.txt`, gains the
module and its eleven functions and loses nothing; no other file under `api/` or `api/wire/` moves. The package now
reads `Fuaran.Compute.Solvers`, which it already referenced: the series stop on its `StoppingRule` and the inverse is
its `Root.bracketedNewton`.

**The wire.** None. The package encodes nothing.

**The law vectors.** `conformance/laws/elementary-laws.json` gains a head member, `special`, and 251 vectors after the
ones it held. Every vector that was there is the line it was, byte for byte, but for the comma that now follows the
last of them. `dataframe-parity.json`, `linear-algebra-laws.json`, `regression-laws.json`, `solvers-laws.json` and
`transform-laws.json` are byte-identical.

**What a consumer must do.** Nothing. A caller that wants a special function opens `Fuaran.Compute.Elementary` and
writes `Special.gammaLn`. A host that ports the functions certifies against the new vectors, each of which states the
function's bits and, where the routine computed them, its distance from a fifty-digit reference. The accuracy is
stated by range in D78 item 10 and is a measurement: a few units in the last place for `gammaLn`, `gamma`, `digamma`
and `trigamma` on the positive axis, and tens to hundreds for the incomplete functions in their tails and at
parameters in the tens of thousands. A series that has not settled in 65536 terms answers NaN, never an unconverged
value.
