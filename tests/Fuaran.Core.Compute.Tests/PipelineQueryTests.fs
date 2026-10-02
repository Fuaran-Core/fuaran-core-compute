module Fuaran.Compute.Tests.PipelineQueryTests

// ---------------------------------------------------------------------------
//  Phase 281 — the registered pipeline query, case by case: what registration admits and refuses
//  beyond the law family's reference pair (a decimal report, a grouped one, a join, the open
//  walks), how each position reads a parameter, what dispatch hands the resolver, and the codec's
//  refusals. The law family itself runs here too, at the seed and size the census runs it.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Compute

let private param name ty required : QueryParam =
    { Name = name
      Type = ty
      Required = required }

let private query (id: string) (ps: QueryParam list) (result: Schema) (source: DataSource) : Query =
    { Id = id
      Params = ps
      ResultSchema = result
      Effect =
        { Host = ReadsHost
          Determinism = Effect.network }
      Source = source
      TimeoutMs = None
      PageSize = None }

let private ledger: Schema =
    [ "region", StringType; "amount", DecimalType; "n", IntType ]

let private pair (q: Query) (pipeline: Transform list) : PipelineQuery =
    { Query = q
      Pipeline = pipeline
      Sources = Map.ofList [ "ledger", ledger ] }

let private dec (text: string) : Cell =
    match Cell.decimal text with
    | Some c -> c
    | None -> failwithf "not decimal text: %s" text

/// A daily report: rows of one region at or above a decimal floor, the amount and the region.
let private daily: PipelineQuery =
    pair
        (query
            "daily"
            [ param "floor" DecimalType true; param "region" StringType true ]
            [ "region", StringType; "amount", DecimalType ]
            (Ref "ledger"))
        [ Filter(ColExpr.Binary(Ge, ColExpr.Col "amount", ColExpr.Param "floor"))
          Filter(ColExpr.Binary(Eq, ColExpr.Col "region", ColExpr.Param "region"))
          Project [ "region", "region"; "amount", "amount" ] ]

let private ledgerTable: Table =
    { Schema = ledger
      Columns =
        [ Column.create "region" StringType [ Str "north"; Str "south"; Str "north"; Null ]
          Column.create "amount" DecimalType [ dec "10.50"; dec "3.25"; dec "0.10"; dec "99.99" ]
          Column.create "n" IntType [ Int 1; Int 2; Int 3; Int 4 ] ] }

let private reads (pq: PipelineQuery) =
    PipelineQuery.paramReads pq |> List.distinct

[<Tests>]
let tests =
    testList
        "PipelineQuery"
        [ testCase "the law family is green, and the same seed gives the same report"
          <| fun _ ->
              let results = PipelineQueryConformance.laws 2810 100

              let fails =
                  results
                  |> List.filter (fun r -> not r.Passed)
                  |> List.map (fun r -> r.Law + ": " + defaultArg r.Counterexample "")

              if not fails.IsEmpty then
                  failtestf "pipelineQuery laws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (PipelineQueryConformance.laws 2810 100) results "same seed => identical report"

          testCase "a decimal report registers, reads its floor as a decimal, and dispatches exactly"
          <| fun _ ->
              Expect.equal
                  (reads daily)
                  [ "floor", ParamRead.Scalar(Some DecimalType)
                    "region", ParamRead.Scalar(Some StringType) ]
                  "each read at the type of the column it is compared with"

              let reg =
                  match PipelineQueryRegistry.register daily PipelineQueryRegistry.empty with
                  | Ok r -> r
                  | Error e -> failtestf "refused: %A" e

              let answer =
                  PipelineQueryRegistry.dispatch
                      reg
                      "daily"
                      [ "floor", dec "0.50"; "region", Str "north" ]
                      (fun bound ->
                          match DataFrame.evalPipeline bound.Pipeline ledgerTable with
                          | Ok t ->
                              Ready
                                  { Rows = t
                                    PageNum = 0
                                    TotalRowCount = None
                                    NextPageToken = None }
                          | Error e -> Failed(sprintf "%A" e))

              match answer with
              | Ok(Ready r) ->
                  Expect.equal r.Rows.Schema daily.Query.ResultSchema "the declared schema"

                  Expect.equal
                      (r.Rows.Columns |> List.map _.Cells)
                      [ [ Str "north" ]; [ dec "10.50" ] ]
                      "the one north row at or above 0.50, exactly"
              | other -> failtestf "expected a settled answer, got %A" other

          testCase "a decimal floor declared as a float is refused, naming the type it is read at"
          <| fun _ ->
              let floatFloor =
                  { daily with
                      Query =
                          { daily.Query with
                              Params = [ param "floor" FloatType true; param "region" StringType true ] } }

              Expect.equal
                  (PipelineQuery.check floatFloor)
                  (Error(ParamReadAs("floor", FloatType, DecimalType)))
                  "a decimal is never read through a float"

          testCase "a grouped report registers at the aggregate's own type"
          <| fun _ ->
              let totals =
                  pair
                      (query "totals" [] [ "region", StringType; "total", DecimalType; "rows", IntType ] (Ref "ledger"))
                      [ GroupBy(
                            [ "region" ],
                            [ { Name = "total"
                                Fn = Sum
                                Of = "amount" }
                              { Name = "rows"; Fn = Count; Of = "n" } ]
                        ) ]

              Expect.equal (PipelineQuery.check totals) (Ok()) "the walk types both aggregates"

          testCase "a pivot's columns are the data's, so its walk is open and the pair is refused"
          <| fun _ ->
              let pivoted =
                  pair
                      (query "pivoted" [] [ "n", IntType ] (Ref "ledger"))
                      [ Pivot
                            { Index = [ "n" ]
                              On = "region"
                              Values = "amount"
                              Agg = Sum } ]

              match PipelineQuery.check pivoted with
              | Error(ResultSchemaOpen _) -> ()
              | other -> failtestf "expected ResultSchemaOpen, got %A" other

          testCase "a join reads its named source's declared schema, and an undeclared one is refused by name"
          <| fun _ ->
              let joined =
                  { pair
                        (query
                            "joined"
                            []
                            [ "region", StringType
                              "amount", DecimalType
                              "n", IntType
                              "region_right", StringType
                              "manager", StringType ]
                            (Ref "ledger"))
                        [ Join(Ref "regions", [ "region", "region" ], Inner) ] with
                      Sources =
                          Map.ofList [ "ledger", ledger; "regions", [ "region", StringType; "manager", StringType ] ] }

              Expect.equal (PipelineQuery.check joined) (Ok()) "both named sources declared"

              Expect.equal
                  (PipelineQuery.namedSources joined)
                  [ "ledger"; "regions" ]
                  "the declaration's source, then the join's"

              Expect.equal
                  (PipelineQuery.check
                      { joined with
                          Sources = Map.ofList [ "ledger", ledger ] })
                  (Error(SourceUndeclared("regions", [ "ledger" ])))
                  "the join's source is named, with the ones that are declared"

              // The cause, not the symptom: a Project over an undeclared source closes the column set
              // with types nobody declared, and the refusal still names the source.
              let projected =
                  pair (query "projected" [] [ "r", StringType ] (Ref "elsewhere")) [ Project [ "region", "r" ] ]

              Expect.equal
                  (PipelineQuery.check projected)
                  (Error(SourceUndeclared("elsewhere", [ "ledger" ])))
                  "the undeclared source, not an undecidable type"

          testCase "an embedded source declares its own schema and needs no entry"
          <| fun _ ->
              let embedded =
                  { Query = query "embedded" [] ledger (Embedded ledgerTable)
                    Pipeline = []
                    Sources = Map.empty }

              Expect.equal (PipelineQuery.check embedded) (Ok()) "the table's own schema is the result"

          testCase "a derived column has the type its expression decides; a param or the clock does not (Phase 338)"
          <| fun _ ->
              let derived ty e =
                  pair (query "d" [] (ledger @ [ "label", ty ]) (Ref "ledger")) [ Derive("label", e) ]

              Expect.equal
                  (PipelineQuery.check (derived StringType (ColExpr.Lit(Str "x"))))
                  (Ok())
                  "a string derivation is a string over every frame"

              Expect.equal
                  (PipelineQuery.check (derived IntType (ColExpr.Cast(IntType, ColExpr.Col "n"))))
                  (Ok())
                  "an int derivation is an int over every frame, an empty one included"

              Expect.equal
                  (PipelineQuery.check (derived FloatType (ColExpr.Cast(IntType, ColExpr.Col "n"))))
                  (Error(ResultColumn("label", ResultDisagreement.TypeDiffers(FloatType, IntType))))
                  "a decided derivation declared at another type differs, by name"

              let withParam =
                  { derived IntType (ColExpr.Param "k") with
                      Query =
                          { (derived IntType (ColExpr.Param "k")).Query with
                              Params =
                                  [ { Name = "k"
                                      Type = IntType
                                      Required = true } ] } }

              Expect.equal
                  (PipelineQuery.check withParam)
                  (Error(ResultColumn("label", ResultDisagreement.TypeUndecidable IntType)))
                  "a param derivation's type is the argument's, which only the data decides"

              Expect.equal
                  (PipelineQuery.check (derived DateType (ColExpr.Now NowGrain.Date)))
                  (Error(ResultColumn("label", ResultDisagreement.TypeUndecidable DateType)))
                  "a clock derivation's type is the witness's, which only the data decides"

          testCase "each position decides the type it reads a parameter at, or decides none"
          <| fun _ ->
              let over pipeline =
                  pair (query "p" [] [] (Ref "ledger")) pipeline

              Expect.equal
                  (reads (
                      over
                          [ Filter(ColExpr.InList(ColExpr.Col "region", [ ColExpr.Param "a"; ColExpr.Lit(Str "east") ]))
                            Derive(
                                "q",
                                ColExpr.Quotient(
                                    ColExpr.Col "amount",
                                    ColExpr.Param "b",
                                    { Scale = Slot.Param "c"
                                      Mode = RoundingMode.HalfEven }
                                )
                            )
                            Derive("k", ColExpr.Cast(IntType, ColExpr.Param "d"))
                            Filter(ColExpr.Param "e")
                            Derive("w", ColExpr.Coalesce [ ColExpr.Param "f"; ColExpr.Col "n" ]) ]
                  ))
                  [ "a", ParamRead.Scalar(Some StringType)
                    "b", ParamRead.Scalar None
                    "c", ParamRead.Scalar(Some IntType)
                    "d", ParamRead.Scalar None
                    "e", ParamRead.Scalar(Some BoolType)
                    "f", ParamRead.Scalar(Some IntType) ]
                  "membership peers, an exact operand (any exact type), a scale, a cast operand, a predicate, a coalesce peer"

          testCase "an optional parameter left unbound stays a parameter, so evaluation names it"
          <| fun _ ->
              let optional =
                  { daily with
                      Query =
                          { daily.Query with
                              Params = [ param "floor" DecimalType true; param "region" StringType false ] } }

              let bound = PipelineQuery.substitute [ "floor", dec "1" ] optional

              Expect.equal (Transform.paramsOf bound.Pipeline) [ "region" ] "only the unbound one is left"

              match DataFrame.evalPipeline bound.Pipeline ledgerTable with
              | Error(UnboundParam("region", _)) -> ()
              | other -> failtestf "expected UnboundParam region, got %A" other

          testCase "the codec refuses another $type and a source declared twice"
          <| fun _ ->
              let encoded = PipelineQueryCodec.encode daily
              Expect.equal (PipelineQueryCodec.decode encoded) (Ok daily) "round-trip"

              Expect.isError
                  (PipelineQueryCodec.decode (encoded.Replace("\"pipelineQuery\"", "\"query\"")))
                  "another $type"

              let twice =
                  encoded.Replace("\"sources\":[", "\"sources\":[{\"name\":\"ledger\",\"schema\":[]},")

              match PipelineQueryCodec.decode twice with
              | Error m -> Expect.stringContains m "ledger" "names the source"
              | Ok _ -> failtest "a source declared twice was decoded" ]
