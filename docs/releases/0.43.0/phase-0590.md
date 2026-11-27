### A pinned dense-matrix library ships as `Fuaran.Compute.LinearAlgebra` (Phase 590, `DECISIONS.md` D49, D50) — additive, new package

**What changed.** A fifth package, `Fuaran.Compute.LinearAlgebra`, first in the build order, depending on
`Fuaran.Core.Column` and FSharp.Core only, Fable-clean, in namespace `Fuaran.Compute.LinearAlgebra`. It publishes
an opaque immutable column-major `Matrix` (`Matrix.init` / `identity` / `ofColumns` / `column` / `columns` /
`transpose` / `toColumnMajor` and the reads), with vectors as the substrate's `Vector<float>`; levels 1 to 3 in
`Blas` (`dot`, `axpy`, `scale`, `add`, `sub`, the scaled `norm2`, `gemv`, `gemm`, `gram`); the checked
`UpperTriangular` (`ofMatrix`, `solveUpper`, `solveUpperT`); the one `Givens` rotation (`make`, `apply`);
`LinAlgError` (`ShapeMismatch`, `Singular`, `RankDeficient`, `NotFinite`, `NotTriangular`, `NotConverged`) and
`Dims`; `Exactness` (`Pinned | HostRounded`) with `Operations.exactness` classifying every operation `Pinned`;
and `LinearAlgebraLaws.laws`. Every operation runs in the one order D49 pins — left-to-right folds from `+0.0`,
no fused multiply-add, SIMD across independent outputs only, the scaled 2-norm — so its answer is the same bits
on .NET and under Fable, and a NaN leaves as the host's canonical NaN.

**The class.** A new package with a new baseline (`api/Fuaran.Compute.LinearAlgebra.txt`); no existing
package's surface or wire baseline moves. **It RIDES `0.43.0`**: additive, on an untagged, publicly unpinned
slot.

**The law vectors.** `conformance/laws/linear-algebra-laws.json` (new, written by `--emit-laws`): the package's
answers over authored hard inputs — cancellation a fused multiply-add or a reordered fold would answer
differently, subnormals, scaling extremes, rank deficiency and every refusal — each float its bit pattern, a NaN
the token `nan` (D50 item 10). The suite holds the file fresh on .NET and equal to the document the node leg
renders under Fable. No existing vector moved.

**Publishing.** The nuget.org Trusted Publishing policy must name `Fuaran.Compute.LinearAlgebra` before the
`v0.43.0` tag.
