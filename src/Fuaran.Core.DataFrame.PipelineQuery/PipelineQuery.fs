namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.DataFrame.PipelineQuery (Phase 281) — a registered pipeline
//  query: a named declaration whose body is a pipeline over a source.
//
//  The substrate's `Query` is a declaration — an id, typed parameters, a result
//  schema, an effect class and a `Source` — and it has no body: what a query
//  does lives with the host resolver, which for a database is text. So a fixed
//  report could declare its parameters and its result as data, and its meaning
//  only as a string behind a name. This package pairs the declaration with the
//  `Transform` pipeline that IS its body, and holds the two to each other:
//
//    * the pipeline's static output schema (`SchemaWalk`) over the source the
//      declaration names is CLOSED and IS the declared `ResultSchema` — names,
//      order and types — or the pair is refused, naming the column;
//    * every parameter the pipeline reads is declared, at the type the
//      pipeline reads it at, and every declared parameter is read.
//
//  The pairing lives here and not in the substrate because no substrate package
//  may reference the dataframe layer (both repositories' boundary tests hold
//  that). It evaluates nothing new: dispatch validates the arguments through the
//  substrate's `Query.validateParams`, substitutes them into the pipeline, and
//  hands the bound pair to a host resolver that evaluates it with the evaluator
//  it already has — answering in the substrate's `Deferred` envelope, with the
//  same three outcomes and the same unreachable fourth, because the dispatch IS
//  the substrate's `QueryRegistry.dispatch`. A duplicate id and an unregistered
//  id are the substrate's own `DuplicateQuery` and `NoSuchQuery`, reused.
//
//  No plan cache: the planner's rewrite and the expression compiler each cost a
//  microsecond or two, so a registered pipeline re-plans on every evaluation.
//
//  FSharp.Core only, Fable-clean.
// ============================================================================

/// A registered pipeline query (Phase 281): a substrate `Query` declaration paired with the
/// `Transform` pipeline that is its body.
///
/// `Sources` declares the schema of every NAMED source the pair reads — the declaration's own
/// `Source` when it is a `Ref`, and the `Ref` operand of any `Join` / `Union` / `Intersect` /
/// `Except` step. A `Ref` is resolved by the host, so its schema is whatever the pair declares, and
/// a `Ref` it declares nothing for leaves the static walk open, which registration refuses. An
/// `Embedded` source declares its own schema and needs no entry.
type PipelineQuery =
    { Query: Query
      Pipeline: Transform list
      Sources: Map<string, Schema> }

/// How a pipeline reads one parameter (Phase 281): as a scalar — a `ColExpr.Param`, a `Sort` key
/// slot, a `Limit` slot, a rounding scale — or as a list, the `ColExpr.InParam` membership test. The
/// type is the one the read's position decides, `None` where the position decides none (a `Cast`
/// operand, a scalar-function argument, a comparison whose other side the schema does not type).
[<RequireQualifiedAccess>]
type ParamRead =
    | Scalar of readAs: ColumnType option
    | List of readAs: ColumnType option

/// How one result column disagrees with the declaration (Phase 281). Positions count from zero.
[<RequireQualifiedAccess>]
type ResultDisagreement =
    /// Declared, and the pipeline does not produce it.
    | NotProduced
    /// Produced, and the declaration does not declare it.
    | Undeclared
    /// The name occurs more than once in the declared or the produced schema.
    | Duplicated
    /// Declared at one position and produced at another.
    | OutOfOrder of declaredAt: int * producedAt: int
    /// Produced at a type other than the declared one.
    | TypeDiffers of declared: ColumnType * produced: ColumnType
    /// Produced at a type only the DATA decides. The evaluator types a derived column from its
    /// first present cell and falls back to `StringType` over an empty or all-null frame, so a
    /// `Derive` of anything but a string answers the declared type over some frames and `StringType`
    /// over others; no declaration can agree with that on every frame. `Project` or `GroupBy` over
    /// a declared-type column, or a string-typed derivation, is what can.
    | TypeUndecidable of declared: ColumnType

/// Why a pipeline query was refused at registration (Phase 281). Total, and each refusal names what
/// it is about. The substrate's own refusals ride `QueryRefused` unchanged rather than being minted
/// again.
type PipelineQueryError =
    /// The substrate's own named refusal — `DuplicateQuery` at registration.
    | QueryRefused of QueryError
    /// The pair reads a named source — the declaration's own, or a `Join` / set-op operand — whose
    /// schema it does not declare. Refused before the walk is read, because a `Project` or a
    /// `GroupBy` over an undeclared source closes the column set with types nobody declared, and
    /// what would then be reported is a symptom of the missing declaration rather than the cause.
    | SourceUndeclared of name: string * declared: string list
    /// The pipeline's static output schema is open: the walk cannot name every column (a `Pivot`
    /// whose columns are the data's), so it cannot be shown to be the declared one. The reason is
    /// the walk's.
    | ResultSchemaOpen of reason: string
    /// A result column on which the pipeline and the declaration disagree.
    | ResultColumn of column: string * disagreement: ResultDisagreement
    /// The pipeline reads a parameter the declaration does not declare.
    | ParamUndeclared of name: string * declared: string list
    /// The pipeline reads a declared parameter at a type other than the declared one.
    | ParamReadAs of name: string * declared: ColumnType * readAs: ColumnType
    /// The pipeline reads one parameter both as a scalar and as a list.
    | ParamReadAsScalarAndList of name: string
    /// The declaration declares a parameter the pipeline never reads.
    | ParamUnread of name: string

/// Pure, total derivations over a pipeline query (Phase 281).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PipelineQuery =

    /// The named-source lookup the pair declares.
    let sources (pq: PipelineQuery) : string -> Schema option = SchemaWalk.ofMap pq.Sources

    /// The static output schema of the pair's pipeline over the declaration's source — the knowledge
    /// registration holds to the declared `ResultSchema`.
    let resultKnowledge (pq: PipelineQuery) : SchemaKnowledge =
        let lookup = sources pq
        SchemaWalk.ofPipelineFrom lookup (SchemaWalk.ofSource lookup pq.Query.Source) pq.Pipeline

    /// The typed columns of a knowledge — the schema the expression typer reads.
    let private typed (k: SchemaKnowledge) : Schema =
        SchemaWalk.columns k
        |> List.choose (fun c -> c.Type |> Option.map (fun t -> c.Name, t))

    let private slotRead (readAs: ColumnType) (s: Slot<'T>) : (string * ParamRead) list =
        match s with
        | Slot.Param n -> [ n, ParamRead.Scalar(Some readAs) ]
        | Slot.Lit _ -> []

    /// The reads in one expression, `expected` being the type the expression's position decides.
    /// Closed over `ColExpr` with no catch-all, so a new case stops the compiler here.
    let rec private exprReads (cols: Schema) (expected: ColumnType option) (e: ColExpr) : (string * ParamRead) list =
        let go = exprReads cols
        let typeOf = DataFrame.typeOf cols

        // Operands that must agree with each other: each is read at the first type any of them has,
        // else at the type the position decides.
        let peers (fallback: ColumnType option) (xs: ColExpr list) =
            let known = xs |> List.tryPick typeOf |> Option.orElse fallback
            xs |> List.collect (go known)

        let scale (r: Rounding) = slotRead IntType r.Scale

        match e with
        | ColExpr.Col _
        | ColExpr.Lit _
        | ColExpr.Now _ -> []
        | ColExpr.Param n -> [ n, ParamRead.Scalar expected ]
        | ColExpr.Binary(op, a, b) ->
            match op with
            | And
            | Or -> go (Some BoolType) a @ go (Some BoolType) b
            | Contains
            | StartsWith
            | EndsWith -> go (Some StringType) a @ go (Some StringType) b
            | Add
            | Sub
            | Mul
            | Div
            | Mod
            | Eq
            | Ne
            | Lt
            | Le
            | Gt
            | Ge -> go (typeOf b) a @ go (typeOf a) b
        | ColExpr.Not x -> go (Some BoolType) x
        | ColExpr.Coalesce xs -> peers expected xs
        | ColExpr.Case(cases, els) ->
            (cases |> List.collect (fun (w, _) -> go (Some BoolType) w))
            @ peers expected ((cases |> List.map snd) @ [ els ])
        | ColExpr.Cast(_, x) -> go None x
        | ColExpr.ApplyFn(_, xs) -> xs |> List.collect (go None)
        | ColExpr.InList(x, items) -> peers None (x :: items)
        | ColExpr.IsNull x -> go None x
        | ColExpr.InParam(x, n) -> go None x @ [ n, ParamRead.List(typeOf x) ]
        | ColExpr.Quotient(a, b, r) -> go None a @ go None b @ scale r
        | ColExpr.Rounded(x, r) -> go None x @ scale r

    /// The reads in one step over the typed columns alive at it. Closed over `Transform` with no
    /// catch-all, for the reason `exprReads` is.
    let private stepReads (cols: Schema) (step: Transform) : (string * ParamRead) list =
        match step with
        | Filter p -> exprReads cols (Some BoolType) p
        | Derive(_, e) -> exprReads cols None e
        | Sort keys -> keys |> List.collect (fun (c, _) -> slotRead StringType c)
        | Limit(n, offset) -> slotRead IntType n @ slotRead IntType offset
        | Project _
        | GroupBy _
        | Join _
        | Window _
        | Pivot _
        | Unpivot _
        | Distinct
        | Union _
        | Intersect _
        | Except _ -> []

    /// Every parameter read in the pair's pipeline, in occurrence order with repeats, each with how
    /// it is read. Its names are exactly `Transform.paramsOf` of the pipeline (the law family holds
    /// that), so this is the census `paramsOf` is, with the type each read decides.
    let paramReads (pq: PipelineQuery) : (string * ParamRead) list =
        let lookup = sources pq

        pq.Pipeline
        |> List.fold
            (fun (k, acc) step -> SchemaWalk.ofTransform lookup k step, acc @ stepReads (typed k) step)
            (SchemaWalk.ofSource lookup pq.Query.Source, [])
        |> snd

    let private isList (r: ParamRead) =
        match r with
        | ParamRead.List _ -> true
        | ParamRead.Scalar _ -> false

    let private readType (r: ParamRead) =
        match r with
        | ParamRead.Scalar t
        | ParamRead.List t -> t

    let private refOf (source: DataSource) : string list =
        match source with
        | Ref name -> [ name ]
        | Embedded _ -> []

    /// Every named source the pair reads, in order: the declaration's own, then each step's
    /// operand. Closed over `Transform` with no catch-all, so a new verb carrying a source stops
    /// the compiler here.
    let namedSources (pq: PipelineQuery) : string list =
        let stepRefs (step: Transform) =
            match step with
            | Join(source, _, _)
            | Union source
            | Intersect source
            | Except source -> refOf source
            | Filter _
            | Project _
            | Derive _
            | GroupBy _
            | Window _
            | Pivot _
            | Unpivot _
            | Sort _
            | Distinct
            | Limit _ -> []

        refOf pq.Query.Source @ (pq.Pipeline |> List.collect stepRefs) |> List.distinct

    /// `checkResult` once every named source is declared.
    let private checkWalk (pq: PipelineQuery) : Result<unit, PipelineQueryError> =
        match resultKnowledge pq with
        | SchemaKnowledge.AtLeast(_, reason) -> Error(ResultSchemaOpen reason)
        | SchemaKnowledge.Closed produced ->
            let declared = pq.Query.ResultSchema
            let producedNames = produced |> List.map _.Name
            let declaredNames = declared |> List.map fst

            let duplicate (names: string list) =
                names
                |> List.countBy id
                |> List.tryPick (fun (n, c) -> if c > 1 then Some n else None)

            let fail column why = Error(ResultColumn(column, why))

            match duplicate declaredNames |> Option.orElse (duplicate producedNames) with
            | Some n -> fail n ResultDisagreement.Duplicated
            | None ->
                match declaredNames |> List.tryFind (fun n -> not (List.contains n producedNames)) with
                | Some n -> fail n ResultDisagreement.NotProduced
                | None ->
                    match producedNames |> List.tryFind (fun n -> not (List.contains n declaredNames)) with
                    | Some n -> fail n ResultDisagreement.Undeclared
                    | None ->
                        let misplaced =
                            declaredNames
                            |> List.mapi (fun i n -> n, i, List.findIndex ((=) n) producedNames)
                            |> List.tryFind (fun (_, i, j) -> i <> j)

                        match misplaced with
                        | Some(n, i, j) -> fail n (ResultDisagreement.OutOfOrder(i, j))
                        | None ->
                            let typeDisagreement =
                                declared
                                |> List.tryPick (fun (n, ty) ->
                                    match (produced |> List.find (fun c -> c.Name = n)).Type with
                                    | None -> Some(n, ResultDisagreement.TypeUndecidable ty)
                                    | Some t when t <> ty -> Some(n, ResultDisagreement.TypeDiffers(ty, t))
                                    | Some _ -> None)

                            match typeDisagreement with
                            | Some(n, why) -> fail n why
                            | None -> Ok()

    /// The pipeline's output schema against the declared `ResultSchema`: every named source
    /// declared, the walk closed, the same names (none twice), in the same order, at the same types.
    /// The first disagreement is the answer, in that order — an undeclared source, an open walk, a
    /// duplicated name, a declared column not produced, a produced column not declared, a column
    /// out of order, then a type.
    let checkResult (pq: PipelineQuery) : Result<unit, PipelineQueryError> =
        match namedSources pq |> List.tryFind (fun n -> not (Map.containsKey n pq.Sources)) with
        | Some name -> Error(SourceUndeclared(name, pq.Sources |> Map.toList |> List.map fst))
        | None -> checkWalk pq

    /// The parameters the pipeline reads against the ones the declaration declares, both directions:
    /// every read names a declared parameter, no parameter is read both as a scalar and as a list,
    /// every read whose position decides a type reads the declared type, and every declared
    /// parameter is read. The first refusal is the answer, in that order.
    let checkParams (pq: PipelineQuery) : Result<unit, PipelineQueryError> =
        let reads = paramReads pq
        let declared = pq.Query.Params
        let declaredNames = declared |> List.map _.Name

        match reads |> List.tryFind (fun (n, _) -> not (List.contains n declaredNames)) with
        | Some(n, _) -> Error(ParamUndeclared(n, declaredNames))
        | None ->
            let mixed =
                reads
                |> List.map fst
                |> List.distinct
                |> List.tryFind (fun n ->
                    let ofName = reads |> List.filter (fun (m, _) -> m = n) |> List.map snd
                    List.exists isList ofName && List.exists (isList >> not) ofName)

            match mixed with
            | Some n -> Error(ParamReadAsScalarAndList n)
            | None ->
                let mistyped =
                    reads
                    |> List.tryPick (fun (n, r) ->
                        match readType r with
                        | None -> None
                        | Some t ->
                            let p = declared |> List.find (fun p -> p.Name = n)

                            if p.Type <> t then
                                Some(ParamReadAs(n, p.Type, t))
                            else
                                None)

                match mistyped with
                | Some e -> Error e
                | None ->
                    match
                        declared
                        |> List.tryFind (fun p -> not (reads |> List.exists (fun (n, _) -> n = p.Name)))
                    with
                    | Some p -> Error(ParamUnread p.Name)
                    | None -> Ok()

    /// Agreement, as registration demands it: `checkResult`, then `checkParams`.
    let check (pq: PipelineQuery) : Result<unit, PipelineQueryError> =
        checkResult pq |> Result.bind (fun () -> checkParams pq)

    /// The pair with `args` substituted into its pipeline: a parameter the pipeline reads as a list
    /// takes every binding of its name, in order (`Transform.substituteListParams`); any other takes
    /// its binding (`Transform.substitute`; the last, where a name is bound twice, as the
    /// substrate's validation reads it). A parameter left unbound stays a parameter, so evaluation
    /// names it — the strict `EvalError.UnboundParam` — unless the host prunes it first.
    let substitute (args: (string * Cell) list) (pq: PipelineQuery) : PipelineQuery =
        let reads = paramReads pq

        let readAsList (name: string) =
            reads |> List.exists (fun (n, r) -> n = name && isList r)

        let scalarEnv = args |> List.filter (fun (n, _) -> not (readAsList n)) |> Map.ofList

        let listEnv =
            args
            |> List.filter (fun (n, _) -> readAsList n)
            |> List.groupBy fst
            |> List.map (fun (n, bindings) -> n, bindings |> List.map snd)
            |> Map.ofList

        { pq with
            Pipeline =
                pq.Pipeline
                |> Transform.substitute scalarEnv
                |> Transform.substituteListParams listEnv }

/// The pipeline-query registry (Phase 281) — the substrate's `QueryRegistry` of declarations, and
/// the body of each. Its representation is private, so a pair whose pipeline disagrees with its
/// declaration cannot be in one: `register` is the only way in, and it refuses that pair.
type PipelineQueryRegistry =
    private
        { Declarations: QueryRegistry
          Bodies: Map<string, PipelineQuery> }

/// Register, enumerate, dispatch (Phase 281) — the substrate's registry pattern, over pairs.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PipelineQueryRegistry =

    let empty: PipelineQueryRegistry =
        { Declarations = QueryRegistry.empty
          Bodies = Map.empty }

    /// Register a pair — additive, never an overwrite. A duplicate id is the substrate's own
    /// `DuplicateQuery`; then the pair is held to `PipelineQuery.check`, and a disagreement is
    /// refused naming what disagrees.
    let register (pq: PipelineQuery) (r: PipelineQueryRegistry) : Result<PipelineQueryRegistry, PipelineQueryError> =
        match QueryRegistry.register pq.Query r.Declarations with
        | Error e -> Error(QueryRefused e)
        | Ok declarations ->
            PipelineQuery.check pq
            |> Result.map (fun () ->
                { Declarations = declarations
                  Bodies = Map.add pq.Query.Id pq r.Bodies })

    let tryFind (id: string) (r: PipelineQueryRegistry) : PipelineQuery option = Map.tryFind id r.Bodies

    /// Every registered pair in a stable order (by id) — the discovery surface, as the substrate's
    /// `QueryRegistry.enumerate` is for declarations.
    let enumerate (r: PipelineQueryRegistry) : PipelineQuery list = r.Bodies |> Map.toList |> List.map snd

    /// The declarations alone, as the substrate's registry — for a host or a model that discovers
    /// queries through the substrate's surface and never needs the bodies.
    let declarations (r: PipelineQueryRegistry) : QueryRegistry = r.Declarations

    /// Dispatch an invocation. This IS the substrate's `QueryRegistry.dispatch` over the
    /// declarations: an unregistered id is `NoSuchQuery`, the arguments are validated by
    /// `Query.validateParams` before anything runs, and the resolver answers in the `Deferred`
    /// envelope — so the outcomes are the substrate's three (settled, pending, refused typed), and
    /// a resolver's `Failed` is the enumerated `ExecutionFailed`, never `Ok(Failed _)`. The resolver
    /// receives the pair with the validated arguments substituted (`PipelineQuery.substitute`).
    let dispatch
        (r: PipelineQueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (resolve: PipelineQuery -> Deferred<QueryResult>)
        : Result<Deferred<QueryResult>, QueryError> =
        QueryRegistry.dispatch r.Declarations id args (fun q ->
            match Map.tryFind q.Id r.Bodies with
            | Some pq -> resolve (PipelineQuery.substitute args pq)
            // Unreachable: the declarations and the bodies are added together, in `register` only.
            | None -> Failed("no body registered for " + q.Id))

/// The canonical wire codec for a pipeline query (Phase 281):
/// `{"$type":"pipelineQuery","pipeline":[…],"query":{…},"sources":[{"name":…,"schema":[…]}]}` —
/// the declaration in the substrate's `QueryCodec` form, the pipeline in `DataFrameCodec`'s, and
/// the declared source schemas by name, in name order. Decoding yields a value; registration is
/// what holds it to its declaration. Fable-clean.
module PipelineQueryCodec =

    let private schemaJson (s: Schema) : JVal =
        JArr(
            s
            |> List.map (fun (n, t) -> JObj [ "name", JStr n; "type", JStr(ColumnType.tag t) ])
        )

    let private schemaOf (el: JVal) : Result<Schema, string> =
        Decode.mapList
            (fun e ->
                Decode.strField "name" e
                |> Result.bind (fun n ->
                    Decode.strField "type" e
                    |> Result.bind (fun tag ->
                        match ColumnType.ofTag tag with
                        | Some t -> Ok(n, t)
                        | None -> Error("unknown column type: " + tag))))
            el

    let private queryErrorText (e: QueryError) : string =
        match e with
        | ExecutionFailed(m, _) -> m
        | other -> sprintf "%A" other

    /// The pair as a JSON value.
    let encodeJson (pq: PipelineQuery) : JVal =
        let query =
            // The substrate's codec renders the declaration canonically; reading its own output back
            // cannot fail, and doing so is what keeps one encoding of a `Query` rather than two.
            match Decode.parse (QueryCodec.encode pq.Query) with
            | Ok j -> j
            | Error m -> failwith ("QueryCodec.encode produced unparseable JSON: " + m)

        JObj
            [ "$type", JStr "pipelineQuery"
              "query", query
              "pipeline", JArr(pq.Pipeline |> List.map DataFrameCodec.encodeTransform)
              "sources",
              JArr(
                  pq.Sources
                  |> Map.toList
                  |> List.map (fun (name, schema) -> JObj [ "name", JStr name; "schema", schemaJson schema ])
              ) ]

    let encode (pq: PipelineQuery) : string = Canon.render (encodeJson pq)

    /// A pair from its JSON value: the `"$type"` must be `pipelineQuery`, and a source named twice is
    /// refused rather than resolved by order.
    let decodeJson (el: JVal) : Result<PipelineQuery, string> =
        let typeTag =
            match Decode.strField "$type" el with
            | Ok "pipelineQuery" -> Ok()
            | Ok other -> Error("expected $type pipelineQuery, got " + other)
            | Error m -> Error m

        let query () =
            Decode.getProp "query" el
            |> Result.bind (fun j ->
                QueryCodec.decode (Canon.render j)
                |> Result.mapError (fun e -> "query: " + queryErrorText e))

        let pipeline () =
            Decode.getProp "pipeline" el
            |> Result.bind (fun j ->
                DataFrameCodec.decodePipelineJson j
                |> Result.mapError (fun e -> "pipeline: " + ColumnCodec.errorString e))

        let sources () =
            Decode.getProp "sources" el
            |> Result.bind (
                Decode.mapList (fun e ->
                    Decode.strField "name" e
                    |> Result.bind (fun n ->
                        Decode.getProp "schema" e |> Result.bind schemaOf |> Result.map (fun s -> n, s)))
            )
            |> Result.bind (fun pairs ->
                match pairs |> List.countBy fst |> List.tryFind (fun (_, c) -> c > 1) with
                | Some(n, _) -> Error("source declared twice: " + n)
                | None -> Ok(Map.ofList pairs))

        typeTag
        |> Result.bind query
        |> Result.bind (fun q ->
            pipeline ()
            |> Result.bind (fun p -> sources () |> Result.map (fun s -> { Query = q; Pipeline = p; Sources = s })))

    let decode (s: string) : Result<PipelineQuery, string> =
        Decode.parse s |> Result.bind decodeJson
