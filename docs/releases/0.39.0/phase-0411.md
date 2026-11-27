### A join never names two columns alike (Phase 411, `DECISIONS.md` D20) — BREAKING, `behaviour`

**What changed.** A combining join (`Inner` / `Left` / `Right` / `Outer`) names a right column that
collides with a left one `x_right` as before, unless the output already carries `x_right` — then
`x_right_2`, `x_right_3`, …, the first suffix free against every name the output carries (the left's,
the right's own, and the suffixes earlier colliding right columns took), decided in right column
order. A right name that collides with no left name keeps its name, as before. Until now the suffix
was checked against the LEFT names only, so a left carrying `x` and `x_right` joined to a right
carrying `x` emitted two columns named `x_right`, and a table re-entering the evaluator read the
left's under the right's name. A join whose output was already unique names its columns exactly as
it did.

**One rule, read everywhere.** `DataFrame.joinRightNames` (new, additive) is the rule; the evaluator
and `SchemaWalk` both read it, so the static and the evaluated names cannot disagree. `SchemaWalk`
over an OPEN right source (`AtLeast`) no longer names a right column that collides with a left one:
the right's unnamed columns decide its suffix, so the walk keeps only the right columns that keep
their own names, and says why in the reason.

**The class.** The surface family prints `Fuaran.Compute.DataFrame` **`additive`** (one member,
`joinRightNames`); the change a consumer can observe is the output names of a join that used to
repeat one, which is a `behaviour` change. The `0.39.0` draft is untagged, publicly unpinned and
already carries a BREAKING class (Phase 423), so this change RIDES it.

**The law vectors.** No vector in `conformance/laws/transform-laws.json` joins a left carrying a
suffixed name, so none moves. The new law (`Conformance.schemaWalkLaws`: a combining join adds no
repeated name, and the walk names a built pair of joins as the evaluator does) is a kit law, not a
corpus vector; the corpus copy follows the release rule (D6 (4)) unchanged.
