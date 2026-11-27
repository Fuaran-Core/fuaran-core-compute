### Least squares by a streaming Givens QR, byte-identical on every host (Phase 591, `DECISIONS.md` D51) — additive, within the new package

**What changed.** `Fuaran.Compute.LinearAlgebra` gains the streaming least-squares kernel: `QRAccumulator`
(`empty`, `addRow`, `addRows`, `r`, `qty`, `rss`, `count`, `cols`) folds weighted rows into an O(p²) state with
the one Givens rotation; `LeastSquares` (`ofAccumulator`, `solve`, `requireFullRank`) reads a `LeastSquaresFit`
off it — coefficients with `None` at an aliased column, the rank, the kept and aliased columns, the residual sum
of squares and degrees of freedom, `R`, `R⁻¹` and `Qᵀy`; `QR.householder` and `QR.pivoted` (a `PivotedQR`) are
the batch factorizations, pinned but not byte-equal to the streaming kernel; and `LinAlgWire` encodes a `Matrix`
and a `QRAccumulator` by their IEEE-754 bit patterns through `Fuaran.Core.Wire`, so a primed fit survives a
restart and continues to the same bits. `LinAlgError` gains `InvalidWeight of row`, appended last. The package
now references `Fuaran.Core.Wire`. `Operations.exactness` classifies every new operation `Pinned`, and
`LinearAlgebraLaws.laws` gains fifteen laws (byte laws for the fold, the append, the codec and the native
rotation; tolerance laws for `RᵀR`, the normal equations, the Householder factor and aliasing).

**The class.** The surface guard reads the package as a first snapshot against `v0.42.0`, which carries no
baseline for it: everything here is new in this draft, including the `LinAlgError` case, which no released
consumer can match on. A new wire baseline, `api/wire/Fuaran.Compute.LinearAlgebra.txt`, is a first snapshot
too; no other package's surface or wire baseline moves. **It RIDES `0.43.0`.**

**The law vectors.** `conformance/laws/linear-algebra-laws.json` gains twenty-four vectors after the Phase 590
set, which is byte-identical: accumulator states (a non-negative diagonal, weights scaled by `√w`, a zero cell
as the identity, rows of extreme scale), the four row refusals and a shape mismatch, least-squares fits (an
exact line answering `2` and `3` to the bit, a noisy line, a zero-weight outlier, a duplicated, a zero and a
nearly collinear column aliased, more columns than rows, no rows, ill-scaled columns, an overflowing state
refused), the Householder and pivoted factors, and one accumulator's wire text, compared byte for byte. The suite
holds the file fresh on .NET and equal to the document the node leg renders under Fable. The corpus copy follows
the release rule (D50 item 10).

**What a consumer does.** Nothing: no released surface moves.
