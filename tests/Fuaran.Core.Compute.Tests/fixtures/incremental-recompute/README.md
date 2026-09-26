# Incremental-recompute corpus vectors

Five vectors of the estate's `incremental-recompute` conformance family — §12.7
of the app-composition wire specification — vendored here so the legs of
`../../IncrementalCorpusTests.fs` run in any clone of this repository rather
than reporting themselves skipped.

The first two files' bytes were the family's, unedited, until Phase 117
**re-pinned `sort-declines-in-full`'s footprint triple** — see "The Phase 117
re-pin" below, which says exactly what moved and why. `point-edit-row-local` is
untouched.

**`window-declines-in-full` and `join-declines-in-full` are Phase 120's, and
they were WRITTEN HERE rather than taken from the corpus.** The corpus records
no window or join footprint at all — that absence is what D20's gate was about —
and an operator decision of 2026-09-02 waived that gate for these two classes,
on the understanding that the corpus records the family's own vectors afterwards
from the real consumer. Until it does, these two lead the corpus in the same way
the re-pinned sort vector does; see "Where the Phase 120 pair came from" below.

**`rank-declines-in-full` is `0.19.0`'s, written here on the same terms**, when
the window boundary was relaxed from frame boundedness to row-set preservation.
Its "before" is the nearest evaluator rather than the oldest — see "The
`0.19.0` rank vector" below.

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

## Why these five

They are one control and four widenings, and the pairing is what makes any of
them mean anything.

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

- **`window-declines-in-full`** and **`join-declines-in-full`** are Phase 120's
  pair, written on exactly the terms above. The first's pipeline is a filter
  followed by a **bounded-frame** window (a `lag` over the tie-heavy partition
  key); the second's is a filter followed by a **semi** join against a two-row
  lookup, with the edit moving a row's key OUT of the relation — so the verdict
  cached for that row is precisely what has to be recomputed, and every other
  row's is precisely what may be reused. Each records the full evaluation its
  class declined into before the widening, and each falls from six
  row-evaluations to one.

- **`rank-declines-in-full`** is `0.19.0`'s, and it is the one whose recorded
  "before" is **the evaluator immediately preceding it** rather than the
  pre-Phase-115 one. Its pipeline is the window vector's with `lag` replaced by
  `rank`, over the same source and the same edit — deliberately, because the
  claim being measured is that the two cost *the same*, and two vectors that
  differed in anything else could not say so. Its recorded refresh is therefore
  `windowFrameUnbounded` / `rank`, which is what Phase 120 produced, and it too
  falls from six row-evaluations to one. A test asserts that equality directly.

So a widened vector's recorded *refresh class* is no longer an oracle for this
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

`IncrementalCorpusTests.fs` reads only what these five vectors use — `filter`,
`derive`, `groupBy`, `sort`, `window` and `join` steps; `column`, `literal` and
`binary` (`greaterThan`, `multiply`) expressions; `setCell`, `appendRow` and
`removeRow` edits under the `identity` scheme; and the decline reasons the
recorded triples carry, `windowFrameUnbounded` among them — and **refuses**
anything else by name. The refusal is per MEMBER, not per verb: the reader
models `lag`, `lead` and `rank` and refuses `cumulSum`, models `semi` and `anti`
and refuses `inner`, because a vector whose `cumulSum` it silently read as a
`lag` would certify a frame the corpus did not write. That is a statement about
what these bytes have been read against, never about what the seam admits —
`Incremental.plan` is what answers that, and since `0.19.0` it admits every
window function including the `cumulSum` this reader refuses. A
vector using a verb the reader silently skipped would be certified against a
pipeline the corpus did not write, which is worse than a vector nobody ran. An
`ordinal`-addressed stream is refused rather than run as an identity one: that
distinction is the whole of §12.7's re-addressing pair.

## Where the Phase 120 pair came from, and the one spelling invented here

Those two vectors are this repository's own. Everything about their shape
follows the two the corpus wrote — the same `family`, the same footprint triple,
the same `identity`-addressed edit stream — and three members had no precedent
to follow, because no corpus vector uses them:

| member | spelling used here |
|---|---|
| a window step | `{"verb":"window","partitionBy":[…],"orderBy":[{"column":…,"direction":…}],"fn":"lag","of":…,"as":…}` |
| a join step | `{"verb":"join","how":"semi","on":[{"left":…,"right":…}],"source":{"columns":…,"rows":…}}` |
| a **null cell** | `{"null": true}` |

The null cell is the one worth flagging. A bounded frame's first row in each
partition has no predecessor, so a `lag` column **cannot** avoid a null, and the
corpus — having never recorded a window vector — has never had to spell one. The
flat single-member shape used here matches its `{"int": n}` / `{"string": s}`
siblings, which is the most it can claim: it is a choice, not a convention.
**When the corpus records the family's own window and join vectors, this
repository adopts the corpus's spellings and these bytes follow them.** The
reader refuses anything it does not model, so that adoption arrives as a failing
read rather than as a silently different meaning.

## The `0.19.0` rank vector

`rank-declines-in-full` follows the Phase 120 pair's shape exactly — the same
`family`, the same footprint triple, the same `identity`-addressed edit stream —
and needed no new spelling at all: `{"fn":"rank"}` is the canonical wire tag the
codec already carries, and the reason `windowFrameUnbounded` is the one this
repository emitted between Phase 120 and this vector. It records **no null
cell**, because a rank has no first row without a predecessor to spell.

Its "before" being the immediately-preceding evaluator rather than the oldest
one is a deliberate difference from the four above, and the reason is what the
vector is *for*. The three widened vectors each measure a class against the
evaluator that declined it *by verb*; this one measures a class against the
evaluator that declined it *by frame*, which is the distinction being relaxed.
Recording `stepNotRowLocal` / `window` here would have been a true statement
about a still older evaluator and would have measured the wrong gap.

**The same standing division applies**: re-recording these on the corpus side is
that specification's act, not this repository's, and when it records the
family's own window vectors this repository adopts its spellings. The reader
refuses what it does not model, so that adoption arrives as a failing read.

## Pointing the tests at a different corpus

Set `FUARAN_INCREMENTAL_CORPUS` to a directory holding all five files. A
directory that does not hold them is refused by name rather than falling back
silently.
