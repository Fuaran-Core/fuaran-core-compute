### Regression leaves `Fuaran.Compute.LinearAlgebra` for a package of its own, `Fuaran.Compute.Regression` (Phase 632, `DECISIONS.md` D75) — breaking

The same class as Phase 630's move, so it RIDES the `0.45.0` draft rather than advancing it.

**What changed.** A seventh package, `Fuaran.Compute.Regression`, holds regression modelling above the matrix
operations: `QRAccumulator` (the streaming least-squares state and its folds), `RobustVariant`, `LeastSquaresFit` and
`LeastSquares` (from LinearAlgebra's `QR.fs`, now `LeastSquares.fs`), the whole of `Glm.fs` (`GlmFamily`, `GlmLink`,
`GlmControl`, `GlmFit`, `Glm`), the accumulator's codec as `RegressionWire` (`accumulatorToJson`, `accumulatorOfJson`,
`encodeAccumulator`, `decodeAccumulator`, which were `LinAlgWire`'s), its own exactness table
`RegressionOperations.exactness` over its own `RegressionOperations.Exactness` union, and `RegressionLaws.laws` — the 27
laws `LinearAlgebraLaws.laws` ran over that code, each body unchanged. It references `Fuaran.Compute.LinearAlgebra` and
`Fuaran.Compute.Elementary`. `Fuaran.Compute.LinearAlgebra` keeps the matrix operations — `Matrix`, `Blas`,
`UpperTriangular`, `Givens`, `QR.householder` / `QR.pivoted` / `PivotedQR`, the rank rule (now its own `Rank.fs`) and
`LinAlgWire`'s matrix codec — and references nothing in Compute: its reference to `Fuaran.Compute.Elementary` is gone,
because `Glm` was its only reader. `Fuaran.Compute.DataFrame` references `Fuaran.Compute.Regression` in its place.
`api/Fuaran.Compute.Regression.txt` and `api/wire/Fuaran.Compute.Regression.txt` are new;
`api/Fuaran.Compute.LinearAlgebra.txt` loses 118 lines and gains none. No body moved: every function answers the bits it
answered.

**The wire.** The accumulator's documents keep their bytes: the two specimens in `api/wire/Fuaran.Compute.Regression.txt`
hash to the same sha256 they had as `linearAlgebra / qrAccumulator` in LinearAlgebra's wire baseline, which now holds the
matrix's two documents alone (class `breaking` against `v0.44.0`, because two documents left it). A `qrAccumulator`
document encoded by `0.44.0` decodes under `RegressionWire.decodeAccumulator` unchanged.

**The law vectors.** The 20 least-squares vectors (`qrAccumulator`, `leastSquares`, `accumulatorWire`) leave
`conformance/laws/linear-algebra-laws.json` for a new `conformance/laws/regression-laws.json` (family `regression`), each
row byte for byte as it was. The 54 vectors that stay are byte-identical, the last losing only the comma that separated
it from the rows that left; the linear-algebra file's description loses its accumulator sentences for a pointer to the
new file. `transform-laws.json`, `elementary-laws.json` and `dataframe-parity.json` do not move.

**What a consumer must do.** Reference `Fuaran.Compute.Regression` and add `open Fuaran.Compute.Regression`; calls stay
spelled `Glm.fit`, `LeastSquares.solve`, `QRAccumulator.empty`, `RobustVariant.HC3`. The accumulator's codec is spelled
`RegressionWire.encodeAccumulator` (and its three siblings) where it was `LinAlgWire.encodeAccumulator`. A consumer that
referenced `Fuaran.Compute.DataFrame` receives the new package transitively and needs only the `open`. `LinAlgError`,
`Matrix` and the rest of the matrix surface stay in `Fuaran.Compute.LinearAlgebra`. Known consumers outside this
repository: none open `Fuaran.Compute.LinearAlgebra` for `Glm`, `LeastSquares` or the accumulator. `fuaran-dotnet`'s
cut-time Fable gate (`tests/core-fable/`) names the compute packages it compiles (`$ComputeOwned` in `core-fable.ps1`,
the `PackageReference` list in `CoreFable.fsproj`); it adds `Fuaran.Compute.Regression` there in its next cohort raise
onto this version, beside Phase 630's `Fuaran.Compute.Elementary`, not in this one.
