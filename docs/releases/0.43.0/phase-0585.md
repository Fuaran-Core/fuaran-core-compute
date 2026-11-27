### A refresh over an edited prepared version costs the edit, not the table (Phase 585, `DECISIONS.md` D47) — additive, no public surface moves

**What changed.** A `SetCell` through `ColumnOps.applyPrepared` now records, on the version it produces, the row it
wrote. `Incremental.refreshPrepared` (and `refreshOnPrepared`) over that version, with a state evaluated over the
version it was edited from, reads the record as an in-place delta: on the row-local walk (a `Filter`, a `Project`, a
`Derive` beside them, a group step after them) it keys the written row and no other, where it keyed every row of the
new version before. One edit to a 10,000-row table through the op-derived delta now costs three key computations and
one row evaluated on a filter, at every row count; a diffed refresh of the same edit keys the new table at least once.
And the table an edit's version stands for (`DataFrame.toTable`) now shares every column the edit did not write with
the prior version's table, by reference, where the prior version's table had been built: only the written column is
concatenated afresh. `SetCell`, `SetColumn`, `InsertColumn` and `RemoveColumn` share; `AppendRows` does not.

**Why.** The UI tier's grid edits drive exactly this path, and on a filter the op-derived delta bought nothing over a
diff, because the refresh had no record of where the edited version's rows sat and keyed the whole table to find out.

**What a consumer must do.** Nothing. Results are unchanged: every refresh still equals the full evaluation, and
`toTable (applyPrepared op (prepare t)) = apply op t` still holds. A consumer that compared column objects between two
versions by reference will now find the untouched ones equal, which is the point; a column is immutable, so neither
version's cells can move. A state evaluated over some other version than the edited one's parent, a written row whose
key moved, and a version built any other way take the general walk exactly as before.
