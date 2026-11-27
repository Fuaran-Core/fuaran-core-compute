### A package of pinned solvers, `Fuaran.Compute.Solvers`, below `Fuaran.Compute.Elementary` (Phase 633, `DECISIONS.md` D77) — additive

The draft already carries two breaking moves, so this change RIDES `0.45.0`.

**What changed.** A new package, `Fuaran.Compute.Solvers`, FSharp.Core only: `StoppingRule` (with `StoppingTest`,
`StoppingArm`, `Stopped` and `SolverError`), `Root.bisect`, `Root.bracketedNewton`, `Root.integerSearch`,
`Minimise.goldenSection`, `Damped.iterate` (with `DampedProblem`), `SolversOperations.exactness` and `SolversLaws.laws`.
Its baseline is the new `api/Fuaran.Compute.Solvers.txt`. `Fuaran.Compute.Elementary` now depends on it, and so does
every package above Elementary, transitively; no code in Elementary reads it yet. No other package's surface moves:
every other file under `api/` and `api/wire/` is unchanged. Brent's method is not shipped (D77 item 8).

**The wire.** None. The package encodes nothing, and is recorded so beside the other packages that emit no document.

**The law vectors.** `conformance/laws/solvers-laws.json` is new: 69 vectors over authored functions, each method's
answer with the arm that stopped it and its iteration count, and each refusal by name. `dataframe-parity.json`,
`elementary-laws.json`, `linear-algebra-laws.json`, `regression-laws.json` and `transform-laws.json` are byte-identical.

**What a consumer must do.** Nothing. A consumer of `Fuaran.Compute.Elementary` restores one more package. A host that
ports the solvers certifies against `solvers-laws.json`, transcribing the functions its head describes. `Glm` does not
call the solvers yet: its iterations and their answers are unchanged, and a law in the suite holds the solvers to them
bit for bit so that the phase which makes the call moves nothing.
