module Fuaran.Compute.Tests.PreparedEditRefreshTests

// ---------------------------------------------------------------------------
//  Phase 585 — a refresh over an edited prepared version costs the edit, not
//  the table.
//
//  The shape fuaran#1891 measured: a 10,000-row grid, one `SetCell` through
//  `ColumnOps.applyPrepared`, the delta read off the op (`deltaOfPrepared`),
//  and the refresh over the new version — against the same edit diffed
//  (`Delta.diff` over the two versions' tables) and against a full evaluation.
//  A derive-only pipeline takes the chunked path (Phase 268); a filter takes
//  the row-local walk, which keyed the whole new table before this phase,
//  because an op-derived delta carries no record of where its rows sit.
//
//  COUNTED, not timed: the witness counts every key it computes (`KeyOf`), and
//  the footprint counts the rows evaluated. Both are exact on a loaded machine.
//  On the pre-phase tree the op-derived filter refresh keys every row (n), and
//  the bound below is red; the two go-reds rebuild the same version without the
//  edit's record and with every column concatenated afresh, and the bound must
//  be red over each.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

/// `byColumn "id"` under its own scheme, counting every key it computes.
let private countingId (keyed: int ref) : RowIdentity<Cell> =
    let b = RowIdentity.byColumn "id"

    { b with
        KeyOf =
            fun t i ->
                keyed.Value <- keyed.Value + 1
                b.KeyOf t i }

let private columnOf (name: string) (t: Table) : Column =
    match Table.tryColumn name t with
    | Some c -> c
    | None -> failtestf "no column %s" name

let private grid (n: int) : Table =
    { Schema =
        [ Field.create "id" StringType
          Field.create "a" IntType
          Field.create "label" StringType ]
      Columns =
        [ KitColumn.create "id" StringType [ for i in 0 .. n - 1 -> Str("r" + string i) ]
          KitColumn.create "a" IntType [ for i in 0 .. n - 1 -> Int(i % 100) ]
          KitColumn.create "label" StringType [ for i in 0 .. n - 1 -> Str("l" + string (i % 13)) ] ] }

let private filterPipeline: Transform list =
    [ Filter(Binary(Ge, Col "a", Lit(Int 50))) ]

let private derivePipeline: Transform list =
    [ Derive("b", Binary(Add, Col "a", Lit(Int 1))) ]

/// The edit: one cell of a non-key column, in the middle of the table, moved across the filter.
let private edit (n: int) : ColumnOp = SetCell("a", n / 2, Int 99)

/// What one tick cost: the keys the witness computed across the whole tick (deriving the delta and
/// refreshing), the rows the refresh evaluated, and the state it produced.
type private Tick =
    { Keyed: int
      Evaluated: int option
      State: IncrementalEval }

let private evaluatedOf (s: IncrementalEval) : int option =
    match (Incremental.footprint s).Recompute with
    | RowsRecomputed n -> Some n
    | GroupsRecomputed(n, _) -> Some n
    | ReusedPrior -> Some 0
    | _ -> None

/// The op-derived tick over `v1`, the version `op` produced from `v0`.
let private opDerived (pipeline: Transform list) (n: int) (shape: Prepared -> Prepared) : Tick * Prepared =
    let keyed = ref 0
    let idw = countingId keyed
    let v0 = DataFrame.prepare (grid n)
    let s0 = ok (Incremental.primeOnPrepared idw pipeline v0)
    let op = edit n
    let v1 = shape (ok (ColumnOps.applyPrepared op v0))
    keyed.Value <- 0
    let delta = ColumnOps.deltaOfPrepared idw v0 op
    let s1 = ok (Incremental.refreshOnPrepared idw pipeline s0 delta v1)

    { Keyed = keyed.Value
      Evaluated = evaluatedOf s1
      State = s1 },
    v1

/// The diffed tick: the same edit, its delta diffed from the two versions' tables.
let private diffed (pipeline: Transform list) (n: int) : Tick * Prepared =
    let keyed = ref 0
    let idw = countingId keyed
    let v0 = DataFrame.prepare (grid n)
    let s0 = ok (Incremental.primeOnPrepared idw pipeline v0)
    let v1 = ok (ColumnOps.applyPrepared (edit n) v0)
    keyed.Value <- 0
    let delta = ok (Delta.diff idw (DataFrame.toTable v0) (DataFrame.toTable v1))
    let s1 = ok (Incremental.refreshOnPrepared idw pipeline s0 delta v1)

    { Keyed = keyed.Value
      Evaluated = evaluatedOf s1
      State = s1 },
    v1

/// The bound the phase claims for a one-cell edit's op-derived tick: a constant number of keys —
/// the edited row, before and after, to derive the delta, and once more to confirm it in place —
/// whatever the row count.
let private editBound = 4

let private withinBound (t: Tick) = t.Keyed <= editBound

let private agreesWithFull (pipeline: Transform list) (tick: Tick) (v1: Prepared) (what: string) =
    Expect.equal
        (Ok(Incremental.result tick.State))
        (DataFrame.evalPipeline pipeline (DataFrame.toTable v1))
        (what + ": the refresh is the full evaluation")

/// Go-red (a): the edited version with its record dropped — the delta still describes the edit
/// truthfully, but nothing says where the version's rows sit.
let private recordDropped (v: Prepared) : Prepared = { v with Edit = None }

/// Go-red (b): the edited version rebuilt from its rope with every column concatenated afresh —
/// the pre-phase shape of an `applyPrepared` version.
let private columnsRebuilt (v: Prepared) : Prepared =
    Prepared.ofChunks v.Cols v.Count (Prepared.columns v)

[<Tests>]
let preparedEditRefreshTests =
    testList
        "PreparedEditRefresh"
        [ testCase "a filter: one edit to 10,000 rows keys the edit, evaluates one row, and agrees with full"
          <| fun _ ->
              let n = 10_000
              let op, v1 = opDerived filterPipeline n id
              let df, dv1 = diffed filterPipeline n
              agreesWithFull filterPipeline op v1 "op-derived"
              agreesWithFull filterPipeline df dv1 "diffed"

              Expect.isTrue
                  (withinBound op)
                  (sprintf "op-derived keys %d, at most %d (the edit's row)" op.Keyed editBound)

              Expect.isGreaterThanOrEqual df.Keyed n "the diffed tick keys the new table at least once"
              Expect.isLessThan op.Keyed df.Keyed "op-derived costs less key work than diffed"
              Expect.equal op.Evaluated (Some 1) "one row evaluated, at its one step"

          testCase "a filter: the op-derived key work does not grow with the row count"
          <| fun _ ->
              let small, _ = opDerived filterPipeline 1_000 id
              let large, _ = opDerived filterPipeline 10_000 id
              Expect.equal large.Keyed small.Keyed "the same keys at ten times the rows"
              Expect.equal large.Evaluated small.Evaluated "the same rows evaluated at ten times the rows"

          testCase "a derive-only pipeline: one edit refreshes one chunk and agrees with full"
          <| fun _ ->
              let n = 10_000
              let op, v1 = opDerived derivePipeline n id
              let df, dv1 = diffed derivePipeline n
              agreesWithFull derivePipeline op v1 "op-derived"
              agreesWithFull derivePipeline df dv1 "diffed"
              Expect.equal (Incremental.chunksTouched op.State) (Some 1) "one chunk evaluated"
              Expect.isTrue (withinBound op) (sprintf "op-derived keys %d" op.Keyed)
              Expect.isLessThan op.Keyed df.Keyed "op-derived costs less key work than diffed"

          testCase "go-red: the same version without the edit's record keys the whole table"
          <| fun _ ->
              let n = 10_000
              let red, v1 = opDerived filterPipeline n recordDropped
              agreesWithFull filterPipeline red v1 "record dropped"
              Expect.isFalse (withinBound red) (sprintf "the bound must be red here, keyed %d" red.Keyed)
              Expect.isGreaterThanOrEqual red.Keyed n "every row keyed"

          testCase "go-red: the same version with every column rebuilt keys the whole table"
          <| fun _ ->
              let n = 10_000
              let red, v1 = opDerived filterPipeline n columnsRebuilt
              agreesWithFull filterPipeline red v1 "columns rebuilt"
              Expect.isFalse (withinBound red) (sprintf "the bound must be red here, keyed %d" red.Keyed)

          testCase "the edited version's table shares every column the edit did not write"
          <| fun _ ->
              let v0 = DataFrame.prepare (grid 3_000)
              let t0 = DataFrame.toTable v0
              let v1 = ok (ColumnOps.applyPrepared (SetCell("a", 7, Int 1)) v0)
              let t1 = DataFrame.toTable v1

              let same name (a: Table) (b: Table) =
                  obj.ReferenceEquals((columnOf name a).Data, (columnOf name b).Data)

              Expect.isTrue (same "id" t0 t1) "the key column is the prior version's own"
              Expect.isTrue (same "label" t0 t1) "an untouched column is the prior version's own"
              Expect.isFalse (same "a" t0 t1) "the written column is built afresh"
              Expect.equal (Column.cell 7 (columnOf "a" t1)) (Int 1) "and holds the edit"
              Expect.equal (Column.cell 7 (columnOf "a" t0)) (Int 7) "the prior version is unmoved"
              Expect.equal (Ok t1) (ColumnOps.apply (SetCell("a", 7, Int 1)) t0) "toTable (applyPrepared) = apply"

              let rebuilt = DataFrame.toTable (columnsRebuilt v1)
              Expect.isFalse (same "label" t0 rebuilt) "go-red: a rebuilt version shares nothing"

          testCase "a chain of edits, each refreshed, stays in place and agrees with full"
          <| fun _ ->
              let keyed = ref 0
              let idw = countingId keyed
              let n = 5_000
              let mutable v = DataFrame.prepare (grid n)
              let mutable s = ok (Incremental.primeOnPrepared idw filterPipeline v)

              for k in 1..5 do
                  let op = SetCell("a", k * 701, Int(if k % 2 = 0 then 10 else 90))
                  let v' = ok (ColumnOps.applyPrepared op v)
                  keyed.Value <- 0
                  s <- ok (Incremental.refreshOnPrepared idw filterPipeline s (ColumnOps.deltaOfPrepared idw v op) v')
                  v <- v'
                  Expect.isLessThanOrEqual keyed.Value editBound (sprintf "edit %d keys the edit" k)

                  Expect.equal
                      (Ok(Incremental.result s))
                      (DataFrame.evalPipeline filterPipeline (DataFrame.toTable v))
                      (sprintf "edit %d: the full evaluation" k)

          testCase "an edit that moves the row's key, or skips a version, takes the general walk and agrees"
          <| fun _ ->
              let idw = RowIdentity.byColumn "id"
              let n = 2_000
              let v0 = DataFrame.prepare (grid n)
              let s0 = ok (Incremental.primeOnPrepared idw filterPipeline v0)

              let keyMove = SetCell("id", 11, Str "moved")
              let v1 = ok (ColumnOps.applyPrepared keyMove v0)

              let s1 =
                  ok (Incremental.refreshOnPrepared idw filterPipeline s0 (ColumnOps.deltaOfPrepared idw v0 keyMove) v1)

              Expect.equal
                  (Ok(Incremental.result s1))
                  (DataFrame.evalPipeline filterPipeline (DataFrame.toTable v1))
                  "a moved key: the full evaluation"

              // v2 is an edit of v1, but the state was evaluated over v0: the record names v1, so it
              // does not apply, and the diffed delta from v0 is what the refresh is handed.
              let v2 = ok (ColumnOps.applyPrepared (SetCell("a", 3, Int 77)) v1)
              let delta = ok (Delta.diff idw (DataFrame.toTable v0) (DataFrame.toTable v2))
              let s2 = ok (Incremental.refreshOnPrepared idw filterPipeline s0 delta v2)

              Expect.equal
                  (Ok(Incremental.result s2))
                  (DataFrame.evalPipeline filterPipeline (DataFrame.toTable v2))
                  "a skipped version: the full evaluation"

          testCase "a filter feeding a group step reads the edit in place and agrees with full"
          <| fun _ ->
              let keyed = ref 0
              let idw = countingId keyed

              let p =
                  [ Filter(Binary(Ge, Col "a", Lit(Int 50)))
                    GroupBy([ "label" ], [ { Name = "n"; Fn = Count; Of = "a" } ]) ]

              let n = 10_000
              let v0 = DataFrame.prepare (grid n)
              let s0 = ok (Incremental.primeOnPrepared idw p v0)
              let op = edit n
              let v1 = ok (ColumnOps.applyPrepared op v0)
              keyed.Value <- 0

              let s1 =
                  ok (Incremental.refreshOnPrepared idw p s0 (ColumnOps.deltaOfPrepared idw v0 op) v1)

              Expect.equal
                  (Ok(Incremental.result s1))
                  (DataFrame.evalPipeline p (DataFrame.toTable v1))
                  "the full evaluation"

              Expect.isLessThanOrEqual keyed.Value editBound "the edit's keys only" ]
