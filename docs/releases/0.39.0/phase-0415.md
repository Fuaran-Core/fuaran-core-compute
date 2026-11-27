### The evaluator takes a source by position: `DataFrame.prepareRows` (Phase 415, `DECISIONS.md` D25) — `additive`

**What changed.** `DataFrame.prepareRows schema rows` is new. It is the source seam's door for a source
that is not a `Table`: a schema and its rows, read by POSITION. Cell `c` of every row is column `c` of
the schema, so nothing is looked up by name on the way in, and a schema that names a column twice keeps
both columns. The answer is a `Prepared`, which every evaluator over a prepared source takes, and so do
`ColumnOps` and the incremental seam. A row whose width is not the schema's is refused with `TypeError`,
naming the row. A pipeline split at any step hands its prefix's result through this door
(`evalToPrepared`, its rows read out by position, `prepareRows`, then `evalPrepared` for the remainder)
and answers what the whole pipeline answers. The `Table` door, `prepare`, still reads by name, as Core's
`Table` addresses columns.

**What did not change.** Every whole-table entry point, every output and every wire byte. The seam does
no IO and nothing asynchronous: the rows are a sequence the host hands in, pulled once.

**The class.** The surface family prints `Fuaran.Compute.DataFrame` gaining one member,
`prepareRows`: an `additive` move. It rides the `0.39.0` draft and moves no number.

**The law vectors.** None moves. The new law (the split law, in the suite) is a suite law, not a corpus
vector, and the corpus copy follows the release rule (D6 (4)) unchanged.
