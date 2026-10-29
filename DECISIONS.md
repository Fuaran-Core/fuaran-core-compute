# Fuaran.Core.Compute — decisions (newest first)

## 2026-10-29 — D46: the transform law vectors pin an unknown sort, partition or order key as `UnknownColumn`, a union pair with no type join as `TypeError` and a union whose sides' names differ as `JoinError`, each beside an answered neighbour; D45 item 3's "every other refusal stays unnamed" is narrowed by exactly these three classes

**Context.** The reference refuses a `Sort` key that names no column (Phase 412, D21), on the fused `Sort` + `Limit`
path too, and a `Window` partition or order key that names no column (Phase 441, D26), each as
`UnknownColumn(name, available)` before any row is read; and it types a union column by the column-type join,
refusing a pair no column type holds together (`unionField`, Phase 469, D36). On 2026-10-28 the four host twins were
read: the Python, Go and TypeScript twins sort by an absent key and return the input order, and the Python union keeps
the left side's types whatever the right side holds. All were certified green, because no transform law vector
reached any of these refusals.

**Premise check.** Confirmed on the tree before any vector was written. `knownSortKeys` (through `unknownSortKey` and
`unknownKey`) refuses an unknown key on the `Sort` step and on the fused top-n arm of `foldHead`, which
`evalPipeline` takes, since it plans; `windowRefusal` refuses an unknown partition key, then an unknown order key,
through the same `unknownKey`; `unionField` refuses an unjoinable pair with `typesBesideEachOther`, a `TypeError`,
and `evalUnion` refuses differing column names as `JoinError` before it pairs any column. Refusals were encoded as
D45 left them: `"verdict": "error"`, with `expected.error` on the overflow vectors alone, so no existing vector pinned
any class here. One premise of the shard was false: the file DID carry `Union` steps (`decimal-union`, `unit-union`,
and `unit-union-refused`, a unit mismatch), but every one paired two columns of one type, so none reached the type
join. No production code moves in this phase.

**Ruled.**

1. **The vectors.** Thirteen `evalPipeline` shapes are appended after the overflow shapes (iterations 91 to 103), so
   every earlier vector is byte-identical. The ordering family: a sort by the absent `revenue` alone, after a `Pivot`
   (the reported shape) and fused with a `Limit`; a `Window` partitioned by the absent `dept` and one ordered by the
   absent `revenue`; and an in-schema neighbour for each verb that must be answered. The union family: an int beside
   an int, an int beside a float (answered as a float, the join), an int beside a string (refused) and two sides whose
   second column's names differ (refused). They live in the shared sample for the reason D45 item 1 gives.

2. **An unknown ordering key is pinned as `UnknownColumn`.** It is the class every other verb answers for a column
   that is not there, and D21 and D26 made the ordering keys answer it on purpose: a key is a column reference, and a
   host that refused it as `TypeError` would have a different rule for name resolution in three verbs. The twins
   already carry the class (the base `unknown-column` vector's refusal is one), so the name costs a host nothing it
   does not have.

3. **An unjoinable union pair is pinned as `TypeError`, and differing names as `JoinError`.** `TypeError` is the
   class the reference gives every pair no column type holds together, a derived column's and a union's alike
   (`typesBesideEachOther`); `JoinError` is the class it gives a set verb whose sides do not line up by name. Both
   were read in the Python, Go and TypeScript twins before they were pinned: the twins spell the name mismatch
   `JoinError` today, and carry `TypeError` as a class.

4. **Every other refusal stays unnamed.** D45 item 3 stands for every refusal not named here: a decimal `Div`, a unit
   mismatch and a repeated name remain unmeasured vocabularies. The suite now asks the named families by shape
   (`LawVectorExport.refusalClassOf`), and holds every other vector to no class.

5. **The in-process law is unchanged**, for D45 item 4's reason.

6. **The version.** `0.42.0` is tagged, so the change advances the slot to `0.43.0`, as D45 item 5 did for the same
   class of change. No package's public surface moves.

**Rejected.** *Leaving the classes unnamed:* the ordering vectors would still turn a silently-sorting host red, but
a host could then refuse an unknown key as anything, and the rule D21 and D26 fixed is name resolution, which has a
class. *A case kind of its own:* D45 item 1. *Fixing the twins here:* they are other repositories, and follow in
fuaran#2485.

## 2026-10-24 — D45: the transform law vectors pin int32 overflow on `Add`, `Sub`, `Mul` and integer `Pow`, and a refused overflow vector names its class, `OverflowError`; every other refusal stays unnamed, and the in-process parity law still compares refusal with refusal

**Context.** The reference has range-checked every integer result against the int32 band since Phase 39:
`checkedInt` refuses an out-of-band `add`, `sub`, `mul` or `mod` as `OverflowError`, and since Phase 557
`powIntExact` refuses an integer power the same way, every product and every base square checked. No transform
law vector reached the band. On 2026-10-24 the four host twins were measured computing `add`, `sub` and `mul`
over a wider integer and answering unchecked, and every one was certified green, because nothing in the file
could see it. Separately, the twins' new `pow` refusal reports `TypeError` where this reference reports
`OverflowError`, and the file could not see that either: an `error` vector carried `"verdict": "error"` and
nothing else.

**Premise check.** Confirmed against the tree before any vector was written: `checkedInt` is the one check
behind `Add`, `Sub` and `Mul` on two integers (and the Fable arm's float carrier recomputes in int64 before the
same check, Phase 326); integer `Pow` with a non-negative exponent runs `powIntExact`; a negative exponent is a
float reciprocal and cannot overflow the band. Neither the vectors nor `DataFrameConformance.transformLaws`
compared the refusal's class: the law's `Error _, Error _` arm agrees on any two refusals, and the file's
`description` told hosts not to gate on a class. No production code moves in this phase.

**Ruled.**

1. **The vectors.** Twelve `evalPipeline` shapes are appended to the drawn sample after the power shapes
   (iterations 79 to 90), so every earlier vector is byte-identical and every reader that follows
   `iterations` follows them. For each operator, one vector lies past the upper edge of the band, one past
   the lower edge, and one in-band neighbour lands on or beside the edges: `Int32.MaxValue` and
   `Int32.MinValue` are reached by `add`, `sub` and `mul`, and `MinValue` by `(-2) ^ 31`. The upper edge of
   `mul` is `MinValue * -1`, the negation no int32 holds; the lower edge of `pow` is `(-1291) ^ 3`, refused by
   its final product. Each operand is derived into a column first, so the operation runs over a column as a
   host's kernel would, never over two literals a host might fold. They live in the shared sample
   (`TransformVectorSample`) rather than in a block of their own because they are evaluator answers like
   every other, and a case kind of their own would be skipped by a host that ignores kinds it does not know.

2. **The class is pinned for overflow, by name.** A refused overflow vector carries
   `"error": "OverflowError"`. The token is the `EvalError` case as it is named, which is the vocabulary every
   host's evaluator already mirrors, and it follows the member the substrate's decimal laws already use for a
   named refusal (`"verdict": "reject", "error": "MalformedShape"`). The overflow class is pinned because it
   is the class the reference has named on purpose since Phase 39, and because a host that answers an
   overflow with some other refusal has implemented a different rule, not spelled the same one differently:
   `TypeError` says the operands were wrong, where `OverflowError` says the operands were right and the
   result left the band. The twins' `pow` refusal is therefore a divergence these vectors make red, and
   fuaran#2444 is where the twins move.

3. **Every other refusal stays unnamed.** The file's other refusals (an unknown column, a decimal under
   `Div`, a unit mismatch, a repeated name) are not given classes here. Each would be a separate claim about
   a cross-host vocabulary nobody has measured, and this phase measured only overflow. The `description`
   now says a refusal is unnamed unless the vector carries `expected.error`.

4. **The in-process law is unchanged.** `DataFrameConformance.transformLaws` still agrees on any two
   refusals. Its subjects are .NET evaluators that return this repository's own `EvalError`, so the class
   comparison would add a behaviour change to a published kit for evaluators that cannot answer a different
   case without being a different evaluator. The cross-host contract is the vector file, and that is where
   the class is pinned. This suite checks it there: a vector's named class must be the reference's
   (`LawVectorTests`), with a go-red that perturbs the recorded class.

5. **The version.** `0.41.0` is tagged, so the change advances the slot to `0.42.0`. No package's public
   surface moves, but the vectors are part of what a version publishes and the corpus copy is re-emitted
   from a released version (D6 (4)), so a released `0.41.0` cannot be the version that carries them.

## 2026-10-22 — D44: a transform law vector is compared under the tolerance it carries, on every host and in this suite — a host-rounded column cell by cell within its bound, a zero, a sign and a non-finite value exact, everything else byte for byte — by one comparison the conformance kit ships; the committed file's freshness is asked the same way; the over-limit oracle expression is balanced, and the evaluator's measured stack headroom is recorded

**Context.** `main` was red in CI from `38ae3d8` (Phase 557's landing). On the Linux runners, in Debug and in
Release, `transform-77-power-ln-log10` answers `0.5118833609788744` in row 4 of `l10` where the vector,
emitted on a Windows x64 host, records `0.5118833609788743`: one ulp, which D43's bound admits. D43 item 6
had put the bound in the data (`expected.tolerance`), but nothing in this repository read it. Three legs
compared the recorded table byte for byte: the oracle question of the committed-vectors case
(`LawVectorTests`), the prepared-result law over the vectors (Phase 342) and the kernel pair over the
vectors (both `DataFrameTests`). They were green where the file was emitted and red everywhere else.
Separately, on Windows `verify.ps1` died with `0xC00000FD` inside the `Proofs.Oracle` case "the finding
holds on the shipped evaluator".

**Ruled.**

1. **The comparison.** An `ok` vector with no `tolerance` member is compared byte for byte, as before. One
   with the member is compared column by column. Every column it names must be present in both tables,
   under one type and one length, and null in the same rows. Each pair of present cells must be equal,
   except that two nonzero finite floats of one sign agree when they lie within the column's bound. The
   distance is the measure D43 item 8 was taken in: the difference of the two doubles' IEEE-754 binary64
   bit patterns, read as ordered integers. Then the answer, with every named column replaced by the
   recorded one, must encode to `expected.table` byte for byte, so the schema, the column order and every
   column the member does not name are compared exactly as before. The bound is the vector's own, never a
   constant on the host side; today every vector carries D43's 2. The rule is
   `Fuaran.Compute.Conformance.VectorTolerance` (`ofExpected`, `tableAgrees`, `floatsAgree`, `ulpDistance`).
   It is the kit a .NET host certifies with, and the three legs above call it. The file's `description`
   states the same rule for a host in another language, which is the re-emit: one line, and every vector
   is byte-identical. It is a module of its own because it is the comparison the families' vectors are
   read under, not a family, and so has no pre-split spelling for `Forwards.fs` to carry.
2. **A zero, a sign and a non-finite value are exact.** D43 item 6 says the special-value vector is pinned
   because "no wrong special value lies within two ulps of the right one", and gives `0` against a
   subnormal as an instance. Under the bare measure that is false: `0` and the least subnormal are one
   apart, and `expUnder` (`exp(-746)`, decided by the table) records `0`. So a zero of either sign is
   compared bit for bit, and so is a NaN or an infinity (the wire carries neither). Two floats of opposite
   signs never agree. This makes D43's sentence true as written. It is strictly narrower than D43's bound,
   and admits nothing D43 refused.
3. **Freshness is the same question.** The committed file and this host's render are the same file when
   they are byte-identical. They are also the same when they have the same lines and each line that differs
   is one vector carrying the member, identical in every member but its recorded table, whose tables agree
   under item 1. A header line that moved is staleness, and so is any other vector line. The within-host
   laws stay byte-exact: the render byte-stable across renders, the kept result against `evalPrepared`, the
   portable kernel member against the native one.
4. **The corpus copy is not re-synced here.** `copies.json` grades it `against: release`. It holds the
   `v0.40.0` release's file (67 iterations, `kitVersion` 0.40.0). The `CORPUS COPY STALE` banner is the
   standing, non-fatal report every run has printed since: the green run on `5d0435f` prints it at 89 lines
   against 93. Moving the copy now would publish twelve draft vectors no certifying host evaluates yet,
   ahead of the release they belong to. It moves with `0.41.0`, by that release's own re-emit.
5. **The stack overflow, at its cause.** Reproduced from this tree in Debug on Windows x64: 255 repeats of
   `resolveUnitsIn` / `go` / `both`, three frames for each `Binary` level. On `5d0435f` the same case passes.
   The case's 513-node expression was a left-deep chain 256 deep. The finding it pins is about the node
   COUNT (§21.8's limit is enforced nowhere, `pipeline-limit-unenforced`). The depth was incidental, and the
   evaluator recurses as deep as an expression is in several walks: unit resolution, typing, compilation.
   The expression is now a balanced tree of the same nodes (one `Col`, 256 `Add`, 256 `Lit 1`), ten deep,
   and the case asserts the depth. No thread's stack is raised and no production code moves.
6. **Measured, on 2026-10-22 (.NET 10, Windows x64).** A probe evaluates a left-deep `Binary(Add, _, Lit 1)`
   chain through `evalPipeline` on a thread of a stated stack, and halves the interval to the deepest chain
   answered. Debug, 1 MB: about 162 levels at `38ae3d8` and 190 at `5d0435f`. Debug, the default thread:
   about 250 and 292. Release, 1 MB: about 772 at `38ae3d8`, the limit there being `compileExpr`. The
   256-deep case therefore passed by about 36 levels before Phase 557 grew `resolveUnitsIn`'s frame with the
   `Pow` arm, and failed by about 6 after. The falsifier is a chain the probe reports answered at a depth it
   also reports overflowing. The instrument was checked both ways: the same chain passes at 150 and
   overflows at 200 on a 1 MB Debug thread.
7. **Recorded, and left where it is decided.** Nothing bounds an expression's depth. `decodePipeline`
   accepts a chain 300 deep, and the node limit is unenforced. Enforcing the node limit, the decision
   `pipeline-limit-unenforced` records as open, bounds the depth at 511. That is inside a Release build's
   measured 1 MB headroom and outside a Debug build's. This entry adds the measurement to that open
   decision, so whoever takes it starts from the number; it does not take it.

**Premises checked against the tree.** The shard asked that the format say which answers are
tolerance-compared and by how much. The format already did, since 557 (`expected.tolerance`). What it did
not say was the measure, or what else in a named column is exact, and the description now states both. The
CI's corpus banner is not one of the three failures, and it predates 557 (item 4). The overflow does not
predate 557 on this case, but it is a margin 557 consumed rather than a defect it introduced (item 6).

**Rejected.** *A host-side constant bound in the comparison:* the shard rules it out, and it would let a
vector's tolerance and a host's disagree silently. *Comparing the freshness of the committed file only on
the emitting host:* nothing records which host that was, and a CI on two operating systems is the case the
check exists for. *Raising the test thread's stack, or running the case on a thread of its own:* that hides
the depth instead of removing an incidental one. *Shrinking `resolveUnitsIn`'s frame:* it moves the first
walk to overflow without bounding any of them, and depth is item 7's question. *Re-syncing the corpus copy
now:* item 4.

## 2026-10-21 — D43: `Pow`, `Exp`, `Ln` and `Log10` enter the scalar set — an integer power runs one pinned routine, exact on every host; the transcendentals are host-rounded within 2 ulp behind a special-value table; the static walk marks every host-rounded column and reports each discrete decision such a value reaches; the Pow/Log line of Fuaran.Core's D13 is superseded

**Context.** Fuaran.Core's D13 (Phase 101, recorded in that repository's `DECISIONS.md` before the compute
strand was cut from it; this repository's own D13 is unrelated) declined `Pow` and `Log`: IEEE-754 does not
require a transcendental function to be correctly rounded, so two conformant hosts may differ in the last
ulp, and one ulp is a different byte in the canonical float layout — a parity failure. The reasoning
holds. What changes is the conclusion, on the operator's instruction (2026-10-21): "we should solve the
pow / log problem, or at least deal with it to acceptable tolerance per the host languages — a project in
one host should at least agree with other formulae in that host". Instead of refusing the functions, each
states the guarantee it carries, and the laws enforce that guarantee. The precedent is D13's own "no clock"
line, which Phase 125 reversed by pinning the clock through a witness rather than banning it.

**The three choices were ruled by the operator on 2026-10-21, at the phase's planning**, and are recorded
as rulings, not proposals: **(a)** the cross-host bound is **2 ulp**, measured against a correctly rounded
reference; **(b)** out-of-domain input yields **`Null`**, never a NaN, matching how the evaluator already
answers an invalid input (`Div` by zero, a negative `Sqrt`), and NaN input propagates as D16's one NaN;
**(c)** a `HostRounded` value that reaches a discrete decision — a `Filter` comparison, a `GroupBy` /
`Join` / `Distinct` key, a `Sort` key, `Round` / `Floor` / `Ceil`, a `Cast` to an int or a decimal — is
**admitted and reported by the schema walk**, never refused; the cross-host law promises its tolerance only
on a column no such decision reaches.

**Decision.**

1. **Four `ScalarFn` cases, `Pow`, `Exp`, `Ln`, `Log10`**, wire tags `pow` / `exp` / `ln` / `log10`.
   Typing: the transcendentals answer a float; `Pow` over an `Int` base under a LITERAL exponent at or
   above zero answers an `Int` (`Typing.powLit`), under a negative literal a float, under an int column
   the cells decide (an `Int` for a non-negative cell, a float for a negative one), and every other
   numeric pair a float. A `Decimal` operand is refused by name, as `Sqrt` refuses one
   (`pow of a decimal is not exact: write Pow(Cast(float, x), n)`). `Plan.isTotal` admits the
   transcendentals over any numeric operand and `Pow` over every numeric pair except an int base under
   an int exponent, which can overflow. One implementation, `applyScalar`, serves the reference row
   evaluator, the compiled derive (its boxed arm) and the incremental seam, so the three paths cannot
   disagree; it is Fable-clean, and the node measurement below ran it under node.
2. **An integer exponent — an `Int` cell — runs one pinned routine, `DataFrame.powInt`**: binary
   exponentiation over the host's IEEE-754 multiply, the accumulator empty until the first set bit, so a
   power takes exactly the multiplies its bits name — `x^0` is `1.0` with no multiply, `x^1` is `x`
   itself, `x^2` is `x * x`, `x^3` is `x * (x * x)`, `x^4` is `(x * x) * (x * x)`. Every conformant host
   rounds a single multiply the same way, so the product tree and not the host decides the bits, and the
   result is byte-identical on .NET, under Fable and in any twin that runs the routine. **Up to the cube
   the routine's cells are the repeated-multiplication spelling's** (`x * x * x` as a consumer writes it
   with `Mul`), a single multiply being commutative; from the fourth power the squaring tree is a
   DIFFERENT tree from the left fold's and no equality is claimed — the shard's "equals the repeated-
   multiplication spelling bit for bit" was checked and holds exactly that far, which is what the law
   asserts. A negative exponent is one reciprocal of the positive power, and **`Null` where that power is
   zero** — a zero base, or a square that underflowed to zero (`1e-300` to the power `-2`) — because the
   consumer's spelling `1 / (x * x)` meets `Div` by zero there and answers `Null`; the two spellings
   therefore answer one cell on every input. An `Int` base under a non-negative exponent runs the same
   tree over `int64` and refuses every product that leaves the int32 band by name
   (`OverflowError`, `pow overflowed int32: …`), a base square that leaves the band with exponent bits
   still to take included, since every value it would still multiply into is at least as large.
3. **A float exponent, `Exp`, `Ln` and `Log10` are host-rounded behind a special-value table** that is
   evaluated BEFORE the host is called, so the host's behaviour on those inputs is never reached:
   - `pow`: a NaN operand answers NaN (`pow(1, NaN)` and `pow(NaN, 0)` included — C99 says `1`,
     JavaScript says NaN for the first, and D16's one NaN is the answer); a zero exponent and a base of
     one answer `1`; a base of minus one under an infinite exponent answers `1` (C99 `1`, JavaScript
     NaN); a zero base under a negative exponent is `Null`; a finite negative base under a finite
     non-integer exponent is `Null`; everything else is the host's `Math.Pow`.
   - `exp`: a NaN propagates; above `709.782712893384` (the natural log of the largest double) the
     answer is `Inf` and below `-745.1332191019412` (the natural log of half the smallest subnormal)
     it is `0` — the two edges where a host's last-ulp rounding decides between a finite value and the
     limit; between them the host's `Math.Exp`.
   - `ln` / `log10`: a NaN propagates; zero and below is `Null`; `1` is exactly `0`; `Inf` is `Inf`;
     otherwise the host's `Math.Log` / `Math.Log10`.
   Within one host the answer is deterministic and agrees with every other formula evaluated in that
   host — the operator's bar. Across hosts it agrees within ruling (a)'s bound, measured below.
4. **Units** (D29 extended). `Pow` under a literal integer exponent composes through Core's `Unit.pow`
   (`m` squared is `m2`, `s` to `-1` is `/s`, to `0` unconstrained); an exponent product the algebra
   cannot carry — `Unit.pow` raises past `±2147483647` — is refused as `UnitMismatch` against the text
   `a power the algebra can carry` (the node in the error carries the exponent), never thrown. `Pow` under any other exponent, `Exp`, `Ln`
   and `Log10` take a DIMENSIONLESS operand: unconstrained, or stating `1` (a ratio of one unit, `t / t`,
   renders `1` and is admitted); a dimensioned operand or exponent is refused against `1`.
   `docs/units.md` states the rule; `proofs/Pipeline.fst` section 13 models it with `u_pow` a fourth
   member of the algebra parameter, and `refused_is_rejected` now also admits a power the algebra
   cannot raise as a rejection.
5. **The exactness mark.** `ColumnKnowledge` gains `Exactness` — `Pinned` or `HostRounded` — and the
   walk computes it per output column: an input field is pinned; a derive is host-rounded exactly where
   its expression's lineage passes through `Exp`, `Ln`, `Log10`, a `Pow` whose exponent the typer cannot
   call an int (a literal int, an int column or a cast to int is an integer exponent; anything else may
   be a float cell), or a column already so marked — a boolean over one included, since two hosts can
   decide it differently; a project, a filter, a sort, a join keep the mark; an aggregate over a
   host-rounded column is host-rounded, `Count` excepted (presence is decided by the table before any
   host is called) and `CountDistinct` not excepted (a decision over the values); a window column is
   its source's mark joined with its partition and order keys' (a rank over a host-rounded key is a
   decision); a union, an unpivot's `value` join their inputs' marks. `SchemaWalk.exactnessOf` reads
   one; `SchemaWalk.hostRoundedDecisions` lists every discrete decision of ruling (c) in pipeline
   order — the step, the verb (`filter`, `sort key`, `group-by key`, `join key`, `distinct`,
   `intersect`, `except`, `pivot key`, `window key`, `count-distinct`, `round`, `floor`, `ceil`,
   `rounded`, `cast int`, `cast decimal`) and the host-rounded operand (the column, or the function's
   tag where it is computed inside the deciding expression). Nothing is refused.
6. **Laws.** Within a host, the byte laws are unchanged: a host-rounded pipeline is byte-identical across
   repeated, reordered-batch and incremental evaluation, because the three paths share `applyScalar`
   and D16's one exit. Across hosts, the transform law vectors gain a per-column tolerance: a vector
   whose accepted output the walk marks host-rounded carries `expected.tolerance`, an object naming each
   such column with the bound in ulps (`LawVectorExport.hostRoundedUlps`, 2); a certifying host compares
   those columns within it and every other column byte for byte; a pinned column is never named, so
   every earlier vector is the bytes it was. Eight `power-` shapes follow the repeat shapes over the
   decimal table: `power-int-exponent` and `power-int-base` (byte-exact), `power-cube-is-repeated-multiply`
   (the routine beside the `Mul` spelling and their equality, byte-exact), `power-special-values` (every
   finite row of the table — the wire carries no `Inf` or NaN, so those rows are held by the suite alone;
   its columns read host-rounded functions and so carry the bound, which pins them all the same, since no
   wrong special value lies within two ulps of the right one), `power-float-exponent`, `power-exp` and
   `power-ln-log10` (host-rounded, with the bound), and `power-decimal-refused`. A host that does not
   implement the functions reports the vectors as unsupported and never skips them silently (Phase 356's
   rule, restated in the file's description).
7. **Proofs.** `proofs/Pipeline.fst` section 14 models `powInt` clause for clause over an abstract
   multiply (`fp`): `pow_int_zero` to `pow_int_four` state the product tree per exponent, `pow_int_neg`
   the reciprocal, and `pow_int_is_fold_to_three` the equality with the left fold up to the cube,
   **conditional on the multiply commuting** (IEEE-754's does). The tables are modelled concretely over a
   classification of the float (`pow_table`, `exp_table`, `log_table`), and the host functions are
   PARAMETERS reached only where a table does not answer: `pow_table_before_host`,
   `exp_table_before_host` and `log_table_before_host` prove that two hosts whose functions differ
   arbitrarily answer the same cell on every input a table decides. **What is conditional, named:** every
   theorem of section 14 is over the abstract arithmetic — that a single multiply and divide round the
   same way on every host is the premise `pipeline-ieee-multiply-is-host-independent` (`proofs.json`),
   and the 2 ulp bound is a measurement, never a theorem. Typing (`typing_apply_fn`, `typing_pow_lit`),
   the totality admission (`admit_fn`) and the unit rule are mirrored, and `verdict_sound` carries the
   one extra fact the literal-exponent typing needs (`pow_lit_admitted`: where the verdict admits a power
   under a literal int, the literal rule and the general rule name one typing, because the pair they
   decide differently is the pair the verdict refuses). The oracle is re-extracted and the differential
   extended: the generator draws the four functions, and `powInt`, `powSpecial`, `expHost` and `logHost`
   are compared to the extracted `pow_int`, `pow_table`, `exp_table` and `log_table` instantiated at the
   host's arithmetic.
8. **Measured, on 2026-10-21 (node v24.16.0; .NET 10 on this machine's Arm64 host and on its x64 host
   under emulation — the x64 libm's code, which is what the measurement is about).** The probe evaluates
   each function THROUGH the evaluator (a `Derive` over a float column) over a seeded draw at seed 557 —
   `exp` over 561 inputs spanning `-745` to `709.7` with clusters round zero and the edges; `ln` and
   `log10` over 509 inputs log-uniform across the positive doubles with a cluster round `1`, the
   smallest subnormal and the largest double included; `pow` over 508 (base, non-integer exponent)
   pairs across thirty decades — and reads the canonical result wire; the reference is Python's
   `decimal` at 60 digits rounded to the nearest double, and the distance is the difference of ordered
   bit patterns. The worst observed distance from the correctly rounded value:

   | host | `exp` | `ln` | `log10` | `pow` |
   |---|---|---|---|---|
   | .NET 10, Arm64 | 0 ulp | 0 ulp | 0 ulp | 0 ulp |
   | .NET 10, x64 | 1 ulp | 0 ulp | 0 ulp | 0 ulp |
   | node v24.16.0 (Fable) | 1 ulp | 0 ulp | 1 ulp | 0 ulp |

   No host exceeds the bound; none is a finding. The instrument was checked both ways: the two .NET
   hosts DISAGREE with each other on `exp` at one drawn input (index 46), so a difference is something
   it sees, and the Arm64 zeros are against an independent reference, not against the host. The
   falsifier is a draw at which a host sits more than two ulps from the reference; the probe and its
   draw are reproducible from the seed and the recipe above.
9. **Content hashes and cached results are host-scoped where a `HostRounded` column is in play**
   (`STABILITY.md`): a digest over the output bytes of such a pipeline names the host that produced
   them, so any cache keyed on output bytes must carry the host identity, and a consumer addressing
   results across hosts keys on the pinned columns or on the input.

**Premises checked against the tree.** The README carries no omissions list (the shard's "the README's
omissions list" names a paragraph that was only ever in `DataFrame.fs`); the verb table gained the four
and a guarantee paragraph. The "bit for bit the repeated-multiplication spelling" law was checked
and bounded (item 2). `Unit.pow` raises on an exponent past its range, so the unit rule carries a refusal
the shard did not name (item 4). The wire carries no non-finite float, so the special-value vectors hold
the finite rows and the suite holds `Inf` (item 6).

**Rejected.** *A correctly rounded implementation shipped with every host* (a port of a correctly
rounded libm into .NET, JavaScript and each twin) would make the four functions byte-exact and need no
mark — at the cost of carrying and certifying a maths library in every host, for a difference the
measurement puts at one ulp. Declined for now; the mark and the bound are the honest claim, and the
door is open: a host that becomes correctly rounded measures zero and changes nothing. *Comparing
NaNs equal in the laws* — D16's answer stands: the evaluator emits one NaN. *Refusing a decision over a
host-rounded value* — ruled out by (c): a consumer who filters on `exp(x) > 1` has an ordinary
pipeline, and the walk's report is where the cross-host caveat is read. *Marking `Count` host-rounded*
— presence is pinned by construction (every `Null` the functions answer is decided by the table), so a
count is exact and the mark would be false.

**Supersedes** the Pow/Log line of Fuaran.Core's D13, whose entry carries the status line; the rest of
that entry stands.

## 2026-10-21 — D42: the per-row caches of an incremental state are held in chunks below the large-object threshold, so an append shares the prior chunks; the refresh-against-full case draws each call's source from a pool; the 20,000-row append is timed, and D27's 448.C reach is closed

**Context.** D27 held the refresh alone against the full evaluation at every size for an edit, but for
an append only at 1,000 and 100,000 rows. At 20,000 rows the append's refresh on `byRegion`,
`filter > groupBy` and `filter > groupBy > filter` read 0.8 to 2.5 times a full evaluation of 0.24 to
0.45 ms, and Phase 448 named two causes: the large-object growth of two per-row arrays, and an
instrument that re-refreshed one (state, delta, source) triple. Phase 451 was filed for both (448's
deferred task 448.t3).

**Both premises were checked on this tree before anything was built.** A probe counted the runtime's
large-object allocation events over 500 in-place appends at 20,000 rows. It found exactly one 160 KB
`string[]` (the row tokens) and one 160 KB `Cell[]` (a step's cells) per refresh on the three nodes,
and nothing else on the large-object heap. On `byRegion` the `Cell[]` is the `Derive` step's cells,
which are also the walk's frame column, so chunking the cache alone would not have reached it. A third
per-row cache, the rows' groups (`int[]`), was copied whole on every in-place refresh too, and on an
edit the copy was then discarded. At 20,000 rows it is 80 KB, just under the threshold, and it crosses
the threshold at 21,250 rows. It is the same class of array and is taken in the same change. The
instrument premise held as well; the A/B is below.

**Decision.**

1. **The three per-row caches are `RowChunks`**: chunks of 8,192 entries, 64 KB of references, each
   owned by the cache that allocated or copied it. `RowChunks.grow` shares every chunk of the prior
   cache that keeps its length, copies the one it extends, and allocates the new ones. A shared chunk
   is copied before its first write. So a prior state's caches never move, and two refreshes of one
   state see only their own writes. A state built by a full walk, a prime included, is chunked too, so
   the first refresh after a prime shares as every later one does. No array a state keeps per row is
   ever a large object.
2. **A `Derive`'s column in the walk's frame is the step's chunks** (`WalkFrame.Cached`). A step that
   reads only the rows a refresh walks reads them off the chunks (`cellAt`, `cellAtSlot`,
   `cellsAtSlots`, the patched result column). A step that reads the column whole (a sort, a window, a
   group streamed in full) concatenates it once (`column`).
3. **The wire form is the flat arrays'.** The encoder flattens each cache (`RowChunks.toArray`) and the
   decoder chunks it again, so no encoded byte, corpus vector or result value moves.
4. **The refresh-against-full case draws each call's source from a pool** of prepared (source, delta)
   pairs (`ScalingTests.refreshPool`: 128 at 1,000 and 20,000 rows, 16 at 100,000), as a consumer's
   tick has a source of its own. The full evaluation it is held against still re-evaluates one source,
   so the bound is not loosened. The case prints the generation-2 collections each refresh timing drew.
   `refreshSizes` is one list, and the 20,000-row append is timed on every node the seam accepts.

**The A/B** (Arm64, Release, the case narrowed to the append at 1,000 then 20,000 rows, two runs per
arm; the figures are in `docs/performance.md`, "A 20,000-row append refresh is held against the full
evaluation"). The heap budget was moved two ways: the generation-0 budget raised to 256 MB, and the
large-object threshold raised past the caches. On the pre-451 library (`5cd9ecc`) with one source, the
three nodes read 0.55 to 1.88 times the full, with 1 to 23 generation-2 collections per cell, and went
red on a counted attempt in both default runs. With the pool they drew no generation-2 collection, but
`filter > groupBy` still read 0.11 to 0.17 ms across the three budgets. On this phase's library with
the pool, it read 0.086 to 0.095 ms on all three. One source on this phase's library held the three
grouped nodes steady too, but not `lines`, whose refresh still allocates its result's 160 KB column:
the pool stays.

**448.C is closed** (448.t3). One clock leg of the case on this tree read the 20,000-row append at
0.09 to 0.43 of the full evaluation on every node the seam accepts. Across every size and both ticks
no cell read above 0.85. A main-suite case pins the representation: over every node the seam accepts,
a prime and two chained appends at 20,000 rows leave no per-row array of 85,000 bytes or more, and
each append shares every chunk of the prior caches it does not write. It went red under each of two
perturbations, run one at a time: `grow` returning one flat array, and `grow` copying the chunks it
shares.

**Considered and rejected.**

- **A shared buffer with spare capacity**, written past the prior's length by the next append. Two
  refreshes of one state, which the seam allows and the suite does, would both write the same spare
  slots, and one state would read the other's rows.
- **Raising the large-object threshold** (`GCLOHThreshold`). It is a process-wide runtime setting.
  A library cannot own it for its consumers, and it only moves the cliff.
- **Flat caches after a full walk, chunked from the first refresh.** It saves a prime the chunking,
  but the first refresh after every prime then re-chunks the whole state. That is the refresh the case
  times, and the one a consumer pays first.
- **Timing the full evaluation over the pool too.** It is the symmetric choice, but it can only make
  the full evaluation dearer and the bound easier, so it is not taken.

**Not taken here, and why.** The walk's own per-slot scratch (`Prior`, `Stable`, a filter's order) is
allocated per refresh and dropped. It crosses the threshold at about 21,000 rows for an `int[]` and
85,000 for a `bool[]`. It is not a cache and is outside this phase's acceptance. A refreshed result's
computed column is the consumer's answer, and a 20,000-row column of floats is 160 KB whatever the
seam does. The group order (`GroupOrder`) has one entry per group, not per row.

**Consequences.** No public signature, answer or wire byte moves (`STABILITY.md`, class `none`,
`performance`), and the `0.41.0` draft is ridden. The clock leg's case count is unchanged. The main
suite gains one case.

## 2026-10-16 — D41: the relational model's union refuses a pair no column type holds together through the cells' typing's own definitions, and the relational differential excuses nothing — its D18 arm was dead and is gone; the frame reads a packed cell through `Pipeline.fst`'s `pack_cell`, tied by a proved equality; D38 item 1's twin sentence is corrected

**Context.** Phase 470 (D38) left two things out of its scope. The relational differential in
`ProofOracleTests` kept an excuse arm — a production `TypeError` "which no column type holds together"
against a model `Ok` was tallied as outside the model's domain — and `Frame.fst`'s per-cell packing was
a twin of `Pipeline.fst`'s (D38 item 1). Phase 476 was filed to close both.

**The first premise was false, and was checked before anything was built.** The shard said
`Relational.fst`'s `Union` "still lacks the D18 refusal". It did not: Phase 469 (D36) modelled the
union's per-column rule (`union_ty`, F#: `unionField`), and `union_types` has refused a pair no column
type holds together, with the D18 text, since then; the pinned `423-union-refuses-unrelated` shape
compared that refusal on both sides. Production's D18 refusal of a union reads the two SCHEMAS
(`unionField` over the two fields' types), never the cells, so there was no cell-level refusal left for
the schema model to lack. MEASURED: with the arm deleted and nothing else changed, the relational
differential is green and its `rowRefused` tally is 0 — the arm fired on no step, generated or named.
What WAS true is narrower: the model restated what it should have reused. `types_beside_each_other`
was a byte-identical twin of `Pipeline.types_beside`, `type_tag` a twin of `Pipeline.type_tag`, and
`join_column_type` re-derived the join from `widens` both ways instead of reading the cells' typing.

**Decision.**

1. **The union's column typing reads the cells' typing by name.** `join_column_type a b` is
   `if disjoint_types a b then None else Some (widen_type a b)` — `Pipeline.disjoint_types` is
   `typeFromCells`' test of a pair no column type holds together and `Pipeline.widen_type` its join, so
   a union column and a derived column refuse one pair by one rule. It equals the old definition by case
   analysis (`widens` relates equal types and an int to a float or a decimal, nothing else). The refusal
   is `Pipeline.types_beside`; `types_beside_each_other` and `type_tag` are deleted. `union_ty` keeps its
   shape (`unionField`'s equal-types arm first). Every relational theorem is re-proved with its statement
   unchanged — `union_sound`, `union_walk_widens`, `walk_sound` and the rest — and none needed a proof
   edit.

2. **The excuse arm is deleted, and the comparison is shown to reach the refusal.** The differential now
   runs every ordered pair of the generator's seven column types as a one-column union (`rUnionPairs`,
   49 shapes, a null row each) beside the named shapes, and counts a union refused on D18 by BOTH sides
   with the same text (`RBeside`); the case asserts at least 39 — the 38 pairs no widening relates and
   the 423 shape — and measures exactly 39. **Go-red, one perturbation, two runs** (on the extracted
   oracle, so no prover run was spent): `union_types` answering the left type where it refuses. With the
   arm restored as it stood, and the new count's floor relaxed to 0 as the case stood before, the
   differential is GREEN and the 39 refusals are excused (`rowRefused=39`); without the arm the same
   perturbation is RED on all 39 (`production refused TypeError … model evaluated to …`). So the arm
   would have hidden exactly the defect it named, had the model ever had it.

3. **The frame reads a packed cell through Pipeline's definition, tied by a proved equality.** One
   definition is not possible within this phase's files: `Pipeline.pack_cell` reads its two carriers
   (`float_of_int`, `int_text`) off `prims`, `Frame.fst` off `host`, and the records share exactly those
   two fields under the same names; making them one definition means narrowing what `Pipeline.pack_cell`
   takes, a `Pipeline.fst` change and a re-proof of every model that opens it. So: inside `Frame.fst`
   the int's widening has ONE definition — `norm_cell`, the typed carrier's reading, now re-renders the
   temporal texts and reads every other cell through `widen_cell` — and two theorems tie it to Pipeline's
   by name: `widen_cell_is_pack_cell` (the boxed cell IS `Pipeline.pack_cell pr (erase ty)` wherever
   `pr` and `h` agree on the two carriers) and `pack_cell_is_pipeline` (so `Vec.pack`'s read-back is
   Pipeline's packing at every non-temporal type, typed or boxed). A temporal column differs only where
   the typed carrier re-renders a text that was not canonical, which is what the premise
   `pipeline-temporal-cells-canonical` already names. `pack_reads` and every frame theorem keep their
   statements. The premise is checked where it is instantiated: a `Proofs.Oracle` case holds
   `ModelFrame.pack_cell fHost` and `widen_cell fHost` to `ModelPipe.pack_cell pPrims` over drawn
   columns at the five non-temporal types.

4. **D38 item 1's twin sentence is corrected in place.** It gave the wrong reason the two could not be
   one definition (`Frame.fst` opening `Pipeline` is what lets the frame READ Pipeline's definition);
   the reason is the carrier record, and the two are now tied.

**Evidence and cost.** The proof leg is green with every model checked and extracted (`check.ps1 -Extract`, one cold run, a contended pass at x0.89): `Relational` 54s against its 120s budget (recorded 57s; 62s on the authoring run), `Frame` 69s against 130s (recorded 64s; 69s on the authoring run too), `IncrementalMaintained`, which opens `Relational`, 208s against 480s. Both moved modules stay inside their budgets and no cost finding was raised, so `modules.json` is not re-seeded: a 5s rise on a contended pass is not a re-seed obligation by the file's own rule, and the figures are recorded here instead. No production code moves and `STABILITY.md` is untouched: production
already refused what the model now refuses through Pipeline's definitions.

**Consequences.** No proof differential in the repository excuses a production refusal the reference
does not model. `Relational.fst` re-models nothing `Pipeline.fst` models. The frame and the list model
read a packed cell through two definitions tied by a proved equality; one definition remains available,
at the price of narrowing `Pipeline.pack_cell`'s record, and nothing here needs it.

## 2026-10-16 — D40: an int under a float or a decimal column reads as that type's value whatever the carrier — `Vec.packAt` widens it on the boxed form as on the typed one, so a cell's reading is a function of the cell and the column's type alone; the pack verdict leaves the walk's key, `packs_as_is` leaves the reorder theorem, and the models pack cell by cell; Core already read so, and nothing there moves; D38 item 5 is closed

**Context.** D38 item 5 named the faithful alternative to tracking `Vec.packAt`'s VERDICT — typed, or boxed
with every cell as it was — through the incremental walk: widen every int under a float or a decimal
column on the boxed form too, so that the verdict vanishes, the pack key is the type alone, and the
premise `packs_as_is` Phase 470 had to give `derive_then_filter` and `reorder_in_context` is unnecessary.
The operator chose it on 2026-10-15 (Phase 472), under the 2026-10-12 ruling that a behaviour change in a
published package is not a reason to keep a defect. The defect: under the verdict, one unrelated row (a
decimal text past the scaled carrier's width, arriving or leaving) changed how every stable int in the
column read, with no delta naming those rows — the shape Phases 464 and 470 each found production
diverging on, and each fixed by tracking more state.

**Decision.**

1. **`Vec.packAt` widens on the boxed form.** The boxed fall-back — taken on a present cell the type does
   not carry, and for a decimal column on texts `ScaledDecimal.scaleOf` cannot carry at one scale — reads
   an `Int` under a `FloatType` column as `Float (float v)` and under a `DecimalType` column as
   `Decimal (string v)` (`widened`), exactly as the typed carrier reads it back through `Vec.cellAt` and
   as Core's `Column.ofCells` holds it. Every other cell is as it was; no other type is touched. `Cell.Int`
   is a 32-bit int (D39 item 1), exact in a float and in a decimal, so the widening loses nothing. A
   `behaviour` change on the open `0.41.0` draft (`STABILITY.md`): what a consumer observes is a `Float`
   or a `Decimal` cell where a boxed column answered an `Int`, and a refusal where arithmetic between an
   int and a float once answered over a boxed decimal column.

2. **The walk's verdict tracking is deleted, not maintained.** `Incremental.fs`'s `typedPack` (D38's
   mirror of the verdict) is gone; `widenCells` widens an int under a float or a decimal column
   unconditionally again, as D37 first wrote it; `retypedSince` compares the type alone, as D37 wrote it,
   and no longer takes the step's cells. The two production divergences 464 and 470 closed stay closed by
   construction: there is no verdict left to disagree about.

3. **The Core half's premise is false, and nothing in Core moves.** The shard asked for `Column.ofCells`
   to widen on "the boxed path, as on the typed one". Core has no boxed path: `ColumnData` is the seven
   typed cases and nothing else, `storageOfCells` holds an `Int` under a `FloatType` column as `float i`
   and under a `DecimalType` column as `string i`, and REFUSES (`TypeMismatch`, the first offending cell
   named) any present cell the type does not carry. A Core column already reads an int under a float
   column as its float on its only path, pinned by Core's own suite (`ColumnTests`: "ofCells normalises a
   widened Int: Float in a float column, Decimal in a decimal column"). The two halves are therefore
   independent: this repository widens in `Vec.packAt` before any cell reaches Core, and a boxed vector
   handed to `Column.ofCells` (through `Vec.toColumn`'s boxed arm) was already widened there. No pin is
   raised: Core `0.38.0` is an untagged draft and this repository pins the public `0.37.0`, which this
   change does not need. Core's `DECISIONS.md` D161 records the same finding from its side.

4. **The models pack cell by cell.** `Pipeline.fst`'s `pack_cells pr ty` is `map_cells (pack_cell pr ty)`;
   `pack_admits`, `all_admitted`, `dec_texts`, `dec_mask`, `typed_pack`, `pack_one`, `pack_with`,
   `packs_as_is`, `pack_with_as_is` and `pack_cells_as_is` are gone, and `prims` loses `scale_of` (the
   packing consults no scale). `derive_then_filter` and `reorder_in_context` are proved with their
   pre-470 statements — Phase 269's three premises, no `packs_as_is` — because a per-cell packing commutes
   with the filter's selection: the row lemma `filter_ext` is stated over the per-cell map `g` the derived
   column went through (`zip_ext k rows (map_cells g ws)`), and its proof is the old one with `g w` for
   `w`. `kept_cells_has_type` and `derive_cells_fit` are gone with the premise; `planner_reorder_sound`
   keeps Phase 338's statement standing on `derive_total` alone. `Incremental.fst` records the type per
   derive (`entry.e_pack : option column_type`; `pack_key`, `pack_key_of` and `cells_of_pack_cache` are
   gone), `pack_cache pr ty` maps `pack_cell`, and `walk_reuse` switches on `ty0 = ty`;
   `IncrementalMaintained.fst` the same. `Frame.fst`'s twin `pack` (the frame model, held to `Vec.pack`
   by the frame differential) widens on its boxed arm too (`pack_boxed`), and `pack_cell` there reads
   the boxed cell through `widen_cell`. Every other theorem over the derive is re-proved with its
   statement unchanged (`prime_is_full`, `refresh_is_prime`, `refresh_agrees`, `mprime_is_full`,
   `mrefresh_is_prime`, `mrefresh_agrees`, `eval_total`, `budget_monotone`, `work_bounded`,
   `verdict_sound`, `planner_reorder_sound`, `pack_reads`). The temporal-text premise
   (`pipeline-temporal-cells-canonical`) stands, worded over `pack_cell`.

5. **Evidence.** The go-red probe (`ProofOracleTests`, "an int under a float or a decimal column reads as
   that type's value on the boxed form too …"): `Vec.pack FloatType [| Int 1; Str "x"; Null |]` is boxed
   and reads `Float 1.0` at row 0, `Vec.pack DecimalType [| Int 1; wide; Int -30 |]` is boxed and reads
   `Decimal "1"` and `Decimal "-30"` — red before this phase; the model packs the same cells the same
   way; and a model that keeps Phase 470's verdict (`pVerdictDerive`, kept once) answers the first boxed
   shape (`pBoxedShapes`: an int beside a decimal past the width, read by an `Add` of a float) where
   production and the corrected reference refuse `arithmetic between a decimal and a float`. The two
   boxed shapes still hold the refresh to the full evaluation, and `pCornerAgrees` holds the three models
   to production on every retype and boxed shape. The module budgets are re-measured in `modules.json`.

**Consequences.** D38 item 5 is closed; D38 item 4's premise is retired with its reason. A column's
reading no longer depends on rows other than the one read, in the full evaluation, the incremental
refresh and the planner's reorders. `Vec.packAt`'s boxed form is no longer a reading of its own: a kernel's
reference arm over a boxed float or decimal column never meets an `Int`.

## 2026-10-15 — D39: the maintained `Sort` is modelled and proved — the merge of the moved rows into the prior produced order by binary insertion, under `arrivedAsBefore`, answers the full stable sort, in a module of its own under `IncrementalMaintained.fst`; the comparator is modelled concretely and proved a total preorder within a typed column, so the incomparable-pair lead of D37 item 1 does not arise and no production code moves; D37 item 1 is closed

**Context.** D37 declared `Sort` out of the maintained walk's model by name: production's refresh of a `Sort`
(`Incremental.fs`, `MergeOrder`) merges the rows the delta moved into the prior PRODUCED order by binary
insertion (`mergeOrders`, Phase 274), reusing that order only while the stable rows arrived in the same
relative order as before (`arrivedAsBefore`, Phase 215), and proving that equal to the full stable sort
needed two things Phase 464 did not start — the uniqueness of a sorted permutation under the comparator
read as a strict total order with the arrival position as the tie-break, and a comparator premise, since
across cell types `compareCells` answers `None`, which `compareResolved` reads as `0`, and a tie that is
not transitive makes a sort depend on its input order. Phase 471 was cut to do both: the premise check
first, against production, then the model composed with 464's walk and 470's packing reference, the
agreement and state theorems, a differential with a go-red, and the census moved.

**Decision.**

1. **The premise check: a column reaching a `Sort` holds one family, and within one family the
   comparator is a total preorder — the lead does not arise, and nothing in production moves.** The
   shard asked whether a column reaching `Sort` can hold cells of more than one type, and the driver
   named the lead: after D38 a BOXED column under a float or decimal type (the typed pass abandoned)
   holds `Int` cells beside `Float` / `Decimal` ones. Established against `DataFrame.compareCells`
   (`DataFrame.fs`) and Core's `Cell.compare`: an int against a float compares in the float carrier
   (`Kernels.compareFloat` over `numOf`), an int against a decimal EXACTLY (`Cell.compare`:
   `DecimalText.compare` over `string i` and the canonical text), an int against an int in the float
   carrier — and **`Cell.Int` carries a 32-bit int, exact in a float and in a decimal alike**, so the
   three readings of a mixed pair agree with the exact numeric order and the boxed column is one total
   preorder whichever cells stayed raw. `compareCells` answers `None` on exactly three shapes: two
   families (a string beside an int), a float beside a decimal, or a `Decimal` whose text is not decimal
   text — and no frame a `Sort` reaches holds any of them: `typeFromCells` refuses two families and a
   float beside a decimal (D18, D38 item 3), `Vec.packAt` carries an int under a float or decimal column
   as that type or raw, every decimal primitive and `castCell` refuse a text outside the grammar, and a
   temporal cell's text is canonical (`pipeline-temporal-cells-canonical`). So the cross-type case does
   not arise from the ints Phase 472 will widen, and it does not arise any other way: there is no
   production defect to fix here, `Incremental.fs`, `DataFrame.fs`, `STABILITY.md` and `api/` are
   untouched, and the typing fact is stated as the premise the two theorems compose through (item 3).
   Pinned on production directly (`ProofOracleTests`): a boxed decimal column holding `Int32.MaxValue`,
   `Int32.MinValue`, `0`, `5`, `-7`, `2147483646` and a decimal past the scaled carrier's width sorts to
   ONE value sequence — the exact numeric order — from every one of ten arrival orders; and the two sort
   paths production runs, the order codes where every key's vector codes (`Ordering.codesAll`) and the
   comparator otherwise, answer one order over three hundred generated tables under drawn keys.

2. **A module of its own, `proofs/MaintainedSort.fst`, opening `Pipeline` and `Incremental`, under
   `IncrementalMaintained.fst`.** `IncrementalMaintained` was 229s of a 400s budget after D38, and the
   sort's lemmas are a theory of their own — a strict total order over a list's members (`sto_on`),
   sortedness, the insertion sort (`isort`, F#: `List.sortWith` as the reference's `evalSort`), the
   uniqueness of a sorted permutation (`sorted_unique`), the bisection (`bisect`, the `while a < b` loop
   with its invariant `bisect_cut`) and the merge (`merge_bin`, F#: `mergeOrders` — each moved row placed
   in the cached run by bisection under "goes out before", the run before it copied whole, the search
   continuing from there), positions (`indexed`, F#: `posOf`) and the order `lt_of` they make with a
   total preorder (F#: `before`, and `sortSlots`'s comparison: the comparator, then the position) — so
   they live where `IncrementalMaintained` can open them by name and cost nothing to its budget. The
   definitions the sort's theorem is stated over — `tokens_of`, `distinct`, `mem_a`, `wk`, `ok_row`,
   `wk_mem`, `wk_of_rows`, `mem_tokens`, `assoc_of_mem`, `assoc_mem` — MOVED down from
   `IncrementalMaintained.fst` verbatim, so there is one `wk` and one `distinct`. The step is modelled
   concretely (`sort_step`): the cached order merged where `arrived_as_before` holds — the stable tokens
   of the prior arrival still here, read in step with the stable tokens now, equal (F#: `arrivedAsBefore`
   compares the two sequences without building either) — with the cached run the prior produced order's
   tokens read through the rows that hold them now (`lookup_order`, F#: `prevOrder` through
   `stableNow`) and the moved rows sorted among themselves (`unstable`, F#: `arrival |> Array.filter
   (fun s -> not r.Stable[s])` then `sortSlots`); the whole arrival sorted from scratch where it does
   not (F#: `reusable = None`); and the step records its arrival and what it produced (`sort_record`,
   F#: `SortOrders[sortIdx] = (arrival, ordered)`). `IncrementalMaintained`'s alphabet gains `MSort`
   (F#: `WSort`, its keys literal — `split` refuses a param key and the walk never sees one), `entry`
   gains `e_order`, `mwalk` gains the arm (the reference's unknown-key refusal first, Phase 412),
   `mrows` the row function (`sort_rows`: the stable sort under the comparator over the frame's rows),
   and `mwalk_is_go`, `mwalk_no_reuse` and `mwalk_reuse` the arm each: a prime sorts from scratch and
   its rows are the reference's with the tokens riding along (`rows_of_sort_alive`), a walk reusing
   nothing sorts from scratch whatever record it was handed (`sort_step_no_reuse` — the merge of no
   cached row with every row moved IS the sort, F#: an empty `kept`), and a refresh answers the prime's
   sort (item 3).

3. **The theorems, and what each assumes.** `sort_step_reuse` (THE STEP THEOREM): a `Sort` refreshed
   against the record a prime left — the prior arrival and the prior full sort, as tokens — under the
   delta premise read one row at a time (`wk`), the prior source's tokens distinct, and the row
   comparator a total preorder, answers the full stable sort of the rows that arrived; `mrefresh_is_prime`
   and `mrefresh_agrees` carry it through the walk with their statements as D37 left them plus the one
   premise (`row_cmp_preorder sp`). The proof is the one the shard named: the merge of two sorted,
   disjoint runs is sorted with their union's members (`merge_bin_sorted`, over `bisect_cut`), and a
   sorted permutation under a strict total order is unique (`sorted_unique`), so the merge is the
   insertion sort of the arrival (`merge_is_isort`) — provided the cached run IS sorted under the order
   NOW, which is where `arrivedAsBefore` earns its keep (`lookup_sorted`, through `relabel_lt`): a prior
   stable row ordered before another is, read now, still before it, because its cells are the cells it
   had (`stable_cells`, from `wk` and the distinct tokens) and its position in the arrival now is on
   the same side of the other's as it was — a filter keeps the relative order of what it keeps
   (`filter_order`), and the two stable sequences `arrived_as_before` asserts equal are two such filters
   (`pos_is_idx`, `stable_pos`, `distinct_pos`). The cached run's members are exactly the stable rows
   now (`cached_memb`: the stable tokens now are distinct, `stable_now_nodup`, so a token finds one row,
   `find_tok_unique`), the moved rows the rest, and the premise survives the permutation on both sides
   (`sort_alive_wk`, through `assoc_perm`). `compare_resolved_preorder` (THE COMPARATOR THEOREM):
   `compare_resolved` — `compareResolved` restated arm for arm over `compare_cells`, `compareCells`
   arm for arm — is a total preorder over rows whose cell at each key is what a column of that key's
   type holds (`typed_row` through `fits`: its own type, an int under a float or a decimal column, a
   decimal whose text is decimal text, or `Null`), from the primitives' laws alone (`sprims_laws`,
   item 4): the per-key comparison is one (`key_cc_refl` / `key_cc_anti` / `key_cc_trans`, from
   `cells_preorder` — the decimal family through one key `dkey`, `cc_decimal`; the float family through
   the carrier, `cc_float`) and the lexicographic composition of preorders is one (`lex_preorder`).
   `compare_cells_none` names the pairs read as a tie, which is item 1's list. **What the two theorems
   compose through, and do not prove:** the models do not track cell typing through the walk (`wf` is
   the frame's width alone), so the step theorem is stated for a comparator that is a preorder
   everywhere (`row_cmp_preorder`) and the comparator theorem for the rows a typed frame holds; the
   composition is production's typing fact of item 1, carried as the premise row
   `incremental-maintained-sort-primitives` with `unscheduled` the typing invariant through
   `Pipeline.fst`'s evaluator that would discharge it. Stated rather than weakened silently.

4. **The parameters, each an assumption, and what the differential checks of each.** `sprims`: the row
   comparator the walk and the reference both call (`row_cmp` — F#: `compareResolved (resolveSortKeys
   cols by)`), the float carrier's order (`cmp_float` — `Kernels.compareFloat`), the decimal order and
   canonical form (`cmp_dec`, `canon_dec` — `DecimalText.compare`, `tryCanonical`), the ordinal order
   (`cmp_ord` — `String.CompareOrdinal`), beside Phase 470's `float_of_int` and `int_text` on `prims`.
   `sprims_laws`: the float and ordinal orders are total preorders; the decimal order is one over
   canonical decimal texts, where it always answers; an int's text is canonical, a canonical text is its
   own canonical form; and `compareFloat (float i) (float j)` IS `DecimalText.compare (string i)
   (string j)` — the 32-bit fact of item 1 as a law. Every one is instantiated at production's own and
   checked there on pools that include `NaN`, `-0.0`, the infinities, the int extremes and a decimal past
   the width; the model's `compare_resolved` under them answers what production's `compareResolved`
   answers on every pair of rows of three hundred generated typed tables under drawn keys.

5. **The differential, its go-red, and the acceptance's perturbation.** The maintained generator draws a
   `Sort` among the prefix steps — over the group column, where ties are the rule and the arrival
   position decides; over the int or the decimal column descending, nulls among them; over two keys —
   and, one time in three, REORDERS two rows of the mutated table after the mutations: `Delta.diff`
   reports a reordering as quiet, so a stable sort's answer moves while no row is named, the case
   `arrivedAsBefore` exists for; the tally counts sorted pipelines, produced orders that moved and
   refreshes under a reordering, and holds each above a floor. The go-red (`mStaleArrival`): every sort
   record's arrival overwritten with the arrival the refresh sees, so `arrived_as_before` never fails and
   the merge reads the prior produced order under the new positions — it disagrees with production where
   the stable rows were reordered. The acceptance's perturbation, made in the model and run once: a
   binary insertion that ignores the arrival position on a tie (`lt_of` as the comparator alone) — the
   prover refuses `lt_of_sto_indexed` (two rows that tie are neither before the other, so there is no
   total order over the arrival) and `sort_step_reuse` with it: SEMANTIC, `MaintainedSort.fst(493)`, "Failed to prove: sto_on (lt_of cmp) l" — the totality clause,
   two rows that tie being neither before the other; restored, the module verifies.

6. **Cost and evidence.** `MaintainedSort.fst` is seeded in `modules.json` from one cold run of the leg on this
   machine, 50s under `--quake 3` (a contended pass by the kit's own label, x0.93, the kit's self-tests
   and a test build beside it), and a direct run under `--quake 1` during authoring, 36s — a 100s budget
   and an 18s floor by the file's two rules. `IncrementalMaintained.fst`, re-measured with the arm: 185s
   under `--quake 3` in the same leg against the 198s recorded and the 400s budget, 104s direct — neither
   number moves. Every theorem D37 and D38 stated is re-proved with its statement unchanged but for the
   one premise (`row_cmp_preorder`); the oracle gains `MaintainedSort.fs` (726 lines extracted) and the
   suite's `Proofs.Oracle` family gains four cases (item 5), the maintained differential's tally reading
   155 sorted pipelines, 92 produced orders moved and 33 refreshes under a reordering over 700 trials,
   with the sort go-red disagreeing 21 times. Raised to 700 trials from 500 so the existing adequacy
   floors (a window's partition key moved among them) keep their room beside the new draws.

**Consequences.** D37 item 1 is closed: `Transform.Sort` joins `Incremental.prime` and `Incremental.refresh`
on the ladder's proved rows for the maintained walk, and the `Incremental` census entry's reason no longer
names the merged `Sort` among what no model states. Still declared out, each with D37's reason: the resumed
prefix-fold window, the in-place and append fast paths, the staleness guards, the quiet-delta shortcut, the
chunked path and the codec. Phase 472's widening (D38 item 5's faithful alternative) stands on its own
grounds — cell-locality of a column's reading — and not on this phase's: the sort is sound over the raw ints
too.

## 2026-10-15 — D38: the reference model packs a derived column as production does — `Vec.packAt` read back, verdict and all, as one definition the three models share; the cells' typing refuses two types beside each other as production does; the planner's reorder theorem names the packing premise its corollary discharges; and the walk's switch compares the PACK KEY, which closes a second production divergence (the typed pass abandoned)

**Context.** D37 item 4 named a gap in the REFERENCE model: `Pipeline.fst`'s `eval_derive` held a derived
column's cells raw (`zip_append f.rows cells`) where production packs them under the decided type
(`Vec.packAt`: an int under a float column is the float of its value, under a decimal column the decimal
of its value), and `Incremental.fst`'s `extend` held them raw too, so the pipeline differential and Phase
433's `prime_is_full` were stated against a reference that differs from production exactly where D37's
production defect lived. Phase 470 was cut to close it: the packing modelled on production's, as ONE
definition the three models reuse, every theorem over the derive re-proved with its statement unchanged,
the differential's generators reaching the corner, and the old raw reference shown failing on it once.

**Decision.**

1. **The packing, modelled concretely on `Vec.packAt` as the column reads back through `Vec.cellAt`**
   (`Pipeline.fst`, `pack_cells`): one pass over the whole column decides a VERDICT — carried typed when
   every present cell is one the type carries (`pack_admits`: its own type, or an int under a float or a
   decimal column) and, for a decimal column, when `ScaledDecimal.scaleOf` answers a scale over the
   column's present texts (an int's rendered as `string v`); else the typed pass is abandoned and the
   column is BOXED, every cell as it was. Under the verdict an int in a float column reads back as the
   float of its value and in a decimal column as the decimal of its value (`pack_cell`); every other cell
   reads back as itself. `eval_derive` packs its cells under the type `column_type_by` decides, and the
   two walks pack the same cells through `Incremental.pack_cache` (the cache's cells under the key, each
   under its token), whose cells ARE `pack_cells` over the raw cells (`pack_cache_is_pack_cells`) — the
   one definition the shard asked for. The verdict being a function of the WHOLE column is what the rest
   of this record is about. It is the twin of `Frame.fst`'s `pack` / `pack_cell` on the list column. (Corrected by
   D41, Phase 476: this record first said the two could not be one definition because `Frame.fst`
   opens `Pipeline` and not the reverse — the wrong reason, since that is what lets the frame read
   Pipeline's definition. What keeps them two is the carrier record, `prims` here and `host` there;
   they are tied by a proved equality, `widen_cell_is_pack_cell` and `pack_cell_is_pipeline`.)

2. **Three parameters more, each an assumption the theorems make nothing of beyond its type:** the float
   carrier of `float v` (`float_of_int`), `string v` (`int_text`) and `ScaledDecimal.scaleOf`
   (`scale_of`), carried on `prims` under the names `Frame.fst`'s host carries them, so the differential
   instantiates both models from one production function (`pPrims` beside `fHost`). What `scaleOf`
   answers — the width, the grammar — is neither modelled nor claimed: a decimal text is an opaque
   carrier here as it has been since Phase 277. One thing is read as the cell's own text by a NAMED
   premise (`pipeline-temporal-cells-canonical`): a date or a timestamp the typed carrier reads back as
   the canonical text of its value, which is the cell's text when that text is canonical — every
   primitive produces one, and only a literal can carry another into a derive — so `pack_admits` carries
   a temporal cell as itself where `packAt` consults `TemporalText`.

3. **The cells' typing refuses two types no column type holds together, as production has since D18.**
   `typeFromCells` refuses a derived column whose present cells span two types the widening join does
   not relate (`typesBesideEachOther`, D18 item 2), and the model's `column_type_by` did not — the
   differential excused the pair as outside the model's domain. Modelled now (`first_disjoint`: the first
   pair the fold met, the type so far beside the cell's, named in the refusal's own text), and the excuse
   is gone from the pipeline differential. It matters to the packing: with it, a column the cells type
   holds one family only, so the typed pass is abandoned on exactly one reachable corner — a decimal
   column past the scaled carrier's width — and that corner is where item 5 found production diverging.

4. **The planner's reorder theorem names a packing premise; the corollary does not.** Under the
   column-wide verdict `derive_then_filter` is FALSE as Phase 269 stated it, over arbitrary primitives:
   a primitive that yields, for a derive the typer decides `float`, an int on one row and a string on
   another, with the filter dropping the string's row, boxes the column on the derive-then-filter side
   (the int reads raw) and carries it typed on the filter-then-derive side (the int reads as a float).
   The theorem (`derive_then_filter`, `reorder_in_context`) now requires `packs_as_is`: the decided type
   carries the derived cells as they are — no int under a float or a decimal column — under which the
   packing is the identity on the whole column and on the kept rows alike (`pack_cells_as_is`,
   `kept_cells_has_type`). **The corollary `planner_reorder_sound` keeps Phase 338's statement exactly**:
   the verdict's premise `admits` already gives every derived cell the typing's type (`verdict_sound`),
   from which the packing premise follows (`derive_cells_fit`), so the planner's admission is proved as
   before. The premise is also read off production's own primitives by a `Proofs.Oracle` case over drawn
   tables and expressions. Stated here rather than weakened silently, per the phase's own rule.

5. **The walk's switch compares the PACK KEY — the decided type AND the verdict — and so does production's
   `retypedSince`; a second production divergence, found and fixed.** D37 recorded the type: a derive
   whose decided type is not the prior evaluation's reuses nothing from there on. The verdict is a
   function of the whole column as the type is, so a named row can flip it under ONE type — a decimal
   text past the width arriving, or leaving — and every stable row's int then reads the other way
   (`Decimal "1"` on the typed pass, `Int 1` on the boxed one) with no delta naming the row. The models
   record the key per derive (`Incremental.entry.e_pack`, `IncrementalMaintained.entry.e_pack`) and
   switch to `no_reuse` where it moved (`walk_reuse`'s two arms: `unchanged_extend` under one key,
   `walk_no_reuse` past another; `mwalk_reuse` likewise). Production's walk (`Incremental.fs`) widened an
   int under a float or decimal column UNCONDITIONALLY (D37's `widenCells`) where `Vec.packAt` abandons
   the typed pass, and compared the type alone: a probe (`pBoxedShapes` in `ProofOracleTests`) showed
   the full evaluation answering `Float 1.5` where the refresh refused `arithmetic between a decimal and
   a float`, on a derived column a named row retyped from int to decimal and boxed in one move. Fixed
   under the operator's 2026-10-12 ruling: `widenCells` widens only under `typedPack`'s verdict, which
   mirrors `packAt`'s (admission, and `ScaledDecimal.scaleOf` over the rendered texts for a decimal
   column), and `retypedSince` compares the type beside the verdict. A `behaviour` change on the open
   `0.41.0` draft (`STABILITY.md`). The faithful alternative — widening every int in the boxed carrier
   too, in `Vec.packAt` and Core's `Column.ofCells`, so that an int under a float column is ALWAYS the
   float of its value and the verdict vanishes — would make item 4's premise unnecessary and the key the
   type alone; it changes the full evaluation's answer on a degenerate column in two repositories and is
   the operator's to decide, not this phase's.

6. **Cost and evidence.** Every theorem over the derive is re-proved with its statement unchanged
   (`prime_is_full`, `refresh_is_prime`, `refresh_agrees`, `mprime_is_full`, `mrefresh_is_prime`,
   `mrefresh_agrees`, `eval_total`, `budget_monotone`, `work_bounded`, `verdict_sound`,
   `planner_reorder_sound`) except the two of item 4, whose premise is new. The differential's generators
   draw the corner one pipeline in four (`pCornerShapes`: a float retype read by a `Mod`, a decimal
   retype read by an `Add` of a float, and a decimal retype past the width, each on the rows that stand);
   the three models are held to production on every retype shape (`pCornerAgrees`); the old raw derive
   is kept once (`pRawDerive`) to show it answering the first shape where production refuses. The
   module budgets stand: the re-measured cold runs are recorded on each entry in `modules.json`.

**Consequences.** D37 item 4 is closed; the two incremental models are faithful to production on a
retyped derived column read by a type-sensitive step, and on a boxed one. `Incremental.prime` and
`Incremental.refresh` read a derived column exactly as `DataFrame.evalPipelineWithInEnv` carries it,
typed or boxed. The pipeline differential no longer excuses the D18 refusal. `proofs.json` gains the
premise row `pipeline-temporal-cells-canonical` and the rows over the derive name the packing.

## 2026-10-15 — D37: the incremental refresh is modelled and proved past the row-local class — the maintained `GroupBy`, `Project`, `Limit`, `Window` and the filtering joins — in a module of its own over `Incremental.fst`; a derived column the cells retype is read as the reference packs it and reuses nothing downstream (a production defect, fixed); `Sort`, the resumed prefix fold, the fast paths, the guards, the chunked path and the codec stay declared out

**Context.** D31 proved the refresh for `Filter` and `Derive` and declared the other classes out, each
with its reason; Phase 433's deferral carried them to Phase 464. Each class keeps state a delta must
update rather than recompute — the group table and its aggregate cells, a join's key index, a window's
column — and each is where a silent divergence from the full evaluation is most likely. The shard asked
for a scope note before modelling, the model, the agreement and state theorems per covered class
composed with 433's walk, a differential with a go-red per class, any production divergence fixed
under the operator's 2026-10-12 ruling, and the classes still out named.

**Decision.**

1. **The classes covered, and each aggregate's maintenance rule.** The walk production runs on the
   general refresh path (`runIncremental`) is modelled for `PropagateRows` (`Project` beside the two
   row-local verbs), `MaintainGroups`, `TruncateOrder`, `RecomputeFrame` and `FilterByRelation`:
   - the maintained `GroupBy` — the groups of the rows alive at the step in first-appearance order of
     the key cells, the partition `evalGroupBy` builds; a group's aggregates REUSED exactly when every
     member is stable and the group's member tokens are the prior group's in order (`groupStable`,
     `sameMembers`), and RECOMPUTED over its members in member order otherwise by the reference's own
     aggregate function. **Every aggregate is maintained by recompute on this path** — `Sum`, `Mean`,
     `Min`, `Max`, `Count`, `Median`, `StdDev`, `First`, `Last` and `CountDistinct` alike — because a
     touched group is recomputed whole rather than updated; the invertible `Count` and int `Sum` of
     the in-place and append fast path (`groupStepInPlace`, `maintained`) are that path's, which is
     declared out with it. A group with no member vanishes; the group table walks on under one token
     per key, a group reusable downstream exactly when its aggregates were (`Stable[g] = existed &&
     not RecomputedAt[g]`);
   - `Project` — a column permutation and renaming that touches no row and caches nothing, refused on
     an unknown source and then on a repeated output (D35);
   - `Limit` — the positional slice (`max 0 offset`, `max 0 n`, clamped) over the frame the walk holds,
     which is the reference's frame; a row re-entering the slice has no cached cells past where it
     left and is evaluated fresh — the model's cache misses it;
   - `Window` — the column the reference computes over the rows alive at the step in their order,
     appended, with NOTHING reused after it (`Array.fill r.Stable … false`); the resumed prefix fold
     of Phase 333, which keeps the stable prefix of each partition, reuses MORE than the model and is
     declared out (its output is the same column; what is unproved is that its reuse is sound);
   - the filtering joins (`Semi`, `Anti`) — a verdict per left row, kept iff some right key matches or
     none does, cached as a filter's cells are and reused for a stable row exactly when the joined
     relation's key index is the prior one (`relationMoved`); the relation is resolved afresh on every
     refresh.
   **Declared out, with its reason:** `Sort` (`MergeOrder`) — its refresh merges the named rows into
   the prior PRODUCED order by binary insertion, valid while the stable rows' arrival order stands
   (`arrivedAsBefore`); proving that equal to the full stable sort needs the uniqueness of a sorted
   permutation under the comparator read as a strict total order with the arrival position as the
   tie-break, and a comparator premise (`compareResolved` is a total preorder within a typed column;
   across cell types `compareCells` answers `None`, read as `0`, which is not transitive) — a proof of
   its own size, which this phase did not start rather than ship half of; the operator decides whether
   it gets a successor.
2. **A module of its own, `proofs/IncrementalMaintained.fst`, opening `Pipeline`, `Incremental` and
   `Relational`.** On proof cost, per the shard: `Incremental.fst` checks in 101s and its two
   theorems would have been re-priced by every arm added to its walk, where a sibling keeps each
   module's cold check its own (Phase 458's lesson). The row-local arms are `Incremental.fst`'s
   definitions by name (`step_cells` restated over any cell function as `step_cells_by`, so a join's
   verdicts are the same step; `keep_true`, `extend`, `ext_schema`); the reference is `Pipeline.fst`'s
   driver over `Relational.fst`'s `other_of`, narrowed to this module's row functions (`mrows`), so the
   schema and every refusal are the relational model's and only the rows are new. Four parameters,
   each an assumption and each instantiated at production's own by the differential: the aggregate
   function (`Column.aggregate`, which the streamed accumulators equal by law), the join key match
   (`joinKeysMatch`), the window column (`windowStep`) and the group token. Three theorems:
   `mprime_is_full`, `mrefresh_is_prime` (output, every entry, the group table and the tail),
   `mrefresh_agrees`. The delta premise is 433's read one row at a time (`wk`): a reusable row the
   prior source held under its token has the cells it had, and one it did NOT hold is admitted — the
   cache misses it, as production reads it unstable — so the premise is strictly weaker than
   `Incremental.unchanged`, and it is what a `Limit` needs (a row re-entering the slice). Two premises
   are added: the prior source's tokens are distinct (production's witness refuses a duplicate
   identity, `RowIdentityUnusable`) and the group token is one per key (production mints it so). The
   theorems are stated for a new source of the prior's SCHEMA, types included — a source whose schema
   moved is `SourceSchemaMoved` in production and evaluates in full; a DERIVED column's retype is
   inside the walk and is item 3.
3. **The defect, and the fix.** A derived column typed by its cells (`ByCells`: an int arm beside a
   float arm in a `Case`, a `Coalesce` of an int column and a float literal) is a function of the
   WHOLE column, so one named row can retype it from int to float while every other row's cell stands.
   The reference packs the column under its decided type (`Vec.packAt`: an int under a float column is
   the float of its value, under a decimal column the decimal of its value, Core `1.0.0`); the walk held
   the step's cells RAW. The probe (`mRetypeShapes` in `ProofOracleTests`) showed the refresh — and the
   PRIME — answering where the full evaluation refused: a `Mod` over the retyped column read `Int 1`
   where the reference read `Float 1.0`; a reused group's `Sum` stood at `Int 3` beside a fresh
   `Float 3.0`; a recomputed `Min` answered the raw int. Production's own contract ("The result equals
   a full `DataFrame.evalPipelineWithInEnv` over `source` — always, for every delta") was false on
   these, and the operator's ruling (2026-10-12) is that it is fixed, not documented. Three changes in
   `Incremental.fs`, all internal:
   - the FRAME's column after a derive is the step's cells widened to the decided type (`widenCells`);
     the CACHE keeps the cell as evaluated, because a later refresh may type the column back and the
     column's type is decided over the raw cells on every refresh, as the reference decides it;
   - a derive whose decided type is not the prior evaluation's (`retypedSince`: the prior type re-read
     off the prior step's reached cells for a `ByCells` typing; a decided type is the same on every
     refresh) clears stability for every row, in place as a window does, and ends the in-place
     reading — every cell cached downstream and every reused group was computed over the other reading
     of the column;
   - the in-place group path (`groupStepInPlace`) refuses a frame whose unlisted slots are not all
     stable, which is its own invariant made a precondition.
   The model carries the rule (`entry.e_type`, the switch to `no_reuse`), so `mrefresh_is_prime` is
   stated over a retype rather than around it. A `behaviour` change on the open `0.41.0` draft
   (`STABILITY.md`): a consumer whose pipeline retyped a derived column between refreshes read a result
   the full evaluation would not have given, and now reads the full evaluation's.
4. **A gap in the REFERENCE model, named and not closed here.** `Pipeline.fst`'s `eval_derive` holds
   the derived cells raw (`zip_append f.rows cells`) where production packs them, so the pipeline
   differential and Phase 433's `prime_is_full` were stated against a reference that differs from
   production on exactly the corner of item 3 — the pools drawn never reached it, and the comparison
   through `ColumnCodec.encode` packs the output, which hides a raw cell unless a type-sensitive step
   reads it first. The correct closure is a widening in `eval_derive` (and in `Incremental.fst`'s
   `extend`), which re-prices every theorem over the derive in three modules; `Pipeline.fst` is outside
   this phase's files, and the work is a phase of its own, named in Phase 464's outcome as a successor
   need. Until it lands the two incremental models are faithful to production everywhere but a
   retyped derived column read by a type-sensitive step, which the differential's generators do not
   draw and the regression case pins on production directly.
5. **Still declared out, each with its reason (the rest of D31 item 3):** the staleness guards
   (`PipelineChanged`, `EnvChanged`, `SourceSchemaMoved`, `DeltaIsFullRefresh`, `OrdinalAddressing`,
   `RowIdentityUnusable`) send a refresh to a full evaluation, which `Pipeline.fst` already models — what
   is unmodelled is the decision, a comparison of the state's keys with the inputs; the quiet-delta
   shortcut (`ReusedPrior`) returns the prior output under `Delta.isQuiet` and an unmoved source, a
   claim about `Delta.isQuiet`, which is `Delta.fs`'s; the in-place and append fast paths (Phases 323,
   359) compute the same caches from the prior arrays, with their invertible `Count` and int `Sum` and
   the `patchedColumn` output; the chunked path (Phase 268, `refreshPrepared`) is a second evaluator
   over a `Derive`-only pipeline; the state's wire form (`IncrementalCodec`) is a codec.

**Consequences.** `Transform.Project`, `Transform.GroupBy`, `Transform.Limit`, `Transform.Window` and
`Transform.Join` join `Incremental.prime` and `Incremental.refresh` on the ladder's proved rows for the
incremental seam, and `DataFrame.aggregateCells`, `DataFrame.joinKeysMatch` and `DataFrame.windowStep`
leave the census for the differential's tested row, as the parameters it instantiates. The
`Incremental` census entry's reason names what the two models now cover and what remains. The proof leg
gains its seventh module, costed in `modules.json`.

## 2026-10-15 — D36: the static walk types a `Union` as the evaluator does, through the evaluator's own per-column function; a pair the evaluator refuses reads as a column of undecided type, an undeclared right decides no type, and `walk_sound` holds for every verb

**Context.** D32 recorded finding (b): `SchemaWalk.ofTransform`'s `Union` arm answered its input, so
the walk kept a union column's LEFT type where the evaluator, since Phase 423 (D18), widens the column
to the join of the two sides' types — an `int` beside a `float` is a `float` column. A planner, or the
pipeline-query registry's agreement check, reading the walk read a type the evaluator does not produce;
`walk_sound` carried `Union` as its one exception and `union_walk_type_drifts` stated the drift. The
operator ruled (2026-10-15) that it is fixed, not documented.

**Decision.**

1. **One function.** The evaluator's per-column rule is lifted out of `evalUnion` into
   `DataFrame.unionField` (internal): the left field under the one unit the pair states (D29) and the
   join of the two types (`joinColumnType`, D18), or the refusal — the `UnitMismatch` first, then
   `typesBesideEachOther`. `evalUnion` folds it over the zipped schemas exactly as before, and the
   walk's `Union` arm reads the same function, so the two cannot drift again by one being edited.
2. **The walk's arm reads the right as a join does** (`ofSource`), and answers in three cases:
   - both operands closed, with the same names: the left's columns zipped by position with the right's
     (as the evaluator zips them), each typed through `unionField` — so the column's type, unit and
     metadata are the evaluator's;
   - an open left against a closed right: each visible column meets the right's first column of its
     name, which is the column it is zipped with whenever the evaluator answers;
   - an undeclared right (`Ref` with no declared schema): every name stays — any answer has the left's
     names — and no type is decided, because the right's type may widen any of them (`int` widens to
     `float` or `decimal`, a coarse timestamp to a finer unit). Keeping the types that no widening can
     move would need the walk to enumerate Core's widening relation, which a new column type would
     silently outgrow; an undeclared source already costs the walk certainty elsewhere (a join's right
     columns), and the registry refuses an undeclared source before it reads the walk.
3. **A refused pair is a column of undecided type, not a refusal.** The shard asked that "a pair the
   evaluator refuses, the walk refuses with the same typed error"; that premise does not hold.
   `SchemaKnowledge` has no refusal channel (D26, D28 item 5, D35 item 5) — the walk answers a knowledge
   for every pipeline, including the ones the evaluator refuses — and inventing one for a single arm
   would be a second, partial refusal surface beside the evaluator's. So: two closed operands whose
   names disagree answer the input, unchanged (the evaluator's `JoinError`); a column pair no type holds
   together (an `int` beside a `bool`) or whose units disagree answers the column with no decided type,
   as the unpivot's refused value column already does (Phase 338). The typed refusal lives where it
   always has, in the evaluator (`unionField`'s error, through `evalUnion`); a registered pipeline query
   meets the undecided column as `TypeUndecidable`, whose doc now names this case.
4. **The unit under an undeclared right is the left's.** `ColumnKnowledge.Unit` has no "unknown" (a unit
   is a fact of the schema); where the left states none and the right is undeclared, the evaluator may
   adopt the right's unit and the walk cannot say which. The arm keeps the left's, as the old arm did for
   every case. This is outside the proof (the model is unitless) and unreachable from the registry
   (every source declared); a closed right decides the unit exactly, through `unionField`.
5. **The model.** `proofs/Relational.fst` lifts `union_ty` out of `union_types` (the shared function's
   type half), models the arm's three cases (`union_known_pos`, `union_known_by_name`, `untyped`), and
   proves `union_sound`; `walk_sound` now holds for all fourteen arms with no `Union` exception.
   `union_walk_type_drifts` is retired with the arm it described, and its witness is restated as
   agreement (`union_walk_widens`: over `int` beside `float` both answer `float`); `union_sound_names`,
   the names-only half, is subsumed and retired. Re-introducing the old arm (the input, unchanged) leaves
   `union_sound` unprovable, and so `walk_sound` — measured, one perturbation run. The differential now
   also holds the shipped walk to the model's `sound` on every step production answers, and a go-red shows
   the old arm's knowledge is not sound on the widening shape.

**Consequences.** A behaviour change to a public walk, of D21's, D26's and D28's class: no public
signature moves (`unionField` is internal) and `api/` is unchanged. It rides the open `0.41.0` draft
(`STABILITY.md`). A consumer that read the left type off a walk over a widening union now reads the
joined type; one over a refused pair or an undeclared right reads an undecided type. A registered
pipeline query that declared the left type of a widening union column is now refused at registration
with `TypeDiffers` (declare the joined type), and one over a refused pair with `TypeUndecidable` (it was
refused by the evaluator on every frame already). The caller survey (Phase 469's record) found no
in-workspace consumer whose behaviour moves at its current pin.

## 2026-10-15 — D35: a `Project`, a `GroupBy`, an `Unpivot` and a `Pivot` refuse an output that names a column twice, with a `TypeError` naming the verb, the name and both producers, through one check; the pivot refuses too, rather than suffixing, although its collision is the data's

**Context.** D32 recorded finding (a): four verbs answer `Ok` with two columns of one name —
`Project [a, x; a, x]`, a `GroupBy` key named as an aggregate, an `Unpivot` id column named `variable`, and a
`Pivot` whose index column is named as an `on` value's text — and stated each as a theorem with a witness
that the differential asserted on production. Such a table re-enters the evaluator by name as the first of
its two columns (D25) and Core's `Table.validate` refuses it. D20 closed the join's way of producing one
(a suffix) and D28 the window's (a refusal). The operator ruled (2026-10-12, applied 2026-10-15) that the
four are fixed in production, not documented, and that the pivot's choice is made on correctness.

**Decision.**

1. **One check.** `DataFrame.repeatedOutput` takes each output column's name beside what produced it,
   in output order, and refuses the first name an earlier output took:
   `TypeError("<verb> output 'x' is named twice, by <first> and by <second>; give each output a name of
   its own")`, where a producer reads `the projection of 'a'`, `the key 'k'`, `the aggregate over 'v'`,
   `the id column 'n'`, `the unpivot's variable column`, `the index column 'i'` or `the column for the
   on value 't'`. The four verbs ask it through `projectRefusal`, `groupByRefusal`, `unpivotRefusal`
   and `pivotRefusal`. It covers every repeat a verb can make, not only the four witnesses: two pairs of
   one output, a key twice, two aggregates of one name, an id column twice or named `value`, an index
   column twice, and two value columns whose `on` values share a canonical text.
   **Except where the two value columns are one column.** The pivot's on-values are `List.distinct`
   over the cells, and two `NaN`s are distinct there while `cellEq` (Phase 325) equates them: the pivot
   answered two columns named `"NaN"`, each collecting every `NaN` row — identical in name and content,
   pinned by Phase 264's oracle. Refusing a float `on` column for holding two `NaN` rows would make
   ordinary data fail; the two are the one column they name, so the pivot keeps one on-value of each
   text among the cells `cellEq` equates, and refuses only a text shared by cells `cellEq` keeps apart.
   No information is lost (each of the pair collected the same rows), and the model already read the two
   `NaN`s as one cell (its floats are their round-trip text).
2. **Where it sits.** After every name the arm resolves (an unknown name is still named first, as in
   D28), and before any row is read: `Project`, `GroupBy` and `Unpivot` refuse over a source with no
   rows exactly as over one with rows, and the unpivot refuses before its value column is typed. The
   pivot's value names are its distinct present `on` values, so its check reads the `on` column and runs
   after the on values are collected and before any value is aggregated: an aggregate refusal over a
   colliding pivot is never reached.
3. **No new `EvalError` case.** `TypeError` is what D28 answers for the window's collision; a new case
   would break every exhaustive match over `EvalError` and the model's error type for a refusal a host
   reports by its text (D25, D28).
4. **Every evaluator path asks it.** The reference arms, the incremental seam's own `Project` and
   maintained `GroupBy` walk (`Incremental.fs`, which would otherwise have answered the table the
   reference refuses), and, through the reference, the pivot and the unpivot. The planner's totality
   verdict counts a repeating `Project` as not total and stops tracking the schema at one, so no rewrite
   moves a step across it.
5. **The schema walk still names the column twice.** The shard asked that "the static walk refuses
   the same pipelines"; that premise does not hold. `SchemaWalk.ofTransform` carries no refusal verdict
   (D26, D28 item 5): it answers a knowledge for every pipeline, including ones the evaluator refuses for
   an unknown key or a window's taken name. Over a refused `Project` it answers the closed knowledge with
   the name twice, which is about a pipeline the evaluator refuses and is marked by the repeat; the
   walk's soundness theorem is stated over steps that answer `Ok` and is untouched. The planner, which
   does decide, decides as the evaluator does (item 4).
6. **The model.** `proofs/Relational.fst` models the check clause for clause (`repeated_output`, the
   producer lists, the refusal placed where each arm places it), and proves `project_no_dups`,
   `group_by_no_dups`, `unpivot_no_dups` and `pivot_no_dups` with no hypothesis on the input, beside
   `window_no_dups` and `join_no_dups`: none of the six verbs that name their output answers `Ok` with a
   name twice. The four `*_repeats_name` witnesses become `*_repeat_refused`, the same inputs answering
   the refusal; the differential compares production's refusal text to the model's on the four shapes
   and over the generated sample. Removing the check from the group-by arm leaves `group_by_no_dups`
   unprovable (one perturbation run).

**Why the pivot refuses rather than suffixing.** D32 left the pivot open because its colliding name is
the data's, like a join operand's, and D20 suffixes a join to keep it total when an operand gains a
column. The pivot is not the join's case. A join's right column keeps its identity under a suffix —
`x_right` is still "the right's `x`", every right name is suffixed by one rule, and `joinRightNames`
lets a caller compute it. A pivot's value column has no identity BUT its name: the column for `on`
value `eng` is found by reading `eng`, and every consumer of a pivot — a later step, a host rendering
the table, a chart keyed on the on values — reads it that way. A suffix would rename exactly the one
column whose name is its meaning, silently, and only when the data happens to hold the index's name: a
later `Col "eng"` would read the index where it meant the value column (or the reverse, under the other
suffix), a plausible wrong answer with no error anywhere — the outcome D28 rejected for the window.
Suffixing the INDEX instead would break every read of the column the author named. A refusal is honest:
the pipeline names a column that the data also names, so the reads of that name are ambiguous, and the
author resolves it by renaming the index (a `Project` ahead of the pivot) or filtering the value. The
cost is that the pivot becomes partial in its data — a pipeline that ran over a table refuses over one
that gains such an `on` value. That is not new: the pivot already refuses by its cells through an
aggregate, and the unpivot by its melted cells (Phase 338); and the static walk already declares the
pivot's value names unknown, so no planner or registry relied on knowing them.

**Consequences.** A behaviour change to a public evaluator, of D21's, D26's and D28's class: no public
signature moves (the check and its four callers are internal), and `api/` is unchanged. `0.40.0` is
tagged, so the change ADVANCES the slot to `0.41.0`, a new draft (`STABILITY.md`). Four refusal vectors
join `conformance/laws/transform-laws.json` after the unit shapes (`repeat-*-refused`, over the decimal
table); every earlier vector is byte-identical, the file's `kitVersion` stamp follows `<Version>`, and the
corpus copy follows the release rule (D6 (4)) and is not re-stamped by a draft. A consumer whose pipeline
named an output twice renames one of the two columns.

## 2026-10-15 — D34: the dense frame and its kernels are modelled in their own module over `Pipeline.fst`'s frame and Core's column; the read-back is the table's rows and the filter kernels are the row-major filter, as theorems; the native member, the morsel runner, the sort kernels and the rope are declared out

**Context.** Phase 423 made the evaluator's frame a view over Core's typed column vectors, and Phases 280,
376, 420, 422 and 440 gave it a decimal carrier, byte masks, a null-free column with no mask, integer
temporal storage and a decimal view. `Frame.fs` and `Kernels.fs` had no model: every theorem of
`Pipeline.fst` is stated over the ROW-MAJOR frame, and nothing said the dense frame the evaluator
actually runs over is that frame. Core proved the matching fact for its own typed column (Phase 419,
`ColumnRefinement.fst`); Phase 435 asked for the compute side's.

**Decision.**

1. **A module of its own, `proofs/Frame.fst`, opening `Pipeline`.** D31 and D32 are the precedent. It
   reuses `Pipeline`'s `cell`, `frame`, `wframe`, `eval_expr`, `filter_rows`, `eval_step`, `index_of`
   and `assoc` by name, and states its theorems against them: `logical` is the dense frame as the
   `wframe` `Pipeline.fst` evaluates. It restates `List.tryItem` (`vnth`) because `Pipeline`'s is
   `noextract`.
2. **Core's column is restated to the minimum, and named.** `validity` (`AllValid | Mask`), the seven
   `ColumnData` cases and `Column.cell` (`core_cell`) are written at `Pipeline.cell`; `core_cell` is the
   Fuaran.Core repository's `ColumnRefinement.cell_at` with its `to_mask` unfolded, and that module's
   `cells_of_nth` is what makes it the i-th of `Column.toCells`. Fuaran.Core is read, not edited; the
   correspondence is stated in the module header and on the ladder row.
3. **The theorems say what the code computes.** `of_table_reads_columns`: over a table whose columns
   hold their fields' types and the table's length, cell `(i, j)` of `Frame.ofTable` IS
   `Column.cell i` of column `j`, at every column type, with a mask or `AllValid` — the abstraction
   theorem. `rows_of_reads`: the read-back is total and is the vectors through the selection.
   `pack_reads` / `of_rows_exact`: the row door carries each cell as its type does, and rows carried
   exactly come back as they went in. `to_table_reads`: `Frame.toTable` writes exactly the logical rows.
   `filter_kernel_is_step`: where `filterBits` answers, the kernel branch of the filter IS
   `Pipeline.eval_step`'s `Filter` over the read-back, never an error.
4. **Every parameter is an assumption, listed.** The host record (the temporal texts and readers, an
   int's float carrier and decimal text, `scaleOf` / `tryScaled`, `compareFloat`) is assumed NOTHING by
   the read-back theorems — `Column.cell` and `Vec.cellAt` call the same functions. The kernel theorem
   rests on `kernel_prims` (the comparison and Kleene primitives at exactly the cells a leaf meets,
   ladder row `frame-kernel-primitives`) and, for the decimal leaf, on `decs_ok`, which the boundary
   builds under one premise of `scaleOf` (`scale_admits`). Two representations are taken as given and
   named on `frame-host-abstract`: the decimal carrier's float64s are exact integers (`Width`), and a
   bitmap packs 32 rows to a word.
5. **The acceptance's two perturbations, measured, one per prover run.** Reading a present bit as absent
   in `Vec.cellAt`'s int arm fails `view_reads`; building `AllValid` as an all-absent mask (dropping the
   null-free guard) fails `mask_of_reads`. A third, comparing a left-hand constant without `flip`, fails
   `bits_eval`.

**Declared out, with the reason.**

- **The native kernel member** (`Kernels.Native`: 128-bit vectors, the thread pool). Its contract is the
  portable member's answer; the suite runs the transform law vectors through both on one host, and the
  differential runs `filterBits` through both against the model.
- **The compiled filter path and the morsel runner** (`compileExpr`, `filterMorsel`, `keptInOrder`,
  `RunMorsels`). It is the reference evaluator's per-row answer, the very evaluator `Pipeline.fst`
  models; a model of the compiled tree would be a second model of `evalExpr`. The theorem covers the
  kernel branch `filterBits` takes, which is the part of the filter with no other model.
- **The keyed verbs' kernels** (`SortFinite`, `SortPositions`, `Partitions`, `RunPipelines`). They are
  scheduling and sorting over verbs `Pipeline.fst` takes as a parameter (D32 narrowed it to the rows);
  their contract — every correct sort answers one array — is stated on the record and held by the suite.
- **`gatherOrNull`, `Vec.append` / `slice` / `concat` / `setAt` / `sameCells`, `ofTemporalTexts`,
  `Frame.project` / `withColumn` / `appendColumn` / `concat`, the `Shared` allocator.** The verbs that
  use them are parameters of `Pipeline.fst`; none is on the read-back's path or the filter's.
- **The rope** (`Chunked`, Phase 268), whose edit `ColumnOps.fst` models, and the two boxed arms of
  `Vec.toColumn` (a non-string string-family vector and `Cells`), which build through Core's
  `Column.ofCells`, modelled in the Fuaran.Core repository (`to_cells_of_cells`).

**No defect was found.** Two behaviours the model makes explicit and the theorems state rather than
change: a column of another type or length than its schema field is read through `Vec.pack` of its cells
padded or cut to the table's length (so an int column under a float field reads as floats); and a mask
of another length than its column's values is read padded absent or cut, as Core's `Column.cell` reads
it. Both are what the per-index reads always answered (Phase 423); `Table.validate` refuses both tables.

**Consequences.** `proofs.json` gains three proved rows (`frame-read-back`, `frame-table-out`,
`frame-kernel-agrees`), a tested row (`frame-differential`) and two assumed rows (`frame-host-abstract`,
`frame-kernel-primitives`). The operation census loses `DataFrame.prepare`, `DataFrame.prepareRows` and
`DataFrame.toTable`, now named by the read-back and table-out rows. `modules.json` budgets the module
(120s, floor 26s); `check.ps1` and the oracle project register it. No production code changed.

## 2026-10-15 — D33: a declined shape's tick is held to what a declined refresh is — the diff and the reference evaluation it runs, timed beside the tick, plus the per-call floor; and a refresh no longer walks an embedded relation to learn that its pipeline did not move

**Context.** Compute `v0.39.0` (`02d25fd`) failed its publish run (38554709427, attempt 1) in the
Release clock leg: `append pivot @ 100000` read tick 10.07 to 10.34 ms against a one-thread full
evaluation of 5.53 to 5.76 ms (x1.79 to x1.82, bound x1.6) on all three counted attempts. The run's second attempt
read the same cell at x1.19 and x1.29, passed and published `0.39.0` to nuget.org, so the release
stands; the gate had flapped, not caught a regression. The same cell had been red on `ci` at `fc0ef74` and `3142469`, and read x1.08 on the same
commit's `ci` run; `ci` attempt 1 at `02d25fd` was red on `append inner join` instead. Both cells that
flap are the two shapes the seam DECLINES (`ScalingTests.refreshDeclinedShapes`, D27): a refresh of
either is `Incremental.runReference`, the full evaluation through `DataFrame.evalPreparedCounted` on the
default kernel set, while the tick bound's denominator is the full evaluation on `Kernels.oneThread`
(Phase 371). D27 already leaves those two shapes out of the refresh-against-full case for that reason;
the edit and append tick cases still held them to `1.6 x full`, and on x64, which has no recorded
baseline table (D19), to that arm alone. The operator chose (2026-10-14): first measure the declined
path and remove any avoidable cost, then rule how a declined shape's tick is judged.

**Measured first (Phase 459, before any change).** Release, `dotnet <dll>`, Arm64 (12 logical
processors, calibration baseline 4.91 ms), the corpus nodes and deltas exactly as the clock leg builds
them, each figure the best of five batched samples, two probe runs; the probe split the tick into the
diff, the boundary (`DataFrame.prepare`) and the reference evaluation, and timed the evaluation on the
default set beside `Kernels.oneThread`. At 100,000 rows, in ms:

| Cell | tick | diff | refresh | prepare | eval, default set | eval, `oneThread` | full, `oneThread` | pipeline `=` |
|---|---|---|---|---|---|---|---|---|
| `inner join` edit | 6.7–9.1 | 1.31–1.39 | 6.66–6.79 | 0.12 | 4.7–5.5 | 5.6 | 5.6–5.8 | 1.19–1.22 |
| `inner join` append | 7.4–8.6 | 0.60–0.91 | 6.2–7.1 | 0.14 | 5.0–5.8 | 5.6 | 5.6 | 1.19–1.23 |
| `pivot` edit | 13.9–16.0 | 1.75–2.08 | 13.9–15.7 | 0.25 | 13.3–16.7 | 13.2–13.8 | 13.9–16.3 | 0 |
| `pivot` append | 15.1–17.1 | 1.09–1.22 | 14.4–15.1 | 0.21 | 13.7–14.5 | 13.2 | 13.5–15.4 | 0 |

Timed against each other in one interleaved window (the tick, and the diff followed by the reference
evaluation over a fresh source object, as the tick receives it), the tick's excess over "diff plus
reference evaluation" read 0.95 to 1.42 ms on the join at 100,000 rows (once 0.11), 0.23 to 0.28 ms at
20,000 and 0.015 ms at 1,000; on the pivot it read -1.48 to +0.38 ms at 100,000 and within 0.2 ms at the
other sizes, which is the reading's noise. The refresh allocates what the full evaluation allocates to
within 0.01 MB a call (join 7.58 against 7.57 MB, pivot 9.35 against 9.35 MB at 100,000 rows). The diff
allocates 0.19 MB a call for the edit and 1.63 MB for the append.

So the declined tick costs one diff, one reference evaluation and a preamble, and the three suspected
costs read as follows:

1. **A second boundary walk: none.** `runReference` prepares the source once (or takes the state's
   prepared form) and evaluates over it; the boundary is 0.12 to 0.25 ms at 100,000 rows either way.
2. **A kernel set that loses on few cores: none, for these shapes, by construction.** `evalPivot` and
   `evalJoin` take no kernel set, so the default set and `oneThread` run the same code for both; the
   measured evaluations agree within their noise. The x64 publish runner's x1.8 is therefore not the
   kernel set.
3. **Work the full evaluation does not do: found, on the join.** The refresh's staleness test asked
   whether the pipeline moved with structural equality (`pipelineMoved`, Phase 355), not through
   `samePipeline`, the identity-first test Phase 333 wrote for exactly this, which had been left
   unreferenced. The join EMBEDS its right relation in the pipeline, so every refresh walked 100,000
   rows of it cell by cell to learn that a pipeline held across ticks had not moved: 1.2 ms at 100,000
   rows, a fifth of the full evaluation, and 0.25 ms at 20,000. That is the join's whole measured
   excess.

The tick is not "one full evaluation plus a one-row diff" for an edit, and is not meant to be: an edit's
diff keys every row of both tables (Phase 272's floor of any diff by identity), 1.3 to 2.1 ms at 100,000
rows. That is the diff's cost for every shape, accepted or declined, and it is no part of the declined
path.

**x64, from the `ci` and publish logs** (no x64 machine here; `ci` runs on a push to `main` and on pull
requests only, so a branch push takes no x64 reading, and the split above was not repeated there). At
100,000 rows: publish attempt 1 (calibration 3.74 ms), `pivot` edit tick 7.00 ms against full 8.13 ms
(x0.86) and append 10.07 to 10.34 against 5.53 to 5.76 (x1.79 to x1.82); `inner join` edit x1.46, append
x1.19 to x1.39. Publish attempt 2 (calibration 4.71 ms): `pivot` x1.19 to x1.29, `inner join` x1.18 to
x1.38. `ci` at `02d25fd` (calibration 4.56 ms): `pivot` x1.08 to x1.24, `inner join` x1.28 to x1.63. The
pivot's full evaluation read 8.13 ms in the edit loop and 5.53 to 5.76 ms in the append loop of one leg,
over sources one row apart: the denominator itself moves by 1.4 times between two windows on that
runner, which a tick and a denominator timed in separate windows cannot cancel.

**Decision 1 — the avoidable cost goes (459.B).** `pipelineMoved` reads the in-process case through
`samePipeline`: identity first, structural equality only when the objects differ. A decoded state still
compares canonical hashes. The answer is the one `=` gives except for a pipeline object holding a NaN
literal, which now reads as unchanged against itself instead of moved; both readings refresh to the same
result, and the declined footprint and its reason are unchanged (the main suite's pin and the law
families are green). After it, the join's excess over "diff plus reference evaluation" read within 0.13
ms either way at every size. Nothing else in the declined path was avoidable: the boundary is a view
since Phase 423 (a second `prepare` injected into `runReference` moved no clock-leg cell), and the
kernel set is not read by either shape.

**Decision 2 — how a declined shape's tick is judged (459.C; operator ruling 2026-10-14).** A shape
in `refreshDeclinedShapes` has its edit and append ticks held to what a declined refresh is:
`tick <= diff + reference evaluation + floor`. The reference evaluation is `DataFrame.evalPreparedCounted`,
the kernel set the declined refresh itself runs on, over a fresh table object as the tick receives one,
and the diff is the same `Delta.diff`; both are timed beside the tick in one interleaved window
(`ScalingTests.pairedBatchedMs`: one call count for both, the order alternating, the best of 25 rounds
of samples of at least 2 ms each), so a load or a collection that reaches one reaches the other. That is
Phase 371's like-with-like rule applied to what a declined tick actually replaces. A declined tick cannot
be cheaper than recomputing, so `1.6 x one-thread full` was never its claim; what it can be is dearer
than the fall-back it reports, and that excess is the refresh's preamble, which the per-call floor
bounds. Every other corpus shape keeps D19's `max(1.6 x full, 1.5 x baseline) + floor`. `tickBound`,
`tickBaselineFactor` and `tickFloorFactor` did not move, no x64 baseline table was recorded, and
`refreshDeclinedShapes` stays the one list the edit tick case, the append tick case and D27's refresh
case read, still pinned against `Incremental.plan` by the main suite. The Arm64 table's rows for the two
declined shapes are now unread and are kept: the table is the recording script's verbatim output, and a
shape that stops being declined returns to the baseline arm with them.

Why 25 short samples rather than `batchedMs`'s five of 20 ms: at 100,000 rows a pivot call is about
15 ms, so a 20 ms sample is two calls, and the best of five of each arm still moved by up to 3% between
runs, three times the floor. One call a sample and the best of 25 read within the floor on the clock leg.

**Held.** On the fixed tree, three Arm64 clock legs (Release, `dotnet <dll>`) were green, 17 of 17 cases.
On the two at calibration 6.20 ms every declined cell read x0.93 to x1.04 of its diff and reference
evaluation; on the third, at 7.67 ms (the machine was loaded by other work), the `pivot` edit cells read
x1.09 to x1.17 on two attempts and x1.00 on the third, green under D22's same-cell rule. **Go-red:**
with the structural pipeline comparison put back, the `inner join` cells at 20,000 and 100,000 rows read
x1.24 to x1.33 and both tick cases were red on all three counted attempts, the same cells each time, while
the accepted shapes' cells kept their verdicts; under the old condition those cells had passed at 1.6
times the full evaluation. A redundant boundary walk, the perturbation the phase proposed, does not
redden it, because it costs nothing measurable since Phase 423, which is the finding rather than a gap in
the condition.

**Release record.** `0.39.0` was published (the publish run's second attempt, verified on nuget.org's
flat-container index), so its `STABILITY.md` heading stands as released and is not retitled; this
phase's entry rides the `0.40.0` draft. No public signature, answer or wire byte moves (class `none`,
`performance`).

## 2026-10-15 — D32: the relational verbs' output schema and refusals are modelled in their own module over `Pipeline.fst`'s frame, the twelve-verb parameter is narrowed to the rows, the static walk's soundness is a theorem; and two findings are recorded with their witnesses, not fixed — four verbs answer a repeated name, and the walk keeps a union's left type

**Context.** `proofs/Pipeline.fst` models the expression evaluator, `Filter`, `Derive`, the dispatch and
the counted driver, and takes the other twelve verbs as a parameter typed only to answer a well-formed
frame or a named error. Phase 411 (a join emitting two columns of one name) and Phase 412 (a sort key
naming no column dropped in silence) both sat in that assumption; a model of each verb's output schema
and refusals would have stated both, and Phase 434 asked for it, leaving open whether the model extends
`Pipeline.fst` or stands beside it.

**Decision.**

1. **A module of its own, `proofs/Relational.fst`, opening `Pipeline`.** `Pipeline.fst` verifies in
   about a minute cold and its budget is 530s; the relational model is sixteen hundred lines of
   definitions and lemmas and verifies in about a minute of its own. Folding it in would double the
   module every later phase re-verifies and put the two models' costs under one number. The new module
   reuses `Pipeline`'s types and helpers by name (`schema`, `frame`, the verbs, `eval_error`, `assoc`,
   `index_of`, `names`, `app`, `column_type_by`, `derived_typing_of`) and re-models nothing; it restates
   `List.tryItem` and `List.contains` only because `Pipeline`'s are `noextract`. `Incremental.fst` (D31)
   is the precedent for a model opening `Pipeline` rather than growing it.
2. **The narrowing is a construction, not an edit.** `Pipeline.fst`'s `other_fn` stays as it is;
   `Relational.other_of` builds one from `step_schema` — each verb's schema and schema-level refusals,
   clause for clause from `evalStepWith`'s arms — and a ROW FUNCTION, which answers the rows under the
   schema the model computed, as wide as it, or a refusal that read a cell (`AggError`). Every theorem
   of `Pipeline.fst` holds for `other_of` because it holds for every `other_fn`; what the ladder's
   twelve-verb row now assumes is the row function and nothing about a schema. The differential runs the
   pipeline comparison with `other_of` over production's rows as the parameter, so the narrowing is
   measured over four hundred generated pipelines, not only stated.
3. **Row content is read exactly where a schema reads it, and the header says where.** The pivot's
   distinct present `on` values name its columns; the unpivot's melted cells type `value` where the
   declared types leave it to them (Phase 338). Nothing else reads a cell. The parameters are a record of
   four host functions (the canonical cell text, an int's text, the string order, the join's suffix rule)
   and the resolver; each is listed with what the theorems assume of it, and only two assumptions are
   load-bearing: the suffix rule is injective in its index (`cand_injective`, a string fact the prover
   has no axiom for, on which `join_no_dups` rests and which the 411 go-red measures by crossing the
   pre-D20 rule), and the walk's declared sources are the resolver's schemas (`sources_agree`, the
   contract a caller of `SchemaWalk.ofPipelineFrom` with `ofMap` takes on).
4. **The walk's soundness is the agreement theorem, stated for open knowledge too.** `walk_sound`:
   over knowledge sound for the input's schema, when a verb answers `Ok`, `SchemaWalk.ofTransform`'s
   answer is sound for the output — a closed knowledge has the output's names in order and every type it
   knows is the output's; an open one names columns the output carries with the types it knows. The
   pivot's open answer is the index columns the evaluator's output carries first; a join over an
   undeclared `Ref` keeps the left columns and the right columns that keep their own names. The union's
   NAMES agree; its types are finding (b) below. A join's open-right case needs the left's names
   distinct: with a name repeated, the walk's second column of that name claims a type a by-name read
   never finds — which is finding (a) seen from the walk's side.
5. **Two findings, recorded with witnesses and not fixed here.** The acceptance allows either; these are
   filed, because each is a behaviour change to a public evaluator or walk of D21's, D26's and D28's
   class, which wants its own decision and `STABILITY.md` entry, and the files that carry the change are
   outside this phase's declared key files.
   - **(a) Four verbs answer `Ok` with a repeated output name.** `Project [a, x; a, x]` answers `x`
     twice; `GroupBy [k] [Sum v as k]` answers `k` twice; `Unpivot [variable] [v]` answers `variable`
     twice; a `Pivot` whose index column is named as an `on` value's canonical text answers that name
     twice. D20 closed this for a join (the suffix) and D28 for a window (the refusal); the four were
     not considered then. The model states each as a theorem (`project_repeats_name`, …) and the
     differential asserts each on the shipped evaluator, so a fix moves both. **The ruling this waits on**
     is D28's reasoning applied: in `Project`, `GroupBy` and `Unpivot` the repeated name is the author's
     own choice written into the pipeline, so D28's refusal — a `TypeError` naming the column, before any
     row is read — is the candidate; in `Pivot` the colliding name is the data's, like a join operand's,
     so D20's suffix or a refusal are both candidates and the choice is an operator's.
   - **(b) `SchemaWalk.ofTransform` keeps the left type for a `Union` where the evaluator widens it.**
     Since Phase 423 (D18) a union's column takes the join of the two sides' types — `Int` beside `Float`
     is a `Float` column — and the walk's `Union` arm is `input`. Over `Union` of an `Int` column with a
     `Float` one the walk says `Int` and the evaluator answers `Float`; a planner, or the registry's
     agreement check, reading the walk reads a type the evaluator does not produce. The model states it
     (`union_walk_type_drifts`), the walk comparison in the differential holds production's walk to the
     model's over the shape (`423-union-widens`), and the schema comparison records the evaluator's
     answer beside. The fix is the walk's `Union` arm reading the source as `Join` does and joining the
     types where both are known; it is one arm and one `schemaWalkLaws` law, and it is not this phase's.
6. **The census moves.** The twelve `Transform.<Verb>` entries D30 wrote leave
   `coverage-exclusions.json` for the ladder's rows, with `SchemaWalk.ofTransform`, `SchemaWalk.ofSource`
   and `DataFrame.joinRightNames`; the `SchemaWalk` entry's reason names what moved.

**Consequences.** No production source changed: the model found the two findings above and no
disagreement with the evaluator's schema over the generated sample or the named shapes. The proof leg
gains a module (`Relational`, budgeted in `modules.json` from this phase's runs) and the oracle project
its extraction. Re-introducing 411's defect in the model (a suffix free against the left names only)
leaves `join_no_dups` unprovable (`rename_right_no_dups`'s hypothesis that every right name is taken no longer holds) and the leg red — measured by one perturbation run; re-introducing 412's (an unknown sort key
dropped) makes the `Sort` arm answer `Ok` and `sort_unknown_refused` fails. A later phase that closes
finding (a) changes the four `*_repeats_name` theorems into refusal theorems and deletes the
`finding-*` shapes from the differential in the same commit; one that closes (b) removes
`union_walk_type_drifts` and extends `walk_sound` to the `Union` arm.

## 2026-10-15 — D31: the incremental refresh is modelled and proved for the row-local class — `Filter` and `Derive` — over `Pipeline.fst`'s frame; every other verb class is declared out, by name and with its reason, and the model's one premise is the delta's truthfulness

**Context.** `Incremental.fs` is about seven thousand lines and `Delta.fs` two thousand: a refresh walks
the pipeline step by step over a frame whose per-step cells it keeps (`StepCells`, aligned with the
source's row tokens), reads a cached cell for every row the delta does not name and evaluates the rest,
maintains a `GroupBy`'s groups, merges a `Sort`'s order, slices a `Limit`, resumes a `Window`'s run and
re-checks a filtering join's key index. Its agreement with full evaluation was sampled
(`Conformance.incrementalLaws`, the refresh-cost tests) and stated by no model (D30's census said so,
operation by operation). Phase 433 asked for the theorem, allowing a split by verb class provided the
split is recorded rather than implied.

**Decision.**

1. **The covered class is the pipeline every step of which is a `Filter` or a `Derive`** — the two verbs
   `Incremental.classifyStep` reads as `PropagateRows` AND `Pipeline.fst` models as a clause. The model
   (`proofs/Incremental.fst`) is the walk at the level production's comments state it: one cell cache per
   evaluating step keyed by the row's token, a step evaluated row by row in frame order with a reused row's
   cell read from the prior cache and every other row's evaluated by `Pipeline.eval_expr`, the first error
   winning; a `Filter` keeping the rows whose cell is `Bool true`; a `Derive` replacing or appending its
   column, typed over the rows alive at the step by `derived_typing_in` and `column_type_by` — the
   reference's own rule, re-run on every refresh, exactly as `walk`'s `WDerive` arm re-runs it
   (`Incremental.fs`, "Reading it off a cache would type the column differently from the reference the
   moment a filter downstream dropped the only typed row"). `prime` is the walk reusing nothing;
   `refresh` is the walk reusing every unnamed row against the primed caches. Three theorems:
   `prime_is_full` (priming is `Pipeline.eval_uncounted`, frame for frame and error for error),
   `refresh_is_prime` (the state theorem: output AND caches), `refresh_agrees` (the agreement theorem).
   The lemma that carries them is `eval_expr_names`: the evaluator reads a schema's names and never its
   types, so a cell cached under one column typing is the cell under another — which is what makes a
   per-row cache sound under a column typing that is a function of the whole column.
2. **The one premise beyond `Pipeline.fst`'s is the delta's truthfulness**, as the walk reads it:
   every row the delta does not name is a row of the prior source, under its token, with the cells it had
   (`unchanged`). It is the ladder's `incremental-delta-truthful` row, and it is production's own
   contract ("The delta must truthfully describe the change (`Delta.diff` produces exactly that)"). The
   theorems are stated for a new source that renames no column; a source whose schema moved is
   `SourceSchemaMoved` in production and evaluates in full, outside the walk.
3. **Declared out, each a follow-up of its own, each still a `measured-elsewhere` entry in the census:**
   - `Project` — row-local in production, but a parameter (`other_fn`) in `Pipeline.fst`; covering it
     needs a concrete `Project` clause there and a bridge row saying the parameter agrees with it;
   - the maintained `GroupBy` — the group table, the per-group member order and aggregate caches
     (`GroupMembers`, `GroupAggs`, `GroupOrder`, `TailCells`), and the rule that a touched group is
     recomputed over its members in order while an untouched one is reused; `GroupBy` is a parameter
     in `Pipeline.fst`, so the model needs the verb's own clause first;
   - top-N — `Sort` (`MergeOrder`: the arrival and produced orders, a merge of the named rows into the
     produced order, valid only while the unnamed rows' arrival order stands) and `Limit`
     (`TruncateOrder`: a slice over the maintained order); both parameters in `Pipeline.fst`;
   - `Window` (`RecomputeFrame`, the resumed prefix-fold runs of Phase 333) and the filtering joins
     (`FilterByRelation`, the cached key index of Phase 120);
   - the staleness guards (`PipelineChanged`, `EnvChanged`, `SourceSchemaMoved`, `DeltaIsFullRefresh`,
     `OrdinalAddressing`, `RowIdentityUnusable`), the quiet-delta shortcut (`ReusedPrior`), the in-place
     and append fast paths (Phases 323, 359 — the same caches reached from the prior arrays), the chunked
     path (Phase 268) and the state's wire form (`IncrementalCodec`).
   Row-local first because it is the class the other classes' walks run THROUGH — every admitted step
   after a `GroupBy` or a `Sort` is this same restricted walk one frame along — so its theorem is the
   base the others extend, and because it is the class `Pipeline.fst` already models concretely: nothing
   had to be assumed of a verb to state it.

**What the model is not.** It is not `Incremental.fs` clause for clause: the slot arrays, the `Order`
array, the sparse reads, the `WalkFrame`'s origins and tails are representation, and the model carries
the relation they implement — a row's cell at a step, found under its token. The differential
(`Proofs.Oracle`, the incremental cases) is what ties the two: production's `prime` and `refresh` beside
the extracted `prime` and `refresh` over generated row-local pipelines, priors and deltas built by
`Delta.diff`, with the delta's named rows read off the delta production was handed.

**Consequences.** `Incremental.prime` and `Incremental.refresh` move from the census to the ladder's
proved rows and `Incremental.result` to its tested row; the rest of the `Incremental` entry stands, its
reason now naming the row-local theorem and the classes still outside it. No production source changed:
the model found no defect. A later phase that covers a class above extends `Incremental.fst` (a new
`wstep` case, or a clause in `Pipeline.fst` first) and deletes that class's census members in the same
commit, as D30 requires.

## 2026-10-15 — D30: the operation census is ported from Fuaran.Core, and the two repositories have one coverage vocabulary; the census is the modelled packages' operations and the relational verbs

**Context.** Phase 259 carried only the package half of Fuaran.Core's coverage file into this
repository: every packable package is modelled or excluded. A modelled package therefore read as
covered whole, although `proofs/Pipeline.fst` takes the twelve verbs that evaluate no expression as a
parameter and nothing models the delta algebra, the incremental engine or the planner's rules. Those
gaps were recorded nowhere a check reads. Fuaran.Core closed the same gap for itself with an operation
census (its Phase 335): every public operation maps to a ladder row, a law family's roster or a recorded
exclusion with a class from a closed vocabulary.

**Decision.** Port that census (Phase 432), keeping Core's vocabulary and check shape.

1. **One vocabulary.** `proofs/coverage-exclusions.json` carries Core's `operationClasses` verbatim
   (`trivial`, `forward`, `obsolete`, `host-seam`, `measured-elsewhere`), with the same entry fields
   (`members`, `class`, `reason`, `forwardsTo`, `standIn`, `through`) and the same both-ways check:
   an unmapped operation fails, and so does an entry for an operation that is no longer published or
   that a ladder row now names. A term means the same thing in either repository. A change to the
   vocabulary is made in Core first and re-copied here. It is never edited here alone.
2. **The census.** It covers the packages a model in `proofs/modules.json` names. Their `method` lines
   in `api/*.txt` are censused, plus the `union-case` lines of `Transform`, spelled `Transform.<Verb>`.
   The verbs are this layer's operations even though the compiler publishes them as union cases, and a
   census of methods alone would never name them. A package with no model is not censused. Its
   package-level exclusion already records the decision for the whole package.
3. **The model route is the ladder.** A modelled operation is named in a `proved` or `tested` row's
   `evidence.operations` in `proofs.json`, as in Core. This repository ships no law-family operation
   roster, so Core's roster route is omitted from the check rather than carried empty. The law
   families that sample an operation are named in its entry's reason.
4. **The planner is not on the ladder.** `Pipeline.fst` proves the premise of the derive-filter
   reorder and the expression half of the totality verdict. It does not prove `Plan.rewrite`'s other
   rules or `Plan.isTotal` over the twelve unmodelled verbs. `Plan.rewrite`, `Plan.isTotal` and
   `Plan.explain` are therefore `measured-elsewhere`, and the entry says which half is proved.
5. **What the census says today** is generated into `proofs/README.md` ("What is not proved") by the
   `Proofs.Coverage` family (`CORE_APPROVE_LADDER=1` rewrites it). No count is written by hand. The
   phases that add models delete their entries in the commit that adds the ladder row.

**A premise that proved false.** The phase asked for entries for the public members of `Frame` and
`Kernels`. Neither has any: every module and type in `Frame.fs` and `Kernels.fs` is `internal`, and the
baseline publishes nothing from them. They reach the census only through the public operations that
call them, such as `DataFrame.prepare`, `DataFrame.evalPrepared` and `ColumnOps.applyPrepared`, whose
entries say that no model describes the dense representation.

**Consequences.** A newly published operation of a modelled package, or a new verb, reds the suite at
the commit that adds it and names itself. The code moves from `ProofsLadderTests.fs` to
`ProofCoverageTests.fs` together with the package clause, so `Proofs.Coverage` has one home, as in Core.

## 2026-10-14 — D29: the evaluator propagates and checks units — a column's unit rides every verb that keeps the column, addition and comparison require one unit, multiplication and division compose through Core's algebra, a conversion is explicit and answers a float, and a column that states no unit is unconstrained, so a unitless pipeline is untouched

**Context.** Since Phase 427 a schema entry is a `Field` that can carry a unit of measure (Core's
`Fuaran.Core.Unit`, Phase 426: a product of UCUM atoms with integer exponents, one canonical text per
product, `compatible` and `conversionFactor` reading what a unit denotes). The evaluator read none of
it: `Add`, `Sub`, `Mul` and `Div` typed their operands by cell type alone, so a model adding metres to
seconds evaluated, and every verb that produced a column (`Derive`, an aggregate, a window) made a fresh
field with no metadata — `Frame.withColumn` says so in its doc. The unit was a label.

**Decision.** The evaluator carries each column's metadata from input to output and refuses, before any
row is read, the operations a unit makes meaningless. The rules, each held by `UnitPropagationTests`
(one test per row, the refusal asserted by name) and by the law families named beside them:

| Where | Rule |
|---|---|
| A column | Its unit is its field's (`Field.Unit`). A column that states no unit is UNCONSTRAINED: it composes with anything and never refuses, so a pipeline over unitless columns answers byte-identically to `0.39.0` (held by every pre-429 vector in `conformance/laws/transform-laws.json` being byte-unchanged, and proved: `unitless_unchanged`, `checked_step_unitless`). |
| A literal, a param, a `now` | Unconstrained. A literal is given a unit by `Convert(Lit 5, m)`. |
| `Add`, `Sub` | The operands' stated units must be ONE unit — one canonical text; `km` beside `m` is refused as `N` beside `kg.m/s2` is, until converted, because implicit conversion hides exactly the error this exists to catch. The answer carries that unit; an unconstrained operand takes the other's. |
| `Eq` … `Ge` | The same rule; the answer is a boolean and carries none. `And`, `Or`, `Contains`, `StartsWith`, `EndsWith`, `Not`, `IsNull`: none. |
| `Mul` | Composes through `Unit.mul`; an unconstrained factor contributes nothing (`m * 2` is `m`, `m * m` is `m2`). |
| `Div`, `Quotient` | Composes through `Unit.div`; an unconstrained divisor leaves the dividend's unit, an unconstrained dividend is dimensionless over a stated divisor (`1 / s` is `/s`). `Mod` keeps the dividend's. |
| `Coalesce`, a `Case`'s arms, `Least`, `Greatest`, `InList` | One unit among the stated ones, in order, and the answer carries it (`InList` answers a boolean). A `Case` agrees its arms with its ELSE, the else first — it is the base the arms fall through to — so a mismatch names the else's unit first. |
| `Cast`, `Abs`, `Round`, `Floor`, `Ceil`, `Rounded` | Keep the operand's unit. |
| `Sqrt`, the text and date functions, `Length`, `IndexOf`, `DateDiffDays` | None. The algebra states integer powers only, so a root's unit is not derived; a text and a day count measure nothing. |
| `Convert(expr, unit)` | The one explicit conversion. Over an unconstrained operand it is a TAG; over an operand of the same unit the identity; over an operand of a compatible unit the exact ratio `Unit.conversionFactor` gives; across dimensions it is REFUSED. It is resolved by SUBSTITUTION against the step's schema before evaluation, as `InParam` and `Now` are: a tag or an identity becomes `Cast(FloatType, operand)`, a conversion `Cast(FloatType, operand) * Lit(Float factor)` with the ratio as a double — so a conversion always answers a FLOAT (a measurement), a null operand propagates, and a decimal operand is cast as `Cast` casts it; a caller that needs the exact product writes `Quotient` with the factor. The evaluator proper never meets a `Convert` (`resolved_convert_free`); `resolveExpr` resolves a stray one the same way. |
| `Sum`, `Mean`, `Min`, `Max`, `Median`, `First`, `Last`, `StdDev` | Keep the source column's unit. `Count`, `CountDistinct`: none. (`DataFrame.aggregateUnit`.) |
| `Lag`, `Lead`, `CumulSum`, `CumulMax`, `CumulMin`, `RollingSum`, `RollingMean` | Keep the source's. `RowNumber`, the ranks, `NTile`: none. (`DataFrame.windowUnit`.) |
| `Join` | A key pair is an equality, so the comparison's rule holds it: two stated units must be one unit (`join key 'a' = 'b'`), an unconstrained side matches anything. Non-key columns keep their own fields, the right's suffixed as D20 names them. |
| `Union`, `Intersect`, `Except` | A column pair is one column: two stated units are refused (`union column 'x'`), the left's field is kept, and a left that states none takes the right's stated unit (a union). |
| `Project`, `Filter`, `Sort`, `Limit`, `Distinct`, `Window` | Keep the input's fields — unit, label, description and extension metadata — a projection under its new name. |
| `Derive`, an aggregate column, a window column | Carry the unit the rule above derives and NO label or description: the step has no syntax to state one, and a derive onto an existing name is a fresh column (D-less until a step can name a label). |
| `Pivot` | The index columns keep their fields; every value column is one aggregate of `Values`, so each carries that aggregate's unit (`Sum` of kilograms is kilograms, a `Count` none). |
| `Unpivot` | The id columns keep their fields; `variable` carries none; `value` carries the one unit every value column states, and none where they differ or one states none — an unpivot melts measures of different kinds into one column by design, and a unit it cannot state it does not invent (`DataFrame.unpivotUnit`). |

1. **The refusal is typed and static.** `EvalError.UnitMismatch(step, expr, left, right)` names the step
   (`derive 'speed'`, `filter`, `join key 'd' = 'k'`, `union column 'd'`), the expression the two units met
   in — the node as written — and both units by canonical text. It is decided over the step's schema
   before any row, so an empty frame is refused exactly as a full one (the derive-typing family's built
   refusal holds it on a full and an empty frame alike). `errorString` renders it as
   `unit mismatch in derive 'x': m against s in add('d', 't')`.
2. **One check, read by everything.** `DataFrame.resolveUnits step cols expr` is the walk: the expression's
   unit (`None` for unconstrained) and the expression with every `Convert` resolved. `evalFilter` and
   `evalDerive` run it before their first row, as do the incremental seam's walk and the worker pool's
   hand-off (which falls back to the sequential path for a step the check refuses); the planner's verdict
   (`isTotal`) declines a step the check refuses, and its `Known` schema carries fields so a pruned or
   reordered pipeline's output schema is the reference's (`plannerLaws`: the planned schema is the
   reference's, metadata included); `SchemaWalk` states the unit, label, description and extension metadata
   of every output column (`ColumnKnowledge` gains the four members, built through the `ColumnKnowledge`
   module so the next member is additive; `schemaWalkLaws` holds the walk's metadata to the evaluator's); a
   registered pipeline query's result knowledge carries them, and registration refuses a declared field
   whose unit differs from the produced one (`ResultDisagreement.UnitDiffers`, both directions — the
   declaration is the contract); the incremental state wire and the registry wire carry a field's
   metadata through Core's own field document (`ColumnCodec.fieldJson`), the state wire keeping the
   `[name, type]` pair for a field that states none so a unitless state is the bytes it was.
3. **Proved, with the algebra as a parameter** (`proofs/Pipeline.fst` section 13; the rows
   `pipeline-unit-check-*` in `proofs.json`): an accepted expression evaluates convert-free; over a schema
   stating no unit a convert-free expression is accepted with no unit and UNCHANGED, step by step; every
   refusal names two different units or a conversion the algebra has no factor for; and each rule is the
   unfolding of the check. That the algebra is Core's — canonical in its texts, exact in its factors — is a
   named premise (`pipeline-unit-algebra-is-core`), Core's `proofs/Unit.fst`'s to prove and the unit
   differential's to instantiate (`ProofOracleTests`: the extracted check at Core's algebra beside
   production's, over the unit vectors and six hundred generated expressions).

**Considered and rejected.**

- **Implicit conversion of a compatible scale** (`km + m` scaling the kilometres). Rejected: a silent
  scale is the error class — pence read as pounds — and the explicit `Convert` costs one node.
- **A literal typed DIMENSIONLESS rather than unconstrained.** Rejected: `price + 1` over a priced column
  would refuse, and every unitless pipeline would refuse at its first literal beside a stated unit;
  unconstrained composes exactly as the unitless world did, and a literal that means a measurement says
  so with `Convert`.
- **A conversion that keeps the operand's type** (an int column in metres converted to kilometres as an
  int). Rejected: `1500 m` is `1.5 km`; a measurement under a factor is a float, and the typer's rule for
  a conversion is then one line (`Typing.cast FloatType`), with no second answer for an identity.
- **A `Sqrt` that halves exponents.** Rejected for now: the algebra states integer powers only, and a root
  of `m` has none; an unconstrained answer invents nothing, and a consumer that knows the root is in
  metres writes `Convert(Sqrt(x), m)` — a tag.
- **Refusing a join key of two DIFFERENT dimensions only** (the shard's wording). Rejected in favour of
  the comparison's rule: a key match is `Eq`, and `1 m = 1 km` is as wrong as `1 m = 1 s`.
- **Carrying the hand-off's schema as a `Field list`.** Rejected by the node leg: a hand-off crosses a
  worker boundary by structured clone, which cannot clone a map's comparer; it crosses as the canonical
  field documents, a string, as the step and the environment already do.

**Consequences.** `ColExpr` gains `Convert` and `EvalError` gains `UnitMismatch` (a consumer matching
either exhaustively adds an arm); `ColumnKnowledge` gains `Unit`, `Label`, `Description` and `Ext` (a
full literal gains four members — construct through `ColumnKnowledge.named` / `ofField`); the wire gains
`{"$type":"convert","expr":…,"unit":"<text>"}`; the planner's `Known` and the schema walk carry fields.
`0.39.0` is tagged, so the slot advances to `0.40.0` (STABILITY.md). What a consumer gains is the check:
a pipeline that adds metres to seconds fails where it is written, with the step, the expression and both
units named, and a sum over kilograms is a sum in kilograms on every host that reads the vectors.

## 2026-10-14 — D28: a window output that names a column the input already carries is refused with a `TypeError` naming it, through the window spec's one check, before any row is read; the join's suffix rule (D20) was considered and rejected for a window

**Context.** A `Window` appends its output column under the name its `As` gives. When that name was a
column the input already carried, the result held two columns of one name (Phase 450). D25 measured
what that costs: the evaluator reads a table's columns by name, so the result handed back through the
`Table` door reads its second column as the first, and Core's `Table.validate` refuses such a table
outright. D20 closed the join's way of producing a repeated name and left the window's as it stood.
This was pinned before the change: a `RowNumber` window over `people` whose `As` is `salary` answered
`Ok`, with `salary` twice in its schema.

**Decision.** The window is refused, with `TypeError("window output 'x' names a column the input
already carries; …")`.

1. **One check.** `windowRefusal`, the window spec's single check (D26), gains a last clause: after the
   bucket count, the read column, the partition keys and the order keys, an output name the schema
   carries is refused. An unknown key is still named first, because a spec whose keys do not resolve
   is wrong whatever its output is called.
2. **Before any row is read, in every form.** The frame form, the public row form (`windowStep`) and
   the incremental seam all ask `windowRefusal`, so all three refuse, over a source with no rows as over
   one with rows. A refresh whose source has gained the output's name refuses as the full evaluation of
   that source does.
3. **No new `EvalError` case.** `TypeError` is what the window already answers for a spec it cannot
   evaluate (a bucket count below one). A new case would break every exhaustive match over `EvalError`
   and the proof model's error type for a refusal a host reports by its text, as D25 reasoned for its
   own refusal.
4. **The planner's totality verdict already agrees.** `Plan.isTotal` has never counted a `Window` as
   total, and the pruning walk treats a window as a barrier, so no rewrite removes a column ahead of a
   window and the collision with it. The planned pipeline refuses as the pipeline as written does, and a
   test holds that over the shape a pruning would reach (`[ Window …; Project … ]`).
5. **The schema walk still appends.** `SchemaWalk` carries no refusal verdict, as for an unknown key
   (D26), so over a colliding window it names the column twice. That answer is about a pipeline the
   evaluator refuses, and the repeated name marks it. Retyping the column in place, as a `Derive` does,
   would describe an evaluation that answers `Ok`.

**Why a refusal and not D20's suffix.** The two verbs differ in who chose the name. A join's right
column names come from its operand, so a collision is a fact about two tables the author may not
control, and D20 keeps the join total rather than make it fail when an operand gains a column. A
window's `As` is the author's own choice, written into the pipeline, and the steps after it read the
column by that name. Under a suffix, `As = "rank"` over a table already carrying `rank` would put the
window's column under `rank_2`, and a later `Col "rank"`, `Sort` or `Project` would read the table's
`rank`: a plausible wrong answer with no error anywhere, which is the defect this phase removes. Under
the refusal, the author learns at once, by name, and renames the output or projects the old column
away. The cost is D20's own worry: a pipeline that ran over a table refuses over the same table once
it gains a column of the output's name. That is the honest answer there, because the pipeline's later
reads of that name have become ambiguous. **Replacing the column**, as a `Derive` does with a name it
already carries, was the third candidate and was not taken: a window reads its input's columns (its
`Of`, its keys) to make its output, so replacing one of them would let a single step's output erase its
own input under one name, a reading no SQL window offers and one every consumer would have to learn.

**The split law and the refresh.** The source seam's split law (`The source seam (Phase 415)`) now
holds over the colliding shape as a refusal: the whole pipeline refuses, and so does every split of it,
whether the window falls in the prefix or the remainder. The incremental refresh over a source that
gained the window's output name refuses as the full evaluation does.

**Consequences.** A behaviour change to a public evaluator, of D21's and D26's class. No public
signature moves, because the function that carries the rule is internal. It is versioned under the
`0.39.0` draft (`STABILITY.md`), which it rides. No vector in `conformance/laws/transform-laws.json`
names a window's output after an existing column, so none moves, and the corpus copy follows the release
rule (D6 (4)) unchanged. Two suite cases that used such a name only as a convenient output name now use
a fresh one.

## 2026-10-14 — D27: a refresh assembles its result from the prior one; no corpus shape is declined; the named tick exceptions are retired; a clock case holds the refresh against the full evaluation

**Context.** Phase 423 made the full evaluation a few passes over typed vectors. Phase 439 then found
that on three corpus shapes the refresh ALONE cost more than the full evaluation it replaces:
`lines`, `window CumulSum` and `group-by high-card`, 2.1 to 3.4 times (0.6 to 0.9 before 423). Phase
439's bound stayed green on the baseline arm, and D19 and D22 held three cells to their own post-423
cost as named exceptions until Phase 448: `lines` edit, `lines` append and `window CumulSum` edit, all
at 20,000 rows. The operator's rulings of 2026-10-12/13 bind this phase: the tick bound is
`max(1.6 x full, 1.5 x recorded pre-423 tick) + floor`, and a shape the seam declines (a visible
fall-back to the full evaluation, with the reason) is a legitimate answer where it is better for a
consumer.

**Measured first (448.A).** The refresh was timed apart from the diff and split by stage (Release,
Arm64, the 439 tree, 20,000 rows, the corpus's one-row edit). The refreshed span was not the cost, and
neither was re-keying (the delta carries the diff's keys). Result assembly was: 82% of the `lines`
refresh (both derived columns packed from every row's cell), 73% of the `group-by high-card` refresh
(the 10,000-row group table packed from its row arrays) and 41% of the `window CumulSum` refresh (the
window column built as a 20,000-cell list and then packed). The rest of the window's was its resume's
row-sized bookkeeping arrays.

**Decision, per shape (448.B).** No shape is declined. On each of the three, maintaining the result
is cheaper than recomputing it once assembly stops going through cells, and declining would make every
refresh a full evaluation and lose the edits and appends that are already far cheaper.

1. **`lines` (row-local steps): the prior result's column, written at the rows that moved.** When
   every step is row-local and the refresh reads in place, every other row of the result is the prior
   result's row. A computed column is then the prior column's typed vector, copied and written at the
   changed and appended rows (`patchedColumn`).
2. **`group-by high-card` (a maintained group-by): the prior group table, written at the recomputed
   groups** and grown by the new ones, through the same function. The group outcome's rows are an
   array now, so they are not converted between a list and an array on every refresh.
3. **`window CumulSum` (a resumed prefix fold): the column read straight off the typed carrier the
   scan wrote** (`runColumn`). The run's cell list, which the state's encoding carries, is deferred:
   it is read off the column only when the state is encoded, so no wire byte moves. The resume also
   writes its scanned suffix in one loop per carrier, and builds no position table where the run
   already holds the rows in arrival order.

Each copy answers what packing answers: the same carrier, values, absent rows and the zero values
under them. Where that cannot be shown (a carrier the copy does not handle, a written cell the carrier
does not hold, a field whose type moved), the refresh packs the cells as before. Making either new
path write one row wrongly reddens the incremental and window law families. A filter that keeps every
row now hands its order on rather than copying it.

**The named exceptions are retired (448.D).** `ScalingTests.tickBaselineExceptions`, the ceilings
beside it, the condition that read them and the main-suite case that pinned them are deleted. So is
the `-Ceiling` mode of `gates/record-tick-baselines.ps1`, which wrote nothing else. Every corpus cell is
judged by the two arms alike. On three clean Arm64 clock legs the three formerly excepted cells read
0.49–0.50 ms (`lines` edit), 0.43–0.44 ms (`lines` append) and 0.61–0.62 ms (`window CumulSum` edit),
each within 1.6 times its full evaluation. The `lines` edit cell had read 1.39 to 5.27 ms.

**Held (448.C).** A seventeenth clock case times the refresh alone, handed the delta the diff built,
against the one-thread full evaluation on every corpus node the seam accepts: `refresh <= full +
floor`. The two nodes the seam declines, `inner join` (a combining join's rows are not its left rows)
and `pivot` (its output depends on rows the delta does not name), are printed with the plan's reason
and not timed. A main-suite case pins that list against `Incremental.plan` and checks that a declined
node's refresh reports a full evaluation with that reason, so a shape cannot come to be declined, or
stop being declined, silently. Figures: `docs/performance.md`, "A refresh assembles its result from the
prior one".

**Where the case does not time, and why.** The refresh case times the edit at 1,000, 20,000 and
100,000 rows and the append at 1,000 and 100,000 (`ScalingTests.refreshSizes`). At 20,000 rows the
append's refresh on the small-group grouped nodes (`byRegion`, `filter > groupBy`, `filter > groupBy >
filter`) read 0.8 to 2.5 times a full evaluation that costs 0.24 to 0.45 ms. That was measured, not
assumed: an in-place append grows two per-row reference arrays (the row tokens and a step's cell
cache), about 160 KB each at that size, just past the runtime's large-object threshold, and the
collections those allocations trigger decide the reading. With the threshold raised past them the same
refresh read 0.43 times the full with no gen-2 collection, and the same cell read 1.05 times from a
pool of 500 prepared sources and 1.3 to 2.4 times from one, with the gen-2 count moving from 0 to 17
per 500 calls. The diff and refresh together stay inside the append tick case's bound on every
reading, and the edit and every other size of every shape read 0.13 to 0.98 of the full. It was so
before this phase too (1.0 to 2.0). Keeping those arrays below the threshold is a representation change
to the seam's per-row caches, not result assembly, and it is a later phase's. The case's reach was
proposed to the operator with this evidence; the ruling is recorded with the phase.

**Consequences.** No public signature, answer or wire byte moves (`STABILITY.md`, class `none`,
`performance`), and the `0.39.0` draft is ridden. The clock leg holds seventeen cases. Re-recording the
baseline table is unchanged (D19).

## 2026-10-13 — D26: a partition key that names no column is refused with `UnknownColumn`, through the one resolution a sort key passes; the window's keys are checked before any row is read

**Context.** D21 made a sort key that names no column a refusal, in a `Sort` and in a `Window`'s
ordering. It left a `Window`'s `PartitionBy` alone, because a partition key is not a sort key
(Phase 441). `windowPartitionIdx` still dropped a name the schema lacks, so a misspelled partition key
partitioned by nothing. The window then answered one partition's column as if it were several: a
plausible wrong result, `Ok`. This was pinned before the change: a `RowNumber` window over `[ "dpt" ]`
on a table carrying `dept` answered `Ok`.

**Decision.** A partition key that names no column is refused with `UnknownColumn(name, available)`,
the error every other verb answers.

1. **One resolution.** `unknownKey` is the one check for the keys a step takes from a pipeline to
   order or partition its rows by. `unknownSortKey` is now that check over a sort's columns, and
   `windowRefusal` is the window's whole spec check: a bucket count below one, then a read column the
   schema lacks, then the partition keys, then the order keys, refused at the first failure. Because
   the partition and order keys pass through the same function, the two rules cannot drift apart
   again, which is how the partition rule was left behind in D21.
2. **Partition keys before order keys.** Where both a partition key and an order key are unknown, the
   partition key is named. That is the spec's own order, and SQL's `PARTITION BY … ORDER BY`. The
   function's own checks still come first, as they did under D21.
3. **Before any row is read.** The frame form (`windowColumnOfWith` and `windowColumnOverWith`), the
   public row form (`windowStep`) and the incremental seam all ask `windowRefusal`. The incremental
   walk asks it at the step's entry, ahead of resuming a prior run or ordering the rows, as its `Sort`
   step does under D21. A source with no rows refuses exactly as one with rows does.
4. **The planner's totality verdict already agrees.** `Plan.isTotal` has never counted a `Window` as
   total, so no rewrite reorders a filter across one or prunes a column behind one. An unknown key
   leaves that unchanged.

**What it does not change.** A `Window` whose `As` names an existing column still appends a second
column of that name (D20, D25). The exported comparator `rowCompareBy` still drops an unknown key, as
D21 says. The proof model treats `Window` as one of the verbs it does not model, so it holds no copy
of this rule.

**Consequences.** A behaviour change to a public evaluator, of the same class as D21's. No public
signature moves, because both new functions are internal. It is versioned under the `0.39.0` draft
(`STABILITY.md`), which it rides. No vector in `conformance/laws/transform-laws.json` partitions a
window on a column its table lacks, so none moves, and the corpus copy follows the release rule
(D6 (4)) unchanged.

## 2026-10-13 — D25: the evaluator's source seam is the prepared source, entered by position; `prepareRows` reads a schema and its rows, so a split pipeline hands its prefix through it rather than through a re-entered `Table`

**Context.** Every evaluator entry point took a whole table (Phase 415). A placement that splits a
pipeline — a prefix pushed down to a database as a statement, a cached cell, a checkpoint — had to build
a `Table` from the rows it held and feed it back in. The `Table` door reads columns by NAME:
`Frame.ofTable` looks each schema field up with `Table.tryColumn`, which answers the first column of that
name. So a result that names a column twice re-enters wrong. D20 closed the join's way of producing such
a result, but the evaluator still produces one itself: a `Window` whose `As` names an existing column
appends a second column of that name, which D20 left as it stands. Handed back through the `Table` door,
that second column reads the first one's cells.

This was measured, not assumed. The split law below was run once with the hand-off going through
`prepare (toTable result)` instead of through the seam. It failed on the generated sample (a `CumulMax`
window whose `As` names an existing int column, split after the window) and on the test written for the
shape. Through the seam, both hold.

**Decision.**

1. **The carrier is `Prepared`.** The evaluator's own working form is a schema and one column vector per
   field, in order: a result that carries its column identities by position, not by name. `Prepared`
   already had a way in from a table (`prepare`) and a way out of an evaluation (`evalToPrepared`, Phase
   342). Every evaluator over a prepared source already takes it, and so do `ColumnOps` and the
   incremental seam. What it lacked was a way in that is not a `Table`. A new carrier type would have been
   a second form of the same thing, with one user.
2. **The new door is `DataFrame.prepareRows schema rows`: a schema plus a row reader, read by position.**
   Cell `c` of every row is column `c` of the schema, so a schema that names a column twice keeps both,
   as the evaluator's own frame does. Rows, not columns, because the readers a host holds (a statement's
   reader, a cache file) yield rows. Each column is packed under its field's type exactly as the
   evaluator packs the rows a verb emits. So the seam checks no more of a cell's type than the `Table`
   door does: a cell its column's type cannot hold is carried the way that door carries one. A row whose
   width is not the schema's is refused with `TypeError`, naming the row. No new `EvalError` case was
   added, because that would make an additive door a breaking change for every exhaustive match. The way
   out is `toTable`, whose `Columns` follow its `Schema` by position, or the `Prepared` itself.
3. **The whole-table entry points stay, and were already adapters.** Each of them is `prepare` followed
   by the prepared-source evaluator, and has been since Phase 267. This was checked against the tree
   before anything was built, and nothing in them moves.
4. **How it relates to the incremental seam.** The incremental seam takes rows APPENDED to a source it
   already primed: a delta against a state it holds. This seam takes a whole source, by position. A
   source entered through it primes the incremental seam (`Incremental.primePrepared`) exactly as the
   table it came from does, and a test holds that.
5. **How it relates to a host that pushes a prefix down.** Such a host runs a pipeline's prefix as a
   statement, reads the statement's rows into `prepareRows`, and runs the remainder with
   `evalPrepared`. No `Table` is built between the two halves, so nothing between them is read by name.
   The host still receives the source and the pipeline together, and choosing where to split is still
   the host's decision. The evaluator offers a split that is safe at every step, not a place to make one.
6. **What it deliberately does not do.** It does no IO and nothing asynchronous: the reader is a
   sequence the host hands in, pulled once, synchronously, and the evaluator opens and closes nothing.
   It does not stream. The rows become columns before the remainder runs, because every step reads whole
   columns. A `Ref` that a `Join`, `Union`, `Intersect` or `Except` names still resolves to a `Table`
   (`resolve : string -> Result<Table, EvalError>`). A resolved operand is a whole second source, not a
   prefix. Widening the resolver's type would move every entry point's signature, a breaking change for
   a shape that neither the corpus nor any host has needed. The planner plans each half of a split on
   its own, so a rewrite that would have crossed the split is not taken. That changes no answer: the
   planned evaluation equals the reference as written (`Conformance.plannerLaws`).
7. **The `Table` door is unchanged.** It reads by name, which is Core's `Table` contract:
   `Table.validate` refuses a repeated name, so a valid table never meets the defect. A `Window` result
   with a repeated name that re-enters through that door is still read wrong. That rule belongs to the
   `Window`'s `As`, not to this seam, and it is recorded here with its measurement so that the phase
   which takes it starts from it.

**The law.** The suite list `The source seam (Phase 415)` splits every pipeline after every step: the
prefix is evaluated with `evalToPrepared`, its rows are read out by position and back in through
`prepareRows`, and the remainder is evaluated with `evalPrepared`. Every split answers what the whole
pipeline answers, refusals included. That holds over the transform law vectors, and over 400 generated
pairs of chained pipelines under a param env, with more than 500 splits falling strictly inside a
pipeline. It also holds over Phase 411's collision shape (a left already carrying `x_right`, so the
right's `x` is named `x_right_2`) and over the repeated-name `Window` shape above.

**Consequences.** `DataFrame.prepareRows` is new and additive. Nothing else moves: no signature, no
output and no wire byte. It is recorded under the `0.39.0` draft in `STABILITY.md` without moving its
number. The corpus copy follows the release rule (D6 (4)) unchanged, because no vector moves.

## 2026-10-13 — D24: the bare `Rank` window function is deprecated in documentation, keeps its wire tag, and may be removed no earlier than `1.0.0`, never re-pointed

**Context.** `WindowFn.Rank` is the dense rank (`1, 1, 2`). SQL's `RANK()` is the competition rank
(`1, 1, 3`), so the name misleads any reader coming from SQL. Phase 101 added the two explicit
spellings: `DenseRank`, which computes exactly what `Rank` computes, and `CompetitionRank`, which is
SQL's `RANK()`. It documented `Rank` as dense and kept it, because re-pointing it would silently
change the output of every pipeline that uses it. Until now nothing told an author who writes it.

**1. How it is marked: documentation only.** Three ways were weighed.

- *`[<Obsolete>]` on the union case* — rejected. F# reports an obsolete union case wherever it is
  constructed AND wherever it is matched (FS0044). An exhaustive `match` over `WindowFn` must name
  the case, so every consumer that matches exhaustively would get a warning it cannot remove except
  by adding a wildcard. Adding a wildcard gives up the exhaustiveness check on a closed vocabulary. A
  consumer that builds with warnings as errors would break on the next package update. This
  repository's own evaluator, codec and conformance kit would also warn at every one of their arms.
- *A validator warning on the wire form* — rejected. No verb or reader here has a channel for
  advisory findings: every check answers `Ok` or a refusal. Adding one for a single deprecated tag is
  a new seam with one user, and the rule-of-three bar is not met. Refusing `"rank"` would break every
  stored document.
- *Documentation* — taken. The case's doc comment opens with the deprecation and names both explicit
  spellings, saying which one is the same computation. An IDE shows it in the tooltip at the point
  where an author writes `Rank`. It is the one marker that cannot break a build.

**2. The wire form and the encoder.** `"rank"` decodes to `Rank`, unchanged. The encoder keeps
writing `Rank` as `"rank"`, so a document holding it re-encodes byte for byte and decodes to the same
value. Encoding it as `"denseRank"` would make it decode to a different case, which breaks the codec's
round trip. A new pipeline is expected to construct `DenseRank` (or `CompetitionRank`), and each
encodes as its own tag. So the encoder never writes `"rank"` unless the pipeline it was given holds
`Rank`.

**3. When it may go, and the rule.** `Rank` is **never re-pointed** at the competition semantics:
evaluating a stored pipeline must not change its answer under the same tag. It may be **removed no
earlier than `Fuaran.Compute.*` `1.0.0`**, the first major version, and only on these terms:

- the removal is its own `STABILITY.md` entry of class `removal`, with the migration
  `Rank` → `DenseRank`;
- in the same release, the decoder keeps `"rank"` as a legacy alias that decodes to `DenseRank` and
  normalises on re-encode, as `"cumSum"` does for `CumulSum`. A stored document then evaluates to
  the same table after the removal as before it;
- before `1.0.0` it stays, deprecated in documentation, through every `0.x` release.

**The class.** No signature, no output and no wire byte changes, so this is documentation, recorded
under the `0.39.0` draft without moving its number.

## 2026-10-13 — D23: an evaluation decides a derive its params decide; `Round`, `Floor` and `Ceil` over an undecided operand are undecided; the rest of the cells' share is recorded, not decided

**Context.** D5 types a derived column by its expression, and leaves it to the cells only where the
typer answers `Unknown`. Under that rule an empty or an all-null frame types a cells-decided column
`string`, so a full answer and an empty one disagree, and the static schema cannot name the column.
Phase 413 walked every shape the typer answers `Unknown` for, to decide each one statically where the
expression determines the type and to record why the cells must decide where it does not. The walk
also found that one shape the typer DID decide was decided wrongly.

**The shapes, each with a sample and its verdict.**

| # | Shape | Sample | Verdict |
|---|---|---|---|
| 1 | A bound `Param`, and every node whose typing it leaves undecided | `Param "p"`, `Binary(Add, Col "i", Param "p")`, `ApplyFn(Round, [ Param "p" ])` | **Decided at evaluation** by the bound cell (ruling 1); the static reader holds no env and still cannot name it |
| 2 | `Round`, `Floor`, `Ceil` over an operand the typer cannot decide | `ApplyFn(Round, [ Case([ c, Col "i" ], Col "m") ])` | **Was decided `float`, wrongly**: now undecided (ruling 2) |
| 3 | An unbound `Param` | `Param "unbound"` | Residue: every row is refused `UnboundParam`, so only a frame with no row answers |
| 4 | An unpinned `Now` | `Now NowGrain.Date` | Residue (ruling 3) |
| 5 | A column the schema does not carry | `Col "nope"`, `Binary(Add, Col "nope", Lit(Int 1))` | Residue: statically the schema in hand may be open; at evaluation every row is refused `UnknownColumn` |
| 6 | A join of two types the exact typer keeps apart, and every node over one | `Case([ c, Col "i" ], Col "f")`, `Coalesce [ Col "m"; Lit(Int 0) ]`, `ApplyFn(Least, [ Col "i"; Col "f" ])`, `Case([ c, Col "s" ], Col "i")` | Residue (ruling 4) |
| 7 | An operator over operands it refuses | `Binary(Add, Col "s", Col "i")`, `Not(Col "i")`, `ApplyFn(Abs, [ Col "s" ])`, `Binary(Div, Col "m", Col "i")`, `Rounded(Col "f", r)`, `Binary(Eq, Col "s", Col "d")` | Residue (ruling 5) |
| 8 | A bound param whose substituted arms put a float beside a decimal | `Case([ c, Param "pf" ], Param "pd")`, `pf` a float and `pd` a decimal | Residue (ruling 1) |

An `Unpivot` of a string beside an int value column is the same rule's one cells-decided unpivot, and
D18 already refuses it where the cells hold both.

**1. A bound param decides the derive at evaluation.** `DataFrame.derivedTypingIn env cols e` is the
rule every evaluating path reads: the reference evaluator, the morsel runner, and the incremental
seam's row walk and chunked path. Where `derivedTyping` decides or refuses, it is the answer. Where it
leaves the column to the cells, the answer is what the same rule DECIDES over `e` with the
evaluation's params substituted. That is `ColExpr.substitute`'s expression, which evaluates exactly as
`e` does (the substitution witness `paramLaws` certifies). So a bound param types as the literal it is
bound to, on a full, an empty and an all-null frame of that evaluation alike. The planner and every
static reader (`SchemaWalk`, `PipelineQuery`, `Plan`) hold no env and read `derivedTyping` unchanged.
So a param-decided derive is still not total to the verdict, and the planner still moves no filter
past it.

The substituted rule DECIDES and never REFUSES. Where the substituted arms put a float beside a
decimal (row 8), the column stays the cells', as it was. Refusing it would turn every frame whose cells
hold one family from an answer into a refusal. The cells already refuse the frames that hold both. That
consistency with a literal arm, which D5 refuses statically, is the one thing this ruling declines. It
is a behaviour break with no defect behind it, and is left for a phase that wants it.

**2. `Round`, `Floor` and `Ceil` over an undecided operand are undecided.** The typer answered `float`
for any operand that was not a decided decimal, `Unknown` included. Over a decimal those three answer a
decimal (Phase 277), so an undecided operand that turned out to be one put a decimal cell in a column
declared `float`. Two cases were observed before the fix. `Round(Param p)` with `p` bound to a decimal
gave a `float` column holding `Decimal "3"`. `Round(Case([ i > 0, Col "i" ], Col "m")` over rows taking
both arms threw at the `Table` boundary ("column 'd' cannot be held by its type"), where it should
have answered. Now an undecided operand gives an undecided answer. The cells decide such a column, or
a bound param does by ruling 1. The cells refuse a float beside a decimal by name. The typer and the
model (`proofs/Pipeline.fst`, `typing_apply_fn`) change in the same clause.

**3. An unpinned `Now` stays undecided.** A pinned evaluation substitutes the witness's cell before
anything is typed, so the derive is decided by its literal on every frame of that evaluation, and the
shard's problem does not arise there. An unpinned one refuses every row. The static reader could name
`date` for the date grain. But that name would rest on the host's witness answering a `Date`, which
nothing checks (`nowLaws` certifies determinism, not the cell's type). A static type the evaluator
can contradict is worse than none. The timestamp grain's unit is the witness's in any case.

**4. A join of two types the exact typer keeps apart stays the cells'.** Today a frame whose rows
take only the int arm of `Case(int, float)` answers an int column of ints, and one whose rows take
both answers a float column. Deciding `float` statically would have to be honest at the derive
itself: the int cells would have to become floats there, or the static schema would claim `float`
over int cells for a later step. The totality verdict reads that claim as exact, because two ints
added can overflow where two floats cannot (D3). So the decision is a value change on every frame
that answers with ints today. That reopens D3's and D5's rulings, and is left to an operator ruling
rather than taken inside a typer phase. A pair no widening relates (a string beside an int) is refused
by the cells where both occur (D18). Deciding it statically would refuse frames that hold one of them.

**5. An operator over operands it refuses stays the cells'.** Every row with its operands present is
an error, so every frame that answers holds no present value. The cells already type that frame
`string`, which is what a decided "no present value" would say. No two answering frames disagree.
What is missing is only the static name of a column that can hold no value. Buying it costs an
absence rule per operator in the typer and in its model, and changes no answer.

**The law.** `DeriveTypingConformance.laws` gains a sixth law. A derive the walk leaves to the cells,
but decides over its expression with the evaluation's params substituted, types its column the same
over a full, an empty and an all-null frame. The menu gains `Round(Param p)`, `i + p` and a `Round`
over an undecided int/decimal join, and the param is drawn as an int, a float or a decimal. The suite
holds the law over GENERATED expressions too (`DataFrame.Typing`, "a decided derive types its column
the same over a full, an empty and an all-null frame (Phase 413)"). Wherever `derivedTypingIn`
decides, the column takes that type over every frame, every present cell is of it or widens into it,
and a schema-decided type is the one `SchemaWalk` names. Restoring the float answer for an undecided
`Round` turns it red. So do the explicit cases in `DeriveTypingTests`. Restoring the env-less rule in
the incremental seam turns the prime/refresh case and the law family red. In the model,
`derive_type_rows_free` now covers a derive `derived_typing_in` decides. Requiring `ByCells` instead
of `Decided` fails its proof.

**The class.** No public signature moves (`derivedTypingIn` is internal). What a consumer can observe
is `behaviour`:

- a `Round`, `Floor` or `Ceil` over an undecided operand is no longer typed `float` by `typeOf`,
  `SchemaWalk` or `PipelineQuery`, which now names it `TypeUndecidable`;
- such a derive is typed by its cells or its params;
- a bound-param derive over an empty or all-null frame is typed by the param's cell where it was
  `string`.

`STABILITY.md` records it under the `0.39.0` draft, which already carries a BREAKING class.

## 2026-10-13 — D22: the `lines` APPEND and `window CumulSum` edit ticks at 20,000 rows join D19's named exception on the same terms, and a clock case is red only when the same cell is red on every counted attempt

**Context.** D19 holds every corpus tick cell to `max(1.6 x full, 1.5 x baseline) + floor`. It holds one
named exception, `lines` edit at 20,000 rows, to 1.5 times its recorded post-423 cost plus the floor,
and to neither other arm. A later landing round went red on the neighbouring cell, `lines` APPEND at
20,000 rows. An alternating A/B on the Arm64 machine showed the cell reads the same with or without
that round's changes, against a baseline-arm allowance of 2.255 ms:

| Tree | Readings |
|---|---|
| Main | 2.52, 2.36, 2.39 ms |
| Main plus the round's changes | 2.02, 2.38, 2.62 ms |

Phase 439's own landing gate had read it at 1.854 ms. The cell is the same noisy post-423 shape as its
edit tick, not a regression of the round.

**The operator's ruling (2026-10-12, the fifth), in substance:** the `lines` APPEND tick at 20,000 rows
joins the named exception on the same terms. Its ceiling is today's cost, judged by the leg's
three-attempt rule and pinned by the main-suite case. Like the first entry, it is removable only by
Phase 448's acceptance.

**What was done.**

- **The second entry.** `ScalingTests.tickBaselineExceptions` gains exactly one entry:
  ("append", "lines", 20000, "the same noisy post-423 shape as its edit tick, which Phase 448 owns").
  The pin case ("the tick bound's named exceptions are exactly the ruled ones") lists the two ruled
  entries and nothing else.
- **The ceiling.** It was recorded by `gates/record-tick-baselines.ps1 -Ceiling 'append|lines|20000'`
  from main's tree, `12a825d`, by D19's method: the median over three full legs, none discarded, of each
  leg's best.
  - The clean legs' bests were 1.794, 1.915 and 2.196 ms, so the median is 1.915 ms (0.388911
    calibration units, `tickExceptionCeilings`). One leg with a discarded window was set aside.
  - The allowance is about 2.87 ms plus the floor, roughly 3.02 ms in all.
  - Every A/B reading above is under it.
- **Why the recording waited overnight.** The first two recording attempts, on the evening of
  2026-10-12, found no clean leg in eight. The machine was running a single-threaded workload at 1.9
  times its calibration baseline: a 2,000-reading probe had a median of 1.909. That is consistent with
  the operating system throttling a background process. Once the operator exempted the runtime from
  that throttling, a 200-reading probe read 1.000 and the recording took four legs.

**Refreshing it** follows D19: the operator decides, the script is the only writer, and the entry goes
when Phase 448's acceptance removes it.

**Amended 2026-10-13 — the operator's sixth ruling.** The first full gate with the append exception
in place reached a counted RED in its edit case. All three counted attempts were red, with no window
discarded, but a different cell was red on different attempts:

| Attempt | Red cell | Tick | Allowed |
|---|---|---:|---:|
| 1 | `lines` edit @ 20,000 (the first exception) | 3.155 ms | 2.822 ms |
| 2 | `window CumulSum` edit @ 20,000 | 2.351 ms | 1.949 ms |
| 3 | `window CumulSum` edit @ 20,000 | 1.991 ms | 1.949 ms |

`lines` edit @ 20,000 read 3.155, 1.850 and 1.751 ms. `window CumulSum` edit @ 20,000 read 1.916, 2.351
and 1.991 ms. The leg's rule was per CASE: red when every attempt has some red cell. With several noisy
cells, that reddens a case where no single cell is red every time.

The ruling ("Both"), in substance:

1. **The per-cell rule.** A clock case is red only when the SAME cell is red on all three counted
   attempts; different cells red on different attempts is green. The saturation path (no verdict,
   exit 3) is unchanged.
   - It is implemented as `ScalingTests.clockCellVerdict` over each attempt's red-cell set (their
     intersection). A case that judges many cells raises `ClockCellsRed` naming them, and a case with
     one assertion is one cell, so its verdict is the rule it always had.
   - The main-suite case "a clock case is red only when the same cell is red on every counted attempt"
     pins both halves:
     - a different cell red per attempt is green;
     - one cell red on all three is red, whatever else is red beside it.
2. **The third named exception.** `window CumulSum` edit at 20,000 rows joins the exception on the
   same terms, with its reason ("one of the shapes Phase 448 owns: pre-423 median 1.201 ms, post-423
   1.45–2.35 ms against a 1.95 ms allowance").
   - Its ceiling was recorded by `-Ceiling 'edit|window CumulSum|20000'` from `12a825d`. Three full
     legs, none discarded, gave per-leg bests of 1.733, 1.840 and 1.825 ms. The median is 1.825 ms
     (0.371086 units), an allowance of about 2.88 ms with the floor.
   - The pin case lists exactly the three ruled entries.

## 2026-10-12 — D19: the corpus tick bound gains a second arm, the tick's own recorded pre-423 cost, one table per CPU architecture; and the append diff's cheap prefix test is restored

**Context.** The clock leg holds every corpus node's one-row tick (edit and append; `Delta.diff` plus
the refresh) against the full evaluation it replaces: `tick <= 1.6 x full + floor` (Phases 274, 283,
327, 359 and 371). Phase 423 made the `Table` boundary a view. Full evaluations fell 2 to 5 times on
`lines`, `window CumulSum` and `group-by high-card`, and those cells read ×2.4 to ×4.5 against 1.6. The
gate's verify arm was waived to land 423, and the bound was left to the operator (Phases 285 and 327).

Phase 439 re-measured both trees on one machine, the full clock leg in Release, interleaved. The
figures are in `docs/performance.md` ("The tick bound after the boundary became a view"). Three
findings:

- **The edit ticks were no dearer than before 423** (within 1.17×).
- **The append ticks of the grouped nodes were 1.4 to 2.0 times dearer.** Timed apart, the cause is
  the append diff, which was 1.6 to 2.9 times its own pre-423 cost. Its prefix walk compared every row
  through a closure where Phase 359 had tried a cell reference check first. Only a clock sees this: the
  diff allocates nothing for it and mints no extra key.
- **On three shapes the refresh alone now costs 2.1 to 3.4 times the full evaluation** (`lines`,
  `window CumulSum`, `group-by high-card`). It cost 0.6 to 0.9 before 423.

Four bounds were put to the operator:

1. A recalibrated ratio. It needed about ×5 to be green, and it hid the append regression.
2. An absolute non-regression bound against each cell's recorded pre-423 tick.
3. Both of these arms together.
4. A split: the refresh against the full, and the diff held only by its counts. The count arm is blind
   to the regression, and the refresh arm was red on the three shapes.

A work bound (rows the refresh evaluates against rows the full evaluation reads) reaches a verdict on
any machine, because it needs no clock. It is green on every tree, including the regressed one, so it
complements a clock bound and cannot replace one.

**The operator's ruling (2026-10-12), in its own words, abridged to what binds this repository:**

> 1. The assertion: option C at c = 1.5. The incremental tick case (edit and append, every corpus node)
>    asserts tick ≤ max(1.6 × full, 1.5 × recorded pre-423 tick) + the existing per-call floor.
> 2. Fix the append-diff regression in `Delta.fs` in this phase. Restore the cheap prefix comparison
>    before the value compare (`ColumnRead.sameAt`), without changing any diff result. Prove the results
>    are unchanged with the existing delta laws and vectors. The phase lands only when the new bound is
>    GREEN.
> 3. Baselines: the pre-423 ticks, one table per CPU architecture. Record this machine's (Arm64) table
>    from the pre-423 tree `ccbddc7`. Ship a recording script so another architecture can record its own
>    table from `ccbddc7`. A machine whose architecture has no table must NOT pass vacuously: the absolute
>    arm reports loudly, naming the missing table and the script. Keep the 1.6 × full arm active
>    everywhere.
> 4. The finding that refreshing costs more than recomputing on lines, window CumulSum and high-card
>    group-by is NOT this phase's to fix; it goes to a successor phase. Record the finding and the
>    numbers.

**What was done.**

- **The condition.** `ScalingTests.judgeCorpusTick` holds both corpus tick cases to
  `tick <= max(tickBound * full, tickBaselineFactor * baseline) + floor`, with `tickBound` 1.6 and
  `tickBaselineFactor` 1.5. The `Scaling` tick case keeps the ratio alone.
- **The baselines.** `tickBaselines` holds, per `RuntimeInformation.ProcessArchitecture`, each cell's
  pre-423 tick (the median over clean legs of each leg's best, per the second ruling below) in units
  of the leg's calibration baseline. A unit scales with the machine within
  an architecture, as the floor does (Phase 327). The integer chain does not track a memory-bound tick
  across architectures, so tables are not shared across them.
- **A missing table.** On an architecture with no table, or for a cell missing from one, every cell
  prints `NO TICK BASELINE TABLE for architecture …` naming the script and holds the cell to the ratio
  arm alone. That is the stricter condition, so a missing table can redden a leg and never greens one.
- **The Arm64 table** was recorded by `gates/record-tick-baselines.ps1` on 2026-10-12: three full clock
  legs of `ccbddc7`, no window discarded. The two corpus cases run alone read 10 to 25 percent low
  against the gate's full leg, so the script runs the whole leg.
- **The append fix.** `Delta.ColumnRead.samePrefix` compares a column's whole prefix in one typed loop:
  both validities, then the two value vectors, with no closure per row. The append diff walks the rows
  only where that fails. Its `true` is sufficient for `sameAt i i` at every row of the prefix, and its
  `false` decides nothing. No diff result moves: the delta and append laws, the `diffTrustingPrefix`
  falsifier and the delta wire vectors are unchanged and green. After it, the grouped append ticks are
  0.46 to 0.88 of their pre-423 cost.

**Refreshing a table, and who decides.** A table is the contract a cell is held to, so refreshing one
is the operator's decision, recorded here as a dated note under this entry, and never a re-run to make
a red leg green. There are two occasions:

- **Adding the first table for an architecture.** Anyone may record it with the script from
  `ccbddc7`. Landing it is an operator act, because until then that architecture's leg holds the
  stricter ratio-only condition.
- **Changing the baseline TREE** (a later reference than `ccbddc7`), for example after a phase
  deliberately trades tick cost for something else. That is a new ruling, and every architecture's
  table is re-recorded from the new tree in the same change.

A table is never edited by hand, and never re-recorded from the tree under test.

**Scope.** `src/Fuaran.Compute.DataFrame/Delta.fs` and `gates/record-tick-baselines.ps1` lie outside
the phase's declared files and were approved as widenings with the ruling. No public signature moved:
`ColumnRead` is internal.

**Not decided here.** The three shapes whose refresh costs more than recomputing are admitted by the
baseline arm, not by the seam's claim. Making them cheaper than recomputing again is Phase 448's
work.

**Amended 2026-10-12 — the operator's second, third and fourth rulings.** The first ruling's bound
went to the gate and reached a counted RED. Gate runs 1 to 4 on the same tree had reached no verdict.
Two cells were red on all three counted attempts:

- `lines` edit at 20,000 rows: 2.59–5.27 ms against a baseline arm of 1.95 ms.
- `window CumulSum` edit at 100,000 rows: 7.66–8.02 ms against 7.45 ms.

The second cell's baseline was its single lowest pre-423 reading (4.97 ms; its pre-423 legs spread
from 4.97 to 7.46 ms). The first cell is a real regression. Three further rulings followed, each
summarised here as given:

- **Second.** A cell's baseline is the MEDIAN, over at least three full legs with no window
  discarded, of each leg's best reading, and the Arm64 table is re-recorded that way from `ccbddc7`.
  One named exception, `lines` edit at 20,000 rows ("a real 1.3–2× post-423 regression on a shape
  Phase 448 owns"), is listed in `ScalingTests.tickBaselineExceptions`. It is printed on every run,
  pinned exactly by a main-suite case so that no second exception can be added silently, and removable
  only by Phase 448's acceptance.
- **Third.** The exception's shape is a ceiling of TODAY'S COST: the cell is held to
  `1.5 x its recorded post-423 tick + floor`, and to neither other arm. The figure is recorded by the
  same script and method (`-Ceiling`) from the tree carrying Phase 423 and this phase's `Delta.fs`
  fix, and stored beside the exception with its provenance (`tickExceptionCeilings`). The second
  ruling's first wording, "ratio arm only", would have held the cell to ×1.6 of a 0.59 ms full
  evaluation, red on every run; it was corrected here.
- **Fourth.** The ceiling as recorded is kept. The cell is judged by the clock leg's own rule, like
  every other cell: red only when red on all three counted attempts.

**What was recorded.**

- **The baselines.** The Arm64 table was re-recorded on 2026-10-12 from `ccbddc7`: three full legs,
  none discarded.
- **The ceiling.** It was recorded from `3caa557` (the branch tree carrying the fix; committed in
  `1453743`). It took five legs to get three clean ones, after an earlier attempt found none clean in
  nine. The per-leg bests were 1.627, 1.789 and 1.783 ms, so the median is 1.783 ms (0.362989
  calibration units). That makes the allowance 1.5 × 1.783 + 0.147 = 2.821 ms on Arm64.
- **The evidence the ceiling inherits, for Phase 448.** The cell has 34 counted readings on the branch
  tree: minimum 1.389, median 2.397, p90 3.484 and maximum 5.270 ms. Nine are above 2.821 ms. They form
  two clusters (about 1.6–2.1 and 2.6–3.6 ms) with a tail. No leg had the cell above the allowance on
  all of its counted attempts. Its pre-423 readings were 1.30 to 1.59 ms.
- **Legs that discard windows.** The script now sets aside a leg with a discarded window and runs
  another in its place, up to `-MaxLegs`. Every recorded figure therefore comes from legs with none
  discarded, as the second ruling asks.
- **Why so many runs, on this machine.** Discarded windows and no-verdict gate runs share one cause:
  calibration readings step to 6.2 ms against 4.91 (1.26×, just past k = 1.25) at a window's opening
  and after the `lines` cells, and further after the main suites. The ruled answer is to re-run on a
  quieter machine, not to move k.

**Refreshing the ceiling** follows the table rule above. It is the operator's decision, recorded
here; the script is the only writer. It goes when Phase 448's acceptance removes the exception.

**Amended 2026-10-24 — the X64 table (Phase 461).** Until now only Arm64 had a table, so every x64
machine, CI's `ubuntu-latest` runner included, printed `NO TICK BASELINE TABLE for architecture X64`
and held each cell to the ratio arm alone. Phase 461 adds the x64 table. This is the first of the two
occasions above, and the phase that asked for it is the operator's decision to land it.

- **Where it was recorded.** No x64 machine is to hand, so the script got a hosted home:
  `.github/workflows/record-tick-baselines.yml`. It is triggered only by `workflow_dispatch`, so no
  push or pull request runs it. It runs `gates/record-tick-baselines.ps1` unchanged on `ubuntu-latest`,
  the label the ci clock leg runs on. It uploads the block, every leg's log and a description of the
  runner (image, `lscpu`, `dotnet --info`). On a hosted runner the script's provenance line also names
  the runner image (`ImageOS` / `ImageVersion`).
- **What was recorded.** Run 40110148786 used the script at `24bce4e` and recorded from `ccbddc7`,
  with the second ruling's method: the median over three full legs of each leg's best. On image
  `ubuntu24 20261012.334.1`, an AMD EPYC 7763 with 4 vCPUs, all three legs were clean, with no window
  discarded and calibration at 5.21 to 5.22 ms. That table is pasted beside Arm64 in
  `ScalingTests.tickBaselines` with its provenance.
- **Two tables, one rule.** Per cell, the X64 figure is 0.63 to 1.83 times the Arm64 one, with a
  median of 1.12. The two tables do not differ by a constant factor, which is why this entry keys
  tables by architecture rather than scaling one.
- **What CI now asserts.** On x64, every corpus cell is held to
  `max(1.6 x full, 1.5 x baseline) + floor`. Phase 459's declined shapes keep their own rule. Arm64 is
  unchanged.

**Re-recording the X64 table after a runner change.** A table is in calibration units, so a faster or
slower x64 machine reads the same figure. The routine weekly `ImageVersion` bump is therefore not, by
itself, a reason to re-record. Two kinds of change can move the table:

- the `ubuntu-latest` label moving to a new OS release or .NET runtime;
- hosted x64 runners moving to a different CPU family.

Either one is visible as a difference between the table's provenance line and the runner description
of a later run. The ci log's `Set up job` step names the image.

When the x64 clock leg goes red, or keeps reaching no verdict, after such a change, re-recording is
the operator's decision, recorded as a dated note here, and never a re-run to make a red leg green.
To re-record:

1. Dispatch the workflow (`gh workflow run record-tick-baselines.yml --ref main`) with the default
   `commit` (`ccbddc7`).
2. Download its `tick-baselines-x64` artefact.
3. Replace the X64 block with the artefact's `tick-baselines-X64.fs.txt` verbatim, with the run id
   in the provenance comment.

The same workflow serves a change of baseline tree: pass the new tree as `commit`. Arm64 is then
re-recorded from that tree locally, in the same change.

## 2026-10-12 — D21: a sort key that names no column is refused with `UnknownColumn`, in a `Sort` and in a `Window`'s ordering; the pinned "unknown columns skipped" is reversed

**Context.** `Sort` dropped a key that named no column: `resolveSortKeys` resolved the keys against
the step's schema and silently left the unknown ones out, which the comparator's documentation pinned
as "unknown columns skipped". Every other verb refuses an unknown column with a typed `EvalError`, and
the planner's totality verdict counted every literal-keyed `Sort` as total. So a misspelled sort key
returned the input order, `Ok`, with no sign the sort had not happened (Phase 412).

**Decision.** A key a `Sort` takes from a pipeline that the frame does not carry is refused with
`UnknownColumn(name, available)`, the error every other verb answers, at the FIRST such key in key
order, before anything is ordered. The same holds for a `Window`'s `OrderBy`, the other step whose
ordering keys come from the pipeline. The sites that held the old rule, and where each now stands:

- **the `Sort` step and the fused sort-then-limit** (the planner's top-n): both refuse, the fused
  form after the sort's slots resolve and before the limit's, so the first error is the one the two
  steps would have given unfused;
- **`Window`'s ordering** (`windowColumnOfWith`, and `windowColumnOverWith` for a caller holding an
  ordering already): refuses after the window's own `Of` checks;
- **the incremental seam** — its sort step merges under `compareResolved`, and its window steps order
  through the same functions: the sort step refuses before merging, and the window steps refuse
  through the window functions above, so a prime and a refresh refuse exactly where the reference does;
- **the planner's totality verdict** (`Plan.isTotal`): a `Sort` is total only when every key is a
  literal the schema carries;
- **`rowCompareBy`**, the exported comparator: a comparator has no error to answer, so it still drops
  an unknown key, and its documentation now says the reference refuses such a key before it ever
  compares; a caller reproducing the reference refuses first, as the reference does;
- **the conformance laws and the corpus**: no law pinned the skip, and no vector sorts or orders a
  window on a key its table lacks, so no vector moves; the corpus copy follows the release rule
  (D6 (4)) unchanged. The proof model treats `Sort` as one of the verbs it does not model, so it holds
  no copy of the rule.

**Why refuse rather than keep the skip.** The skip made a typo indistinguishable from a sort, and the
pipeline's author is the only one who could have noticed. A refusal is what every other verb already
does, so a consumer handling `UnknownColumn` handles this too.

**What it does not change.** A `Window`'s `PartitionBy` still partitions by the columns it can find
and drops a name the schema lacks; that is not a sort key and is a separate rule, left as it stands
here.

**Consequences.** A behaviour change to a public evaluator, versioned per the draft-slot rule
(`STABILITY.md` `0.39.0`); no public signature moves.

## 2026-10-12 — D20: a join's colliding right column takes the first free suffix against the whole output, `x_right`, `x_right_2`, …; a refusal was rejected

**Context.** A combining join suffixed a right column whose name collides with a left one by appending
`_right`, checked against the LEFT names only. A left carrying `x` and `x_right` joined to a right
carrying `x` therefore emitted two columns named `x_right`; so did a right carrying both `x` and its
own `x_right`. The evaluator reads a table's columns by name, so when such a table re-enters it — a
pushdown boundary, a cache, a checkpoint, a second query over the result — one of the two is read as
the other with no error anywhere (Phase 411).

**Decision.** The colliding right column takes `_right`, or `_right_2`, `_right_3`, … — the FIRST
suffix free against every name the output carries: every left name, every right name (a right name
that collides with no left name keeps its own name, so it is taken before any suffix is chosen), and
every suffix an earlier colliding right column took, decided in right column order. It is one
function, `DataFrame.joinRightNames`, which the evaluator and `SchemaWalk` both read. Over an open
right source the walk no longer names a colliding right column, because the right's unnamed columns
decide its suffix.

**Why the suffix and not a refusal.** A typed refusal naming the colliding column was the other
candidate. It would make a join's success depend on its operands' names, so a pipeline that runs today
over one table would refuse over a table that merely gained a column, and the static walk would have to
carry a new refusal verdict for a step that has always been total. The unique suffix keeps the join
total and changes nothing for any join whose output was already unique — every join the corpus vectors
record, and every one a consumer reading SQL-style output has met — while the case that changes was
already wrong. **What each costs a consumer reading SQL-style output:** under the suffix, a consumer
of a join whose names were already unique sees nothing move, and one that read an output which repeated
`x_right` now finds two addressable columns — the colliding right column under the first free suffix,
so a by-name read of `x_right` may land on the other of the two than it did, which is exactly the read
that was ambiguous before; under a refusal, it would get an error where it got a table, and have to
rename an operand to get one back. Neither
consumer can be told apart from the table alone, which is why the rule is deterministic in column
order and recorded here rather than left to a host to reproduce.

**Consequences.** A behaviour change to a public evaluator, versioned per the draft-slot rule
(`STABILITY.md` `0.39.0`); `DataFrame.joinRightNames` is new and additive. `Conformance.schemaWalkLaws`
gains the law that a combining join adds no repeated name and that the walk names a built pair of
joins as the evaluator does. No corpus vector moves; the corpus copy follows the release rule
(D6 (4)). A `Window` whose `As` names an existing column still appends it, leaving the name twice
(Phase 112's walk pins that); that is a separate rule and this decision does not change it.

## 2026-10-10 — D18: three rulings on what Core `1.0.0`'s typed column leaves without an object — a non-canonical text cast to a temporal type is refused by name, a derived column spanning two unrelated types is refused by name, and the fused sort-then-limit bound is on work, not bytes

**Context.** Until Core `1.0.0` a `Column` was a `Cell list`, so a column could hold a cell of the
wrong type beside the right ones, and three evaluator semantics were stated for such columns. A typed
vector cannot hold them (Phase 417), so each statement lost its object in the Phase 423 raise. The
operator ruled on each on 2026-10-10; the rulings are recorded here because each is a behaviour a
consumer can observe, and each was decided rather than fallen into.

**1. `Cast(date, x)` and `Cast(timestamp, x)` of text that is not the canonical form are REFUSED BY
NAME.** `castCell` used to answer `Date s` / `Timestamp s` for any string `s` and let the column carry
it; a `Date "nonsense"` cell is one Core's `Column.ofCells` refuses since its Phase 422, so the frame
could not hand such a column back as a `Table`. The cast now refuses with a `TypeError` naming the
cell, the target type and the text — `cannot cast 'x' to date: not a canonical YYYY-MM-DD` and
`cannot cast 'x' to timestamp: not a canonical instant of its unit` — at the row, the same way a
string that is not a number refuses `Cast(int, …)`. A canonical text casts as before. The alternative,
answering `Null` for the unparseable text, was rejected: a cast that silently drops a value is the
defect the typed refusals exist to prevent.

**2. A derived column — a `Derive`, or an `Unpivot`'s value column — whose cells span two types no
column type holds together is REFUSED BY NAME** (`typesBesideEachOther`: "derived column 'x' holds a
string beside a int, which no column type holds together: cast one to the other's type first"), on
D5's precedent for a float beside a decimal. Phase 321 typed such a column by its earlier cell and
carried the others as they were, which only a cell list could do. The static typer still decides where
it can (D5); the refusal is the cells' half of the rule, reached where the typer deferred to them. The
`Pipeline.fst` model's list column has no such refusal, so the oracle bridge classes it as outside the
model's domain rather than as a disagreement (`ProofOracleTests`). *The same rule reaches `Union`*,
the one other verb that puts two columns' cells into one: a column of the union takes the join of the
two sides' types where one exists (an int column over a float one is a float column) and is refused by
the same name where none does — the cell list used to carry the right side's cells under the left
side's type, a column no `1.0.0` table can hold.

**3. `PlanTests`' fused `Sort > Limit` bound is on WORK, not on bytes.** Phase 282 held the fused
top-n to a quarter of the full sort's allocation, and that held only because the `Table` boundary's
conversion dominated the full sort's cost. With the boundary a view (Phase 423), the full sort at
20,000 rows allocates LESS than the fused form (1.49 MB against 2.31 MB) while the clock still favours
the fused plan by an order of magnitude (1.3 ms against 18 ms). The claim was always about work — the
top-n keeps ten rows in order where the sort orders them all — so the case now counts the rows each
form hands to an ordering kernel (`SortFinite` / `SortPositions`, through a counting `KernelSet`) and
bounds that; the allocation figures are printed beside it and not gated.

## 2026-10-10 — D17: the substrate pin is raised to Core's `1.0.0` DRAFT through the local feed, with the public CI accepted RED until that version is published; the frame borrows and adopts at the `Table` boundary; and four choices the raise forced

**Decision (operator ruling 2026-10-10, Phase 423).** `FuaranCoreVersion` is `1.0.0`, a version the
public registry does not yet serve: Core's `1.0.0` is a draft slot, packed into the workspace's local
feed, which this repository's `nuget.config` already consults first. The compute strand is the first
consumer of the typed column (Core Phases 417, 418, 420, 421, 422, 426, 427), and the operator chose
to raise it against the draft rather than wait for the release, so that the release ships with a
consumer that has absorbed it.

**1. The accepted state: this repository's GitHub CI is RED from this change until Core `1.0.0` is
published.** CI restores from nuget.org and cannot see an unreleased version, so its restore fails.
This is accepted and dated: no workaround is added (no `nuget.config` change pointing CI at another
source, no conditional pin), because a workaround would make the public repository build against
packages a reader outside the workspace cannot obtain, which is the state the version-pinning mandate
forbids. The red clears when Core tags `v1.0.0`; the gate (`verify.ps1`) is green on the machine that
holds the feed, and that run is the one this change cites.

**Amended 2026-10-12 (fuaran-core Phase 444): the draft is released as `0.37.0`, so the red ends at
`v0.37.0`.** The operator ruled that Core is not published as `1.0.0`; the draft's contents, the same
packages and the same public surface this repository was raised against, are released as the minor
`0.37.0` (Core's DECISIONS.md D147). `FuaranCoreVersion` is `0.37.0`, packed into the local feed from
the renamed slot, and the accepted red now ENDS when Core tags `v0.37.0` and nuget.org serves every
`Fuaran.Core.*` package this file pins at that version; nothing else about the accepted state moves. The
`1.0.0` this entry and D18 name is that slot.

**2. The boundary converts nothing.** `Frame.ofTable` BORROWS each column's storage through
`Vector.Unsafe.borrow` (a slice is copied, since the frame indexes from zero; an `AllValid` column has a
mask built) and `Frame.toTable` ADOPTS the frame's arrays through `Vector.adopt`. The code that
converted — `Frame.Origins`, `Frame.unpackFallback`, the `Chunked.Cells` memo, `Vec.packList`,
`InternedCells` — goes. The no-write rule `Vector.Unsafe` states is one this assembly already obeyed
internally: no verb writes into an array it did not allocate.

**3. Four choices the raise forced, each the smallest that keeps a stated behaviour.**

- *Unknown-tag refusals are `MalformedShape`, not `UnknownType`.* Core `1.0.0` typed `UnknownType`
  to name column types only; the compute codecs' closed vocabularies (verbs, functions, modes) refuse
  an outside tag as `MalformedShape "unknown tag '<got>' (expected one of: …)"`, and the suite reads
  the pair back through its `UnknownTag` pattern.
- *A projection renames a field KEEPING its metadata; a derived, aggregate or pivot column is a fresh
  field with none.* `Schema = Field list` carries a unit, a label and a description per field (Core
  Phase 427). A `Project` that renames a column is the same column under another name, so the field's
  metadata travels (`Fields.rename`); a column the evaluator computes has no provenance a unit could
  be read from, so it is `Field.create name type`.
- *`Column` and `Table` are equatable by their cells and no longer comparable.* `ColumnData` is
  `NoComparison` in Core; nothing in this repository ordered tables, and the two places that compared
  columns for a diff (`ColumnOps.toOps`, `Delta`) compare CELLS, under the cell's own equality, which
  tells a NaN from itself where the vector identity does not.
- *A column that disagrees with its schema entry is read boxed, and one whose cells span two types
  cannot leave the frame.* Core `1.0.0`'s `Table` can hold a column of another type than its field
  says (the frame reads it as cells, as it always did), but never a mixed one: `Frame.toTable` refuses
  such a column by name (`cannot be held by its type`), and the suite's generators stop drawing the
  inputs that produced one (a mistyped column merged with a typed one), because the reference has no
  answer for them. The suite's column builder (`KitColumn.create`) draws a cell no column holds beside
  the others ABSENT, and documents it.

**4. The version.** The public surface of `Fuaran.Compute.DataFrame` and `Fuaran.Compute.PipelineQuery`
moves class `retype` (every signature that carried a schema), so the `0.38.1` release is followed by
`0.39.0`, per the draft-slot rule. The law vectors re-emitted from this tree move ONE vector's bytes:
`column-op-6-setColumn-decimal` records `Decimal "12"` where it recorded `Int 12`, because the decimal
column now holds the int as the decimal of its value (the representation Core's Phase 417 stamped into
its own `decimal-laws.json`). The shared corpus copy follows the release (D6).

## 2026-10-08 — D16: every float the evaluator computes leaves it with one NaN, `Double.NaN`'s bits, and the gate runs the main suite in Release as well as Debug

**Decision.** One rule, evaluator-wide: **every float the evaluator emits carries the canonical NaN.**
A NaN answer is `Double.NaN`'s bit pattern, whatever sign or payload the arithmetic produced; every
other value is untouched. The rule does not loosen the laws; it makes them keep their bit-for-bit
claim. `canonicalNaN` / `canonicalNaNCell` are applied where an answer LEAVES the evaluator, never per
function, so a function added later cannot forget:

- *expressions* — `evalResolved`, the row evaluator's one exit (its recursion is the private
  `evalNode`), which `evalExprInRow` and the incremental seam's re-evaluated cells read;
  `runCompiled`; and a compiled `Derive`'s float root and boxed root, both where the sequential
  member puts them and where a `MorselHandOff` runner puts them;
- *aggregates* — `aggCells` (the deferred path of every `GroupBy` and `Pivot` aggregate, and the
  whole of the public `aggregateCells`), `GroupAgg.Stream.TryCell` (the streamed answer, which the
  incremental seam reads directly) and `GroupAgg.Output.Float` (every float a `GroupBy` or `Pivot`
  writes, unboxed or as a cell);
- *windows* — the window column where `windowColumnOverWith` builds it, the one place every window
  answer passes: the frame form, the row form and the incremental seam's resumed runs.

The batch and the incremental paths therefore go through the SAME exits — the refresh re-evaluates a
derived cell through `evalResolved`, recomputes a group through `TryCell` and `aggregateCells`, and
resumes a window through `windowColumnOverWith` — so they cannot disagree on a NaN's bits. The steps
that compute nothing (`Project`, `Filter`, `Sort`, `Limit`, `Distinct`, the joins and set verbs,
`Unpivot`) hand on the caller's cells as they were given, so a source value keeps the caller's bits
until something computes over it; the moment an expression, an aggregate or a window reads it, the
answer is canonical (a `Derive` of `Col x` over a sign-clear NaN answers `Double.NaN`).

**Why canonicalise rather than compare NaNs as equal.** Bit-determinism of an answer across Debug,
Release and Fable builds, and across hosts, is a property other things stand on: the .NET half of a
parity vector is compared by bits, and any content addressing over raw values (rather than over the
canonical wire) hashes bits — a digest over a derived column included. A law loosened to "any NaN
equals any NaN" would stay green while those consumers saw two answers to one question. Making the
evaluator emit one NaN keeps the law's claim the strong one, and costs a test per float on the way
out.

**The evidence (checked 2026-10-08, Arm64, .NET 10).** The window law failed in Release and passed in
Debug on the same tree, latent since Phase 324 (`fdea31d`), because the main suite ran in Debug only.
A probe over the law's 3,000 seeds found exactly two disagreeing cases, both `CumulSum` (seeds 97 and
394), and it was the PRODUCT that moved between builds, not the reference: the reference answered
`0x7FF8000000000000` in both builds; the scan answered the same in Debug and `0xFFF8000000000000` in
Release, at the rows where the running total, already a NaN from `-inf + +inf` (Arm64's default NaN,
sign clear), met a source `NaN` cell (`Double.NaN`, sign set). Adding two NaNs answers one operand's
payload, and the Release JIT commuted the addition. Every other NaN in those 3,000 cases agreed. The
go-red vectors — a `CumulSum` over `-inf`, `+inf`, then NaNs of both signs, through the frame path and
the row form; a `GroupBy` of seven aggregates over the same; a `Derive` of `-inf + +inf`, `0 * inf`,
and a sign-clear and a sign-set source NaN carried and added both ways round, through the compiled
frame path, the row form and the incremental refresh — are red on the old code in Release and in
Debug on this host, and green on the new code in both. The compiled-versus-row-form law now compares
a float answer by its bits as well as by its token.

**The audit of the strand for the same class** (a float written to the wire, hashed, or compared by
bits, where a NaN can arise):

1. *The wire* (`DataFrameCodec`, `ColumnOps`, the incremental state's wire form, the law vectors) —
   a NaN is the token `NaN` (the substrate's `nonFiniteToken`), and decodes to `Double.NaN`; its bits
   never reach a byte. Nothing to fix.
2. *Hashes and keys* (`cellToken`, `CellKey.hashCell`, `RowHash`'s float codes, the sort's order
   codes, `Delta`'s cell equality, the incremental state's digest) — every one maps every NaN to one
   value before it hashes or compares. Nothing to fix.
3. *Bit comparisons* — only the suite's laws compare floats by bits: the window law, the streamed-
   aggregate law, the `GroupBy` and `Pivot` stream laws, the incremental `GroupBy` and window laws,
   and (since this decision) the compiled-expression law. All of them read answers that now leave
   through the exits above. Fixed by this decision.
4. *Derived columns* — `Derive`'s arithmetic makes a NaN (`inf - inf`, `0 * inf`) and carries a source
   NaN. Nothing observed those bits before this decision, but a later content address or digest over
   a derived column would have, so the rule covers expressions too (the exits above), and the
   incremental refresh reaches them through the same `evalResolved` rather than a copy.

**Fable.** JavaScript has one observable NaN, so `canonicalNaN` is the identity there and the rule
holds by construction; the helper costs a comparison per float.

**The gate.** `verify.ps1 -Configuration Debug|Release`, Debug by default, builds the solution and
runs the whole main suite in the named configuration, following the substrate's shape; `ci.yml`
runs a Debug and a Release leg, and `publish-packages.yml` verifies in Release and packs `--no-build`
from that output, so the assemblies verified are the assemblies shipped. The clock leg runs Release
under either configuration, as the 2026-09-28 operator decision has it (a bound on the clock is a
claim about the code consumers run); under `-Configuration Release` it reuses the gate's own build.
That choice departs from the phase's literal wording (the clock leg "in the named configuration") and
was ruled on and accepted on 2026-10-08.

## 2026-10-06 — D15: the pipeline-query registry's lifecycle verbs land, each delegating to the substrate's on the declarations first; the bodies move only on success

**Decision.** `PipelineQueryRegistry` gains `unregister`, `replace`, `restrict` and `union`,
closing D12's deferral. Each calls the substrate's `QueryRegistry` verb of the same name on the
declarations; a refusal is returned as `QueryRefused`, and the bodies change only when the
declarations did. `replace` then holds the new pair to `PipelineQuery.check`, as `register`
does, so a disagreeing replacement is refused by name and leaves the registry as it was.

**Premises (checked 2026-10-06).** `FuaranCoreVersion` reads `0.35.1`, tagged on the substrate's
remote. The four verbs have in `v0.35.1` the signatures Phase 316 landed (`unregister`, `replace`
and `union` answer `Result<QueryRegistry, QueryError>`; `restrict` answers a `QueryRegistry`).
The registry is still the private pair of declarations and bodies. All three held.

**Held by the laws.** `PipelineQueryConformance` gains a lifecycle family: `unregister` undoes
`register` for a fresh id, `restrict` never widens, `union` refuses a shared id and is
associative, and after any drawn sequence of the verbs the ids of `enumerate` equal the ids of
`declarations`.

## 2026-10-06 — D14: the worker pool lands on a byte-mask frame, opt-in and reached only asynchronously; the masks cost the whole evaluator some collection under node, recorded for the operator

**Decision.** fuaran-core#376 completes D10's yes on the seam D13 landed. Figures in
[`benchmarks/results/2026-10-06-i7-9700-phase-376.md`](benchmarks/results/2026-10-06-i7-9700-phase-376.md).

**1. The frame's masks are bytes under Fable.** A validity mask and a `Bools` vector's values are a
`Mask`: `bool[]` on .NET, as they always were (every accessor is the plain index, so the code there is
the code it was), and `byte[]` — a `Uint8Array` — under Fable, read and written only through the
`Mask` module. A typed vector's carrier and its mask share one buffer. A worker's rebuild of a hand-off
now reads the frame's own arrays (0.02 to 0.04 ms at any size, where 375's copied every mask, 8.7 to
9.7 ms a million rows), and the hand-off carries a `Mask` where it carried bytes.

**2. Shared memory is the opt-in's, not the frame's default.** Until a host opts in, the frame
allocates as before; from `WorkerPool.optIn` on, on a realm with `SharedArrayBuffer`, its int and float
values and masks are allocated over shared memory, and a hand-off copies a vector allocated earlier
into shared memory once, at plan (2 to 4 ms a million rows). A structured clone hands a shared array
to a worker by reference.

**3. One entry point reaches the pool.** `DataFrame.evalToPreparedAsync` folds the planned pipeline
through the same moves as `evalToPrepared` (`foldHead`, lifted out of the fold) and offers each
`Filter` and `Derive` of at least `WorkerPool.PoolRows` rows to the pool; `evalToPrepared` and the
synchronous kernel pair never reach it. From the page's thread a caller awaits it (the pool never uses
`Atomics.wait`, which the page refuses); a dedicated worker hosting the evaluator awaits it the same
way and starts the pool from there.

**4. The pool.** The host hands `optIn` a way to start a worker (`MorselWorker`: post, listen, stop)
whose script calls `WorkerPool.serve`. The pool starts lazily on the first step it would take, uses a
worker from the step after it reports ready, and stays warm until `optOut`. One job at a time: the
caller posts the hand-off to the ready workers, then drains morsels beside them, every runner claiming
the next morsel from one shared counter; results land in each morsel's own slots and the caller
finishes in morsel order. A failed morsel is flagged and no runner claims past it; the caller re-runs
the first failed morsel for its error (375's rule). A worker that faults, or whose error event the
host forwards, is retired and the step answered by the sequential member. A second evaluation that
finds the pool busy runs its morsels on its own thread through the same loop.

**5. The floor is measured.** `PoolRows` is 32,768 rows: at 10,000 rows the pool was faster in 7 of 18
cells (node, Edge's page thread, Edge's dedicated worker; 2, 4, 8 threads) by at most 14 per cent and
more than twice as slow at eight threads on the page's thread; at 20,000 faster in 15 of 18; at 50,000
in all. At 100,000 rows and up the evaluator runs 3.4 to 5.2 times faster under node, 3.5 to 4.7 times
from a dedicated worker and 3.2 to 4.0 times from the page's thread.

**6. The laws, and the gate's node leg.** `PooledLaw` holds every pooled step byte-identical to
`Kernels.oneThread`, and `evalToPreparedAsync` to `evalToPrepared` over whole pipelines, over the
transform vectors and the corpus, with the caller draining and with the workers alone; a shifted
boundary and out-of-order concatenation go red; without isolation or without `SharedArrayBuffer` the
opt-in is refused and the answers are the sequential member's. The suite runs it on .NET over the
thread pool and, through Fable 5.0.0 (now pinned in the tool manifest) and `node`, over
`worker_threads`: the node leg is a case of the gate's own suite, and it fails rather than skips without
Fable or node.

**7. What the masks cost — for the operator.** The step-level sequential figures 375 recorded do not
regress on .NET (identical code; within noise) and under node improve or hold, except the `filter`
(+3.6 per cent a million rows, inside the sittings' spread; +6 per cent at 100,000, outside it). The
corpus harness under node reads 19 of 71 cases faster by 5 per cent or more, the prepared chain at
100,000 rows by 29 to 31 per cent; but the sheet's whole evaluator at 100,000 rows — `Table` in, two
steps, `Table` out — is 5 to 17 per cent slower (by method), consistently, though each of its phases
measured apart is faster. The cause is collection: a call allocates about 3.1 MB of typed-array backing
stores where it allocated 1.6 MB (the masks and the derived carriers were heap arrays), and the
engine collects for them; the same pipeline with its masks drawn from one reused buffer runs faster
than before. That is the price of masks a worker can read in place, which this phase exists to pay.
A ruling the operator may take: keep it (the recorded state), or look for a host-side buffer reuse
that would recover it (no reuse ships: a buffer outliving its vector retains memory, and the lifetime
is not the evaluator's to know). Nothing here is a correctness question.

**Ruled 2026-10-21: keep it.** The collection cost under node is the worker path's price, accepted
as recorded. No host-side buffer reuse is pursued: the lifetime it would need is not the evaluator's
to know, and the regression is confined to the whole-evaluator figure at one size on one host. A
consumer that measures it on a real workload reopens the question with that measurement.

## 2026-10-06 — D13: the worker path's first piece is the step as data, compiled by one path; the frame's byte masks come before the pool, because every worker would otherwise copy them

**Decision.** fuaran-core#375 builds D10's yes in order, and the first piece is the seam a worker
compiles from: `MorselHandOff`, a row-local step (`Filter` or `Derive`) as plain data. It carries the
step and its environment on the canonical wire (a one-step pipeline; each bound name as the `lit`
expression of its cell), the frame's schema as names and type tags, the vectors the step reads (int
and float values as they are, bool values and validity masks as bytes), the logical rows, the morsel
geometry and range, and the result slots: a `Filter`'s kept rows from each morsel's own offset and its
count, a `Derive`'s typed carrier and mask by physical row, and a failed flag per morsel. Strings,
numbers and arrays of numbers only, so a structured clone carries it and shared memory could back
every array in it. It is internal: no runner ships yet.

**1. One compile path.** A runner rebuilds the frame from the data and resolves and compiles the step
with the evaluator's own `resolveExpr` / `compileExpr`, then runs each morsel through the evaluator's
own row loops, `filterMorsel` and `deriveMorsel`. Those loops were lifted out of `evalFilter` and
`evalDerivedColumn`, which now run them too, so the sequential member and a runner cannot disagree on
what a row keeps, what it writes or which error it raises. `deriveMorsel` is inlined at both sites so
neither pays an indirect call a row for its writes.

**2. Errors do not cross.** A runner records only that a morsel failed. The caller re-runs the first
failed morsel, in morsel order, on its own thread through the same loop, and answers that error, so a
runner that ran the morsels in another order still answers the first error in row order, and no
`EvalError` is ever serialised.

**3. What is handed off.** A step whose wire form decodes back to itself (and whose environment does),
whose every read column is an int, float or bool vector, that is not a comparison-kernel filter (Phase
270's bitmap answers that one faster than any tree), and, for a `Derive`, whose root is typed int,
float or bool. Every other step runs as it always ran. A string, decimal or boxed column cannot be
shared, so it is never carried.

**4. The laws.** `MorselHandOffLaw` (Fable-clean) holds every handed-off step byte-identical to
`Kernels.oneThread`, refusals included, over the transform vectors at 1, 2, 3 and 8,192 rows a morsel
and over the corpus's row-local pipelines at 20,000 rows, under runners that run the morsels in order,
in reverse, and (in the suite) one a task on the thread pool. A runner whose morsels after the first
start a row late, and one that hands kept rows back in swapped morsel pairs, go red. Under node the
same law passes with every morsel's record and results sent through `structuredClone`, which is what
`postMessage` does to them (`benchmarks/results/2026-10-06-i7-9700-phase-375.md`).

**5. Why the pool waits for the masks.** On one thread a hand-off's morsels cost what the sequential
member costs, under node and on .NET. Its fixed costs are the copies: under node, at a million rows,
4.9 to 8.0 ms to plan and 8.7 to 9.7 ms for EACH runner to rebuild, the second because the frame's
masks are plain `bool[]` (JavaScript `Array`s) and a runner must turn the hand-off's bytes back into
them. Seven workers would copy about 60 ms between them, half the sequential `filter`. With the masks
as byte arrays in the frame (D10 item 4a; #375's third task) a rebuild copies nothing, so that change
goes first and the pool is built on it rather than paying a copy it would then have to remove.

**What did not land in fuaran-core#375, and why.** The frame's vectors and masks in shared memory under
Fable: masks become byte arrays across `Frame.fs` and every verb that reads them, a representation
change on one host whose laws run only under node, which no gate here runs. The asynchronous entry
point, the pool and runner, the opt-in detection and fallback, and the browser measurements: each
stands on shared vectors, and without them a pool would copy every mask into every worker (item 5).
The pooled-under-node comparison of the laws waits for the pool. Nothing public moved.

**What it bought already.** A `Filter`'s kept rows now land in one buffer, each morsel from its own
offset, where each morsel filled a growable list before: the sequential `filter` is 1.6 to 1.8 times
faster under node at 100,000 and 1,000,000 rows, and .NET and the `derive` are unchanged or faster.

## 2026-10-05 — D12: the pipeline-query registry's lifecycle verbs wait for a released substrate that carries them; the pin names `0.34.0`, which does not

**Decision.** `PipelineQueryRegistry` does not gain `unregister`, `replace`, `restrict` and `union`
yet. They are to delegate to the substrate's `QueryRegistry` verbs of the same names, and those
verbs are not in the substrate this repository pins.

**Evidence (checked 2026-10-05).** `FuaranCoreVersion` reads `0.34.0`. The substrate's `v0.34.0`
tag has no `unregister`, `replace`, `restrict` or `union` in `Fuaran.Core.Query`; they landed with
the substrate's Phase 316 on its `0.35.0` draft. That version is not tagged on the substrate's
remote, so the public registry does not serve it, and only a machine-local feed holds a `0.35.0`.
Raising the pin to it would restore here and nowhere else, which the pin discipline refuses.

**What stays true.** The registry's representation is still the private pair of declarations and
bodies, so the delegation design the phase describes is unchanged. The work is deferred, not
refuted: when `0.35.0` is released and the pin raised, the four verbs, their laws and the
`enumerate`-equals-`declarations` property go in as one additive change.

## 2026-10-05 — D11: the consumer sweep to the `Fuaran.Compute.*` ids stops where a consumer's move is a cohort or cycle decision, not a rename

**Recorded (fuaran-core#361), carrying D4's second stage.** D4 renamed the packages and left the
consumers to a sweep. On 2026-10-05 every consumer but two already names the new ids, at `0.37.0`,
which nuget.org serves under all four. The two still name an old id, and neither is a sweep's to move:

**1. A consumer behind the substrate floor moves with its substrate, and that can be a cohort act.**
The new ids begin at `0.36.0`, whose substrate floor is `0.33.0` (`0.37.0`'s is `0.34.0`). A consumer
pinned at `0.34.0` of the old id over a `0.32.0` substrate takes the rename only together with the
substrate raise and that raise's own source breaks. Done in isolation the raise was measured green in
the consumer itself, and red one hop further out: a repository that builds it by project reference, with
its own substrate pins further behind, no longer compiles, because a type it matches on (a policy
denial's payload) changed shape in the raise. So that consumer moves when the repositories built
against it raise their substrate together, which is a cohort decision for the operator; the verified
change is held on a branch for it, and no pin moved.

**2. A consumer that pins a version to MEASURE it moves by its own cycle decision.** A repository that
holds one substrate version fixed so its measurements can be repeated (and is deliberately outside the
version cohort) cannot take a compute id whose substrate floor is above its cycle pin without moving
that pin. Moving it starts a new measurement cycle, which that repository's own records reserve to a
deliberate decision; a sweep does not make it.

**3. What the producer side changes.** Nothing in the packages. The two facts a consumer needs before
it moves — the substrate floor rides with the rename, and an old id carried transitively by another
package survives a consumer's own pin move — are added to `0.36.0`'s "What a consumer changes" in
`STABILITY.md`.

## 2026-10-05 — D10: whether the browser host takes a worker path that needs cross-origin isolation is the operator's ruling; the operator ruled YES (2026-10-05): an opt-in worker path, fuaran-core#375

**Measured (fuaran-core#346), not yet decided.** Phase 346 opens with an operator ruling — whether
the browser host gets a Web Worker morsel runner over `SharedArrayBuffer`, which exists only on a
page served with `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy:
require-corp`, and if so whether that is opt-in per host (the sequential member when the page is not
isolated) or required. A dispatched session cannot take that ruling (the side's doctrine escalates it),
so this entry records what the ruling needs and stops. Nothing in the evaluator moved. The figures are
in [`benchmarks/results/2026-10-05-i7-9700-phase-346.md`](benchmarks/results/2026-10-05-i7-9700-phase-346.md),
with the harness beside them; native x64 (i7-9700), node v25.9.0 and Edge 154 only.

**1. What threads buy, at best.** The prototype runs the two morsel steps Layer 6 times (`a + b > 500`
and the sheet's two `Derive`s) as hand loops over typed arrays already in shared memory, on a warm
pool, the caller draining morsels beside the workers; every pooled answer was byte-identical to the
sequential one (72 of 72). At 10,000 rows nothing is bought. At 100,000 and 1,000,000 rows, eight
threads run the steps 2.8 to 6.3 times faster under node and 3.6 to 4.8 times faster in Edge when the
caller is a dedicated worker; with the caller on the page's thread Edge reaches only 2.5 to 3.1 times,
and eight threads are slower than four in three of its four large cells.

**2. The sequential member is the larger lever.** The shipped evaluator under Fable on node runs the
same two steps at 251 and 419 ms a million rows — 42 to 50 times the prototype's single-threaded loop
over the same arrays (5.98 and 8.31 ms), and 39 to 44 times the .NET evaluator. Eight workers could
divide that by at most eight; the per-row machinery of the compiled tree under Fable (closures, the
error and null slots, the kept-row list, the boxed root) is a factor of forty, with no deployment
constraint attached to removing any of it.

**3. The costs a worker path adds.** A pool of the prototype's loops starts in 11 to 42 ms; a worker
that compiles a step from data must import the evaluator, which takes 144 ms for one worker and 284 ms
for seven under node. If the frame's vectors are not already in shared memory, copying a million rows
of inputs in costs 0.8 to 3.0 ms and a `Derive`'s outputs out 2.3 to 3.4 ms — 60 per cent of the pooled
`filter` and three times the pooled `derive`. Reading through shared buffers costs the sequential step
between 1 per cent less and 7 per cent more.

**4. The shard's premises, checked.** (a) Under Fable `int[]` and `float[]` are typed arrays, but every
validity mask and `Bools` vector is a plain `Array`, and `Strs` / `Decs` / `Cells` hold strings and
objects: only int and float VALUES could be shared today. (b) The runner cannot be "the JavaScript member
of the kernel pair": `RunMorsels`'s body is a closure over the frame, the compiled tree and the caller's
result arrays, which cannot cross into a worker, and the pair is synchronous, while the page's thread
refuses `Atomics.wait` (`TypeError`, measured) — so a synchronous member could wait only with the whole
evaluator running in a dedicated worker, and on the page's thread only an asynchronous evaluation entry
point could. (c) Without the two headers `SharedArrayBuffer` is undefined, so the opt-in shape's
fallback is exactly today's sequential member.

**5. What a yes touches.** Not `Kernels.fs` alone: a step-as-data seam the worker compiles from (the
pipeline is already data; the frame is not), frame vectors allocated in shared memory under Fable with
masks as byte arrays, the result hand-back, and either an evaluator hosted in a dedicated worker (the
host's job, in `fuaran-live` and every consumer that embeds the engine) or an asynchronous evaluation
entry point on the JavaScript host. Opt-in per host is the only shape that keeps a non-isolated page
working, because such a page has no shared memory at all. Every consumer that opts in takes COOP/COEP,
which constrains the cross-origin content it can embed. The laws are cheap: row-local morsels writing
disjoint rows concatenated in morsel order are byte-identical by construction, and were measured so.

**6. What a no keeps.** The browser host runs the sequential member on one thread, no page needs
isolation headers, and the measured order of levers stands: the Fable per-row cost (item 2) before
threads.

**The ruling (2026-10-05): YES.** The operator ruled to build the worker interface. The shape is opt-in per
host, the only one item 5 found that keeps a non-isolated page working: a host that does not opt in, or a page
without COOP/COEP, keeps the sequential member byte for byte. The runner is the successor phase
fuaran-core#375, sized by item 5: the step as data, frame vectors in shared memory, and an asynchronous
evaluation entry point. Item 2's per-row lever under Fable stands beside it and is not displaced by it.

## 2026-10-04 — D9: whether float `Sum` becomes exactly rounded is the operator's ruling; the operator ruled NO (2026-10-05): the fold stays the default

**Measured (fuaran-core#345); ruled NO on 2026-10-05 (below).** Phase 345 opens with an operator ruling — whether the
float `Sum`, and through it `Mean`, `StdDev`, `CumulSum`, `RollingSum` and `RollingMean`, moves from
the left-to-right fold (D3; `Kernels.fs` header) to an exactly-rounded sum, whose answer is the double
nearest the true sum and so cannot depend on the order or the partitioning of the additions. A
dispatched session cannot take that ruling (the side's doctrine escalates it), so this entry records
what the ruling needs and stops. Nothing in the evaluator moved. The figures are in
[`benchmarks/results/2026-10-04-i7-9700-phase-345.md`](benchmarks/results/2026-10-04-i7-9700-phase-345.md),
with the harness beside them; native x64 (i7-9700) and node only — the Arm64 leg was not run.

**1. The cost, single-threaded.** The fold costs 1.3 ns a value on both hosts. CPython's `math.fsum`
algorithm (Shewchuk's partials, correctly rounded collapse) costs 3.4 to 3.9 times the fold where
every partial sum is exact (whole quarters — the benchmark corpus's shape), 5.5 to 8 times at
ordinary dynamic range (uniform in `[0, 1)`) and 25 to 34 times at wide range (mixed signs across
sixteen decades), on .NET and node alike; the multiple is set by the live partial count (1, 2, 7 to
9), which the input's spread of magnitudes decides, not `n`. Against D7's whole streamed `GroupBy`
(3.94 ms, a million rows, ten keys) that is 7 ms more at ordinary range (a 2.8x slower step) and 42 ms
at wide range. Split into eight ranges on the pool it is 1.9x the fold at a million ordinary values,
below the fold where the inputs are exact, 8.5x at wide range.

**2. The invariance and the cross-host bytes hold.** On every input the exact sum of five shuffles,
of 3 / 5 / 8 / 64 / 1,000-way partitions (each range's partials fed through the same algorithm) and of
the eight-thread run has the sequential exact sum's bit pattern (45 checks); the shuffled fold differs
from the in-order fold on every non-exact input. `.NET`, node and CPython's own `math.fsum` return the
same 64 bits on all nine inputs. The parallel member the shard asks for is therefore sound by
construction: partials are a range's exact sum, and merging partial lists is the same algorithm.

**3. No transform law vector changes bytes.** The corpus's three float summations (`mean` over `w` in
`transform-3` and `transform-11`) are over one or two exactly-summable members; the `sum`s are int or
decimal. The fold and the exact sum agree on every vector, so the corpus cannot distinguish them — a
yes needs new vectors with inexact members (three or more `0.1`-class values) before any host is held
to the new sum.

**4. The twins, corrected from the shard.** Per D6, `fuaran-ts` is the only host running the
transform laws, and it still lacks the decimal strand (fuaran#2001) that blocks the corpus copy today;
`fuaran-rs` and `fuaran-go` carry evaluator twins with no laws leg; `fuaran-py` has no twin (and has
`math.fsum`). The algorithm is ~40 lines per language (`bench.mjs` is the TypeScript body).

**5. What a yes touches — wider than this repository.** The one aggregate semantics is
`Column.aggregate` in `fuaran-core` (`src/Fuaran.Core.Column/Column.fs`, `Sum` / `Mean` / `StdDev`
over `total` and `kept`), consumed here as a package: the exact sum lands THERE first, under a Core
cut, before this evaluator's streamed `MSumFloat` / `MMean` slots, the window's `CumulSum` seed and
running total and the rolling sums (`DataFrame.fs`) and the incremental seam's rescan rule
(`Incremental.fs`: "a float `Sum` is never maintained") can follow. Three consequences worth the
operator's eye: a `CumulSum` continued from a prior run must seed from the prior PARTIALS, not the
rounded float, or the continuation's bits differ from a fresh run's; Phase 306's finite-input overflow
refusal meets the algorithm's own intermediate overflow (CPython raises there); and an exactly-rounded
`Sum` becomes MAINTAINABLE under a delta (adding a member and adding a removed member's negation are
both exact over the partials), which the seam today rescans.

**6. What a no keeps.** D3's order, every byte today's vectors pin, the fold's cost, and the rule in
the `Kernels.fs` header that no float reduction is ever split — with D8's grouping-by-key and this
phase's eight-range figures as the measured price of that rule.

**The ruling — NO, taken by the operator on 2026-10-05.** The float `Sum` and every aggregate above
stay on D3's left-to-right fold; item 6 is what is kept, and no float reduction is split. The cost in
item 1 (a 2.8x slower `GroupBy` step at ordinary range, more at wide range) and the breaking change
in every non-exact answer are not paid for a property nothing today needs. An OPT-IN exactly-rounded
sum (a distinct aggregate beside `Sum`, landing in `Column.aggregate` first per item 5) is left to a
later Suggest-features candidate, not filed here. This entry and the figures are the phase; no
`STABILITY.md` entry is owed.

## 2026-10-04 — D8: the sort, the window and (since fuaran-core#371) the top-n run across threads; the grouping by key and the ranged join probe were measured and not kept

**Decided (fuaran-core#344), on measurement.** Native x64 only (i7-9700, 8 cores); the figures are
in [`benchmarks/results/2026-10-04-i7-9700-phase-344.md`](benchmarks/results/2026-10-04-i7-9700-phase-344.md).
The Arm64 leg was not run.

**1. Kept: three kernels in the pair.** `Partitions` (how many ranges a verb runs: one on the
portable member, one below `PartitionRows` = 32,768 on the native), `SortFinite` and `SortPositions`
(a merge sort, runs sorted on the pool and merged pairwise). The sort keys are distinct under a
total order — the packed keys end in the position, and the unpacked comparison ends in it — so any
correct sort answers the same array, and the portable member's sorts are the engine's own. Through
them: the sort, 2.0 times at 100,000 rows and 2.2 at a million, 4.7 where the keys do not pack; the
window's ordering through the sorts and its partitions scanned in chunks of whole partitions
across threads (1.6 to 3.9 times). No fold is ever split: a window partition's running total is one
sequential scan, so no float byte moves.

**2. Not kept: the grouping by key across threads.** D7 item 5 pointed here: partition by group so
that each thread owns whole groups and no table is merged. Built in the evaluator and measured, it
is 1.59 times faster at ten keys and a million rows and 1.16 at one key per ten rows, but 1.46 and
2.09 times SLOWER at 100,000 rows and slower still at 10,000, and it allocates 2.3 to 2.8 times as
much. Run on one thread it costs 22 ns a row against the sequential grouping's 10: each partition
reads its rows scattered across the whole vector, and the hashing and placing passes are work the
sequential path never does. A grouping that pays only above some size between 100,000 and a million
rows needs that crossover measured before a threshold can be set, so it is not shipped on a guess;
the prototype stays in the harness (`Partitioned.GroupByKey`) for whoever measures it.

**3. Held for the operator: the top-n a range of rows at a time.** Each range's heap on its own
thread, the candidates sorted under the same total order, answers the sequential heap's bytes and
is 2.8 to 4.4 times faster at 100,000 and a million rows. It is not shipped because the verify
gate's corpus tick case (`ScalingTests`, Phase 283) holds the incremental tick to 1.6 times the
full evaluation it replaces, and the full evaluation is what it speeds up: `filter > sort > limit`
at 100,000 rows read 1.75, 1.74 and 2.04 on the three attempts, against a sequential tick (diff and
refresh) of 14 to 16 ms. That case's own text makes a reading above the bound an operator decision
and forbids raising the bound to pass it, so the kernel waits on that decision: either the bound
is restated against the sequential evaluation (what the incremental seam replaces on every host),
or the tick gains threads of its own. The sort and the window, which the same case also times,
stayed within the bound (1.27 and 0.97 at 100,000 rows).

**Item 3 closed (fuaran-core#371) — operator ruling 2026-10-04, option A: the bound is restated
against the evaluation at one thread, it stays 1.6, and the parallel top-n ships.** What the tick
bound protects is that maintaining a result incrementally is not dearer than recomputing it on the
same footing; once the full evaluation ran on every core, the ratio measured the core count, not
the seam. Both tick clock cases (`ScalingTests`, Phases 274 and
283) now time the full evaluation on `oneThread` — the native member's vector kernels, its morsels
run in order on the caller's thread, never partitioned — and hold it equal to the reference answer
before timing it; the tick arm, `tickBound` (1.6) and `tickFloorMs` are unchanged. The ranged heap
from `fb25f9a` is back in the evaluator as it was, and the cross-member law that holds it ("over
frames thousands of rows long, ...") was checked to go red with one range's heap dropped. Figures
(Release, i7-9700, `benchmarks/results/2026-10-04-i7-9700-phase-371.md`): the corpus case's
`filter > sort > limit` cell at 100,000 rows read 1.54, 1.50 and 1.33 quiet and 1.31 under four
CPU burners against the parallel evaluation, and 0.63, 0.67 and 0.67 quiet and 0.66 loaded against
the one-thread evaluation; the worst cell of either case after the change was 1.36 (`inner join` at
1,000 rows, loaded). The top-n re-measured: 2.96 and 2.67 times at 100,000 rows, 4.33 and 3.78 at a
million (ten keys and one key per ten rows).

**4. Not kept: the join probe over ranges of left rows.** No consistent gain at either size (one
sitting, the machine under load): the probe is a small part of the join, whose cost is emitting
the matched pairs and gathering every output column.

**5. The shard's premise, checked: "boxing no longer dominates the grouping" held only at low
cardinality.** At one key per ten rows the grouping spent most of its time boxing the KEY cells
(a `Cell[]` per group, packed back into a vector): 20 of 32 ms at a million rows. The key column is
now read from each group's first row straight into the output vector (`DataFrame.keyColumn`), on
both members: 14.6 ms at a million rows, 1.23 ms at 100,000 (from 2.21). The window's partitioning
paid the same boxing for nothing and no longer does.

**Supporting change.** `windowOrder`, `windowOrderOf`, `windowColumnOf` and `windowColumnOver` keep
their signatures and run the portable member (the incremental seam's callers); the evaluator calls
their `...With` forms with the host kernels.

## 2026-10-03 — D7: the host-kernel verdicts are taken on a native JIT; Phase 270's are restated on Arm64 and x64, and two of them move

**Decided (fuaran-core#341), on measurement.** Phase 270 shipped the host kernels and removed an
order-preserving parallel `GroupBy`. Every timing behind it came from the x64 build running under
emulation on an Arm64 machine. Emulation penalises the intrinsics and the thread scheduling that were
being judged. The benchmark harness now refuses a run whose process architecture is not the
machine's (`Program.main`), and Layer 6 has committed suites (`benchmarks/.../Layer6.fs`). The Arm64
figures are in [`benchmarks/results/2026-10-03-snapdragon-x1e80100-phase-341.md`](benchmarks/results/2026-10-03-snapdragon-x1e80100-phase-341.md).
The x64 figures, taken natively on a 4-core i7-8650U with the machine quiet, are in
[`benchmarks/results/2026-10-03-i7-8650u-phase-341.md`](benchmarks/results/2026-10-03-i7-8650u-phase-341.md).
Each item below gives both. x64 agrees on every decision. One Arm64 reading does not reproduce: at
100,000 rows the morsel `Derive` favours larger morsels on x64 (item 4).

**1. Held: the comparison and bitmap kernels.** 270: 1M rows 37.84 to 1.447 ms (one comparison) and
34.49 to 1.901 ms (two under `And`). Native Arm64, portable over native: `cmp` 4.36 to 0.82 ms (5.3x),
`cmp-and` 6.69 to 1.22 ms (5.5x), and 4.2x to 5.1x at 10,000 and 100,000 rows. The ratio is smaller
because the portable loop is no longer emulated, not because the kernel is weaker. **x64 agrees:**
`cmp` 6.56 to 1.24 ms (5.3x), `cmp-and` 11.0 to 2.30 ms (4.8x) at 1M rows, and 3.4x to 5.1x at
10,000 and 100,000 rows.

**2. Held: the morsel-parallel compiled `Filter`.** 270: 1M rows 55.2 to 10.3 ms. Native: 15.2 to
5.29 ms (2.9x), with 2.9x at 100,000 rows and none at 10,000 (two morsels). **x64 agrees, with a
smaller gain:** 25.0 to 9.82 ms (2.5x) at 1M rows, 1.4x at 100,000 and none at 10,000, on four
physical cores against twelve.

**3. Overturned: the morsel `Derive`.** 270 found it at parity at 1M rows (36.9 to 38.2 ms, "within
noise"). Native: 22.5 to 7.37 ms (3.1x), and 2.4x at 100,000 rows. On a quiet machine it is 2.7x at
both sizes (21.1 to 7.93 ms, 1.94 to 0.717 ms). The morsel runner pays for both row-local verbs.
**x64 agrees:** 34.9 to 18.3 ms (1.9x) at 1M rows and 1.4x at 100,000 (a second sitting: 2.2x and
1.5x), so the verdict is overturned on both architectures.

**4. Held at 100,000 rows, qualified below, unsettled above: `MorselRows` = 8,192.** Swept twice,
once with the machine busy and once quiet, all four sizes per sitting. Both sittings agree on two
points:

- at 10,000 rows 4,096 leads (quiet: compiled `Filter` 0.078 against 0.123 ms, `Derive` 0.112
  against 0.167 ms), because it makes three morsels where 8,192 makes two;
- at 100,000 rows 8,192 is best or tied, and 4,096 loses 9% and 24%.

At 1,000,000 rows the compiled `Filter` is within 6% at every size. The `Derive` favoured 8,192 in the
busy sitting and 16,384 by about 20% in the quiet one, so nothing is decided there. No single constant
wins at every size. The constant does not move in this phase. A morsel size that depends on the row
count (finer for small inputs) is open to the phases that build on this record (343, 344).

**x64 (one quiet sitting of all four sizes, and a second of 8,192 and 32,768):**

- at 10,000 rows it agrees: 4,096 leads (compiled `Filter` 0.136 against 0.223 ms, `Derive` 0.236
  against 0.296 ms);
- at 100,000 rows the compiled `Filter` is tied (within 9% at every size), but **the `Derive` does
  not reproduce "8,192 best or tied"**: 32,768 beats it by 21% to 27% in both sittings (1.80 and
  1.79 against 2.46 and 2.25 ms). That is four morsels, one per physical core, against thirteen. On
  this machine 4,096 loses 15%, as it loses on Arm64;
- at 1,000,000 rows the compiled `Filter` favours 8,192 by about 10% in both sittings. The `Derive`
  read 32,768 ahead by 20% in one sitting and tied in the other (15.2 against 15.4 ms), so it stays
  unsettled on both architectures. Larger-than-8,192 morsels led the `Derive` in Arm64's quiet sitting
  and in one of x64's.

The decision holds and the x64 data strengthens it: no fixed constant wins everywhere. The best
morsel size depends on the core count as well as the row count, which is one more input for the
phases that may make it adaptive.

**5. Overturned in its reason, held in its decision: the parallel `GroupBy`.** 270: parity at 10 keys
(14.1 against 13.8 ms at 100k, 388 against 408 ms at 1M), and 2.3x and 1.8x slower at one key per
ten rows. Its stated cause was that "a grouping over this evaluator is dominated by boxing the
aggregated column". Phase 323 removed that boxing. Rebuilt from 270's description over the streamed
aggregates and measured natively, the per-morsel-tables-merged-on-first-seen shape is:

- **1.55x faster** at 10 keys and 1M rows (quiet: 3.94 against 6.10 ms; busy: 3.85 against 6.15);
- at parity at 100,000 rows (1.06x slower quiet);
- **4.0x and 7.2x slower** at one key per ten rows (quiet: 92.7 against 23.0 ms at 1M, 8.63 against
  1.20 ms at 100k; the busy sitting read 3.0x and 6.4x, because load slowed the sequential arm),
  allocating 177 MB against 20 MB.

**x64 agrees** on four physical cores: **1.63x faster** at 10 keys and 1M rows (7.86 against
12.8 ms); 1.38x slower at 10 keys and 100,000 rows, where Arm64 was at parity; and **3.7x and 6.2x
slower** at one key per ten rows (140.0 against 37.7 ms at 1M, 23.0 against 3.68 ms at 100k), with
the same allocation.

So threads now buy something, but this shape spends it on merging tables that hold nearly every row.
It also reassociates a float `Sum`, which moves wire bytes. It stays unshipped. What carries forward
to Phase 344 is the shape the numbers point to: partition by group, so each thread owns whole groups,
folds them in row order, and no per-morsel table is ever merged.

**6. Held: vectorised integer reductions are not built.** It was never a timing. Every aggregate
still folds scattered physical rows into per-slot accumulators (`GroupAgg.Stream.FeedAll`), so no step
reduces a contiguous typed range. The architecture does not bear on it.

**Supporting change.** The benchmark assembly is the third that may see the dataframe package's
internals (`InternalsVisibleTo`), so the harness can run both members of the kernel pair through
`evalPreparedCountedWith` / `evalStepWith`. It is never packed, and no public surface moves.

## 2026-10-03 — D6: the corpus copy of the transform laws is held at its published stamp until it lands with the first host that reads the new vectors

**Decided (fuaran-core#356), measured rather than assumed.** The shared wire-format corpus carries a
declared copy of `conformance/laws/transform-laws.json` (D1 (6)). That copy is stale: `kitVersion`
0.35.0, 16 `evalPipeline` vectors, in the pre-rename names. The copy is NOT re-published on its own.
It lands in the same change-set as the first host that reads the new vectors, which is fuaran#2001
(the host twins learn the exact decimal and the rounding vocabulary). Until then the record in
[`copies.json`](copies.json) carries a `lag` naming that phase, so the workspace copy registry
reports a lag with an owner rather than an unowned stale copy.

**1. What was measured (2026-10-03).** Emitted from a detached checkout of the `v0.37.0` tag, the
file is byte-identical to the one committed at that tag: 60 vectors, of which 47 are `evalPipeline`
(16 drawn shapes, the decimal shapes from iteration 16, the derive-typing shapes from D5), 9 are
`columnOp` and 4 are `delta`. The released 0.37.0 file and this repository's 0.38.0 draft differ in
the `kitVersion` stamp alone. Run against that file, the one host that certifies against the family
(fuaran-ts, `packages/ops/test/transformLaws.test.ts`) passed 15 vectors, failed 21 and could not
decode the pipeline of 26:

- it has no `decimal` column type, so every decimal source is refused at decode;
- it has no `quotient`/`rounded` expressions and no `intersect`, `except` or `countDistinct`, so
  those pipelines do not decode;
- it has no `ColumnOp` or delta codec, so the `columnOp` and `delta` cases cannot be run at all;
- two vectors that predate the decimal (`transform-5-div-by-zero`, `transform-13-div-by-zero`) now
  expect D5's typing of an all-null derived column, a numeric column of nulls rather than a string
  one, and the host still produces the old answer.

So the work the host needs is the decimal strand itself (D72 of the substrate, carried here by
Phases 276, 277, 321 and 338), not two new case kinds. fuaran#2001 already owns that port for all
four language hosts, and its own first task is this re-emit.

**2. Why the copy is not published ahead of the host.** Publishing the copy alone turns a public
host's gate red for a change that host did not make, 21 failures at once. Marking the vectors the
host cannot evaluate as skipped would make that gate green while certifying none of them. Neither
is acceptable, so the copy waits for the host.

**3. Who reads the family today.** fuaran-ts only. fuaran-go, fuaran-py and fuaran-rs each carry an
evaluator twin but no transform-laws leg (fuaran#2001 adds one to each). The .NET host evaluates
with this repository's packages, so it has no twin to certify. The bundled corpus snapshots in
fuaran-ts and fuaran-py do not carry `laws/`, so a laws-only corpus commit changes neither payload.

**4. Not decided here: release stamp or draft stamp.** fuaran-core#356 asked for the copy to be
emitted at a released version, never an untagged draft, and the measurement in (1) was taken that
way. D1 (6) and `version-derives.json` say a version cut re-emits the copy with the stamp, and the
copy registry compares the copy with this repository's committed file. Those two rules disagree
whenever a draft is ahead of the last release, as `0.38.0` is ahead of `v0.37.0` today. Then a copy
emitted at the release differs from `conformance/` in the stamp alone. This suite's freshness leg
reports that as `STAMP ONLY`, and the registry reports it as stale. The vectors are identical either
way. Which stamp the published copy follows is left to fuaran#2001's landing, and is recorded here
so that the choice is made deliberately.

**4 decided (2026-10-03, operator ruling): the published copy follows the RELEASE stamp.** The shared
corpus is public, and the hosts that certify against it should certify against a contract a consumer
can actually restore; an untagged draft's stamp names a version nobody can obtain. So:

- the copy is re-emitted when a version is **released** (tagged and published), not when a draft is
  cut, and carries that release's `kitVersion`;
- a copy whose stamp is this repository's **latest release** and whose vectors match is **fresh**, even
  while a draft is ahead of that release; the copy registry and this suite's freshness leg are to read
  it that way, rather than comparing with the committed draft file;
- the declared derivation in `version-derives.json` moves from the cut to the release accordingly, once
  the workspace copy registry and version tooling support it.

Until that support ships, a release-stamped copy still reads `STAMP ONLY` here and stale in the
registry; the `lag` in `copies.json` covers the present gap, owned by fuaran#2001.
The lag is kept deliberately (operator, 2026-10-03): it turns an unowned stale record into an honest one
with an owner.

**4 adopted (2026-10-08, Phase 715).** The record in [`copies.json`](copies.json) and the derivation in
[`version-derives.json`](version-derives.json) both declare `"against": "release"`, and the record names
`"stamp": "kitVersion"`. The workspace copy registry now compares the corpus copy with this repository's
file at its newest release tag, reads a committed draft that is ahead of that release in the stamp alone
as information rather than drift, and still reports any other difference as stale; the copy obligation
is raised when a version is released rather than when a draft is cut. A cut still re-emits
`conformance/laws/transform-laws.json` here, because this suite holds that file to what the kit renders
at the declared `<Version>`. The `lag` is unchanged and still owned by fuaran#2001: it says when the copy
moves, while the grading says what the copy is compared with.

**Amendment (Phase 573): the lag is removed.** The corpus copy of `conformance/laws/transform-laws.json` is now identical to this repository's file, so the `lag` block in [`copies.json`](copies.json) held nothing and the registry reported it as removable. The record keeps `against: release` and `stamp: kitVersion` (4, as decided), so it is graded against the released source with no stale exemption. The copy no longer waits for a host, and the hold in the opening paragraph no longer applies.

## 2026-10-02 — D5: a derived column is typed by its expression; the cells decide only where the typer cannot; a float beside a decimal is refused

**Decided (Phase 338, carrying D3's last paragraph).** One rule types every column the dataframe
strand derives — a `Derive`'s, and an `Unpivot`'s `value` — and every reader takes it from one
place (`DataFrame.derivedTyping`, `unpivotTyping`, `columnTypeBy`): the evaluator, the incremental
seam's walk and its chunked path, `SchemaWalk`, the planner and `PipelineQuery.check`. So refresh
and full, the walk and the evaluator, cannot disagree about a type by construction.

**1. The rule.** Over the step's schema, before any row:

- the expression's own arms (through `Case` and `Coalesce`, the nodes whose answer IS an operand's)
  carry a decided `float` and a decided `decimal` → **refused**, on every frame, by name;
- the typer decides it (`Of ty`) → `ty`, on every frame — an empty one, an all-null one;
- the typer knows it produces no present value (`Absent`) → `string`, the type an all-null
  column has always had;
- the typer cannot decide (`Unknown`) → **the cells decide**: Phase 321's widening join of the
  present cells' types (D3 rule 2), `string` where none is present, and a refusal over the whole
  column where the cells hold a `float` beside a `decimal`.

An unpivot reads the same rule with its value columns as the arms, joined by the widening join of
their DECLARED types: no totality verdict reads an unpivot's type as an exactness claim, so the
schema decides an `int`/`float` or `int`/`decimal` melt, and only a pair no widening relates (a
`string` beside an `int`) is left to the cells.

**2. A cell is admitted, never converted.** Under a decided type every present cell is of that type
or widens into it by `ColumnType.widens` (an `Int` in a `float` or `decimal` column); it is stored as
produced, as D2 stores an edited cell. A present cell the decided type does not admit would be the
typer and the evaluator disagreeing: `DeriveTypingConformance.laws` goes red on it (its go-red is in
the suite), and the evaluator keeps the cell as produced rather than coerce it.

**3. The one data-decided remainder.** A `Param` (its cell is the arguments'), a `Now` (the clock
witness's), a column the schema in hand does not carry (an open schema's), and a join the EXACT
typer keeps apart — a `Case` of an `int` and a `float`, a `Coalesce` of a `decimal` and an `int`.
The last is wider than the phase's shard assumed: it was written expecting the typer to join `int`
and `float` at `float`, which D3 had declined (the join is an exactness claim the totality verdict
reads). The rule here does not widen the typer either; those shapes stay the cells', named as such
in `PipelineQuery`'s `TypeUndecidable`, and the planner declines to move a filter past them.

**4. The verdict follows.** `Plan.isTotal` calls a derive total only where its expression is total
AND its column is decided: a cells-typed derive can refuse a float beside a decimal over the whole
column, and a statically refused one always does, so neither may be called total. That is also what
makes a decided derive of ANY type safe for `FilterBeforeDerive` (its type reads no row), so the
planner's string-only clause is gone. `proofs/Pipeline.fst` models the rule clause for clause —
the typer is now extracted, because `eval_derive` reads it — and `verdict_sound`, `step_total`,
`derive_then_filter` (now over a decided type) and the new `derive_type_rows_free` (a decided
derive's column has one type over any two frames of a schema) verify.

**Declined: converting a widened cell.** Writing an `Int` into a float column as `Float` would
make the column's carrier uniform, and would make the type change a VALUE change every host must
mirror; storing as produced keeps the vectors' cells byte-identical and matches the substrate's own
admission.

## 2026-10-02 — D4: the packages take their own ids — `Fuaran.Compute.*` and the `Fuaran.Compute` namespace — opening at `0.36.0`; the `Fuaran.Core.*` ids stop at `0.34.0`

**Decided (Phase 322, superseding D1's first ruling).** D1 kept the ids and namespaces the strand
shipped with, on the argument that a rename is a source change in every consumer for no gain. The
gain turned out to be real and the cost of not paying it recurring: a reader of `Fuaran.Core.DataFrame`
is told by its name that the dataframe is a property of the substrate, which the substrate's own
boundary (its D51: the compute layer is the witness-free strand, not a Core property) says it is not,
and every review of the split had to explain the mismatch again. A pin is a bad reason to keep a name
that says the wrong thing. The name follows the producer.

**1. The mapping.**

| Through `0.34.0` | From `0.36.0` |
|---|---|
| `Fuaran.Core.DataFrame` | `Fuaran.Compute.DataFrame` |
| `Fuaran.Core.Column.Ops` | `Fuaran.Compute.ColumnOps` |
| `Fuaran.Core.DataFrame.Conformance` | `Fuaran.Compute.Conformance` |
| `Fuaran.Core.DataFrame.PipelineQuery` (Phase 281; never published) | `Fuaran.Compute.PipelineQuery` |

The fourth row is the package Phase 281 added after D1; it is renamed with the others, so its first
publication is under the new id. The C# facade (`Fuaran.Core.DataFrame.CSharp`) has no row: it was
deleted outright beside the substrate raise to `0.33.0` (the `0.36.0` STABILITY entry on its removal),
so there is no `Fuaran.Compute.CSharp`.

**2. One namespace, `Fuaran.Compute`; the module names inside are unchanged.** Every source file
declares `namespace Fuaran.Compute` and opens `Fuaran.Core` for the substrate types it is built over.
`DataFrame`, `ColumnOps`, `Transform`, `Incremental`, `Delta`, `Plan`, `DataFrameConformance` and the
rest keep their names, so a consumer's change is its package references and one `open` per file.
The pre-split forwarding module moves with its namespace: `Conformance.<family>` still resolves for a
file opening both namespaces, the fully qualified `Fuaran.Core.Conformance.<family>` does not.

**3. The new ids open at `0.36.0`, the `0.35.0` draft ADVANCED rather than reclassed.** `0.35.0` was
untagged and unpinned, so it could have been reclassed in place; but the draft rule advances a slot
that takes a change of a higher class than it carries, and the first version under new ids should be
a fresh contract rather than a number whose entry already described additions to the old ones. The
`0.35.0` entry's content is folded into the `0.36.0` entry, which says `0.35.0` was never released.

**4. The old ids stop at `0.34.0`, with no deprecation package.** They are not republished; nuget.org
keeps every published version restorable, which is all a consumer that has not moved needs.

**5. What does NOT change.** The repository (`fuaran-core-compute`), its organisation, the
solution and the unshipped test, benchmark and proof-oracle projects keep the repository's name
(`Fuaran.Core.Compute.*`) — the repository is not renamed and none of those projects is a package —
and so does the claims ladder's subject. The wire format names no CLR type, so no canonical byte moves:
each `api/wire/` baseline's body is byte-identical to its predecessor's.

**6. How the move is classed.** The `Public surface` and `Wire surface` families compare each
package with its baseline at the newest tag; a tag cut before the rename holds the baseline under the
old id, so both read through a declared predecessor map (`PublicSurfaceTests.predecessorIds`) rather
than reporting a first snapshot. That is what makes the rename read as the `removal` it is, and what
keeps the decimal's wire `breaking` since `v0.34.0` stated across the rename instead of reset.

**7. The registration is an operator act.** nuget.org Trusted Publishing policies are scoped to
package ids, so the policy for this repository must be extended to the `Fuaran.Compute.*` ids before
the first `v*` tag under them; a failed `NuGet/login` on that tag is the missing registration, not an
authentication fault (the publish workflow's header says so where the failure is met).

## 2026-10-02 — D3: one float order, the substrate's; a derived column's type is the join its cells widen into, and the static typer stays exact

**Decided (Phase 321).** Three rulings about how the evaluator types and orders what it computes,
taken together because each is the decimal's arrival exposing an older looseness.

**1. The evaluator orders floats as the substrate does: `NaN` one value ABOVE every other.** The
evaluator compared floats with the host's `compare`, which puts `NaN` below every value on .NET and,
under Fable, answers `1` for both `compare nan 1.0` and `compare 1.0 nan` — an order on one host and
not an order on the other. The substrate's `Cell.compare` (its Phase 315) puts `NaN` last, and
`Column.aggregate`'s `Min` / `Max` already read that order, so a descending sort and a `Max` named
two different largest values. `Kernels.compareFloat` now states the order once; the sort kernel,
the compiled comparison arm and both comparison kernels (the portable loop and the vector one, whose
`NaN` lanes moved from `<` / `<=` to `>` / `>=`) read it. A float filter `x > k` now keeps a `NaN`
row and `x < k` drops it; a sort puts `NaN` after `+Inf` and before the nulls. Recorded as breaking.

**2. A derived column's type is the join of its present cells' types under `ColumnType.widens`.**
It was the type of the first present cell, so a `Case` answering `Int 1` on one row and `Float 2.5`
on the next built an int column holding a float — a table the substrate's `Table.validate` refuses,
and whose `Sum` its Phase 299 turns into an error. Now `Int` and `Float` join at `Float`, `Int` and
`Decimal` at `Decimal` (the common money shape: `Coalesce(amount, Lit(Int 0))`). A pair no widening
relates keeps the EARLIER type, which is the pre-321 answer for that pair, `Float` beside `Decimal`
included. **Declined: refusing `Float ⊔ Decimal` by name at a derive.** It would add a whole-column
refusal to `Derive`, which the planner's totality verdict (proved sound over the evaluator in
`proofs/Pipeline.fst`) does not model: a derive the verdict calls total could then fail, and the
reorder that verdict licenses could change which error a pipeline reports. That is a change to the
verdict and its proof, and it is left to a phase of its own rather than half-made here. The model
moved with the rule: `Pipeline.fst`'s `infer_type` is the same fold, re-verified and re-extracted.

**3. The static typer's join stays EXACT.** The phase that brought rule 2 also asked for
`Typing.join` to widen. It does not, because `Of t` is an exactness claim the totality verdict reads:
`Of IntType + Of IntType` is not total (two ints can overflow), and a `Case` of an int and a float
typed `Of FloatType` would make the same addition read as total while both operands are ints at run
time. So `Int ⊔ Float` and `Int ⊔ Decimal` stay `Unknown` in the typer, and `DataFrame.typeOf` answers
`None` for them — undecided, never wrong.

**Also recorded: the law vectors carry the two wires (operator ruling, 2026-10-02).**
`conformance/laws/transform-laws.json` is this repository's derived file, and Phase 277 had already
appended its decimal `evalPipeline` vectors to it, so decimal rows through the columnar op wire and
the delta wire are the same act. They are appended AFTER the 39 `evalPipeline` vectors, which are
byte-identical, as two further case kinds: `columnOp` (a source table and an op; the result table or
a refusal, and the op's re-encoded wire string) and `delta` (two tables and a key column; the
encoded delta). `iterations` still counts the `evalPipeline` vectors only. The hosts' copies of the
file follow the 2026-09-20 ruling: they stay as they are until each host raises, and meet the two
kinds then. None of the existing vectors moved under the rulings above.

**Carried to Phase 338, not built here (operator ruling, 2026-10-02).** Refusing a `Float` beside
a `Decimal` at a derive, and typing a derive by its expression rather than by its cells, move with
the rewrite of the totality verdict and `Pipeline.fst` that phase makes; rule 2 above is what this
phase ships in the meantime. (Built by Phase 338: D5. Rule 2 still types a column only its cells
decide, and its "earlier type" for a float beside a decimal is replaced there by the refusal.)

## 2026-10-02 — D2: FS0025 is an error here, and `cellFits` widens exactly as `ColumnType.widens` does

**Decided (Phase 321; the `cellFits` half is an operator ruling of 2026-09-30).**

**1. An incomplete match is a compile error in every project but the proof oracle.**
`Directory.Build.props` escalates FS0025. The strand matches over the substrate's closed unions
(`Cell`, `ColumnType`), which gain cases on a raise; as a warning, an arm blind to the new case
compiled and threw `MatchFailureException` on the first value of it. As an error, the raise itself
names every arm that must move. The proof oracle is exempt by project name: it is extracted F\*
code, total by proof rather than by syntax, and it keeps its own `NoWarn` set. **The dry run this
phase was asked to record:** with FS0025 an error, the repository at Core `0.33.0` built with ZERO
errors — the substrate's raise (Phase 277) had already given every exhaustive arm its `Decimal` case,
so the escalation now guards the next raise rather than closing a list on this one.

**2. `ColumnOps.cellFits` accepts a cell whose type widens into the column's.** It was exact type
equality. The substrate's codec decodes a JSON int into a decimal (and a float) column, and its
`Table.validate` checks every cell through `widens`, so a strict edit check refused cells the
substrate's own wire produces and its own validator accepts: a table valid on one side of the
boundary failed an edit on the other. Now an `Int` fits a `FloatType` or `DecimalType` column, and
nothing else widens (`Float` into decimal and decimal into float are refused by name). **The cell is
stored as given, never converted**, so `invert` restores the previous cell verbatim and its round
trip is exact as before. **The strict reading is declined**, for the reason above. `proofs/ColumnOps.fst`
moved with it: the model gained `DecimalType` and a `widens`, `cell_fits` reads it, every theorem
re-verified unchanged, and the oracle differential now draws decimal columns (it could not before).
`cellToJson` writes the canonical decimal text, so a cell built by hand as `1.50` encodes as `1.5`,
the spelling the decoder reads.

## 2026-09-26 — D1: the compute strand is cut from Fuaran.Core into this repository under the same ids; it opens at `0.33.0`, and the forwarding module goes when the substrate removes the strand

**Decided (Phase 259, carrying out the Fuaran.Core repository's D66).** The substrate's D66 ruled
that `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` are produced by a repository of their own,
together with the dataframe law families, the dataframe half of the C# facade, the two proof models
that cover them and ownership of the transform law vectors, while `Fuaran.Core.Column` and every
other spine package stay. Its reason, in one line: the substrate's programme is stability, laws and
proofs, and it should break almost never; the compute layer's is a performance programme, and it
will break repeatedly — one `<Version>` over both would force either false breaks on the spine or
held-back compute. That entry, and the substrate's D68 (how the boundary was prepared inside it
first, so this cut is a copy of whole assemblies), are the record of WHY. This entry records what the
cut is.

**1. Same ids, same namespaces.** The four packages keep the ids and the CLR namespaces they shipped
with. A consumer raises a version and changes nothing else; nothing here renames anything, and a
rename would be its own breaking decision.

**2. History carried, not re-created.** The moved paths were carried with `git filter-repo` from a
clone of the substrate at `v0.32.0`, so `git log --follow` on a moved file reads its whole history.
Files this repository needed but the substrate keeps — the test infrastructure, the C# proof legs
shared with the substrate's facade proof, the proof-leg kit — are copies; the kit files are declared
in [`copies.json`](copies.json) so a drift between the two is named rather than discovered.

**3. `0.33.0`, the next minor above the substrate's last emission of the ids.** The substrate
released the four ids at `0.32.0`. Opening here at `0.33.0` keeps every consumer's version floor
monotone across the change of producer: no version number ever names two contracts. It is a DRAFT
until it is tagged.

**4. The substrate by package, at one pin.** The five substrate packages the strand stands on
(`Column`, `Wire`, `OpStream`, `Conformance`, `CSharp`) are package references at ONE property,
`FuaranCoreVersion`, restored from the public registry; the `Compute boundary` family refuses a
sixth, a project reference to the substrate, and any package outside the allowed set.

**5. The pre-split spellings go when the substrate removes the strand (Phase 258).**
`Fuaran.Core.DataFrame.Conformance` carries `Forwards.fs`: a module named `Fuaran.Core.Conformance`
whose members forward to `DataFrameConformance`, so code written against the pre-split
`Conformance.<family>` spellings kept compiling after the substrate's Phase 257. It exists for the
window in which both producers can ship the ids. It is removed in the change-set that answers the
substrate's Phase 258 — the phase that removes the strand from the substrate, on a breaking draft
whose entry names this repository and the version the ids continue at. Removing it is a breaking
change here too, so it advances this repository's draft slot rather than riding it.

**6. The transform law vectors are this repository's derived file.** `conformance/laws/transform-laws.json`
is emitted by `--emit-laws` from this repository's evaluator and stamped with this repository's
`<Version>`, and [`version-derives.json`](version-derives.json) declares the shared wire-format
corpus copy as derived from it. Until the substrate removes the strand, both repositories can emit
the file; the vectors are byte-identical and only the `kitVersion` stamp differs (`0.32.0` there,
this repository's version here), so the corpus copy can agree with only one of them. For that window
this repository's CI compares against the corpus and PRINTS a drift rather than failing on it (it
does not set `FUARAN_CORE_CORPUS_FRESHNESS`): made fatal on both sides, one producer's CI would be
red on every push for a byte neither can change. The corpus copy is re-emitted from here, and the
flag set in this repository's CI, in the change-set that answers the substrate's Phase 258 — the
point at which this repository becomes the only producer.
