# Incremental-recompute corpus vectors

Two vectors of the estate's `incremental-recompute` conformance family — §12.7 of
the app-composition wire specification — vendored here so the legs of
`../../IncrementalCorpusTests.fs` run in any clone of this repository rather
than reporting themselves skipped. The bytes are the family's, unedited.

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

## Why these two

They are a pair, and the pairing is what makes either of them mean anything.

- **`point-edit-row-local`** is the **control**. Its pipeline (a filter and a
  derive) was incrementalisable before the sort widening, so all three of its
  recorded footprints must still be reproduced *exactly*. A widening that moved
  a number here would have changed what the seam costs on work it was already
  restricting, which is a regression however much it saved elsewhere.
- **`sort-declines-in-full`** is the vector the widening is **about**. Its
  pipeline is a filter followed by a sort, and its recorded triple is three full
  evaluations declined for `sort` — which is exactly what this repository
  produced until `Sort` was admitted. Its **result must not move**; its **class
  must**. That is the saving, and the test reads the numbers on both sides of
  the comparison off these bytes rather than restating them in F#.

So this vector's recorded *class* is no longer an oracle for this repository,
while its recorded *result* still is. The family's own rule still derives the
declined reason it records — a pipeline carrying an order-dependent verb, named
— so the vector is not wrong; it describes an evaluator that declines, and this
one no longer does. Re-recording the triple on the corpus side is that
specification's act, not this repository's, and until it happens the difference
is the measured saving.

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
