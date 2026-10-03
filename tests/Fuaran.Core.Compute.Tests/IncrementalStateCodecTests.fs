module Fuaran.Compute.Tests.IncrementalStateCodecTests

open Expecto
open Fuaran.Core
open Fuaran.Compute

// ---------------------------------------------------------------------------
//  Phase 355 — the incremental state's wire form.
//
//  The generated family (`IncrementalDelta.stateLaws`) certifies the claim the
//  codec exists for: a refresh from a decoded state is a refresh from the
//  original, over every pair the incremental corpus draws. The hand-written
//  cases below pin what that corpus cannot reach or cannot say: that the
//  package's own SHA-256 is the substrate's, that a cell comes back under the
//  case it left under, that a pipeline is recognised by its canonical encoding
//  and not by the value a consumer happens to build, and the exact refusal for
//  each way an encoding can be unusable.
// ---------------------------------------------------------------------------

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private idw = RowIdentity.byColumn "id"

let private table (rows: (string * Cell * Cell) list) : Table =
    { Schema = [ "id", StringType; "a", IntType; "b", IntType ]
      Columns =
        [ Column.create "id" StringType (rows |> List.map (fun (i, _, _) -> Str i))
          Column.create "a" IntType (rows |> List.map (fun (_, a, _) -> a))
          Column.create "b" IntType (rows |> List.map (fun (_, _, b) -> b)) ] }

let private baseRows =
    [ "r0", Int 1, Int 0
      "r1", Int 2, Int 0
      "r2", Int 3, Int 1
      "r3", Int 4, Int 1
      "r4", Int 5, Int 2 ]

let private baseTable = table baseRows

let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

/// A filter, a derive and a maintained group with a step after it: every cache the state holds.
let private pipeline: Transform list =
    [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
      Derive("twice", Binary(Mul, Col "a", Lit(Int 2)))
      GroupBy([ "b" ], [ agg "total" Sum "twice"; agg "first" First "a" ])
      Derive("more", Binary(Add, Col "total", Lit(Int 1))) ]

let private roundTrip (s: IncrementalEval) : IncrementalEval =
    ok (IncrementalCodec.decode (ok (IncrementalCodec.encode s)))

/// Is `f` the negative zero? Structural equality cannot say: `-0.0 = 0.0`.
let private isNegativeZero (f: float) = f = 0.0 && 1.0 / f < 0.0

[<Tests>]
let tests =
    testList
        "Incremental state codec"
        [

          // ================= the generated family =================

          testCase "the state-codec family is green across seeds"
          <| fun _ ->
              for seed in [ 1; 7; 99; 20260821 ] do
                  for r in IncrementalDelta.stateLaws seed 60 do
                      Expect.isTrue r.Passed (sprintf "seed %d — %s: %A" seed r.Law r.Counterexample)

          // ================= the digest =================

          testCase "the package's SHA-256 is the substrate's, across the block boundary"
          <| fun _ ->
              // The copy exists because this package may not reference the package the digest
              // lives in; this is what holds the copy to it. 55, 56, 63, 64 and 65 bytes are the
              // padding's edges (a 56-byte message is the shortest that needs a second block), and
              // the ill-formed strings are built from code units, never written as literals.
              let lone = string (char 0xD800)
              let low = string (char 0xDC00)

              let corpus =
                  [ ""
                    "abc"
                    "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"
                    String.replicate 55 "a"
                    String.replicate 56 "a"
                    String.replicate 63 "a"
                    String.replicate 64 "a"
                    String.replicate 65 "a"
                    String.replicate 1000 "a"
                    String.replicate 100000 "ab"
                    "é — ✓ 😀 \u0001\u0010"
                    String.replicate 21 "😀é"
                    lone
                    low + lone
                    "x" + lone + "y" + low ]

              for s in corpus do
                  Expect.equal (StateWire.sha256Hex s) (Hash.sha256Hex s) (sprintf "a %d-unit string" s.Length)

          // ================= exact cells =================

          testCase "a cell comes back under the case it left under"
          <| fun _ ->
              let cells =
                  [ Null
                    Int 0
                    Int -7
                    Bool true
                    Bool false
                    Str ""
                    Str "a \"quoted\" \u0001 é 😀"
                    Float 1.5
                    Float 3.0
                    Float 1e300
                    Float 5e-324
                    Float infinity
                    Float(-infinity)
                    Date "2026-10-03"
                    Timestamp "2026-10-03T07:28:39Z"
                    Decimal "12.50"
                    Decimal "12.5" ]

              for c in cells do
                  let wire = Canon.render (StateWire.cellJson c)

                  let back =
                      ok (Json.parseDetailed wire |> Result.mapError NotJson)
                      |> StateWire.cellOfJson false

                  Expect.equal back (Ok c) (sprintf "%A through %s" c wire)

              // The three a structural comparison cannot hold: a NaN is unequal to itself, and the
              // two zeros are equal to each other.
              let through (c: Cell) =
                  Canon.render (StateWire.cellJson c)
                  |> Json.parseDetailed
                  |> Result.mapError NotJson
                  |> Result.bind (StateWire.cellOfJson false)

              (match through (Float nan) with
               | Ok(Float f) -> Expect.isTrue (System.Double.IsNaN f) "a NaN comes back a NaN"
               | other -> failtestf "a NaN came back %A" other)

              (match through (Float(-0.0)) with
               | Ok(Float f) -> Expect.isTrue (isNegativeZero f) "the negative zero keeps its sign"
               | other -> failtestf "the negative zero came back %A" other)

              (match through (Float 0.0) with
               | Ok(Float f) -> Expect.isFalse (isNegativeZero f) "the positive zero keeps its sign"
               | other -> failtestf "the positive zero came back %A" other)

              Expect.isError
                  (through (Unchecked.defaultof<Cell>))
                  "the no-cell slot is not a cell outside a step's cache"

          testCase "a source the column codec would normalise comes back as it was held"
          <| fun _ ->
              // An int in a float column, decimal text that is not canonical, a column shorter than
              // the table and a column the schema does not name: the column codec returns each to a
              // normal form or refuses it, and the evaluator reads all four. The state holds the
              // table it was evaluated over.
              let source: Table =
                  { Schema = [ "id", StringType; "f", FloatType; "m", DecimalType; "gone", IntType ]
                    Columns =
                      [ Column.create "f" FloatType [ Int 3; Float 2.5; Null ]
                        Column.create "id" StringType [ Str "r0"; Str "r1"; Str "r2" ]
                        Column.create "m" DecimalType [ Decimal "12.50"; Decimal "7" ]
                        Column.create "extra" IntType [ Int 1; Int 2; Int 3 ] ] }

              let p = [ Derive("g", Binary(Add, Col "f", Lit(Int 1))) ]
              let state = ok (Incremental.primeOn idw p source)
              let back = roundTrip state
              Expect.equal (Incremental.source back) source "the source, cell for cell and column for column"
              Expect.equal (Incremental.result back) (Incremental.result state) "the result"
              Expect.equal (IncrementalCodec.keyOf back) (IncrementalCodec.keyOf state) "the key"

              Expect.notEqual
                  (IncrementalCodec.sourceFingerprint source)
                  (IncrementalCodec.sourceFingerprint
                      { source with
                          Columns =
                              source.Columns
                              |> List.map (fun c ->
                                  if c.Name = "f" then
                                      Column.create "f" FloatType [ Float 3.0; Float 2.5; Null ]
                                  else
                                      c) })
                  "an int and the float it widens to are two cells, so two fingerprints"

          // ================= the round trip =================

          testCase "a restricted refresh resumes from a decoded state at the cost it had"
          <| fun _ ->
              let state = ok (Incremental.primeOn idw pipeline baseTable)

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r2" then i, Int 30, b else i, a, b)
                  )

              let delta = ok (Delta.diff idw baseTable after)
              let original = ok (Incremental.refreshOn idw pipeline state delta after)
              let resumed = ok (Incremental.refreshOn idw pipeline (roundTrip state) delta after)
              Expect.equal (Incremental.result resumed) (Incremental.result original) "the same table"
              Expect.equal (Incremental.footprint resumed) (Incremental.footprint original) "the same footprint"

              Expect.equal
                  (Ok(Incremental.result resumed))
                  (DataFrame.evalPipeline pipeline after)
                  "the reference's table"

              (match (Incremental.footprint resumed).Recompute with
               | GroupsRecomputed(_, 1) -> ()
               | other -> failtestf "one group moved, so one is recomputed; got %A" other)

              // And from the consumer's side of a process boundary: the delta measured against the
              // DECODED state's own source, which is the table a fresh process holds.
              let decoded = roundTrip state
              let delta' = ok (Delta.diff idw (Incremental.source decoded) after)
              let resumed' = ok (Incremental.refreshOn idw pipeline decoded delta' after)
              Expect.equal (Incremental.result resumed') (Incremental.result original) "the same table"
              Expect.equal (Incremental.footprint resumed') (Incremental.footprint original) "the same footprint"

          testCase "a quiet delta over a decoded state hands the prior result back"
          <| fun _ ->
              let decoded = roundTrip (ok (Incremental.primeOn idw pipeline baseTable))
              let again = table baseRows
              let delta = ok (Delta.diff idw (Incremental.source decoded) again)
              let next = ok (Incremental.refreshOn idw pipeline decoded delta again)
              Expect.equal (Incremental.footprint next).Recompute ReusedPrior "nothing changed, so nothing ran"

          testCase "the encoding is canonical: one state, one string, on every encode"
          <| fun _ ->
              let state = ok (Incremental.primeOn idw pipeline baseTable)
              let text = ok (IncrementalCodec.encode state)
              Expect.equal (IncrementalCodec.encode state) (Ok text) "encode is a function"
              Expect.equal (IncrementalCodec.encode (roundTrip state)) (Ok text) "and the decoded state encodes to it"

              Expect.isTrue
                  (text.StartsWith "{\"$type\":\"incrementalState\",\"body\":{")
                  "the document is tagged, body first"

              // The encoder assembles the document from its members' texts; what it writes is what
              // the canonical renderer writes for the same value.
              Expect.equal
                  (Json.parseDetailed text |> Result.map Canon.render)
                  (Ok text)
                  "the document is its own canonical rendering"

              let detached = ok (IncrementalCodec.encodeDetached state)

              Expect.equal
                  (Json.parseDetailed detached |> Result.map Canon.render)
                  (Ok detached)
                  "and so is the detached one"

          testCase "a state the chunked path built round-trips, and a table refresh from it agrees"
          <| fun _ ->
              let derives =
                  [ Derive("twice", Binary(Mul, Col "a", Lit(Int 2)))
                    Derive("more", Binary(Add, Col "twice", Col "b")) ]

              let state =
                  ok (Incremental.primeOnPrepared idw derives (DataFrame.prepare baseTable))

              Expect.isSome (Incremental.chunksTouched state) "the chunked path built it"
              let decoded = roundTrip state
              Expect.equal (Incremental.result decoded) (Incremental.result state) "the result"
              Expect.equal (Incremental.chunksTouched decoded) (Incremental.chunksTouched state) "the chunk count"

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 9, b else i, a, b)
                  )

              let delta = ok (Delta.diff idw baseTable after)

              Expect.equal
                  (Incremental.refreshOn idw derives decoded delta after
                   |> Result.map (fun s -> Incremental.result s, Incremental.footprint s))
                  (Incremental.refreshOn idw derives state delta after
                   |> Result.map (fun s -> Incremental.result s, Incremental.footprint s))
                  "a refresh over a table from either state"

          // ================= the key =================

          testCase "a pipeline is recognised by its canonical wire string, not by the value built"
          <| fun _ ->
              // An int in a float column and the float it widens to are one cell on the column
              // wire, so the two relations below are two VALUES and one wire string. A decoded
              // state holds the codec's normal form (the float), which is structurally neither the
              // value that was encoded nor the one the consumer builds again; the hash is what
              // recognises the consumer's pipeline.
              let relation (k: Cell) : Table =
                  { Schema = [ "k", FloatType ]
                    Columns = [ Column.create "k" FloatType [ k ] ] }

              let written = [ Join(Embedded(relation (Int 1)), [ "b", "k" ], Semi) ]
              let normal = [ Join(Embedded(relation (Float 1.0)), [ "b", "k" ], Semi) ]
              Expect.notEqual written normal "two values"
              Expect.equal (IncrementalCodec.pipelineHash written) (IncrementalCodec.pipelineHash normal) "one hash"

              let state = ok (Incremental.primeOn idw written baseTable)
              let decoded = roundTrip state
              Expect.equal (Incremental.pipelineOf decoded) normal "the decoded state holds the normal form"
              Expect.equal (IncrementalCodec.keyOf decoded) (IncrementalCodec.keyOf state) "under the same key"

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 9, b else i, a, b)
                  )

              let delta = ok (Delta.diff idw baseTable after)
              let original = ok (Incremental.refreshOn idw written state delta after)
              let resumed = ok (Incremental.refreshOn idw written decoded delta after)

              Expect.equal
                  (Incremental.footprint resumed)
                  (Incremental.footprint original)
                  "the consumer's value resumes"

              Expect.equal (Incremental.result resumed) (Incremental.result original) "to the same table"
              Expect.equal (Incremental.pipelineOf resumed) written "and the refreshed state holds the consumer's value"

          testCase "a pipeline with another wire string is another pipeline, however close its normal form"
          <| fun _ ->
              // `12.50` and `12.5` are two wire strings whose normal form is one value. The decoded
              // state holds that value, so structural equality would take `12.5` for the state's own
              // pipeline and answer its unchanged rows with cells `12.50` computed. The hash does not.
              let written = [ Derive("m", Lit(Decimal "12.50")) ]
              let close = [ Derive("m", Lit(Decimal "12.5")) ]
              Expect.notEqual (IncrementalCodec.pipelineHash written) (IncrementalCodec.pipelineHash close) "two hashes"

              let decoded = roundTrip (ok (Incremental.primeOn idw written baseTable))
              Expect.equal (Incremental.pipelineOf decoded) close "the normal form IS the close pipeline's value"
              let delta = ok (Delta.diff idw baseTable baseTable)

              (match
                  (Incremental.footprint (ok (Incremental.refreshOn idw close decoded delta baseTable))).Recompute
               with
               | FullRecompute(_, PipelineChanged) -> ()
               | other -> failtestf "the close pipeline is not the state's; got %A" other)

              (match
                  (Incremental.footprint (ok (Incremental.refreshOn idw written decoded delta baseTable))).Recompute
               with
               | ReusedPrior -> ()
               | other -> failtestf "the written pipeline is the state's; got %A" other)

          testCase "another pipeline over a decoded state is a full evaluation reporting PipelineChanged"
          <| fun _ ->
              let decoded = roundTrip (ok (Incremental.primeOn idw pipeline baseTable))
              let other = pipeline @ [ Derive("again", Col "more") ]
              let delta = ok (Delta.diff idw baseTable baseTable)
              let next = ok (Incremental.refreshOn idw other decoded delta baseTable)

              (match (Incremental.footprint next).Recompute with
               | FullRecompute(_, PipelineChanged) -> ()
               | other -> failtestf "expected a full evaluation for PipelineChanged; got %A" other)

              Expect.equal (Ok(Incremental.result next)) (DataFrame.evalPipeline other baseTable) "and the right answer"

          testCase "a detached encoding is smaller, needs its source, and resumes over it"
          <| fun _ ->
              let state = ok (Incremental.primeOn idw pipeline baseTable)
              let carrying = ok (IncrementalCodec.encode state)
              let detached = ok (IncrementalCodec.encodeDetached state)
              Expect.isLessThan detached.Length carrying.Length "the source is not on the wire"

              (match IncrementalCodec.decode detached with
               | Error(Malformed m) -> Expect.stringContains m "decodeOver" "the refusal names the remedy"
               | other -> failtestf "a detached encoding read without its source: %A" other)

              // A table EQUAL to the source, built afresh: what a second process holds.
              let mine = table baseRows
              let decoded = ok (IncrementalCodec.decodeOver mine detached)

              Expect.isTrue
                  (obj.ReferenceEquals(Incremental.source decoded, mine))
                  "the consumer's table is the state's source"

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r4" then i, Int 50, b else i, a, b)
                  )

              let delta = ok (Delta.diff idw mine after)
              let resumed = ok (Incremental.refreshOn idw pipeline decoded delta after)

              let original =
                  ok (Incremental.refreshOn idw pipeline state (ok (Delta.diff idw baseTable after)) after)

              Expect.equal (Incremental.result resumed) (Incremental.result original) "the same table"
              Expect.equal (Incremental.footprint resumed) (Incremental.footprint original) "the same footprint"

          testCase "a state decoded over a source it was not built for evaluates in full and says so"
          <| fun _ ->
              let state = ok (Incremental.primeOn idw pipeline baseTable)
              let detached = ok (IncrementalCodec.encodeDetached state)

              // One cell away from the source the state saw. A delta measured against THIS table
              // would name no row, and the caches would answer it with the other table's cells.
              let foreign =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r1" then i, Int 200, b else i, a, b)
                  )

              let decoded = ok (IncrementalCodec.decodeOver foreign detached)
              let delta = ok (Delta.diff idw foreign foreign)
              Expect.isTrue (Delta.isQuiet delta) "the delta the consumer would measure is quiet"
              let next = ok (Incremental.refreshOn idw pipeline decoded delta foreign)

              (match (Incremental.footprint next).Recompute with
               | FullRecompute(_, DeltaIsFullRefresh) -> ()
               | other -> failtestf "expected a full evaluation for DeltaIsFullRefresh; got %A" other)

              Expect.equal
                  (Ok(Incremental.result next))
                  (DataFrame.evalPipeline pipeline foreign)
                  "the right answer, not the cached one"

              // The same holds for a carrying encoding read over a table that is not its source.
              let decoded' =
                  ok (IncrementalCodec.decodeOver foreign (ok (IncrementalCodec.encode state)))

              let next' = ok (Incremental.refreshOn idw pipeline decoded' delta foreign)
              Expect.equal (Incremental.footprint next') (Incremental.footprint next) "from a carrying encoding too"

              // Such a state holds no caches, so it is not one to store.
              (match IncrementalCodec.encode decoded with
               | Error(Malformed _) -> ()
               | other -> failtestf "a state with no caches and a foreign source was encoded: %A" other)

              // The refresh repairs it: the state it returns is an ordinary one.
              Expect.isOk (IncrementalCodec.encode next) "the refreshed state encodes"

          // ================= refusals =================

          testCase "each unusable encoding is refused under its own code"
          <| fun _ ->
              let text =
                  ok (IncrementalCodec.encode (ok (Incremental.primeOn idw pipeline baseTable)))

              (match IncrementalCodec.decode "" with
               | Error(NotJson _) -> ()
               | other -> failtestf "the empty string: %A" other)

              (match IncrementalCodec.decode (text.Substring(0, text.Length / 2)) with
               | Error(NotJson _) -> ()
               | other -> failtestf "half an encoding: %A" other)

              (match IncrementalCodec.decode "[]" with
               | Error(MalformedShape _) -> ()
               | other -> failtestf "an array: %A" other)

              (match IncrementalCodec.decode "{\"$type\":\"rowSet\",\"body\":{},\"digest\":\"\"}" with
               | Error(UnknownType("rowSet", [ "incrementalState" ])) -> ()
               | other -> failtestf "another document: %A" other)

              // One digit of one cached cell: still JSON, still the right shape, no longer the body
              // the digest was taken over.
              let at = text.IndexOf "\"tokens\":[\"k:s:r0\""
              Expect.isGreaterThan at 0 "the token array is where the test expects it"
              let altered = text.Replace("\"tokens\":[\"k:s:r0\"", "\"tokens\":[\"k:s:r9\"")

              (match IncrementalCodec.decode altered with
               | Error(Malformed m) -> Expect.stringContains m "damaged" "the refusal says what it found"
               | other -> failtestf "an altered encoding: %A" other)

              // Another version, with a digest that is honestly its own.
              let body = text.Substring(text.IndexOf "\"body\":" + 7)
              let body = body.Substring(0, body.LastIndexOf ",\"digest\":")
              let future = body.Replace("\"version\":1}", "\"version\":2}")
              Expect.notEqual future body "the version member is where the test expects it"

              let reissued =
                  "{\"$type\":\"incrementalState\",\"body\":"
                  + future
                  + ",\"digest\":\""
                  + Hash.sha256Hex future
                  + "\"}"

              (match IncrementalCodec.decode reissued with
               | Error(Malformed m) -> Expect.stringContains m "version 2" "the refusal names the version"
               | other -> failtestf "a version this reader does not hold: %A" other)

          testCase "a state holding an ill-formed string is refused at encode"
          <| fun _ ->
              let lone = string (char 0xD800)

              let source: Table =
                  { Schema = [ "id", StringType; "s", StringType ]
                    Columns =
                      [ Column.create "id" StringType [ Str "r0" ]
                        Column.create "s" StringType [ Str("x" + lone) ] ] }

              let state = ok (Incremental.primeOn idw [ Derive("t", Col "s") ] source)

              (match IncrementalCodec.encode state with
               | Error(Malformed m) -> Expect.stringContains m "ill-formed" "the refusal names the defect"
               | other -> failtestf "an unpaired surrogate was encoded: %A" other) ]
