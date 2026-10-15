# Fuaran.Core.Compute — decisions (newest first)

## 2026-10-15 — D30: a declined shape's tick is held to what a declined refresh is — the diff and the reference evaluation it runs, timed beside the tick, plus the per-call floor; and a refresh no longer walks an embedded relation to learn that its pipeline did not move

**Context.** Compute `v0.39.0` (`02d25fd`) failed its publish run (38554709427, attempt 1) in the
Release clock leg: `append pivot @ 100000` read tick 10.07 to 10.34 ms against a one-thread full
evaluation of 5.53 to 5.76 ms (x1.79 to x1.82, bound x1.6) on all three counted attempts. Nothing was
published. The same cell had been red on `ci` at `fc0ef74` and `3142469`, and read x1.08 on the same
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
