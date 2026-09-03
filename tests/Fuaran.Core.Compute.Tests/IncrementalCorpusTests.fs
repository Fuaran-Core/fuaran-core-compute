module Fuaran.Core.Tests.IncrementalCorpusTests

open System
open System.IO
open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  Phase 115 — the incremental seam measured against a VENDORED corpus vector
//  rather than against a table this file made up.
//
//  The estate carries a conformance family — `incremental-recompute`, §12.7 of
//  the app-composition wire specification — whose vectors each pair a pipeline,
//  a source, an edit stream and a required result with a RECORDED FOOTPRINT
//  TRIPLE: what a prime over the source cost, what a full evaluation over the
//  changed source cost, and what advancing the primed state cost. Vectors are
//  vendored under `fixtures/incremental-recompute/` (see the README there) so
//  these legs run in any clone of this repository, exactly as the vocabulary
//  spikes vendor theirs.
//
//  Phase 115's two are a PAIR, and the pairing is the whole point.
//
//   * `point-edit-row-local` is the CONTROL. Its pipeline was already
//     incrementalisable before this phase, so every one of its three recorded
//     footprints must still be reproduced exactly. A widening that moved a
//     number here would have changed what the seam costs on work it was already
//     doing, which is a regression however much it saved elsewhere.
//   * `sort-declines-in-full` is the vector the widening is ABOUT. Its recorded
//     triple is three full evaluations, declined for `sort` — the footprint
//     this repository produced until this phase. Its RESULT must not move, and
//     its class must: that is the saving, and the numbers are read off the
//     vendored bytes on both sides of the comparison rather than typed here.
//
//  The vector's own recorded class is therefore not an oracle for this
//  repository any more; the vector's RESULT still is, and the corpus's rule
//  still derives the declined reason it records. Re-recording the triple on the
//  corpus side is that specification's act, not this one's.
// ---------------------------------------------------------------------------

// ---- locating the vendored corpus ----

/// The vectors this repository vendors. `point-edit-row-local` and `sort-declines-in-full` are
/// Phase 115's pair; `window-declines-in-full` and `join-declines-in-full` are Phase 120's, each
/// recording the DECLINED triple its class produced before that phase.
/// `rank-declines-in-full` is `0.19.0`'s, on the same terms — and its recorded decline is the one
/// Phase 120 itself produced (`windowFrameUnbounded` / `rank`), because that is the evaluator this
/// widening is measured against.
let private vectorNames =
    [ "point-edit-row-local"
      "sort-declines-in-full"
      "window-declines-in-full"
      "join-declines-in-full"
      "rank-declines-in-full" ]

let private isCorpus (dir: string) : bool =
    try
        vectorNames
        |> List.forall (fun n -> File.Exists(Path.Combine(dir, n + ".json")))
    with _ ->
        false

let private vendoredCorpus () : string option =
    let rec climb (dir: string) (budget: int) : string option =
        if budget < 0 || isNull dir then
            None
        else
            let cand = Path.Combine(dir, "tests", "Fuaran.Core.Tests")

            if File.Exists(Path.Combine(cand, "IncrementalTests.fs")) then
                Some cand
            else
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)
    |> Option.map (fun projectDir -> Path.Combine(projectDir, "fixtures", "incremental-recompute"))

let private resolveCorpus () : Result<string, string> =
    match Environment.GetEnvironmentVariable "FUARAN_INCREMENTAL_CORPUS" with
    | ovr when not (String.IsNullOrWhiteSpace ovr) ->
        if isCorpus ovr then
            Ok ovr
        else
            Error(
                sprintf
                    "FUARAN_INCREMENTAL_CORPUS is set to '%s', which is not a corpus: it must hold %s. Unset it to use the vendored corpus."
                    ovr
                    (vectorNames |> List.map (fun n -> n + ".json") |> String.concat ", ")
            )
    | _ ->
        match vendoredCorpus () with
        | Some dir when isCorpus dir -> Ok dir
        | Some dir -> Error(sprintf "the vendored corpus at '%s' is missing or malformed" dir)
        | None ->
            Error
                "could not locate tests/Fuaran.Core.Tests (IncrementalTests.fs marker) from the CWD or the test binary"

// ---- reading one vector ----
//
// The reader REFUSES a member it does not model rather than skipping it. A vector using a verb, an
// operator or an edit op this repository's reader silently ignored would be certified against a
// pipeline that is not the one the corpus wrote, which is worse than a vector nobody ran.

let private fail (what: string) = failtestf "vector: %s" what

let private mem (name: string) (v: JVal) : JVal =
    match v with
    | JObj kvs ->
        match kvs |> List.tryFind (fun (k, _) -> k = name) with
        | Some(_, x) -> x
        | None -> fail ("missing member '" + name + "'")
    | _ -> fail ("expected an object to read '" + name + "' from")

let private tryMem (name: string) (v: JVal) : JVal option =
    match v with
    | JObj kvs -> kvs |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
    | _ -> None

let private str (v: JVal) : string =
    match v with
    | JStr s -> s
    | _ -> fail "expected a string"

let private int_ (v: JVal) : int =
    match v with
    | JInt i -> i
    | _ -> fail "expected an integer"

let private arr (v: JVal) : JVal list =
    match v with
    | JArr xs -> xs
    | _ -> fail "expected an array"

/// A cell. `{"null": true}` is THIS REPOSITORY's spelling, introduced with the Phase 120 window
/// vector: a bounded frame's first row in each partition has no predecessor, so a `lag` column
/// cannot avoid one. The corpus that owns this family has not recorded a window vector yet, so it
/// has not spelled a null cell either; when it does, this reader adopts the corpus's spelling and
/// the vendored bytes follow it. See the README beside the vectors.
let private cellOf (v: JVal) : Cell =
    match tryMem "int" v, tryMem "string" v, tryMem "null" v with
    | Some x, _, _ -> Int(int_ x)
    | _, Some x, _ -> Str(str x)
    | _, _, Some(JBool true) -> Null
    | _ -> fail "a cell is {\"int\": n}, {\"string\": s} or {\"null\": true}"

let private columnTypeOf (s: string) : ColumnType =
    match s with
    | "int" -> IntType
    | "string" -> StringType
    | other -> fail ("unmodelled column type '" + other + "'")

let private tableOf (v: JVal) : Table =
    let cols =
        v
        |> mem "columns"
        |> arr
        |> List.map (fun c -> str (mem "name" c), columnTypeOf (str (mem "type" c)))

    let rows =
        v
        |> mem "rows"
        |> arr
        |> List.map (fun r -> r |> mem "cells" |> arr |> List.map cellOf)

    { Schema = cols
      Columns =
        cols
        |> List.mapi (fun i (n, ty) -> Column.create n ty (rows |> List.map (List.item i))) }

let rec private exprOf (v: JVal) : ColExpr =
    match str (mem "expr" v) with
    | "column" -> Col(str (mem "name" v))
    | "literal" -> Lit(Int(int_ (mem "int" v)))
    | "binary" ->
        let l = exprOf (mem "left" v)
        let r = exprOf (mem "right" v)

        match str (mem "op" v) with
        | "greaterThan" -> Binary(Gt, l, r)
        | "multiply" -> Binary(Mul, l, r)
        | other -> fail ("unmodelled operator '" + other + "'")
    | other -> fail ("unmodelled expression '" + other + "'")

let private aggOf (v: JVal) : Agg =
    let fn =
        match str (mem "fn" v) with
        | "count" -> Count
        | "sum" -> Sum
        | "first" -> First
        | other -> fail ("unmodelled aggregate '" + other + "'")

    { Name = str (mem "name" v)
      Fn = fn
      Of = str (mem "of" v) }

let private orderOf (v: JVal) : string * SortDir =
    let dir =
        match str (mem "direction" v) with
        | "ascending" -> Asc
        | "descending" -> Desc
        | other -> fail ("unmodelled direction '" + other + "'")

    str (mem "column" v), dir

let private windowFnOf (s: string) : WindowFn =
    match s with
    | "lag" -> Lag
    | "lead" -> Lead
    // `0.19.0` — the seam admits every window function now, and this reader models one MORE than it
    // did, not all of them. What it models is what the vendored bytes use; the refusal below is a
    // statement about which spellings these bytes have been read against, and it stays narrow for
    // the same reason it was narrow before.
    | "rank" -> Rank
    | other -> fail ("unmodelled window function '" + other + "'")

let private joinKindOf (s: string) : JoinKind =
    match s with
    | "semi" -> Semi
    | "anti" -> Anti
    | other -> fail ("unmodelled join kind '" + other + "'")

let rec private stepOf (v: JVal) : Transform =
    match str (mem "verb" v) with
    | "filter" -> Filter(exprOf (mem "where" v))
    | "derive" -> Derive(str (mem "column" v), exprOf (mem "value" v))
    | "groupBy" -> GroupBy(v |> mem "keys" |> arr |> List.map str, v |> mem "aggregates" |> arr |> List.map aggOf)
    | "sort" -> Sort(v |> mem "by" |> arr |> List.map orderOf)
    // Phase 120. The reader models the WINDOW FUNCTIONS and the JOIN KINDS the vendored vectors
    // use and refuses the rest by name, on the same rule as every other member here: a vector
    // whose `cumulSum` this reader silently read as a `lag` would certify a frame the corpus did
    // not write. That refusal is not a statement about what the seam admits — `Incremental.plan`
    // is — it is a statement about what these BYTES have been read against.
    | "window" ->
        Window
            { PartitionBy = v |> mem "partitionBy" |> arr |> List.map str
              OrderBy = v |> mem "orderBy" |> arr |> List.map orderOf
              Fn = windowFnOf (str (mem "fn" v))
              Of = str (mem "of" v)
              As = str (mem "as" v) }
    | "join" ->
        Join(
            Embedded(tableOf (mem "source" v)),
            v
            |> mem "on"
            |> arr
            |> List.map (fun pair -> str (mem "left" pair), str (mem "right" pair)),
            joinKindOf (str (mem "how" v))
        )
    | other -> fail ("unmodelled verb '" + other + "'")

let private recomputeOf (v: JVal) : Recompute =
    match str (mem "kind" v) with
    | "primed" -> Primed(int_ (mem "rowsEvaluated" v))
    | "reusedPrior" -> ReusedPrior
    | "rowsRecomputed" -> RowsRecomputed(int_ (mem "rowsEvaluated" v))
    | "groupsRecomputed" -> GroupsRecomputed(int_ (mem "rowsEvaluated" v), int_ (mem "groupsRecomputed" v))
    | "fullRecompute" ->
        // `rowsEvaluated` is required here, exactly as it is on every other counting kind
        // (Phase 117). A full evaluation that recorded no count could only be compared with a
        // restricted one by inventing a number for it, which is what reading it off `sourceRows`
        // amounted to.
        let n = int_ (mem "rowsEvaluated" v)

        match str (mem "reason" v) with
        | "stepNotRowLocal" -> FullRecompute(n, StepNotRowLocal(str (mem "verb" v)))
        | "aggregateStepNotLast" -> FullRecompute(n, AggregateStepNotLast(str (mem "verb" v)))
        | "ordinalAddressing" -> FullRecompute(n, OrdinalAddressing)
        // `0.19.0` — the rank vector's recorded "before" is the decline PHASE 120 produced, so the
        // reader has to model a reason this repository no longer emits. That is the ordinary shape
        // of a before/after vector here: the bytes describe the evaluator being improved on, and a
        // reader that could not read them could not measure anything.
        | "windowFrameUnbounded" -> FullRecompute(n, WindowFrameUnbounded(str (mem "fn" v)))
        | other -> fail ("unmodelled decline reason '" + other + "'")
    | other -> fail ("unmodelled recompute kind '" + other + "'")

let private footprintOf (v: JVal) : RecomputeFootprint =
    { SourceRows = int_ (mem "sourceRows" v)
      ResultRows = int_ (mem "resultRows" v)
      Recompute = recomputeOf (mem "recompute" v) }

/// One decoded vector: everything a run of it needs, and everything it claims.
type private Vector =
    { Name: string
      KeyColumn: string
      Pipeline: Transform list
      Source: Table
      Changed: Table
      Result: Table
      Prime: RecomputeFootprint
      Full: RecomputeFootprint
      Refresh: RecomputeFootprint }

/// Apply the vector's edit stream to its source, producing the changed source. `identity` only —
/// an `ordinal` stream is refused rather than run as an identity one, which is the distinction the
/// family exists to hold.
let private applyEdits (keyColumn: string) (source: Table) (ops: JVal list) : Table =
    let names = source.Schema |> List.map fst

    let keyIdx =
        match names |> List.tryFindIndex (fun n -> n = keyColumn) with
        | Some i -> i
        | None -> fail ("the key column '" + keyColumn + "' is not in the source")

    let rows0 =
        [ for i in 0 .. Table.rowCount source - 1 ->
              source.Schema
              |> List.map (fun (n, _) ->
                  match Table.tryColumn n source with
                  | Some c -> Column.cell i c
                  | None -> Null) ]

    let keyOf (row: Cell list) =
        match List.item keyIdx row with
        | Str s -> s
        | other -> string other

    let rows =
        (rows0, ops)
        ||> List.fold (fun rows op ->
            match str (mem "op" op) with
            | "setCell" ->
                let key = str (mem "row" op)
                let colName = str (mem "column" op)

                let ci =
                    match names |> List.tryFindIndex (fun n -> n = colName) with
                    | Some i -> i
                    | None -> fail ("setCell names a column the source does not carry: '" + colName + "'")

                let value = cellOf (mem "value" op)

                rows
                |> List.map (fun r ->
                    if keyOf r = key then
                        r |> List.mapi (fun j c -> if j = ci then value else c)
                    else
                        r)
            | "appendRow" -> rows @ [ op |> mem "cells" |> arr |> List.map cellOf ]
            | "removeRow" ->
                let key = str (mem "row" op)
                rows |> List.filter (fun r -> keyOf r <> key)
            | other -> fail ("unmodelled edit op '" + other + "'"))

    { Schema = source.Schema
      Columns =
        source.Schema
        |> List.mapi (fun i (n, ty) -> Column.create n ty (rows |> List.map (List.item i))) }

let private readVector (dir: string) (name: string) : Vector =
    let text = File.ReadAllText(Path.Combine(dir, name + ".json"))

    let v =
        match Json.parse text with
        | Ok v -> v
        | Error e -> fail (name + " is not valid JSON: " + e)

    let edits = mem "edits" v

    match str (mem "scheme" edits) with
    | "identity" -> ()
    | other ->
        fail (
            "this reader runs identity-addressed vectors only; '"
            + name
            + "' is '"
            + other
            + "'"
        )

    let source = tableOf (mem "source" v)
    let expect = mem "expect" v

    { Name = name
      KeyColumn = str (mem "key" edits)
      Pipeline = v |> mem "pipeline" |> arr |> List.map stepOf
      Source = source
      Changed = applyEdits (str (mem "key" edits)) source (edits |> mem "ops" |> arr)
      Result = tableOf (mem "result" expect)
      Prime = footprintOf (mem "prime" expect)
      Full = footprintOf (mem "full" expect)
      Refresh = footprintOf (mem "refresh" expect) }

// ---- running one vector ----

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

/// Run a vector's three evaluations: prime over the source, a full evaluation over the changed
/// source, and a refresh from the primed state. Returns the three footprints and the refresh's
/// result — the shape §12.7's pass criteria are stated over.
let private run (vec: Vector) =
    let idw = RowIdentity.byColumn vec.KeyColumn
    let primed = ok (Incremental.primeOn idw vec.Pipeline vec.Source)
    let full = ok (Incremental.primeOn idw vec.Pipeline vec.Changed)
    let delta = ok (Delta.diff idw vec.Source vec.Changed)
    let refreshed = ok (Incremental.refreshOn idw vec.Pipeline primed delta vec.Changed)
    primed.Footprint, full.Footprint, refreshed

let private corpus =
    lazy
        (match resolveCorpus () with
         | Ok dir -> dir
         | Error e -> failtest e)

[<Tests>]
let tests =
    testList
        "IncrementalCorpus"
        [

          testCase "the control vector's result AND all three recorded footprints are reproduced"
          <| fun _ ->
              // Already row-local before this phase. Every number here is one the seam produced
              // before the sort widening and must produce after it: a widening that moved a
              // footprint on work the seam was already restricting would be a regression, whatever
              // it saved on the work it newly reaches.
              let vec = readVector corpus.Value "point-edit-row-local"
              let prime, full, refreshed = run vec

              Expect.equal refreshed.Output vec.Result "the refresh produces the recorded result"

              Expect.equal
                  (Ok vec.Result)
                  (DataFrame.evalPipeline vec.Pipeline vec.Changed)
                  "and so does a full reference evaluation over the changed source"

              Expect.equal prime vec.Prime "the recorded prime footprint"
              Expect.equal full vec.Full "the recorded full-evaluation footprint"
              Expect.equal refreshed.Footprint vec.Refresh "the recorded refresh footprint"

          testCase "the sort vector's result is unchanged and its class is not: the recorded saving"
          <| fun _ ->
              // The vector this phase is about. Its pipeline is a filter followed by a sort; its
              // recorded triple is three full evaluations declined for `sort`, which is what this
              // repository produced until the widening. The equality is the pass criterion and the
              // counts are the evidence — both read from the vendored bytes.
              let vec = readVector corpus.Value "sort-declines-in-full"
              let prime, full, refreshed = run vec

              // (1) The pass criterion, unchanged by the widening and the reason it is safe.
              Expect.equal refreshed.Output vec.Result "the refresh produces the recorded result"

              Expect.equal
                  (Ok vec.Result)
                  (DataFrame.evalPipeline vec.Pipeline vec.Changed)
                  "and so does a full reference evaluation over the changed source"

              // (2) The recorded footprint IS the declined one — asserted rather than described, so
              // the "before" side of the saving is the corpus's number and not this file's. Its
              // recorded count is the declining evaluator's own row evaluations at steps (the
              // filter, over six rows), on the same scale this repository now reports.
              Expect.equal
                  vec.Refresh.Recompute
                  (FullRecompute(6, StepNotRowLocal "sort"))
                  "the vendored vector records a decline naming the sort, with what it evaluated"

              Expect.equal
                  (Incremental.rowsEvaluated vec.Refresh)
                  6
                  "and a declined refresh re-evaluates the filter over every row"

              // (3) The class this repository now produces, and the measured saving: one filter
              // predicate re-evaluated where six were before. The sort itself contributes nothing
              // to the count — it evaluates no expression, exactly as a groupBy does not.
              Expect.equal (Incremental.plan vec.Pipeline).Strategy RowLocal "the pipeline is no longer declined"

              // The PRIME and the FULL sides of the recorded triple are reproduced exactly: a
              // declining evaluator and this one prime identically, because a prime avoids nothing
              // whatever the plan says. Only the refresh moves, and that difference is the saving.
              Expect.equal prime vec.Prime "the recorded prime footprint"
              Expect.equal full vec.Full "the recorded full-evaluation footprint"

              Expect.equal prime.Recompute (Primed 6) "priming evaluates the filter over every row"
              Expect.equal full.Recompute (Primed 6) "so does a full evaluation over the changed source"
              Expect.equal refreshed.Footprint.Recompute (RowsRecomputed 1) "the refresh re-evaluates one row"

              Expect.equal refreshed.Footprint.SourceRows vec.Refresh.SourceRows "over the same source"
              Expect.equal refreshed.Footprint.ResultRows vec.Refresh.ResultRows "producing the same result rows"

              Expect.isLessThan
                  (Incremental.rowsEvaluated refreshed.Footprint)
                  (Incremental.rowsEvaluated full)
                  "strictly fewer row-evaluations than the full evaluation it is measured against"

          testCase "the window vector's result is unchanged and its class is not: the recorded saving"
          <| fun _ ->
              // Phase 120's first vector, on exactly the terms Phase 115's sort vector was written
              // on. Its pipeline is a filter followed by a BOUNDED-frame window (a lag over the
              // tie-heavy partition key); its recorded refresh is the full evaluation declined for
              // `window`, which is what this repository produced until the frame was admitted.
              let vec = readVector corpus.Value "window-declines-in-full"
              let prime, full, refreshed = run vec

              Expect.equal refreshed.Output vec.Result "the refresh produces the recorded result"

              Expect.equal
                  (Ok vec.Result)
                  (DataFrame.evalPipeline vec.Pipeline vec.Changed)
                  "and so does a full reference evaluation over the changed source"

              // The "before": the vendored bytes' own declined class, asserted rather than
              // described. A window evaluates no expression, so the six are the filter's.
              Expect.equal
                  vec.Refresh.Recompute
                  (FullRecompute(6, StepNotRowLocal "window"))
                  "the vendored vector records a decline naming the window, with what it evaluated"

              // The "after".
              Expect.equal (Incremental.plan vec.Pipeline).Strategy RowLocal "the pipeline is no longer declined"

              Expect.equal prime vec.Prime "the recorded prime footprint"
              Expect.equal full vec.Full "the recorded full-evaluation footprint"
              Expect.equal refreshed.Footprint.Recompute (RowsRecomputed 1) "the refresh re-evaluates one row"

              Expect.isLessThan
                  (Incremental.rowsEvaluated refreshed.Footprint)
                  (Incremental.rowsEvaluated vec.Refresh)
                  "strictly fewer row-evaluations than the decline it replaces"

          testCase "the join vector's result is unchanged and its class is not: the recorded saving"
          <| fun _ ->
              // Phase 120's second vector: a filter followed by a SEMI join against a two-row
              // lookup, with the edit moving a row's key OUT of the relation — so the verdict the
              // join caches for that row is exactly what has to be recomputed, and every other
              // row's is exactly what may be reused.
              let vec = readVector corpus.Value "join-declines-in-full"
              let prime, full, refreshed = run vec

              Expect.equal refreshed.Output vec.Result "the refresh produces the recorded result"

              Expect.equal
                  (Ok vec.Result)
                  (DataFrame.evalPipeline vec.Pipeline vec.Changed)
                  "and so does a full reference evaluation over the changed source"

              Expect.equal
                  vec.Refresh.Recompute
                  (FullRecompute(6, StepNotRowLocal "join"))
                  "the vendored vector records a decline naming the join, with what it evaluated"

              Expect.equal (Incremental.plan vec.Pipeline).Strategy RowLocal "the pipeline is no longer declined"

              Expect.equal prime vec.Prime "the recorded prime footprint"
              Expect.equal full vec.Full "the recorded full-evaluation footprint"
              Expect.equal refreshed.Footprint.Recompute (RowsRecomputed 1) "the refresh re-evaluates one row"

              // The join drops a row the filter kept, so the result is SMALLER than the prime's —
              // the recorded triple carries three result-row counts, not one.
              Expect.equal prime.ResultRows 3 "the prime kept three rows"

              Expect.equal
                  refreshed.Footprint.ResultRows
                  2
                  "and the refresh two, the moved key having left the relation"

              Expect.isLessThan
                  (Incremental.rowsEvaluated refreshed.Footprint)
                  (Incremental.rowsEvaluated vec.Refresh)
                  "strictly fewer row-evaluations than the decline it replaces"

          testCase "the rank vector's result is unchanged and its class is not: the recorded saving"
          <| fun _ ->
              // `0.19.0`'s vector, on exactly the terms the three before it were written on — and
              // the one whose "before" is the NEAREST evaluator rather than the oldest: its
              // recorded refresh is the decline Phase 120 itself produced, naming the window
              // FUNCTION, because frame boundedness is what this widening relaxed.
              let vec = readVector corpus.Value "rank-declines-in-full"
              let prime, full, refreshed = run vec

              Expect.equal refreshed.Output vec.Result "the refresh produces the recorded result"

              Expect.equal
                  (Ok vec.Result)
                  (DataFrame.evalPipeline vec.Pipeline vec.Changed)
                  "and so does a full reference evaluation over the changed source"

              // The "before": Phase 120's own decline, asserted from the bytes rather than
              // described. A window evaluates no expression, so the six are the filter's — the
              // identical count the bounded-frame vector records, which is the point: the two
              // families cost the same and were classified differently.
              Expect.equal
                  vec.Refresh.Recompute
                  (FullRecompute(6, WindowFrameUnbounded "rank"))
                  "the vendored vector records the frame-boundedness decline, with what it evaluated"

              // The "after", and the identical saving: six row-evaluations to one.
              Expect.equal (Incremental.plan vec.Pipeline).Strategy RowLocal "the pipeline is no longer declined"

              Expect.equal
                  (Incremental.plan vec.Pipeline).Steps
                  [ PropagateRows; RecomputeFrame([ "b" ], [ "a", Asc ]) ]
                  "and it classifies as the case a bounded frame already did"

              Expect.equal prime vec.Prime "the recorded prime footprint"
              Expect.equal full vec.Full "the recorded full-evaluation footprint"
              Expect.equal refreshed.Footprint.Recompute (RowsRecomputed 1) "the refresh re-evaluates one row"

              Expect.isLessThan
                  (Incremental.rowsEvaluated refreshed.Footprint)
                  (Incremental.rowsEvaluated vec.Refresh)
                  "strictly fewer row-evaluations than the decline it replaces"

          testCase "the rank vector's saving is the window vector's, to the row-evaluation"
          <| fun _ ->
              // The whole argument for the relaxation, as an assertion: the two families are not
              // merely both admissible, they cost the same. A widening that bought less on the
              // partition-global family would mean frame boundedness was tracking something real
              // after all.
              let bounded = readVector corpus.Value "window-declines-in-full"
              let global_ = readVector corpus.Value "rank-declines-in-full"

              let _, boundedFull, boundedRefresh = run bounded
              let _, globalFull, globalRefresh = run global_

              Expect.equal
                  (Incremental.rowsEvaluated globalRefresh.Footprint)
                  (Incremental.rowsEvaluated boundedRefresh.Footprint)
                  "the refresh costs the same"

              Expect.equal
                  (Incremental.rowsEvaluated globalFull)
                  (Incremental.rowsEvaluated boundedFull)
                  "measured against the same full baseline"

              Expect.equal
                  (Incremental.rowsEvaluated global_.Refresh)
                  (Incremental.rowsEvaluated bounded.Refresh)
                  "and the declines they replace cost the same too"

          testCase "a corpus this reader cannot fully model is refused, not partly run"
          <| fun _ ->
              // The reader's refusals are the reason its greens mean anything: a vector using a
              // verb it silently skipped would certify a pipeline the corpus did not write.
              Expect.throws (fun () -> stepOf (JObj [ "verb", JStr "pivot" ]) |> ignore) "an unmodelled verb is refused"

              // The reader models three window functions and two join kinds, not the families they
              // belong to — an unmodelled member of a verb the reader DOES model is the refusal
              // most likely to be skipped, so it is the one asserted. `cumulSum` is refused here
              // while `Incremental.plan` ADMITS it (`0.19.0`), which is the distinction the README
              // states: what these bytes have been read against is not what the seam can evaluate.
              Expect.throws (fun () -> windowFnOf "cumulSum" |> ignore) "an unmodelled window function is refused"

              Expect.throws
                  (fun () ->
                      recomputeOf (
                          JObj
                              [ "kind", JStr "fullRecompute"
                                "rowsEvaluated", JInt 1
                                "reason", JStr "envChanged" ]
                      )
                      |> ignore)
                  "an unmodelled decline reason is refused"

              Expect.throws (fun () -> joinKindOf "inner" |> ignore) "an unmodelled join kind is refused"

              Expect.throws
                  (fun () ->
                      exprOf (
                          JObj
                              [ "expr", JStr "binary"
                                "op", JStr "modulo"
                                "left", JObj []
                                "right", JObj [] ]
                      )
                      |> ignore)
                  "an unmodelled operator is refused"

              Expect.throws (fun () -> readVector corpus.Value "no-such-vector" |> ignore) "a missing vector is refused" ]
