### A `Project`, a `GroupBy`, an `Unpivot` and a `Pivot` refuse an output that names a column twice (Phase 468, `DECISIONS.md` D35) — BREAKING, `behaviour`

**What changed.** Four verbs could answer a table naming one column twice — `Project [a, x; b, x]`, a
`GroupBy` key named as an aggregate (or two aggregates of one name), an `Unpivot` id column named
`variable` or `value`, and a `Pivot` whose index column is named as one of its `on` values' text. Each is
now refused with `TypeError("<verb> output 'x' is named twice, by <first> and by <second>; give each output
a name of its own")`. Before, the result re-entered the evaluator by name as the first of its two columns,
and Core's `Table.validate` refuses it. An unknown column is still named first. `Project`, `GroupBy` and
`Unpivot` refuse before any row is read; the pivot's value names are its data, so it refuses once its
`on` values are read and before any is aggregated. The evaluator, the incremental seam (prime and
refresh) and the planned pipeline all refuse; `Plan.isTotal` no longer calls a repeating `Project` total.
A pivot whose `on` column held two `NaN` cells answered two identical columns named `"NaN"`; it now
answers that column once.

**The class.** No public signature moves; the check and its callers are internal, and `api/` is
unchanged. What a consumer can observe is the refusal: a `behaviour` change, the class of Phases 412, 441
and 450. The pivot refuses rather than suffixing as a join does (D20); `DECISIONS.md` D35 argues why.

**What a consumer does.** Rename one of the two columns: give the output, the aggregate or the id column a
name of its own, or `Project` the index column to another name ahead of a pivot whose data may hold it.

**The law vectors.** Four refusal vectors join `conformance/laws/transform-laws.json` after the unit
shapes (`repeat-project-refused`, `repeat-group-by-refused`, `repeat-unpivot-refused`,
`repeat-pivot-refused`); every earlier vector is byte-identical and the `kitVersion` stamp reads `0.41.0`.
The corpus copy follows the release rule (D6 (4)) and moves when this version is released.
