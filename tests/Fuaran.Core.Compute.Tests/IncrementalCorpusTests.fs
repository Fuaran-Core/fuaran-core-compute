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
//  changed source cost, and what advancing the primed state cost. Two of its
//  vectors are vendored under `fixtures/incremental-recompute/` (see the README
//  there) so these legs run in any clone of this repository, exactly as the
//  vocabulary spikes vendor theirs.
//
//  They are a PAIR, and the pairing is the whole point.
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

let private isCorpus (dir: string) : bool =
    try
        File.Exists(Path.Combine(dir, "sort-declines-in-full.json"))
        && File.Exists(Path.Combine(dir, "point-edit-row-local.json"))
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
                    "FUARAN_INCREMENTAL_CORPUS is set to '%s', which is not a corpus: it must hold sort-declines-in-full.json and point-edit-row-local.json. Unset it to use the vendored corpus."
                    ovr
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

let private cellOf (v: JVal) : Cell =
    match tryMem "int" v, tryMem "string" v with
    | Some x, _ -> Int(int_ x)
    | _, Some x -> Str(str x)
    | _ -> fail "a cell is {\"int\": n} or {\"string\": s}"

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

let private stepOf (v: JVal) : Transform =
    match str (mem "verb" v) with
    | "filter" -> Filter(exprOf (mem "where" v))
    | "derive" -> Derive(str (mem "column" v), exprOf (mem "value" v))
    | "groupBy" -> GroupBy(v |> mem "keys" |> arr |> List.map str, v |> mem "aggregates" |> arr |> List.map aggOf)
    | "sort" ->
        Sort(
            v
            |> mem "by"
            |> arr
            |> List.map (fun o ->
                let dir =
                    match str (mem "direction" o) with
                    | "ascending" -> Asc
                    | "descending" -> Desc
                    | other -> fail ("unmodelled direction '" + other + "'")

                str (mem "column" o), dir)
        )
    | other -> fail ("unmodelled verb '" + other + "'")

let private recomputeOf (v: JVal) : Recompute =
    match str (mem "kind" v) with
    | "primed" -> Primed(int_ (mem "rowsEvaluated" v))
    | "reusedPrior" -> ReusedPrior
    | "rowsRecomputed" -> RowsRecomputed(int_ (mem "rowsEvaluated" v))
    | "groupsRecomputed" -> GroupsRecomputed(int_ (mem "rowsEvaluated" v), int_ (mem "groupsRecomputed" v))
    | "fullRecompute" ->
        match str (mem "reason" v) with
        | "stepNotRowLocal" -> FullRecompute(StepNotRowLocal(str (mem "verb" v)))
        | "aggregateStepNotLast" -> FullRecompute(AggregateStepNotLast(str (mem "verb" v)))
        | "ordinalAddressing" -> FullRecompute OrdinalAddressing
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
              // the "before" side of the saving is the corpus's number and not this file's.
              Expect.equal
                  vec.Refresh.Recompute
                  (FullRecompute(StepNotRowLocal "sort"))
                  "the vendored vector records a decline naming the sort"

              Expect.equal
                  (Incremental.rowsEvaluated vec.Refresh)
                  6
                  "and a declined refresh is charged every source row"

              // (3) The class this repository now produces, and the measured saving: one filter
              // predicate re-evaluated where six were before. The sort itself contributes nothing
              // to the count — it evaluates no expression, exactly as a groupBy does not.
              Expect.equal (Incremental.plan vec.Pipeline).Strategy RowLocal "the pipeline is no longer declined"

              Expect.equal prime.Recompute (Primed 6) "priming evaluates the filter over every row"
              Expect.equal full.Recompute (Primed 6) "so does a full evaluation over the changed source"
              Expect.equal refreshed.Footprint.Recompute (RowsRecomputed 1) "the refresh re-evaluates one row"

              Expect.equal refreshed.Footprint.SourceRows vec.Refresh.SourceRows "over the same source"
              Expect.equal refreshed.Footprint.ResultRows vec.Refresh.ResultRows "producing the same result rows"

              Expect.isLessThan
                  (Incremental.rowsEvaluated refreshed.Footprint)
                  (Incremental.rowsEvaluated full)
                  "strictly fewer row-evaluations than the full evaluation it is measured against"

          testCase "a corpus this reader cannot fully model is refused, not partly run"
          <| fun _ ->
              // The reader's refusals are the reason its greens mean anything: a vector using a
              // verb it silently skipped would certify a pipeline the corpus did not write.
              Expect.throws
                  (fun () -> stepOf (JObj [ "verb", JStr "window" ]) |> ignore)
                  "an unmodelled verb is refused"

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
