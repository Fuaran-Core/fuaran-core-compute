namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Phase 338 — the law family over a derived column's TYPE. One rule types a
//  `Derive` and an `Unpivot` value column (`DataFrame.derivedTyping`, DECISIONS
//  D5): the typer's decided type on every frame, `StringType` for an expression
//  with no present value, the cells' widening join only where the typer cannot
//  decide, and a float beside a decimal refused by name. These laws hold the
//  evaluator, `SchemaWalk` and the incremental seam to that one rule, over a
//  full frame, an empty one and an all-null one built from the same draw.
//  Phase 413 adds the evaluation's half: a derive the static rule leaves to the
//  cells but the rule decides once the params are substituted (D23) is typed
//  that way on every frame of the evaluation.
// ============================================================================

/// The derived-column typing laws (Phase 338).
module DeriveTypingConformance =

    /// Phase 429: `i` is in metres and `f` in seconds, so every derive drawn from the menu below has a
    /// unit the check derives (the menu mixes neither with the other) and the built refusal has two
    /// units to meet.
    let private schema: Schema =
        [ Field.create "id" IntType
          Field.create "i" IntType
          |> Field.withUnit (Unit.parse "m" |> Result.defaultWith (fun _ -> Unit.dimensionless))
          Field.create "f" FloatType
          |> Field.withUnit (Unit.parse "s" |> Result.defaultWith (fun _ -> Unit.dimensionless))
          Field.create "m" DecimalType
          Field.create "b" BoolType
          Field.create "s" StringType ]

    let private unitOfColumn (name: string) (t: Table) : string option =
        t.Schema
        |> List.tryFind (fun f -> f.Name = name)
        |> Option.bind _.Unit
        |> Option.map Unit.render

    let private dec (text: string) : Cell =
        Cell.decimal text |> Option.defaultValue Null

    let private idw = RowIdentity.byColumn "id"

    /// The drawn table: `rows` rows keyed by `id`, every other column carrying nulls. `allNull`
    /// keeps the keys and makes every other cell `Null`.
    let private tableOf (rows: int) (offset: int) (allNull: bool) : Table =
        let present (i: int) (c: Cell) =
            if allNull || (i + offset) % 5 = 4 then Null else c

        let col name ty (f: int -> Cell) =
            KitColumn.create name ty [ for i in 0 .. rows - 1 -> present i (f i) ]

        { Schema = schema
          Columns =
            [ KitColumn.create "id" IntType [ for i in 0 .. rows - 1 -> Int i ]
              col "i" IntType (fun i -> Int(i * 3 - offset - 4))
              col "f" FloatType (fun i -> Float(float (i + offset) / 4.0))
              col "m" DecimalType (fun i -> dec (string (i - offset) + ".25"))
              col "b" BoolType (fun i -> Bool((i + offset) % 2 = 0))
              col "s" StringType (fun i -> Str(string (char (97 + (i + offset) % 26)))) ] }

    let private gt0 = Binary(Gt, Col "i", Lit(Int 0))

    /// The expression menu: every decided type, an expression decided over a column that is ALWAYS
    /// null in the full frame (an int `Div` by zero), one decided by an arm that may answer nothing
    /// (a `Case` with a null else), and the shapes the cells decide — a `Case` joining an int and a
    /// float, a `Coalesce` joining a decimal and an int, and a `Round` over that undecided join of an
    /// int and a decimal, which answers a float or a decimal by the row (Phase 413) — and the
    /// shapes an evaluation's params decide: a `Param`, a `Round` of one and an arithmetic over one.
    let private menu: ColExpr list =
        [ Binary(Add, Col "i", Lit(Int 1))
          Binary(Div, Col "i", Lit(Int 0))
          Binary(Mul, Col "f", Lit(Float 2.0))
          Binary(Add, Col "m", Col "i")
          Rounded(
              Col "m",
              { Scale = Slot.Lit 1
                Mode = RoundingMode.HalfEven }
          )
          Binary(Gt, Col "i", Lit(Int 0))
          Not(Col "b")
          ApplyFn(Upper, [ Col "s" ])
          Case([ gt0, Col "i" ], Lit Null)
          Case([ gt0, Lit(Int 1) ], Lit(Float 2.5))
          Coalesce [ Col "m"; Lit(Int 0) ]
          ApplyFn(Round, [ Case([ gt0, Col "i" ], Col "m") ])
          Param "p"
          ApplyFn(Round, [ Param "p" ])
          Binary(Add, Col "i", Param "p")
          // Phase 557 (D43): an int power under a literal exponent is an int (`m2`), a float power
          // a float, a power under a column exponent is decided by the cells, and a transcendental
          // over a dimensionless ratio is a float with no unit.
          ApplyFn(Pow, [ Col "i"; Lit(Int 2) ])
          ApplyFn(Pow, [ Col "f"; Lit(Int -2) ])
          ApplyFn(Pow, [ Col "id"; Binary(Sub, Col "id", Lit(Int 2)) ])
          ApplyFn(Exp, [ Binary(Div, Col "f", Col "f") ])
          ApplyFn(Ln, [ Binary(Div, Col "i", Col "i") ]) ]

    /// The params a `Param` reads, by the draw: an int, a float or a decimal.
    let private envOf (k: int) : Map<string, Cell> =
        Map.ofList
            [ "p",
              (match k % 3 with
               | 0 -> Int k
               | 1 -> Float(float k / 2.0)
               | _ -> dec (string k + ".5")) ]

    /// The unpivot menu: value columns whose declared types join (an int beside a float, a decimal
    /// beside an int, one string), and a pair no widening relates, which the cells decide.
    let private unpivots: string list list =
        [ [ "i"; "f" ]; [ "m"; "i" ]; [ "s" ]; [ "s"; "i" ] ]

    let private typeOfColumn (name: string) (t: Table) : ColumnType option =
        t.Schema |> List.tryFind (fun f -> f.Name = name) |> Option.map _.Type

    let private cellsOf (name: string) (t: Table) : Cell list =
        t.Columns
        |> List.tryFind (fun c -> c.Name = name)
        |> Option.map (fun c -> (Column.toCells c))
        |> Option.defaultValue []

    /// Every present cell of `name` is of the column's type or widens into it.
    let private cellsAdmitted (name: string) (t: Table) : bool =
        match typeOfColumn name t with
        | None -> true
        | Some ty ->
            cellsOf name t
            |> List.forall (fun c ->
                match Cell.typeOf c with
                | None -> true
                | Some ct -> ColumnType.widens ct ty)

    /// The laws over an evaluator `eval env pipeline table` — the reference's own
    /// (`DataFrame.evalPipelineInEnv`) in `laws`, a perturbed one in the suite's go-red cases.
    let internal lawsWith
        (eval: Map<string, Cell> -> Transform list -> Table -> Result<Table, EvalError>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable sameEverywhere = None
        let mutable envDecided = None
        let mutable walkAgrees = None
        let mutable refreshAgrees = None
        let mutable admitted = None
        let mutable refused = None
        let mutable decidedSeen = 0
        let mutable byCellsSeen = 0
        let mutable envDecidedSeen = 0
        let mutable allNullSeen = 0
        let mutable refusalsSeen = 0
        // Phase 429 — the unit law: the first disagreement, the derives whose column carried a unit,
        // and the built metres-plus-seconds refusals.
        let mutable unitAgrees = None
        let mutable unitRefused = None
        let mutable unitsSeen = 0
        let mutable unitRefusalsSeen = 0

        let draw n =
            let v, r = ConfRng.intBelow n rng
            rng <- r
            v

        let typeIn (r: Result<Table, EvalError>) (name: string) =
            match r with
            | Ok t -> Ok(typeOfColumn name t)
            | Error e -> Error e

        for it in 0 .. iterations - 1 do
            let at (msg: string) =
                sprintf "seed=%d iter=%d: %s" seed it msg

            let rows = 2 + draw 6
            let offset = draw 7
            let k = draw 20
            let env = envOf k
            let full = tableOf rows offset false
            let allNull = tableOf rows offset true
            let emptying = Filter(Lit(Bool false))

            // ---- the derive ----
            let e = List.item (draw (List.length menu)) menu
            let derive = Derive("d", e)
            let decided = SchemaWalk.columns (SchemaWalk.ofPipeline schema [ derive ])

            let declared =
                decided |> List.tryFind (fun c -> c.Name = "d") |> Option.bind (fun c -> c.Type)

            let onFull = eval env [ derive ] full
            let onEmpty = eval env [ emptying; derive ] full
            let onNull = eval env [ derive ] allNull

            // ---- the unit (Phase 429, D29): what the check derives over the schema is the column's
            // unit on every frame, and a derive of two units is refused by name on every frame ----
            let expectedUnit =
                DataFrame.unitOf schema e
                |> Result.map (Option.map Unit.render)
                |> Result.defaultValue None

            if expectedUnit.IsSome then
                unitsSeen <- unitsSeen + 1

            for label, r in [ "full", onFull; "empty", onEmpty; "all-null", onNull ] do
                match r with
                | Ok t when unitOfColumn "d" t <> expectedUnit && unitAgrees.IsNone ->
                    unitAgrees <-
                        Some(
                            at (
                                sprintf
                                    "%A: the check derives %A, the %s frame carried %A"
                                    e
                                    expectedUnit
                                    label
                                    (unitOfColumn "d" t)
                            )
                        )
                | _ -> ()

            let mixing = Derive("d", Binary(Add, Col "i", Col "f"))

            for label, r in
                [ "full", eval env [ mixing ] full
                  "empty", eval env [ emptying; mixing ] full ] do
                match r with
                | Error(UnitMismatch("derive 'd'", _, "m", "s")) -> unitRefusalsSeen <- unitRefusalsSeen + 1
                | other ->
                    if unitRefused.IsNone then
                        unitRefused <-
                            Some(at (sprintf "metres plus seconds over the %s frame answered %A" label other))

            for label, r in [ "full", onFull; "empty", onEmpty; "all-null", onNull ] do
                match r with
                | Ok t ->
                    if not (cellsAdmitted "d" t) && admitted.IsNone then
                        admitted <-
                            Some(at (sprintf "%A over the %s frame: a cell outside its column's type %A" e label t))
                | Error _ -> ()

            match declared with
            | Some ty ->
                decidedSeen <- decidedSeen + 1

                let types =
                    [ "full", typeIn onFull "d"
                      "empty", typeIn onEmpty "d"
                      "all-null", typeIn onNull "d" ]

                if (onNull |> Result.map (cellsOf "d")) = Ok(List.replicate rows Null) then
                    allNullSeen <- allNullSeen + 1

                for label, t in types do
                    match t with
                    | Ok(Some got) when got = ty -> ()
                    | other ->
                        if sameEverywhere.IsNone then
                            sameEverywhere <-
                                Some(at (sprintf "%A: decided %A, the %s frame typed it %A" e ty label other))

                // the walk states exactly the type the evaluator gave
                match typeIn onFull "d" with
                | Ok(Some got) when got <> ty && walkAgrees.IsNone ->
                    walkAgrees <- Some(at (sprintf "%A: SchemaWalk says %A, the evaluator %A" e ty got))
                | _ -> ()
            | None ->
                // The evaluation's half (Phase 413): the walk over the expression with this
                // evaluation's params substituted is the type every frame of it gives.
                let byEnv =
                    SchemaWalk.columns (SchemaWalk.ofPipeline schema [ Derive("d", ColExpr.substitute env e) ])
                    |> List.tryFind (fun c -> c.Name = "d")
                    |> Option.bind (fun c -> c.Type)

                match byEnv with
                | Some ty ->
                    envDecidedSeen <- envDecidedSeen + 1

                    for label, r in [ "full", onFull; "empty", onEmpty; "all-null", onNull ] do
                        match typeIn r "d" with
                        | Ok(Some got) when got = ty -> ()
                        | other ->
                            if envDecided.IsNone then
                                envDecided <-
                                    Some(
                                        at (
                                            sprintf
                                                "%A under %A: decided %A by the params, the %s frame typed it %A"
                                                e
                                                env
                                                ty
                                                label
                                                other
                                        )
                                    )
                | None -> byCellsSeen <- byCellsSeen + 1

            // ---- refresh and full agree, the seam reading the same rule ----
            // Over a table (the row walk) and over a prepared source (a derive-only pipeline takes
            // the chunked path there), a derive alone and behind a filter.
            let prime (prepared: bool) pipeline =
                if prepared then
                    Incremental.primePrepared DataFrame.noResolve env idw pipeline (DataFrame.prepare full)
                else
                    Incremental.prime DataFrame.noResolve env idw pipeline full

            let refresh (prepared: bool) pipeline state delta (after: Table) =
                if prepared then
                    Incremental.refreshPrepared
                        DataFrame.noResolve
                        env
                        idw
                        pipeline
                        state
                        delta
                        (DataFrame.prepare after)
                else
                    Incremental.refresh DataFrame.noResolve env idw pipeline state delta after

            for prepared in [ false; true ] do
                for pipeline in [ [ derive ]; [ Filter(Not(IsNull(Col "id"))); derive ] ] do
                    match prime prepared pipeline with
                    | Error _ -> ()
                    | Ok primed ->
                        let viaEval = typeIn (eval env pipeline full) "d"
                        let viaPrime = Ok(typeOfColumn "d" (Incremental.result primed))

                        if viaEval <> viaPrime && refreshAgrees.IsNone then
                            refreshAgrees <-
                                Some(
                                    at (
                                        sprintf
                                            "%A (prepared %b): primed %A, evaluated %A"
                                            pipeline
                                            prepared
                                            viaPrime
                                            viaEval
                                    )
                                )

                        for label, after in [ "all-null", allNull; "full", full ] do
                            match Delta.diff idw full after with
                            | Error _ -> ()
                            | Ok delta ->
                                match refresh prepared pipeline primed delta after, eval env pipeline after with
                                | Ok refreshed, Ok reference ->
                                    let got = (Incremental.result refreshed).Schema

                                    if got <> reference.Schema && refreshAgrees.IsNone then
                                        refreshAgrees <-
                                            Some(
                                                at (
                                                    sprintf
                                                        "%A (prepared %b) refreshed to the %s frame: %A, evaluated %A"
                                                        pipeline
                                                        prepared
                                                        label
                                                        got
                                                        reference.Schema
                                                )
                                            )
                                | _ -> ()

            // ---- the unpivot ----
            let values = List.item (draw (List.length unpivots)) unpivots
            let unpivot = Unpivot([ "id" ], values)

            let walked =
                SchemaWalk.columns (SchemaWalk.ofPipeline schema [ unpivot ])
                |> List.tryFind (fun c -> c.Name = "value")
                |> Option.bind (fun c -> c.Type)

            let outs =
                [ "full", eval env [ unpivot ] full
                  "empty", eval env [ emptying; unpivot ] full
                  "all-null", eval env [ unpivot ] allNull ]

            match walked with
            | Some ty ->
                for label, r in outs do
                    match r with
                    | Ok t ->
                        if not (cellsAdmitted "value" t) && admitted.IsNone then
                            admitted <- Some(at (sprintf "unpivot %A over the %s frame: %A" values label t))
                    | Error _ -> ()

                for label, r in outs do
                    match typeIn r "value" with
                    | Ok(Some got) when got = ty -> ()
                    | other ->
                        if sameEverywhere.IsNone then
                            sameEverywhere <-
                                Some(
                                    at (
                                        sprintf "unpivot %A: decided %A, the %s frame typed it %A" values ty label other
                                    )
                                )
            | None -> ()

            // ---- a float beside a decimal is refused by name, statically and by the cells ----
            let staticMix = Derive("x", Case([ gt0, Col "f" ], Col "m"))
            // The refusal names the column and both families (`DataFrame.floatBesideDecimal`).
            let refusedByName (r: Result<Table, EvalError>) =
                match r with
                | Error(TypeError msg) -> msg.StartsWith("derived column 'x' joins a float and a decimal")
                | _ -> false

            for label, r in
                [ "full", eval env [ staticMix ] full
                  "empty", eval env [ emptying; staticMix ] full ] do
                refusalsSeen <- refusalsSeen + 1

                if not (refusedByName r) && refused.IsNone then
                    refused <- Some(at (sprintf "the static mix over the %s frame answered %A" label r))

            let cellsMix = Derive("x", Case([ gt0, Param "pf" ], Param "pd"))

            let mixEnv = Map.ofList [ "pf", Float 1.5; "pd", dec "2.5" ]
            // Both arms are taken over the full frame only where `i` is both positive and not.
            let iCells = cellsOf "i" full

            let positive (c: Cell) =
                match c with
                | Int v -> v > 0
                | _ -> false

            let both = List.exists positive iCells && not (List.forall positive iCells)

            match eval mixEnv [ cellsMix ] full, both with
            | r, true ->
                refusalsSeen <- refusalsSeen + 1

                if not (refusedByName r) && refused.IsNone then
                    refused <- Some(at (sprintf "the cells' mix answered %A" r))
            | Ok _, false -> ()
            | Error err, false ->
                if refused.IsNone then
                    refused <- Some(at (sprintf "a cells-decided column with one family was refused: %A" err))

            // ... and over an empty frame the cells hold nothing to refuse.
            match eval mixEnv [ emptying; cellsMix ] full with
            | Ok t when typeOfColumn "x" t = Some StringType -> ()
            | other ->
                if refused.IsNone then
                    refused <- Some(at (sprintf "the cells' mix over an empty frame answered %A" other))

        // Each law's arm is counted by the `typing` guard below; its evidence is that arm's count, and
        // the refresh and the unpivot are built every iteration.
        let law = Verdict.covered "DeriveTypingConformance.laws" "typing"

        [ law
              "a decided derive or unpivot types its column identically over a full, an empty and an all-null frame"
              decidedSeen
              sameEverywhere
          law
              "a derive the evaluation's params decide types its column identically over a full, an empty and an all-null frame"
              envDecidedSeen
              envDecided
          law "SchemaWalk states exactly the type the evaluator gives a decided derive" decidedSeen walkAgrees
          law "prime, refresh and the full evaluation type a derived column identically" iterations refreshAgrees
          law "every present derived cell is of its column's type or widens into it" iterations admitted
          law "a float beside a decimal in one derived column is refused by name, never widened" refusalsSeen refused
          law
              "a derive's column carries the unit the check derives over the schema, on a full, an empty and an all-null frame alike"
              unitsSeen
              unitAgrees
          law
              "a derive adding metres to seconds is refused by name, on a full and an empty frame alike"
              unitRefusalsSeen
              unitRefused
          SampleAdequacy.reached
              "DeriveTypingConformance.laws"
              "typing"
              seed
              [ "decided", decidedSeen
                "by cells", byCellsSeen
                "decided by the params", envDecidedSeen
                "all-null decided", allNullSeen
                "refusal", refusalsSeen
                "unit derived", unitsSeen
                "unit refusal", unitRefusalsSeen ] ]

    /// The derived-column typing laws (Phase 338) over the reference evaluator. Self-contained: each
    /// iteration draws a table, a derive from a menu reaching every decided type and every
    /// cells-decided shape, and an unpivot, and BUILDS an empty and an all-null frame from the same
    /// draw. It certifies:
    ///
    ///  - **one type on every frame** — a derive or an unpivot whose column `SchemaWalk` decides is
    ///    typed that way over the full, the empty and the all-null frame;
    ///  - **the params decide on every frame** — a derive the walk leaves to the cells but decides
    ///    over the expression with the evaluation's params substituted is typed that way over the
    ///    full, the empty and the all-null frame (Phase 413);
    ///  - **the walk is the evaluator** — the type `SchemaWalk` states is the one the evaluator gives;
    ///  - **refresh is full** — `Incremental.prime` and `Incremental.refresh` (on the chunked path and
    ///    on the row walk) type the derived column as the full evaluation does;
    ///  - **cells are admitted** — every present cell is of its column's type or widens into it, so a
    ///    typer that claimed a type the evaluator does not produce goes red here;
    ///  - **a float beside a decimal is refused** — statically, over a full and an empty frame alike,
    ///    and by the cells where only they show it;
    ///  - **the unit is the check's** (Phase 429) — a derive's column carries the unit
    ///    `DataFrame.unitOf` derives over the schema, on every frame, and a derive of two units is
    ///    refused by name on every frame.
    let laws (seed: int) (iterations: int) : LawResult list =
        lawsWith DataFrame.evalPipelineInEnv seed iterations
