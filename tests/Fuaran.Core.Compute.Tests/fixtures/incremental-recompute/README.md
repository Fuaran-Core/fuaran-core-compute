# Incremental-recompute corpus vectors

Two vectors of the estate's `incremental-recompute` conformance family — §12.7 of
the app-composition wire specification — vendored here so the legs of
`../../IncrementalCorpusTests.fs` run in any clone of this repository rather
than reporting themselves skipped.

The bytes were the family's, unedited, until Phase 117 **re-pinned
`sort-declines-in-full`'s footprint triple** — see "The Phase 117 re-pin" below,
which says exactly what moved and why. `point-edit-row-local` is untouched.

## What a vector is

Each file pairs a **pipeline**, a **source** table, an **edit stream** and the
**result** a correct evaluation produces, with a recorded **footprint triple**:
what a prime over the source cost, what a full evaluation over the *changed*
source cost, and what advancing the primed state against the edit stream cost.

The two halves are not the same kind of claim, and that is the family's own
point. The **result** is a pass criterion: a refresh that produces a different
table from a full evaluation has failed, with no allowance. The **counts** are
recorded evidence: engines legitimately differ in how much work they can avoid,
so they are compared as *less than* rather than as equal to.

`rowsEvaluated` counts **row evaluations at steps**, not rows. A row that passes
through three steps is evaluated three times; a row a filter drops is not
evaluated by the steps after it; a `groupBy` — and a `sort` — contribute none,
because neither evaluates an expression.

That is **one scale, and it covers a full evaluation too**. `sourceRows` is a
separate field and never stands in for the count: a pipeline with three
evaluating steps over six rows costs eighteen row evaluations, not six, whether
it was evaluated in full or in part. Reading a declined evaluation's cost off
`sourceRows` charged it for a single pass, so a decline compared against its own
full baseline read as having done *less* work than the thing it fell back to.

## Why these two

They are a pair, and the pairing is what makes either of them mean anything.

- **`point-edit-row-local`** is the **control**. Its pipeline (a filter and a
  derive) was incrementalisable before the sort widening, so all three of its
  recorded footprints must still be reproduced *exactly*. A widening that moved
  a number here would have changed what the seam costs on work it was already
  restricting, which is a regression however much it saved elsewhere.
- **`sort-declines-in-full`** is the vector the widening is **about**. Its
  pipeline is a filter followed by a sort, and its recorded refresh is a full
  evaluation declined for `sort` — which is exactly what this repository
  produced until `Sort` was admitted. Its **result must not move**; its
  **refresh class must**. That is the saving, and the test reads the numbers on
  both sides of the comparison off these bytes rather than restating them in F#.

So this vector's recorded *refresh class* is no longer an oracle for this
repository, while its recorded *result* still is. The family's own rule still
derives the declined reason it records — a pipeline carrying an order-dependent
verb, named — so the vector is not wrong; it describes an evaluator that
declines, and this one no longer does. Re-recording the triple on the corpus
side is that specification's act, not this repository's, and until it happens
the difference is the measured saving.

## The Phase 117 re-pin

Phase 117 corrected the instrument the triple is recorded on, so
`sort-declines-in-full`'s footprints were re-pinned here to the corrected
readings **of the same declining evaluator**. Nothing about what that evaluator
does changed; what changed is how honestly its cost is written down.

| | before | after |
|---|---|---|
| `prime` | `fullRecompute` (`stepNotRowLocal` / `sort`) | `primed`, `rowsEvaluated` 6 |
| `full` | `fullRecompute` (`stepNotRowLocal` / `sort`) | `primed`, `rowsEvaluated` 6 |
| `refresh` | `fullRecompute` (`stepNotRowLocal` / `sort`), no count | `fullRecompute`, `rowsEvaluated` 6, same reason |

Two corrections, one vector:

- **A prime is `primed`.** Priming a declined pipeline is not a fall-back —
  there is no prior state to fall back *from*, and a prime evaluates everything
  whatever the plan says. The decline and its typed reason belong to the
  refresh, which is where the fall-back actually happens; a consumer that wants
  to know *before* evaluating asks the plan, not the footprint.
- **A `fullRecompute` carries `rowsEvaluated`.** Six here: the filter over six
  rows, the sort contributing none. It was previously read off `sourceRows`,
  which happens to be six for this vector too — so the *number* is unchanged and
  the *claim* is not. A vector whose pipeline had two evaluating steps would
  have differed.

`point-edit-row-local` needed no re-pin: every one of its footprints was already
`primed` / `rowsRecomputed`, which counted row evaluations at steps all along.
That it did not move is the control working.

**Re-recording the vectors on the corpus side is that specification's act, not
this repository's** — the same standing division the section above states — so
until it happens these two files deliberately lead the corpus they were
vendored from, and the reader here requires the `rowsEvaluated` field a
`fullRecompute` now carries.

## What the reader models

`IncrementalCorpusTests.fs` reads only what these two vectors use — `filter`,
`derive`, `groupBy` and `sort` steps; `column`, `literal` and `binary`
(`greaterThan`, `multiply`) expressions; `setCell`, `appendRow` and `removeRow`
edits under the `identity` scheme — and **refuses** anything else by name. A
vector using a verb the reader silently skipped would be certified against a
pipeline the corpus did not write, which is worse than a vector nobody ran. An
`ordinal`-addressed stream is refused rather than run as an identity one: that
distinction is the whole of §12.7's re-addressing pair.

## Pointing the tests at a different corpus

Set `FUARAN_INCREMENTAL_CORPUS` to a directory holding both files. A directory
that does not hold them is refused by name rather than falling back silently.
