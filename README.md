# Fuaran.Core.Compute

A typed dataframe for F# and Fable: a serializable transform algebra with a pure reference
evaluator whose semantics are pinned for cross-host parity, a **certified incremental evaluator**
(a refresh that costs the rows that changed, proved and tested equal to a full evaluation), and a
**columnar op-stream** — table edits as an append-only, hash-chained, replayable, tamper-evident
stream.

FSharp.Core-only and Fable-clean. Apache-2.0.

These packages shipped from the [Fuaran.Core](https://github.com/Fuaran-Core/fuaran-core) repository
through `0.32.0` and are produced here from `0.33.0` on, **under the same package ids and the same
namespaces** — a consumer raises a version and changes nothing else. Why they moved is
[`DECISIONS.md`](DECISIONS.md) D1.

## Packages

| Package | What it owns | Built over |
|---|---|---|
| **`Fuaran.Core.DataFrame`** | the declarative-compute layer: a serializable `Transform` / `ColExpr` algebra (the verb set below), a pure reference evaluator with pinned null, coercion, ordering and float semantics (the cross-host parity contract), a canonical wire codec, the change-relevance `evalFrom`, the typed row delta (`TableDelta`, `Delta.diff`, an associative composition) and the incremental evaluator (`Incremental.plan` / `prime` / `refresh`) | `Fuaran.Core.Column`, `Fuaran.Core.Wire` |
| **`Fuaran.Core.Column.Ops`** | the columnar op algebra: a `ColumnOp` union (`SetCell` / `SetColumn` / `InsertColumn` / `RemoveColumn` / `AppendRows` / `ApplyTransform`) with a total `apply` / `canApply`, a partial `invert` (undo/redo), a structural `Diff`, a wire codec and a `StreamWitness` — so table edits ride `Fuaran.Core.OpStream` — plus `deltaOf` and `changedColumns`, the bridges into incremental evaluation | `DataFrame`, `Fuaran.Core.OpStream` |
| **`Fuaran.Core.DataFrame.PipelineQuery`** | the registered pipeline query: a substrate `Query` declaration paired with the `Transform` pipeline that is its body (`PipelineQuery`), a registry on the substrate's default-deny pattern that admits a pair only when the pipeline's static output schema is closed and IS the declared result schema and its parameter reads agree with the declared parameters both ways and by type (`PipelineQueryRegistry.register` / `enumerate` / `dispatch`, reusing the substrate's `DuplicateQuery` / `NoSuchQuery` and its `Deferred` dispatch), and a canonical wire codec | `DataFrame`, `Fuaran.Core.Query` |
| **`Fuaran.Core.DataFrame.Conformance`** | the law families over the packages above, beside the substrate's law kit they extend: `transformLaws` (host-evaluator parity against the reference), `aggregateParityLaws`, `columnarOpLaws` / `columnarOpLawsWith`, `incrementalLaws` / `incrementalLawsWith`, `IncrementalDelta.laws` / `lawsWith`, `paramLaws`, `schemaWalkLaws`, `nowLaws`, `slotParamLaws` — in module `DataFrameConformance` — and `PipelineQueryConformance.laws` | `Fuaran.Core.Conformance` |

Dependency order: `DataFrame` → `Column.Ops` → `DataFrame.PipelineQuery` → `DataFrame.Conformance`. **This table is a derived roster, not a hand-kept list**: the suite holds its rows equal
to the packable projects under `src/`.

## The verb set

A pipeline is a `Transform list`, evaluated left to right over a table. Every value below is data —
it has a canonical wire form, so a pipeline can be stored, diffed, sent and evaluated by another host.

| `Transform` | What it does |
|---|---|
| `Filter of ColExpr` | keep the rows the predicate holds for (a `Having` is a `Filter` after a `GroupBy`) |
| `Project of (string * string) list` | select and rename columns |
| `Derive of string * ColExpr` | add or replace a column computed per row |
| `GroupBy of string list * Agg list` | group and aggregate (`sum`, `mean`, `min`, `max`, `count`, `median`, `stdDev`, `first`, `last`, `countDistinct`) |
| `Join of DataSource * keys * JoinKind` | `Inner`, `Left`, `Right`, `Outer`, `Semi`, `Anti` |
| `Window of WindowSpec` | `RowNumber`, `Rank`, `DenseRank`, `CompetitionRank`, `NTile`, `Lag`, `Lead`, `CumulSum`, `CumulMax`, `CumulMin`, `RollingMean`, `RollingSum` |
| `Pivot` / `Unpivot` | reshape wide ↔ long |
| `Sort` / `Distinct` / `Limit` | order (keys may be parameter slots), de-duplicate, page |
| `Union` / `Intersect` / `Except` | set operations against another source |

| `ColExpr` | |
|---|---|
| `Col`, `Lit`, `Param`, `Now` | a column, a literal cell, a named parameter, the pinned evaluation clock |
| `Binary` | `Add`, `Sub`, `Mul`, `Div`, `Mod`, `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge`, `And`, `Or`, `Contains`, `StartsWith`, `EndsWith` |
| `Not`, `Coalesce`, `Case`, `Cast`, `IsNull`, `InList`, `InParam` | logic, null handling, branching, typing, membership |
| `ApplyFn` | `Abs`, `Round`, `Floor`, `Ceil`, `Sqrt`, `Least`, `Greatest`, `Length`, `Lower`, `Upper`, `Substr`, `Trim`, `Replace`, `Concat`, `IndexOf`, `DatePart`, `DateDiffDays` |
| `Quotient`, `Rounded` | an exact (decimal) quotient, and an exact number brought to a stated `Rounding` — a scale slot and a `RoundingMode` |

## Adoption, in five lines

```fsharp
open Fuaran.Core

let pipeline = [ Filter(Binary(Gt, Col "v", Lit(Int 2))); GroupBy([ "g" ], [ { Name = "s"; Fn = Sum; Of = "v" } ]) ]
let full = DataFrame.evalPipeline pipeline table                              // the reference answer
let idw = RowIdentity.byColumn "id"                                             // how a row is keyed
let state = Incremental.primeOn idw pipeline table |> Result.defaultWith (fun e -> failwithf "%A" e)
let next = Incremental.refreshOn idw pipeline state (Delta.diff idw table table' |> Result.defaultValue FullRefresh) table'
```

`Incremental.result next` equals `DataFrame.evalPipeline pipeline table'` for every delta, whichever
internal path ran — that equality is the contract; the saving is the implementation detail.
`Incremental.plan pipeline` says, before anything runs, whether a refresh will be restricted.
[`docs/incremental-evaluation.md`](docs/incremental-evaluation.md) is the full on-ramp.

A consumer that evaluates many pipelines over one source — a sheet, a dashboard — prepares the
source once and pays the `Table` boundary once (`0.34.0`):

```fsharp
let prepared = DataFrame.prepare table                                          // one typed unpack per column
let a = DataFrame.evalPrepared DataFrame.noResolve Map.empty pipelineA prepared // = DataFrame.evalPipeline pipelineA table
let b = DataFrame.evalPrepared DataFrame.noResolve Map.empty pipelineB prepared
let state = Incremental.primeOnPrepared idw pipelineA prepared                  // = Incremental.primeOn idw pipelineA table
```

`Prepared` is opaque; `evalPrepared` takes the resolver, env and pipeline exactly as
`DataFrame.evalPipelineWithInEnv` does, and answers the same cells and the same errors.

## What incremental evaluation costs — measured, and where full evaluation wins

The incremental evaluator is **certified**: its agreement with the reference evaluator is a
generated law family (`IncrementalDelta.laws`) run over every step class, and the pipeline evaluator's
cost model is an F\* theorem (see [Proofs](#proofs)). What it is **not** is always faster, and the
measurements say where:

- **A pipeline that shrinks the table wins.** 20,000 rows, one row edited, one comparison per row:
  `Filter > GroupBy` refreshes in 13.0 ms against a 26.3 ms full evaluation (2.0×);
  `Filter > Sort > Limit 10` in 25.8 ms against 55.7 ms (2.2×); `Filter > GroupBy > Filter` in
  10.4 ms against 16.7 ms (1.6×). The suite's `Scaling` family asserts all three.
- **A pipeline that keeps every row loses above about 1,000 rows** — measured by a downstream
  consumer: a `Derive`-only node refreshed at 0.48× to 0.88× of a full evaluation's speed from
  10,000 rows up, on every kind of edit, and a group-by node lost on a column edit at every size.
  End to end, that consumer's sheet never paid for incremental refresh above 1,000 rows.
- **The floor is one pass over the new source.** A refresh is handed the whole table and must read
  it to know what moved; what scales with the delta is everything else, and
  `IncrementalRefreshCostTests` counts that, clock-free.

So: `plan` tells you whether a refresh will be restricted, never whether it will be faster for your
pipeline. Measure your own before adopting it for a row-preserving shape.

## Composing with `Propagation` — an edit, one refreshed row, a column-granular dirty set

`Column.Ops` is the bridge between a table edit and the two evaluators downstream of it. Worked once,
over an `orders` table of 1,000 rows keyed by `id` and a `lines` pipeline that derives
`amount = qty * price`:

```fsharp
open Fuaran.Core

let rid = RowIdentity.byColumn "id"
let op = SetCell("price", row, newPrice)                        // one cell of the 1,000-row table
let orders' = ColumnOps.apply op orders |> Result.defaultWith (failwithf "%A")

// The row half: deltaOf names the ONE row the edit moved, so the refresh evaluates one row.
let delta = ColumnOps.deltaOf rid orders op                     // RowSet [ ByKey …, RowChanged ]
let next = Incremental.refresh DataFrame.noResolve Map.empty rid lines state delta orders'
// (Incremental.footprint next).Recompute = RowsRecomputed 1

// The column half: changedColumns is the changed-parts function Propagation reads.
let dirty =
    Propagation.dirtyFromChangedParts partDeps (fun _ -> ColumnOps.changedColumns op) (Set.singleton "orders")
// every node reading `price`, and everything downstream of it — not a node that reads only `id`
```

- **One row, where a column invalidation is every row.** The same edit handed to the refresh as a
  column change (`Delta.ofChange rid.Scheme (ColumnOps.changeOf op)`) re-evaluates all 1,000 rows.
  `deltaOf` is one row for a cell edit, the rows whose cell moved for a column edit, the removed and
  added keys for a key edit, and `FullRefresh` wherever the op or the row identity cannot say more (a
  schema change, a whole-table transform, an append reusing a key or carrying none).
- **Only the first hop narrows.** `changedColumns` is `None` for an append, which moves every column,
  and the dirty set then degrades to the node-level one. A node that reads a recomputed node is always
  dirty: which of its columns moved is not known until it is recomputed.
- **The sheet.** The `PropagationComposition` suite composes both halves: a source edited through
  `Column.Ops`, a formula tree edited by `UpdateNode`, driven by `Propagation.evalFromWith` with each
  table node's incremental state carried as its prior, and certified by the substrate's
  `Conformance.propagationEvaluatorLawsWith` — including two evaluators the family must refuse, one
  that trusts a stale prior and one whose prior-blind reading drifts from the reference. The sheet
  is Phase 250's, carried here from the Fuaran.Core suite by Phase 261. `Propagation`, `Ops` and
  `Tree` are test references of that suite only; no package here takes them.

Whether the one-row refresh beats a full evaluation is the question the section above answers by
pipeline shape — for a row-preserving node like `lines`, measure first
([`docs/incremental-evaluation.md`](docs/incremental-evaluation.md), Phase 250).

## How it sits on the substrate

The compute strand stands on four `Fuaran.Core` packages and on nothing above them:
`Fuaran.Core.Column` (the typed, null-aware columnar `Table` and its canonical codec) and
`Fuaran.Core.Wire` (the canonical JSON) are what the dataframe is built over;
`Fuaran.Core.OpStream` records columnar edits; `Fuaran.Core.Conformance` is the law kit the
dataframe families extend. All four are taken **by package**, at one pin (`FuaranCoreVersion` in [`Directory.Packages.props`](Directory.Packages.props)),
and the `Compute boundary` tests refuse a fifth — or any reference that is not a public package.

## The law kit

`Fuaran.Core.Conformance` is a property-based law kit a domain runs against its own witness; this
repository's `Fuaran.Core.DataFrame.Conformance` adds the families over the dataframe layer, reading
the kit's `ConfRng`, `LawResult`, `StreamGen` and adequacy guard and nothing private of it. A host
with its own dataframe evaluator certifies against `transformLaws`, and a host that cannot link this
package reads the same reference answers from
[`conformance/laws/transform-laws.json`](conformance/laws/transform-laws.json) — emitted here,
stamped with this repository's version, and carried as a declared copy in the shared wire-format
corpus.

## Building and testing

Requirements: the .NET SDK pinned in [`global.json`](global.json).

```powershell
./run.ps1            # restore tools, format, sweep, build, test
./verify.ps1         # format-check + publication sweep + build + tests (the gate)
./verify.ps1 -Proofs # ... and the F* proof leg (downloads the pinned prover once)
./pack.ps1           # the three packages into a local folder feed
```

The substrate packages restore from nuget.org; `nuget.config` also names a local folder feed so a
maintainer's fresh pack of the substrate shadows the released package at the same version.

## Benchmarks

`benchmarks/Fuaran.Core.Compute.Benchmarks` is a BenchmarkDotNet harness over a fixed corpus: a
spreadsheet-shaped sheet (two nodes over an `orders` table, each beside a hand-written
arrays-and-loops arm that is the yardstick), the three `Scaling` pipelines, and a join, a
high-cardinality group-by, a pivot, a window and a two-key sort. Allocations are measured on every
benchmark. The verify gate builds it and never runs it; run it by hand, in Release:

```powershell
dotnet run -c Release --project benchmarks/Fuaran.Core.Compute.Benchmarks -- --check        # assert the corpus, time nothing
dotnet run -c Release --project benchmarks/Fuaran.Core.Compute.Benchmarks -- --filter '*'   # every benchmark
dotnet run -c Release --project benchmarks/Fuaran.Core.Compute.Benchmarks -- --filter '*Sheet*'
```

The node leg times the same corpus through the Fable-compiled sources. It needs `node` and a Fable
toolchain, which this repository does not carry: point `-FableFrom` at a directory whose dotnet tool
manifest provides `fable`.

```powershell
pwsh benchmarks/Fuaran.Core.Compute.Benchmarks/Node/run-node.ps1 -FableFrom <dir> [-Runs 10]
```

Recorded runs are under [`benchmarks/results/`](benchmarks/results/), one file per date and machine.

## Proofs

`proofs/ColumnOps.fst` models the columnar op algebra — `apply` / `canApply` / `invert` /
`Diff.toOps` over a table with a validity mask, with totality, all-or-nothing rejection, the dry
run's agreement, well-formedness preservation and `invert`'s round trip proved.
`proofs/Pipeline.fst` models the counted pipeline driver and its expression evaluator — totality,
budget monotonicity, and the evaluator's work bounded by the expression's size. Each model is
checked by a pinned F\* release and extracted to F#; the committed extraction under `proofs/oracle/`
is held to a fresh one, and the `Proofs.Oracle` family runs it beside production. The leg's scripts
under `proofs/kit/` are adopted by copy from the Fuaran.Core repository and declared in
[`copies.json`](copies.json).

## Releases

A `v*` tag runs [`.github/workflows/publish-packages.yml`](.github/workflows/publish-packages.yml),
which packs the three packages and pushes them to nuget.org under **Trusted Publishing** — an OIDC
exchange, no API key. **The nuget.org trusted-publishing policy for THIS repository
(`Fuaran-Core/fuaran-core-compute`, workflow `publish-packages.yml`) must be registered by the package
owner before the first tag**; until it is, the run fails at the `NuGet/login` step. That failure
reads like an authentication problem and is the missing registration. A branch push publishes
nothing. [`STABILITY.md`](STABILITY.md) records each version's changes by class.

## License

Apache-2.0 — see [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).
