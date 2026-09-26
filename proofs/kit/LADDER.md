# The claims-ladder manifest — `proofs.json`

A repository that proves things declares WHAT it proves, at what strength, as data: `proofs.json` at
the repository root. This page is the schema. `templates/proofs.json` is the starting copy; this page
is what that copy is written against, and what any tool reading a ladder reads it by.

## Who reads it, and how strictly

Two kinds of reader, deliberately different.

- **A registry** projects every repository's ladder into one table and checks that each row still
  POINTS at something — a theorem still declared in the model it names, a test family still in the
  suite, a document still in the tree. It is a **tolerant reader**: it reads the fields below,
  ignores every field it does not know, and refuses only what it cannot read (a missing required
  field, an unrecognised `level` or `class`). It verifies no proof and runs no test.
- **The repository's own ladder family** (the host family `templates/proofs.json` describes) is the
  **strict producer check**: it holds every row to the tree — a theorem is a top-level declaration,
  a model is one the leg actually checks, every checked model has a row, every declared case exists.
  What it enforces beyond this page is the repository's own business, and may be stricter than this
  page; it may never be looser.

Because the reader is tolerant, a field added here is additive: a ladder that does not carry it
reads exactly as before.

## The document

```json
{
  "kind": "proofs",
  "version": 1,
  "subject": "Your-Repo",
  "claims": [ … ]
}
```

| Field | | Meaning |
|---|---|---|
| `kind` | required | Always `"proofs"`. The discriminator: a file of this name without it is not a ladder. |
| `subject` | required | The repository's own name. It keys every row a registry reports (`subject:id`), so it is a NAME, not a description — scope prose goes in a `$`-annotation. |
| `claims` | required | The rows. `[]` for a repository that declares none. |
| `version` | optional | The schema version, `1`. Absent means `1`. Additive changes to this page do not move it. |
| `$…` | optional | Annotations (`$comment`, `$levels`, `$scope`, …). Never read by any tool. |

## A claim

| Field | | Meaning |
|---|---|---|
| `id` | required | A slug, unique within the file. Stable: a registry names a finding by it. |
| `level` | required | `proved` \| `tested` \| `assumed` \| `policy`. A CLOSED set, refused when unrecognised — every plausible default is a level, so a typo would silently assert something. |
| `claim` | required | What is claimed, stated so a reader can tell what it does NOT cover. |
| `evidence` | required | An object whose shape the level decides (below). Every level carries one. |
| `phase` | optional | The change that established the row. Optional on purpose: a repository written to a public standard may not cite a planning surface outside itself. |
| `repo` | optional | The workspace-relative repository the evidence lives in, when it is not this one — a row may legitimately rest on a theorem proved in a repository it consumes. |
| `class` | `assumed` only | `domain-obligation` \| `model-bridge` \| `premise` (closed; see `../README.md`, "The Core-to-domain proof contract"). |
| `dischargedBy` | `assumed` only | On a `domain-obligation`: the conformance law whose green run discharges it (`Conformance.<law>`). |
| `closes` | `assumed` only | What would retire the assumption: a change reference, `permanent`, or `unscheduled`. |

Any other field (`mitigation`, `reason`, `note`, …) is commentary: allowed, never read.

## Evidence, by level

Every path is repository-relative (resolved against `repo` when a row carries one). Every NAME is
matched as a whole identifier, not a substring — `run_total` is not found in `run_total_list`.

### `proved` — machine-checked, no admits

| Field | | Meaning |
|---|---|---|
| `theorem` | required | The ONE declaration the claim is — a top-level `val` / `let` / `let rec` in `model`, or, for a claim proved by a type system, the type whose construction carries it. |
| `model` | required | The file that declares it (`proofs/<Model>.fst`, or the source file for a type-system claim). |
| `prover` | optional | The pin that makes re-checking reproducible (`proofs/fstar-pin.json`; `global.json` for the F# compiler). |
| `supporting` | optional | Further declarations in the same `model` the claim rests on and a reader should be able to find — each checked like `theorem`. |

**One row per claim, not per lemma.** `theorem` is the headline; a lemma that only serves it goes in
`supporting`; a lemma a reader would cite on its own is a claim and gets its own row.

### `tested` — measured over sampled inputs, never all of them

| Field | | Meaning |
|---|---|---|
| `family` | required | The test family as it appears in the repository's test sources — or, for a check a script performs rather than a test (an extraction byte-diff), the repository-relative path of that script. A value containing `/` is a path. |
| `cases` | optional | The case names the family runs for this claim. Each must exist; a ladder family may hold the list to what a run actually reported. |
| `host` | optional | The test source file the family lives in. When given, `family` and `cases` are looked for there rather than across the whole test tree. |
| `model` | optional | The model the differential is about, when the family compares one against production. |
| `artifact` | optional | A file the check compares (a committed extraction). Must exist. |
| `seeds` | optional | The seed(s) the run is replayable from. Carried, never checked. |

### `assumed` — stated, and not discharged

| Field | | Meaning |
|---|---|---|
| `assumption` | required | The assumption in one sentence, in terms a reader could go and check it in. |

The row's `claim` says why the ladder needs it; `evidence.assumption` is the premise itself.

### `policy` — stated, not proved

| Field | | Meaning |
|---|---|---|
| `document` | required | Where the policy is stated. Must exist. |

A `policy` row is either a POSTURE the ladder rests on (which prover is pinned, how many cold runs a
check is) or a BOUNDARY it declares (a mechanism no theorem here addresses, stated so its absence
from the rungs above is not read as a claim that failed). Neither is algebra, and a `policy` row
never asserts coverage.

## Writing one from the other spelling

Some ladders were first written to an earlier spelling. The mapping is mechanical:

| Earlier | Here |
|---|---|
| `statement` | `claim` |
| proved `module` | `model` |
| proved `lemmas: [a, b, c]` | `theorem: a`, `supporting: [b, c]` — or a row each, where each is a claim |
| proved `prover: <alias>` + top-level `provers` | `prover: <the pin file>` |
| tested `list` | `family` |
| tested `runner: <script> step N` | `family: <script>` |
| tested `tests` | `cases` |
| `assumed` row with no evidence | `evidence: { assumption }` |
| `policy` row with no evidence | `evidence: { document }` |
| `retired_by` | `closes` |
