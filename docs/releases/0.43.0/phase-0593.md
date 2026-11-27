### Exp, ln, log10 and pow are pinned so every host computes the same bits (Phase 593, `DECISIONS.md` D52) — additive, within the new package

**What changed.** `Fuaran.Compute.LinearAlgebra` gains the module `Elementary`: `exp`, `ln`, `log10` and the
float-exponent `pow`, each one argument reduction and one polynomial written with `+ − × ÷`, comparisons and a
table of the powers of two built by exact doubling — no fused multiply-add and no host mathematics call on any
path a value reaches — so the four answer the same bits on .NET, under Fable and in every host that transcribes
them. `exp`, `ln` and `log10` follow fdlibm's algorithms with their published coefficients; `pow` is
`exp(y · ln x)` with the logarithm and the product carried in double-double arithmetic. Each is within one unit
in the last place of the true value on D52's 13,381-value measurement and on every hard-input vector. D43's
special-value table is answered FIRST, unchanged, and is published as data: `SpecialValue`
(`NaN | Value of float | Undefined | Compute`) and `Elementary.expTable` / `lnTable` / `powTable`, so the
evaluator and every twin read the one table; the functions answer NaN where the table says `Undefined`.
`Operations.exactness` classifies the seven new operations `Pinned`, and `LinearAlgebraLaws.laws` gains four
laws (the table and IEEE edge rows, `pow`'s exact cases and sign rule byte for byte, the inverses and the
functional equations within their one-ulp bounds).

**The class.** Additive within the package that is new in this draft: the surface guard reads it as part of
the first snapshot against `v0.42.0`. No existing surface or wire baseline moves. **It RIDES `0.43.0`.**

**The law vectors.** `conformance/laws/linear-algebra-laws.json` gains one hundred and ten elementary
vectors after the Phase 591 set, which is byte-identical: the reduction boundaries, both edges of the computed
range, subnormal results and inputs, the near-one band of `ln`, `pow`'s exact shortcuts and every row of D43's
table. Each carries `table` (what the table said) and, where the routine computed, `reference` (the correctly
rounded bits) and `ulps` (the package's distance from them, at most one, and itself part of the document a host
reproduces). The suite holds the file fresh on .NET and equal to the document the node leg renders under Fable.

**What a consumer does.** Nothing yet: the evaluator still reads the host's functions behind the same table
until Phase 594 moves `Exp`, `Ln`, `Log10` and float `Pow` onto `Elementary`, which is the change a consumer
sees (those columns become `Pinned`, and the transform vectors that carry a tolerance lose it).
