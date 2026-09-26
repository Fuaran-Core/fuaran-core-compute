# `proofs/kit/` — the proof leg, shipped once

A repository that wants an F\* proof leg needs the same nine things every time: a pinned prover, a
script that locates or downloads it, checks each model from a cold cache, extracts each model to F\#
and diffs the result against a committed oracle, runs the host families, and measures each module
against a declared budget and a declared floor; two hand-written runtime shims the extractor's
output compiles against; a never-packed oracle project with the two settings that generated F\#
needs; a CI job; a claims ladder; and a cost declaration. Written out by hand each time, that is
four copies of one design and — the part that actually bites — **four prover pins, three of which
will be behind the day the fourth moves**, because F\* releases weekly.

This directory is that design, written once. This repository consumes it **in place**: its
`proofs/check.ps1` is a thin caller that declares what this repository has and hands it to
`check-proof-leg.ps1`. Another repository adopts it by **copying** the files listed below and
declaring each copy in its own `copies.json`, so the estate's copy registry names a copy that has
drifted from this one before anybody meets a red gate they did not cause.

**What it deliberately is not:** a package, a module, or a runtime dependency. An adopting
repository builds nothing new, references nothing new, and restores nothing new. It is files copied
by declaration and checked for drift — which is the only shape that works across repositories whose
languages, solutions and release cadences have nothing in common.

**Since Phase 175 the kit also ships the theorems.** A leg that can check a model, handed to a
repository with nothing to check, is a leg that repository must re-model against — and fold
confluence at its own witness is exactly what three named adopters want. So the copy set now carries
the models under `../`: the generic theorems (`DagFold`, `Chain`, `WireCanon`, `WireDecode`,
`JsonParse`, `Limits`), the reference instance a domain instantiates against (`TreeOps`, `Skeleton`,
`Preservation`, `TreeDiff`), and `templates/Instance.fst.template`, which makes fold confluence at a
domain's own algebra a fill-in-the-holes act with exactly one hole that is an obligation. See
"Importing a theorem" below.

## What is here

| File | What it is | Copied? |
|---|---|---|
| `check-proof-leg.ps1` | **The engine.** Knows how to run a proof leg; knows nothing about which models a repository has. Takes the module list, the oracle host and the paths as parameters. Classifies every lost pass into one of the three verdicts below. | copy verbatim |
| `templates/check.ps1` | The thin caller. Three declarations to edit at the top; nothing below them is per-repository. | copy and edit |
| `templates/oracle.fsproj.template` | The never-packed oracle project: `--strict-indentation-`, the FS0058/FS0064/FS1182 `NoWarn`, and the compile order the shims and models need. Named `.template` so no build or glob in a host repository can pick it up. | copy, rename and edit |
| `templates/modules.json` | The cost declaration: the budget rule, the floor rule, the contention threshold, and one worked entry. | copy and edit |
| `LADDER.md` | **The ladder's schema**: every field of `proofs.json`, which are required, what each level's evidence is, and how names are matched. The page the template is written against, and the one any tool reading a ladder reads it by. | read |
| `templates/proofs.json` | The claims ladder: the closed level set, what each level means, and the host family that holds the rows to the tree. Goes at the **repository root**, not in `proofs/`. Written against `LADDER.md`. | copy and edit |
| `templates/ci-proofs-job.yml` | The CI job, with the cache key that hashes the pin file — which is the whole mechanism by which a pin bump reaches CI with no second edit. | copy and edit |
| `templates/Instance.fst.template` | The instantiation template (Phase 175): `../Skeleton.fst` with fourteen named holes. Drop the preamble, fill the holes, and the result is a domain's fold-confluence composite; the one obligation is `{{DIAMOND}}`, a proof of `independence_diamond` at the domain's own footprint and apply. Held to `../Skeleton.fst` byte for byte by the `Proofs.Kit` family, so the template and its first instance cannot drift apart. | copy and instantiate |
| `extraction-post-pass.ps1` | **The extraction post-pass** (Phase 169): two functions, dot-sourced by the engine's EXTRACT stage, that re-indent a mutual type group's `and` to the column F\# expects and touch nothing else. See "The extraction post-pass" below. | copy verbatim |
| `extraction-post-pass.tests.ps1` | Its go-red proof: four arms, two of which need the pinned prover and `dotnet` and are reported NOT RUN where either is absent. Also the machinery that answers the retirement condition. | copy verbatim |
| `check-proof-leg.tests.ps1` | **The engine's refusals, held to their exit codes** (Phase 221). Runs the engine the way a caller does (`&`, in process) over a scratch proofs directory: a green control, then a missing host project, a host filter that cannot run and a refuted model, each of which must exit non-zero and never print `proofs: green`. Needs only the pin and the prover, never your models; `templates/check.ps1` calls it after a green leg. Reports NOT RUN (exit 2) where there is no prover. | copy verbatim |
| `templates/MutualTypes.fst` | The post-pass's fixture — the smallest model that makes the backend emit a mutual type group. **Not a template to instantiate**, and not to be registered in `$modules`: it earns no committed oracle. | copy verbatim |

The rest of what an adopter needs is **not duplicated here**, deliberately, and lives where it is
actually used:

| File | Where | Why not a copy in the kit |
|---|---|---|
| `../fstar-pin.json` | `proofs/fstar-pin.json` | This is the live pin — the one this repository's leg reads and its CI caches on. A copy of it inside the kit would be a second number in the same repository, and a second number is exactly the problem the kit exists to remove. |
| `../oracle/Prims.fs` | `proofs/oracle/Prims.fs` | The hand-written runtime floor grows as models reach for more of F\*'s library, so the live one is always the current one. A kit copy could only ever be behind it. |
| `../oracle/FStar_Pervasives_Native.fs` | `proofs/oracle/FStar_Pervasives_Native.fs` | The same. |
| `../<Model>.fst` — the ten models `copies.json` `$sources` names | `proofs/*.fst` | The models are the live ones the leg checks and the oracle is extracted from. A kit copy would be a second model beside the first, in the same repository, with nothing holding them equal — the sources ARE `proofs/*.fst`, and the registry names each one. Which theorem a model carries and which obligations it asks a domain for is on its `$sources` entry, one per file. |

So the copy set is: everything in this directory, **plus** those three files and the ten models from
`../`. An adopter copies what it needs and declares what it copies; `copies.json` `$sources` at this
repository's root is that set written out, one entry per file, and an adopter's record is the entry
with its own path filled in.

## The three verdict classes (Phase 166)

A leg that reports every lost pass as "did NOT verify" is not an evidence instrument in either
direction. Over 2026-09-14/15 three different things all read that way — a prover that died with
nothing printed, a prover killed under memory pressure, and a dependency's checked-module file
vanishing mid-run — and each cost a session twenty minutes of reading a whole log to establish that
nothing had been refuted. So a non-zero prover exit is **classified before it is reported**, and the
class decides the words, the exit code and whether anything is retried.

| Verdict | What happened | Exit | Retried? | What it obliges |
|---|---|---|---|---|
| **REFUTATION** — `<module>.fst did NOT verify` | The prover exited non-zero **and printed a diagnostic**: an error line, an error summary, `Failed to prove`, `Unexpected`, or a failing-quake line. | the **prover's** code (1 in practice) | **Never.** | Read the model. Something was refuted, or `--report_assumes error` caught an escape hatch. Re-running it to see whether it goes away is the habit this leg exists to make impossible. |
| **ABORT** — `<module>.fst ABORTED (no diagnostic)` | The prover exited non-zero and printed **nothing** about an undischarged query. Nothing was refuted; the prover died. | **3**, on the second abort | **Once**, in the same run, bounded at one retry per module per run and logged as a retry. | Read the machine, not the model. The pre-flight line above the abort says how many provers were running and how much memory was free. A module that aborts across legs is a finding about this machine, or about that model's memory appetite, and is worth a phase. |
| **APPARATUS** — `extraction of <module> hit an APPARATUS fault` | The leg's own machinery failed: at extract, a dependency's `.checked` file missing from the cache, or the cache directory gone. **Named**, so the reader starts from the file. | **4** | **Never** — the thing it needs is gone, so a second attempt asks the same broken apparatus the same question. | Find the second writer. The cache is per invocation and the script owns it, so something removed it underneath the run. |

Three details of that table are load-bearing rather than decorative, and each was measured against
the pinned prover rather than assumed:

- **The discriminator is the LOG, not the exit code.** A killed process and a refuted lemma are both
  "non-zero", and nothing about the number tells them apart. What the leg asks is whether the prover
  *said* anything about an undischarged query — and since F\* prints no per-query line for a query it
  discharged, a log with no diagnostic in it is a log in which every query the module printed was
  discharged. That is the same statement, read off the only evidence there is.
- **Both spellings of an error line are matched, because the prover uses one and the message formats
  use the other.** On the pinned release a diagnostic reads `* Error 19 at Foo.fst(8,39-8,41):`, not
  `(Error 19)`. A leg that knew only the parenthesised form classified a plain error as an ABORT and
  *retried* it — the exact inversion this verdict exists to prevent. The end-of-run
  `N errors were reported` summary is matched as a third, independent witness.
- **A failed extraction is not always a non-zero exit.** Removing a dependency's `.checked` file
  underneath an extraction makes F\* print `* Error 317: Cross-module inlining expects all modules to
  be checked first` and then **exit 0**; the fault surfaces two lines later as "extraction produced
  no `<module>.fs`", which names the symptom and nothing about the cause. So the engine decides
  failure as *non-zero exit **or** no file produced*, and only then classifies.

**A retry's clock is a WARM measurement and feeds neither gate.** The aborted attempt has already
half-filled the cache, so the retry is not a cold run; its line says so, it is compared to neither
the budget nor the floor, and the leg's closing verdict names every abort that was retried and
passed — a green run that lost a prover and got it back is not the same evidence as one that did
not, and it should not take scrolling to find that out.

**Every prover invocation's whole output is teed** to `<WorkDir>/logs/<module>[.retry].<step>.log`,
and each verdict names the transcript it was read from, so a post-mortem reads the classification's
own evidence rather than a scrollback that is gone.

## The extraction post-pass (Phase 169)

**F\*'s F\# backend emits a mutual TYPE group that F\# 10 will not parse**, so an extraction carrying
one does not compile in the oracle project and the leg's step 2 holds the oracle to text nothing can
build. The backend breaks the group at the space before each `and`, which leaves the previous
declaration's last line with a trailing space and the `and` line with **one leading space**:

```fsharp
type node =
| Leaf of Prims.string
| Branch of attr          // <- trailing space
 and attr =               // <- one leading space; F# rejects this
| Flag of Prims.bool
| Nested of node
```

F\# reports `error FS0010: Unexpected keyword 'and' in member definition`, and it reports it **even
under the oracle project's `--strict-indentation-`** — which is the whole reason this is a separate
defect from the pre-F\#-8 match-arm layout that flag was relaxed for, and the reason a reader who
knows about the flag will otherwise assume it is covered.

So the engine runs a normalisation step between the extraction and the byte diff: every line whose
first token is the keyword `and` is re-indented to column 0, and **nothing else changes** — not the
trailing space on the line above it, not a line ending, not a byte anywhere else. The committed
oracles are the normalised text, so step 2's contract ("byte-identical to a fresh extraction") is
unchanged in meaning.

| | |
|---|---|
| **Observed on** | F\* **v2026.09.06** / Z3 4.13.3 — the release `../fstar-pin.json` declares |
| **Scope** | mutual type groups only: DU groups and record groups both, at any name length, always one space |
| **Not in scope** | a value `and`. The backend **hoists local mutual recursion to the top level**, so `let rec f … and g …` written inside an F\* function body extracts as two top-level bindings joined by a column-0 `and`, which the pass never sees |
| **Identity today** | no model in `proofs/` has a mutual type group yet — the three `$proofOnly` vocabulary models do, and are never extracted — so the pass moves no byte in any committed oracle. That is checked rather than claimed (arm B below) |
| **When it fires** | the leg prints a named line saying which module and how many lines. A silent rewrite between an extraction and the artefact it is diffed against is exactly the step that must never be invisible |

**The retirement condition, and how you will hear about it.** The pass exists because of an upstream
defect in a particular prover release. Bump the pin, run
`pwsh ./proofs/kit/extraction-post-pass.tests.ps1`, and arm C answers the question: if a fresh
extraction of `templates/MutualTypes.fst` no longer carries the defect, the script **fails by name**
with the retirement instruction rather than going quietly green — a go-red fixture that can no
longer go red is the signal to delete the machinery it guards. Retiring it means deleting the helper,
its call in the engine's EXTRACT stage, the fixture and that script, and re-extracting every oracle.

**Its four arms**, each named for the direction it can fail in:

| Arm | What it proves | Needs |
|---|---|---|
| **A. unit** | the pass repairs the bytes the backend emits, and leaves a column-0 `and`, an `and_then`, an `and` inside a literal, CRLF endings and trailing whitespace alone | nothing |
| **B. identity** | the pass is the identity on **every** committed oracle | nothing |
| **C. go red** | the RAW extraction of the fixture fails to compile, **with FS0010 at the `and` line** — a failure for the wrong reason is not evidence | the pinned prover, `dotnet` |
| **D. go green** | the same extraction, with the pass run over it, compiles | the pinned prover, `dotnet` |

C and D compile against a scratch project **derived from the oracle project itself** — its target
framework, its `NoWarn`, its `--strict-indentation-` and its package references, with only the
compile list rewritten — so what they prove is a statement about your oracle project and not about a
second one that resembles it. Where the prover or `dotnet` is absent they are reported **NOT RUN**
with the remedy, never skipped quietly: "nothing to check" must not read as "everything checked".

## A cost finding that names a contended pass (Phase 171)

A budget overshoot has two entirely different causes — **this module got more expensive**, or **this
machine was busy** — and the leg used to print the same sentence for both. Three recorded instances,
all of them the second kind: `Chain` overshot twice in seven runs and came in at 16–25s on the other
five, untouched by any phase since it was budgeted; a pass measured `TreeOps` at 145s while inflating
three untouched modules by the same factor, and the phase had to depart from the seeding rule by hand
and write a paragraph explaining why; and `Capability` measured 75s against a 20s budget on a run
contended by three sibling gates. A reader of any of those logs cannot tell them from a regression.

So at the end of every **run** the engine reports one number:

> the **median**, over the modules this working tree did **not** change, of what each module just
> cost divided by the `measuredSeconds` its budget entry records.

An untouched module's cost is a fact about the machine and not about the tree, so a pass in which all
of them came in at 1.7× their recorded measurements is a pass in which the machine was 1.7× slower,
whatever any one line says. The median rather than the mean: one module hitting a pathological query,
or aborting and retrying, is exactly the outlier a mean would launder into the number.

```
==== proofs: contention — run 1 of 1, x0.29 over 16 untouched module(s), at or under the x0.80 threshold: an ordinary pass. A cost finding on this run is about its module.
==== proofs: contention — run 1 of 1, x0.29 over 4 untouched module(s), ABOVE the x0.20 threshold: this was a CONTENDED pass. 1 cost finding(s) on this run carry the label, and a labelled finding is NOT a re-seed obligation.
     ColumnOps.fst took 12s against its 8s budget on run 1 of 1 — 4s over, 150% of budget — CONTENDED PASS (x0.29 against a x0.20 threshold): the modules this tree did not change ran x0.29 of their recorded measurements on this run, so this figure measures the afternoon and not the module
```

Both lines are transcripts: the first from a quiet cold pass of the reference repository's leg, the
second from the phase's own probe, which lowered the threshold to 0.20 and one budget to 8s so that
an ordinary pass crosses it — the labelling path is the same one a genuinely contended pass takes.

Above the threshold, every cost finding from that run is **labelled** where the closing verdict prints
it, and the label carries the whole consequence: **a labelled finding is not a re-seed obligation.**
Re-seeding a budget from one raises a ceiling to fit a slow afternoon, which is precisely how a budget
stops meaning anything. `-Strict` promotes only the **unlabelled** findings — a session that asked for
a red leg on cost asked to be stopped by a regression, and a contended pass is not one — while the
coverage and shape findings belong to no run, are never labelled, and so always promote.

Five details are load-bearing rather than decorative:

- **The number's scale is not the obvious one, and it was measured rather than assumed.**
  `measuredSeconds` is not a typical cost: by the budget file's own seeding rule it is the **slowest**
  cold run ever observed for that module, and budgets are routinely seeded on busy machines. So the
  ratio's neutral point sits well *below* one — a quiet pass of this repository's leg measures
  **x0.29**, not x1. A threshold chosen as though 1.0 meant "normal" would sit above any contention
  the leg can experience and would never fire: the *detector that cannot fire* that the budget file's
  own `TreeOps` note warns about. Seed the threshold from a measured quiet pass and a measured
  contended one, in the file's `contentionSeeding` block, and record both figures there.
- **Nothing is multiplied into a measurement.** The seconds a green line prints stay the wall clock
  the module actually took, and the factor sits beside them. A normalised measurement would be a
  number nobody observed, and the value of this leg's cost half is that every figure in it is one
  somebody's machine really produced.
- **The untouched set is derived, not declared** — `git status --porcelain` over the proofs
  directory, so modified, staged and brand-new all count and no branch name is assumed. Its limit is
  worth knowing: a session that has already **committed** its model edits has a clean tree, so its
  module reads as untouched and votes. That is what the median absorbs, and it is the honest boundary
  of what a working-tree question can answer. Where git cannot answer at all, every module counts as
  untouched and the leg **says so** — "I could not tell" must never print as "nothing is touched".
- **A module too cheap to time does not vote.** The clock is whole seconds, so 0s against a recorded
  2s is a ratio of 0 and 1s is a ratio of 0.5, and neither says anything about the machine. The cut is
  `floorSeeding.zeroBelowSeconds` — this file's existing answer to "below what is a reading
  process-start noise", reused rather than minted again — and a run with fewer than
  `contentionSeeding.minimumSamples` contributors reports the factor as **not computed** rather than
  taking a median of one.
- **An absent `contentionSeeding` block is not a finding.** Unlike a missing budget or floor, which
  fire per module when a model is added, this one is per file and one-off, and a finding present on
  every run of an unseeded repository is one people learn to scroll past. The factor is still computed
  and still printed; nothing is labelled, and the line names the block to seed. That is the pre-171
  behaviour plus one informative number, which is the safe direction for an adopter.

Each entry may also carry an optional **`contentionFactor`** beside its `measuredSeconds`, recorded by
whichever phase seeds that number: it says what the machine was doing when the measurement was taken,
so a later reader can tell a budget seeded on a quiet machine from one seeded on a busy one. It is
**provenance only** — the engine holds it to its shape and computes nothing from it, for the same
reason the factor is never multiplied into a measurement. Absent reads as "not recorded", never as 1.

## Adopting it

1. **Copy `proofs/kit/` wholesale** into `<repo>/proofs/kit/`, and the three files above into
   `<repo>/proofs/fstar-pin.json` and `<repo>/proofs/oracle/`.
2. **`templates/check.ps1` → `<repo>/proofs/check.ps1`.** Edit the three declarations: the models,
   the host families, and where the host project lives. Delete the `templates/` copy from your
   repository if you prefer — it is a starting point, not a dependency.
3. **`templates/oracle.fsproj.template` → `<repo>/proofs/oracle/<Your>.Proofs.Oracle.fsproj`**, and
   add it to your solution. Keep `IsPackable` false: nothing extracted from a model belongs in a
   shipped assembly.
4. **`templates/modules.json` → `<repo>/proofs/modules.json`.** One entry per model. Seed the
   numbers from real runs, per the rules the file carries; do not invent them, and do not seed from
   a convenient half of the data.
5. **`templates/proofs.json` → `<repo>/proofs.json`** (the repository root). Write the ladder, and
   then write the host family that holds it to your tree — the kit ships the leg that runs that
   family, not the family itself, because what a row must be held to is a property of your tree.
6. **`templates/ci-proofs-job.yml` →** your workflow. Keep the cache key hashing the pin file.
7. **Gitignore `proofs/.fstar/` and `proofs/obj/`.** The prover install and the checked-module cache
   are not source.
8. **Write your first model**, run `pwsh ./proofs/check.ps1`, and commit the oracle it extracts.
9. **Declare your copies** (below). Until you do, nothing says when this kit has moved under you.

## Importing a theorem (Phase 175)

An adopter that wants a theorem rather than only a leg — fold confluence at its own witness, which is
what the estate's three named adopters want — does three things, and the leg above is what checks
the third. Nothing in it is a package or a reference; it is the same copy-by-declaration, with a
template where the leg had a caller.

1. **Copy by declaration.** From `$sources`, copy the generic model the theorem lives in — for fold
   confluence that is `../DagFold.fst`, which opens nothing and is the one file every instance
   needs — and the reference instance you will instantiate against, `../TreeOps.fst` and
   `../Skeleton.fst`, into `<repo>/proofs/`. Append one `records` entry per file to your
   `copies.json`, `check: fingerprint`, `regen` as the `$sources` entry says. Each model is now a
   named copy the sweep watches, so a theorem that moves here is a finding there before it is a red
   gate you did not cause.
2. **Write the instance.** `templates/Instance.fst.template` → `<repo>/proofs/<Instance>.fst`. The
   contract is the preamble's three rules: drop everything through the
   `(* ==== END OF PREAMBLE ==== *)` line, replace every `{{HOLE}}` — the same hole takes the same
   value everywhere it occurs — and leave no `{{` behind; an unfilled hole is a refusal, not a
   default. Fourteen holes: twelve identifiers, two prose blocks, and exactly **one obligation** —
   `{{DIAMOND}}`, a lemma in your domain module of type
   `unit -> Lemma (independence_diamond #op #state #rej footprint apply)`. That is the fold
   theorem's one hypothesis about the domain, and it is yours to prove: the template fixes
   everything else, and a fixed line you find you must change is a template defect to send back,
   not a fork to keep. The values that make `../Skeleton.fst`, which the `Proofs.Kit` family holds
   the template to:

   | Hole | `Skeleton.fst` | Yours |
   |---|---|---|
   | `MODULE` | `Skeleton` | the instance's module name |
   | `DOMAIN` | `TreeOps` | the module carrying your algebra, opened beside `DagFold` |
   | `OP` / `STATE` / `REJ` | `op` / `tree` / `rejection` | your operation, state and rejection types, as `DOMAIN` names them |
   | `APPLY` | `wapply` | `OP -> STATE -> outcome STATE REJ` — the GUARDED apply where your apply is only lawful on well-formed states; the guard is where an input invariant is stated rather than assumed |
   | `FOOTPRINT` | `op_fp` | `OP -> footprint` |
   | `FOLD` | `skeleton_fold` | the instance's fold; the theorems are `<FOLD>_confluence` and `<FOLD>_confluence_halt` |
   | `DIAMOND` | `op_independence_diamond` | **the obligation** — your proof of `independence_diamond` at `FOOTPRINT` and `APPLY` |
   | `PROD_APPLY` / `PROD_FOOTPRINT` / `PROD_FOLD` | `Ops.apply` / `Ops.footprint` / `FoldConfluence.foldOnce` | the production names your comments cite; prose only |
   | `HEADER` | the opening comment's interior | what your composite says, which boundaries remain and where each is stated, the licence line |
   | `TRAILER` | `batch_lanes_fold` and its comment | your own evidence after the theorem — keep a non-vacuity witness here, or nothing goes red when your alphabet is quietly narrowed |

   **What the template does not give.** The apply-engine preservation theorems (`../Preservation.fst`),
   the diff's (`../TreeDiff.fst`) and the tree algebra's own diamond (`../TreeOps.fst`) are stated
   ABOUT the skeleton-op tree algebra, not generically over a domain. A domain whose state IS that
   tree under those operations inherits all of them by copying the files and declaring the copies —
   it instantiates nothing. A domain with its own algebra models its own apply, and `{{DIAMOND}}` is
   the theorem it proves about it before the template applies; `../Preservation.fst` is the shape to
   model preservation against, not a template for it.
3. **Check.** Add `DagFold`, your domain module and the instance to `$modules` in
   `<repo>/proofs/check.ps1`, in dependency order (a model follows what it opens), one budget entry
   each in `modules.json`, `pwsh ./proofs/check.ps1`, and commit the oracle it extracts. The leg
   checks the instance from a cold cache like any other model and diffs its extraction like any
   other; nothing about it is special, which is the point.

**What your `proofs.json` rows then say.** One `proved` row per theorem the instance yields —
`<FOLD>_confluence` and `<FOLD>_confluence_halt` — with `evidence.theorem` naming it and
`evidence.model` naming your instance file. The obligation you discharged is a `proved` row at YOUR
domain, `evidence.theorem` your `DIAMOND` lemma in your domain module: this repository's ladder
carries `independence-diamond` as `assumed` / `domain-obligation` because the fold theorem is
generic, and at an instance it is what `tree-independence-diamond` is here. `lanes-apply` stays an
`assumed` / `domain-obligation` row, `dischargedBy: Conformance.concurrencyLaws`, because it is a
statement about a lane set in hand and no instance proves it. A guarded `APPLY` carries an
`assumed` / `domain-obligation` row for its input invariant, `dischargedBy: Conformance.opAlgebra`,
as `tree-algebra-well-formed-states` does here. And every `model-bridge` and `premise` row this
ladder carries about a model you copied is a row you inherit — a copy of `../Chain.fst` is a copy of
`content-id-determines-content`. The kit ships the leg that runs your ladder family; what each row is
held to is a property of your tree, and the family is yours to write.

## The pin moves in one place

The pinned release is `proofs/fstar-pin.json` in **this** repository. An adopting repository's copy
is a copy, and the copy registry is what says when it is behind:

```json
{
  "kind": "copies",
  "producer": "<your-repo>",
  "records": [
    {
      "source": "proofs/fstar-pin.json",
      "consumers": ["<workspace-relative path to YOUR copy>"],
      "check": "bytes",
      "regen": "copy it from the kit's repository"
    }
  ]
}
```

Two details of that shape are easy to get wrong, and both are the registry's, not the kit's:
`source` resolves relative to **the declaring file's own directory** (so a repository stays clonable
standalone), and each `consumers` entry resolves relative to **the workspace root** (because it
names a file in a different repository). `check` is `bytes` or `fingerprint` — `fingerprint`
normalises line endings and trailing whitespace, which is what you want for a text file two
platforms will check out differently, and `bytes` is what you want for anything where a byte is a
byte. An unrecognised value is refused rather than defaulted. A model's record is the same shape with
`source: proofs/DagFold.fst` and `check: fingerprint`; the `$sources` entry is the record with the
`consumers` path left for you to fill.

The sweep is warn-first and offline: it names a drifted copy and the command that regenerates it,
and it never edits anything.

## What this kit does not solve

A copy is still a copy. The registry tells you a copy has drifted; it does not pull the change
through, and it cannot tell you whether the drift matters. That is the trade this shape was chosen
for: it buys an adopting repository a proof leg for the cost of a `cp`, with no package, no
reference and no coupled release — and it pays for that by making the sync a named, visible act
instead of an automatic one. A repository that wants the automatic version wants a package, and a
package is a different decision with a different cost.
