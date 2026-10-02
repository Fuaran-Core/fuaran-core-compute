namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Phase 281 — the law family over the registered pipeline query, on the
//  substrate's `queryLaws` pattern: the declaration half certified as the
//  substrate certifies a `Query` (registry, enumeration, the three dispatch
//  outcomes and the unreachable fourth, the codec), and the half that is new —
//  the agreement between the pipeline and its declaration — certified by
//  BUILDING every refusal registration has, each iteration, over a drawn table.
// ============================================================================

/// The registered-pipeline-query laws (Phase 281).
module PipelineQueryConformance =

    let private fixedSchema: Schema = [ "a", IntType; "b", StringType ]

    /// The reference pair: a report over the named source `orders`, reading a scalar threshold, a
    /// list of tags, a sort column and a page size — one parameter at each kind of position.
    let private reference (id: string) : PipelineQuery =
        { Query =
            { Id = id
              Params =
                [ { Name = "min"
                    Type = IntType
                    Required = true }
                  { Name = "tags"
                    Type = StringType
                    Required = true }
                  { Name = "order"
                    Type = StringType
                    Required = true }
                  { Name = "take"
                    Type = IntType
                    Required = true } ]
              ResultSchema = [ "x", IntType; "y", StringType ]
              Effect =
                { Host = ReadsHost
                  Determinism = Effect.network }
              Source = Ref "orders"
              TimeoutMs = None
              PageSize = None }
          Pipeline =
            [ Filter(ColExpr.Binary(Gt, ColExpr.Col "a", ColExpr.Param "min"))
              Filter(ColExpr.InParam(ColExpr.Col "b", "tags"))
              Project [ "a", "x"; "b", "y" ]
              Sort [ Slot.Param "order", Asc ]
              Limit(Slot.Param "take", Slot.Lit 0) ]
          Sources = Map.ofList [ "orders", fixedSchema ] }

    /// The registered-pipeline-query laws (Phase 281). Self-contained: each iteration draws a table
    /// and a set of arguments, and BUILDS the reference pair and one variant per refusal, so every
    /// refusal is reached on every iteration. It certifies:
    ///
    ///  - **agreement** — the reference pair is admitted, and a pair whose walk is open, or whose
    ///    result column is not produced, undeclared, duplicated, out of order, of another type or of
    ///    a type only the data decides, is refused with exactly that disagreement, naming the column;
    ///  - **parameters, both directions** — a read of an undeclared parameter, a read at another
    ///    type, a read as both a scalar and a list, and a declared parameter never read are each
    ///    refused by name; and the census `paramReads` names exactly `Transform.paramsOf`;
    ///  - **the registry is the substrate's** — a duplicate id is the substrate's `DuplicateQuery`,
    ///    an unregistered one its `NoSuchQuery`, enumeration is id-stable, and the declarations are
    ///    the substrate registry's own;
    ///  - **dispatch has the substrate's three outcomes** — settled, pending, refused typed before
    ///    the resolver runs; a resolver's `Failed` is `ExecutionFailed`, never `Ok(Failed _)`;
    ///  - **the bound pipeline is the pipeline under the arguments** — it reads no parameter, and it
    ///    evaluates exactly as the same pipeline written with the arguments as literals, in the
    ///    declared result schema;
    ///  - **the codec round-trips** the pair, and refuses a document of another `$type`.
    let laws (seed: int) (iterations: int) : LawResult list =
        let agreement = ref None
        let parameters = ref None
        let registry = ref None
        let dispatch = ref None
        let binding = ref None
        let codec = ref None

        let mutable rng = ConfRng.ofSeed seed

        let draw n =
            let v, r = ConfRng.intBelow n rng
            rng <- r
            v

        for i in 0 .. iterations - 1 do
            let at (msg: string) =
                sprintf "seed=%d iter=%d: %s" seed i msg

            let record (cell: string option ref) (holds: bool) (msg: unit -> string) =
                if not holds && cell.Value.IsNone then
                    cell.Value <- Some(at (msg ()))

            // ---- the draw: a table, and arguments for the reference pair ----
            let rows = 3 + draw 6
            let tagSet = [ "p"; "q"; "r" ]

            let aCells = [ for _ in 1..rows -> if draw 7 = 0 then Null else Int(draw 40 - 20) ]

            let bCells =
                [ for _ in 1..rows ->
                      match draw 4 with
                      | 3 -> Null
                      | k -> Str tagSet[k] ]

            let table =
                { Schema = fixedSchema
                  Columns = [ Column.create "a" IntType aCells; Column.create "b" StringType bCells ] }

            let min = draw 20 - 10
            let tags = tagSet |> List.filter (fun _ -> draw 2 = 0)
            let tags = if List.isEmpty tags then [ tagSet[draw 3] ] else tags
            let order = if draw 2 = 0 then "x" else "y"
            let take = 1 + draw rows

            let args =
                [ "min", Int min ]
                @ (tags |> List.map (fun t -> "tags", Str t))
                @ [ "order", Str order; "take", Int take ]

            let pq = reference ("report-" + string i)
            let q = pq.Query

            // ---- agreement ----
            let expectRefused
                (cell: string option ref)
                (label: string)
                (expected: PipelineQueryError)
                (variant: PipelineQuery)
                =
                let got = PipelineQuery.check variant

                record cell (got = Error expected) (fun () ->
                    sprintf "%s: expected the refusal %A, got %A" label expected got)

            record agreement (PipelineQuery.check pq = Ok()) (fun () ->
                sprintf "the reference pair was refused: %A" (PipelineQuery.check pq))

            expectRefused
                agreement
                "a named source with no declared schema"
                (SourceUndeclared("orders", []))
                { pq with Sources = Map.empty }

            let pivoted =
                { pq with
                    Pipeline =
                        pq.Pipeline
                        @ [ Pivot
                                { Index = [ "x" ]
                                  On = "y"
                                  Values = "x"
                                  Agg = Count } ] }

            (match PipelineQuery.check pivoted with
             | Error(ResultSchemaOpen _) -> ()
             | other ->
                 record agreement false (fun () ->
                     sprintf "a pivot's data-named columns did not open the walk: %A" other))

            let withResult schema =
                { pq with
                    Query = { q with ResultSchema = schema } }

            expectRefused
                agreement
                "a declared column not produced"
                (ResultColumn("z", ResultDisagreement.NotProduced))
                (withResult (q.ResultSchema @ [ "z", IntType ]))

            expectRefused
                agreement
                "a produced column not declared"
                (ResultColumn("y", ResultDisagreement.Undeclared))
                (withResult [ "x", IntType ])

            expectRefused
                agreement
                "a duplicated column"
                (ResultColumn("x", ResultDisagreement.Duplicated))
                (withResult (q.ResultSchema @ [ "x", IntType ]))

            expectRefused
                agreement
                "a column out of order"
                (ResultColumn("y", ResultDisagreement.OutOfOrder(0, 1)))
                (withResult (List.rev q.ResultSchema))

            expectRefused
                agreement
                "a column of another type"
                (ResultColumn("x", ResultDisagreement.TypeDiffers(FloatType, IntType)))
                (withResult [ "x", FloatType; "y", StringType ])

            expectRefused
                agreement
                "a column whose type only the data decides"
                (ResultColumn("x", ResultDisagreement.TypeUndecidable IntType))
                { pq with
                    Pipeline =
                        pq.Pipeline
                        @ [ Derive("x", ColExpr.Binary(Add, ColExpr.Col "x", ColExpr.Lit(Int 1))) ] }

            // ---- parameters, both directions ----
            let withParams ps =
                { pq with
                    Query = { q with Params = ps } }

            expectRefused
                parameters
                "a read of an undeclared parameter"
                (ParamUndeclared("take", [ "min"; "tags"; "order" ]))
                (withParams (q.Params |> List.filter (fun p -> p.Name <> "take")))

            expectRefused
                parameters
                "a read at another type"
                (ParamReadAs("min", StringType, IntType))
                (withParams (
                    q.Params
                    |> List.map (fun p -> if p.Name = "min" then { p with Type = StringType } else p)
                ))

            let mixedPipeline =
                Filter(ColExpr.Binary(Eq, ColExpr.Col "b", ColExpr.Param "tags")) :: pq.Pipeline

            expectRefused
                parameters
                "a read as both a scalar and a list"
                (ParamReadAsScalarAndList "tags")
                { pq with Pipeline = mixedPipeline }

            expectRefused
                parameters
                "a declared parameter never read"
                (ParamUnread "unused")
                (withParams (
                    q.Params
                    @ [ { Name = "unused"
                          Type = IntType
                          Required = false } ]
                ))

            for variant in [ pq; { pq with Pipeline = mixedPipeline } ] do
                let census =
                    PipelineQuery.paramReads variant |> List.map fst |> List.distinct |> Set.ofList

                let paramsOf = Transform.paramsOf variant.Pipeline |> Set.ofList

                record parameters (census = paramsOf) (fun () ->
                    sprintf "paramReads names %A where Transform.paramsOf names %A" census paramsOf)

            // ---- the registry is the substrate's ----
            let second = reference ("report-a" + string i)

            match
                PipelineQueryRegistry.empty
                |> PipelineQueryRegistry.register pq
                |> Result.bind (PipelineQueryRegistry.register second)
            with
            | Error e -> record registry false (fun () -> sprintf "the reference pairs were refused: %A" e)
            | Ok reg ->
                (match PipelineQueryRegistry.register pq reg with
                 | Error(QueryRefused(DuplicateQuery id)) when id = q.Id -> ()
                 | other -> record registry false (fun () -> sprintf "a duplicate id was not DuplicateQuery: %A" other))

                let ids = PipelineQueryRegistry.enumerate reg |> List.map _.Query.Id

                let declared =
                    PipelineQueryRegistry.declarations reg
                    |> QueryRegistry.enumerate
                    |> List.map _.Id

                record registry (ids = List.sort ids && ids = declared) (fun () ->
                    sprintf "enumeration %A is not id-sorted or disagrees with the declarations %A" ids declared)

                record registry (PipelineQueryRegistry.tryFind q.Id reg = Some pq) (fun () ->
                    "tryFind did not answer the registered pair")

                (match PipelineQueryRegistry.dispatch reg "no-such-report" args (fun _ -> Pending) with
                 | Error(NoSuchQuery("no-such-report", known)) when known = List.sort [ q.Id; second.Query.Id ] -> ()
                 | other -> record registry false (fun () -> sprintf "an unregistered id was not NoSuchQuery: %A" other))

                // ---- dispatch: the substrate's three outcomes ----
                let evaluate (bound: PipelineQuery) : Deferred<QueryResult> =
                    match DataFrame.evalPipeline bound.Pipeline table with
                    | Ok t ->
                        Ready
                            { Rows = t
                              PageNum = 0
                              TotalRowCount = None
                              NextPageToken = None }
                    | Error e -> Failed(sprintf "%A" e)

                let seen = ref None

                (match
                    PipelineQueryRegistry.dispatch reg q.Id args (fun bound ->
                        seen.Value <- Some bound
                        evaluate bound)
                 with
                 | Ok(Ready _) -> ()
                 | other -> record dispatch false (fun () -> sprintf "a settling resolver did not settle: %A" other))

                (match PipelineQueryRegistry.dispatch reg q.Id args (fun _ -> Pending) with
                 | Ok Pending -> ()
                 | other ->
                     record dispatch false (fun () -> sprintf "a pending resolver did not stay pending: %A" other))

                let ran = ref false

                (match
                    PipelineQueryRegistry.dispatch reg q.Id (("min", Str "nope") :: List.tail args) (fun _ ->
                        ran.Value <- true
                        Pending)
                 with
                 | Error(ParamTypeMismatch("min", IntType, StringType)) when not ran.Value -> ()
                 | other ->
                     record dispatch false (fun () ->
                         sprintf
                             "a mistyped argument was not refused before the resolver (%A; ran: %b)"
                             other
                             ran.Value))

                (match PipelineQueryRegistry.dispatch reg q.Id args (fun _ -> Failed("boom-" + string i)) with
                 | Error(ExecutionFailed(m, [])) when m = "boom-" + string i -> ()
                 | other ->
                     record dispatch false (fun () -> sprintf "a resolver failure was not ExecutionFailed: %A" other))

                // ---- the bound pipeline is the pipeline under the arguments ----
                match seen.Value with
                | None -> record binding false (fun () -> "the settling dispatch never reached the resolver")
                | Some bound ->
                    record binding (List.isEmpty (Transform.paramsOf bound.Pipeline)) (fun () ->
                        sprintf "the bound pipeline still reads %A" (Transform.paramsOf bound.Pipeline))

                    let literal =
                        [ Filter(ColExpr.Binary(Gt, ColExpr.Col "a", ColExpr.Lit(Int min)))
                          Filter(ColExpr.InList(ColExpr.Col "b", tags |> List.map (fun t -> ColExpr.Lit(Str t))))
                          Project [ "a", "x"; "b", "y" ]
                          Transform.sortBy [ order, Asc ]
                          Transform.limit take 0 ]

                    let viaBound = DataFrame.evalPipeline bound.Pipeline table
                    let viaLiteral = DataFrame.evalPipeline literal table

                    record binding (viaBound = viaLiteral) (fun () ->
                        sprintf "the bound pipeline gave %A where the literal one gave %A" viaBound viaLiteral)

                    match viaBound with
                    | Ok t ->
                        record binding (t.Schema = q.ResultSchema) (fun () ->
                            sprintf "the result's schema %A is not the declared %A" t.Schema q.ResultSchema)
                    | Error e -> record binding false (fun () -> sprintf "the bound pipeline was refused: %A" e)

            // ---- the codec ----
            let encoded = PipelineQueryCodec.encode pq

            record codec (PipelineQueryCodec.decode encoded = Ok pq) (fun () ->
                sprintf "the pair did not round-trip: %A" (PipelineQueryCodec.decode encoded))

            let retagged = encoded.Replace("\"$type\":\"pipelineQuery\"", "\"$type\":\"query\"")

            record codec (Result.isError (PipelineQueryCodec.decode retagged)) (fun () ->
                "a document of another $type was decoded as a pipeline query")

        let result (law: string) (cell: string option ref) : LawResult =
            { Law = law
              Passed = cell.Value.IsNone
              Counterexample = cell.Value }

        [ result
              "a pair is admitted iff its walk is closed and IS the declared result schema; each disagreement is refused naming the column"
              agreement
          result
              "every parameter the pipeline reads is declared at the type it is read at, and every declared parameter is read"
              parameters
          result
              "the registry is the substrate's: DuplicateQuery, NoSuchQuery, id-stable enumeration, its own declarations"
              registry
          result
              "dispatch settles, stays pending or refuses typed before the resolver; a resolver failure is ExecutionFailed, never Ok(Failed _)"
              dispatch
          result
              "the resolver receives the pipeline under the arguments: no parameter left, the literal pipeline's answer, the declared schema"
              binding
          result "the pair round-trips the codec, and a document of another $type is refused" codec ]
