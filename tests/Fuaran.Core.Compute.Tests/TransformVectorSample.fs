namespace Fuaran.Compute.Tests

// ============================================================================
//  The declared sample the transform law vectors are drawn from (Phase 342:
//  split out of `LawVectorExport` unchanged, so that a host other than .NET can
//  REPRODUCE the sample rather than read the committed file). FSharp.Core, the
//  dataframe package and the conformance kit's `ConfRng` only, and Fable-clean:
//  the node benchmark harness compiles this file and runs the prepared-result
//  law over the same `(source, pipeline)` pairs the committed
//  `conformance/laws/transform-laws.json` records. `LawVectorExport` renders the
//  file from this module; `LawVectorTests` holds the committed file equal to
//  that render, so the two cannot describe different samples.
// ============================================================================

module TransformVectorSample =

    open Fuaran.Core
    open Fuaran.Compute

    /// The seed and sample size the exported vectors are drawn from. Declared rather than taken
    /// from a test's own law invocation, because a host re-running the family must be able to
    /// reproduce the sample from the file alone.
    let seed = 20260904

    /// Two draws of each of the eight pipeline shapes below, then one of each decimal shape (Phase
    /// 277). Far fewer than a law run: each vector carries three whole wire strings, and a host does
    /// not need a hundred draws of the same eight shapes to disagree with the reference.
    let private baseIterations = 16

    // -----------------------------------------------------------------------
    //  the declared sample generator
    // -----------------------------------------------------------------------

    /// The eight pipeline shapes, chosen to reach the semantics the parity contract pins rather
    /// than to be numerous: null propagation through a predicate, sort/distinct stability over a
    /// tie-heavy key, int↔float coercion, group stability and aggregate null handling, an
    /// offset window, the pinned division-by-zero answer, the canonical float layout on a
    /// non-terminating quotient, and a pipeline the reference REFUSES — which is a parity case in
    /// its own right, since the law requires the host to refuse it too.
    let private baseShapeNames =
        [ "filter"
          "sort-distinct"
          "derive-coerce"
          "group-agg"
          "limit"
          "div-by-zero"
          "float-divide"
          "unknown-column" ]

    /// The decimal shapes (Phase 277), one vector each after the sixteen above: a decimal column
    /// through every verb, the exact arithmetic, `Quotient` and `Rounded` under a stated rounding,
    /// `Round` / `Floor` / `Ceil` as their scale-0 specialisations, and four refusals — a decimal
    /// `Div`, a decimal beside a float, a float `Rounded`, and a scale past `1000`.
    let private decimalShapeNames =
        [ "decimal-filter"
          "decimal-project"
          "decimal-derive-exact"
          "decimal-quotient"
          "decimal-rounded-mod-abs"
          "decimal-round-floor-ceil"
          "decimal-cast"
          "decimal-group-agg"
          "decimal-join"
          "decimal-window"
          "decimal-pivot"
          "decimal-unpivot"
          "decimal-sort"
          "decimal-distinct"
          "decimal-limit"
          "decimal-union"
          "decimal-intersect"
          "decimal-except"
          "decimal-in-list"
          "decimal-div-refused"
          "decimal-float-refused"
          "decimal-rounded-float-refused"
          "decimal-quotient-scale-refused" ]

    /// The typing shapes (Phase 338), one vector each after the decimal ones, over the decimal
    /// table: a decided derive's column type over an empty frame and over an all-null one (an int
    /// and a decimal), a `Case` joining an int and a float (the cells decide), an unpivot of an int
    /// and a float value column over a full and an empty frame, and the float-beside-decimal
    /// refusal.
    let private typingShapeNames =
        [ "typed-derive-empty"
          "typed-derive-all-null"
          "typed-case-int-float"
          "typed-derive-decimal-empty"
          "typed-derive-decimal-all-null"
          "typed-unpivot-int-float"
          "typed-unpivot-empty"
          "typed-float-decimal-refused" ]

    /// Every shape after the base draws, in the order the iterations take them.
    let private drawnShapeNames = decimalShapeNames @ typingShapeNames

    /// Every shape a vector is rendered for, in the order the iterations take them.
    let shapeNames = baseShapeNames @ drawnShapeNames

    /// The sample size: the sixteen base draws, then one draw per decimal and typing shape.
    let iterations = baseIterations + List.length drawnShapeNames

    /// The shape iteration `i` takes.
    let shapeOf (i: int) : string =
        if i < baseIterations then
            List.item (i % List.length baseShapeNames) baseShapeNames
        else
            List.item (i - baseIterations) drawnShapeNames

    let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

    /// A decimal cell from a count of hundredths, through the canonicalising constructor.
    let cents (c: int) : Cell =
        let a = abs c

        let text =
            (if c < 0 then "-" else "")
            + string (a / 100)
            + "."
            + (string (a % 100)).PadLeft(2, '0')

        Cell.decimal text |> Option.defaultValue Null

    /// The decimal draw's table (Phase 277): a tie-heavy string key, a decimal column carrying
    /// nulls, an int column and a float column.
    let decimalTable (rows: int) (offset: int) : Table =
        let keys = [| "a"; "b"; "c" |]
        let g = [ for i in 0 .. rows - 1 -> Str keys[i % 3] ]
        let v = [ for i in 0 .. rows - 1 -> Int(i * 2 - offset) ]
        let w = [ for i in 0 .. rows - 1 -> Float(float (i + offset) / 4.0) ]

        let m =
            [ for i in 0 .. rows - 1 ->
                  if (i + offset) % 4 = 3 then
                      Null
                  else
                      cents ((i * 137 + offset * 25) % 500 - 200) ]

        { Schema = [ "g", StringType; "m", DecimalType; "v", IntType; "w", FloatType ]
          Columns =
            [ Column.create "g" StringType g
              Column.create "m" DecimalType m
              Column.create "v" IntType v
              Column.create "w" FloatType w ] }

    let private decimalPipelineOf (shape: string) (rows: int) (offset: int) : Transform list =
        let m = Col "m"

        let dec (t: string) =
            Lit(Cell.decimal t |> Option.defaultValue Null)
        // The second operand of a join or a set verb: the same table less its last row, so the two
        // share every row but one.
        let other = Embedded(decimalTable (rows - 1) offset)

        match shape with
        | "decimal-filter" -> [ Filter(Binary(Ge, m, dec "0.5")) ]
        | "decimal-project" -> [ Project [ "m", "amount"; "g", "g" ] ]
        | "decimal-derive-exact" ->
            [ Derive("t", Binary(Sub, Binary(Add, Binary(Mul, m, Lit(Int 3)), Col "v"), dec "0.05")) ]
        | "decimal-quotient" ->
            [ Derive(
                  "q",
                  Quotient(
                      m,
                      Lit(Int 3),
                      { Scale = Slot.Lit 4
                        Mode = RoundingMode.HalfEven }
                  )
              ) ]
        | "decimal-rounded-mod-abs" ->
            [ Derive(
                  "r",
                  Rounded(
                      Binary(Mod, ApplyFn(Abs, [ m ]), dec "0.7"),
                      { Scale = Slot.Lit 1
                        Mode = RoundingMode.HalfUp }
                  )
              ) ]
        | "decimal-round-floor-ceil" ->
            [ Derive("r", ApplyFn(Round, [ m ]))
              Derive("lo", ApplyFn(Floor, [ m ]))
              Derive("hi", ApplyFn(Ceil, [ m ])) ]
        | "decimal-cast" -> [ Derive("c", Cast(DecimalType, Col "w")); Derive("f", Cast(FloatType, m)) ]
        | "decimal-group-agg" ->
            [ GroupBy(
                  [ "g" ],
                  [ agg "s" Sum "m"
                    agg "lo" Min "m"
                    agg "hi" Max "m"
                    agg "k" CountDistinct "m"
                    agg "mean" Mean "m" ]
              ) ]
        | "decimal-join" -> [ Join(other, [ "m", "m" ], Inner) ]
        | "decimal-window" ->
            [ Window
                  { PartitionBy = [ "g" ]
                    OrderBy = [ "v", Asc ]
                    Fn = CumulSum
                    Of = "m"
                    As = "run" } ]
        | "decimal-pivot" ->
            [ Pivot
                  { Index = [ "v" ]
                    On = "g"
                    Values = "m"
                    Agg = Sum } ]
        | "decimal-unpivot" -> [ Unpivot([ "g" ], [ "m" ]) ]
        | "decimal-sort" -> [ Transform.sortBy [ "m", Desc ] ]
        | "decimal-distinct" -> [ Project [ "m", "m" ]; Distinct ]
        | "decimal-limit" -> [ Transform.sortBy [ "m", Asc ]; Transform.limit 2 1 ]
        | "decimal-union" -> [ Union other ]
        | "decimal-intersect" -> [ Intersect other ]
        | "decimal-except" -> [ Except other ]
        // The items: the decimal the table holds at row 1, an int, and a decimal it does not hold.
        | "decimal-in-list" ->
            [ Filter(InList(m, [ cents ((137 + offset * 25) % 500 - 200) |> Lit; Lit(Int 2); dec "9.99" ])) ]
        | "decimal-div-refused" -> [ Derive("q", Binary(Div, m, Lit(Int 2))) ]
        | "decimal-float-refused" -> [ Derive("x", Binary(Add, m, Col "w")) ]
        // ---- Phase 338: the derived column typed by its expression ----
        | "typed-derive-empty" -> [ Filter(Lit(Bool false)); Derive("t", Binary(Add, Col "v", Lit(Int 1))) ]
        | "typed-derive-all-null" -> [ Derive("t", Case([ IsNull(Col "g"), Col "v" ], Lit Null)) ]
        | "typed-case-int-float" -> [ Derive("t", Case([ Binary(Gt, Col "v", Lit(Int 0)), Col "v" ], Col "w")) ]
        | "typed-derive-decimal-empty" -> [ Filter(Lit(Bool false)); Derive("t", Binary(Mul, m, Col "v")) ]
        | "typed-derive-decimal-all-null" -> [ Filter(IsNull m); Derive("t", Binary(Add, m, Lit(Int 1))) ]
        | "typed-unpivot-int-float" -> [ Unpivot([ "g" ], [ "v"; "w" ]) ]
        | "typed-unpivot-empty" -> [ Filter(Lit(Bool false)); Unpivot([ "g" ], [ "v"; "w" ]) ]
        | "typed-float-decimal-refused" -> [ Derive("t", Case([ Binary(Gt, Col "v", Lit(Int 0)), Col "w" ], m)) ]
        | "decimal-rounded-float-refused" ->
            [ Derive(
                  "r",
                  Rounded(
                      Col "w",
                      { Scale = Slot.Lit 2
                        Mode = RoundingMode.HalfEven }
                  )
              ) ]
        | _ ->
            [ Derive(
                  "q",
                  Quotient(
                      m,
                      Lit(Int 3),
                      { Scale = Slot.Lit 1001
                        Mode = RoundingMode.Down }
                  )
              ) ]

    let private pipelineOf (k: int) : Transform list =
        match k with
        | 0 -> [ Filter(Binary(Gt, Col "v", Lit(Int 0))) ]
        | 1 -> [ Transform.sortBy [ "g", Asc; "v", Asc ]; Distinct ]
        | 2 -> [ Derive("d", Binary(Add, Col "v", Col "w")) ]
        | 3 -> [ GroupBy([ "g" ], [ agg "s" Sum "v"; agg "n" Count "v"; agg "m" Mean "w" ]) ]
        | 4 -> [ Transform.limit 2 1 ]
        | 5 -> [ Derive("q", Binary(Div, Col "v", Lit(Int 0))) ]
        | 6 -> [ Derive("r", Binary(Div, Col "w", Lit(Float 3.0))) ]
        | _ -> [ Filter(Binary(Gt, Col "nope", Lit(Int 0))) ]

    /// One drawn sample: a three-column table (a tie-heavy string key, an int column carrying
    /// nulls, a float column) and the shape for this iteration. The shape is the iteration index
    /// modulo eight rather than a draw, deliberately: a drawn shape can be missed over sixteen
    /// iterations, and a corpus artefact that silently stopped carrying a shape would leave a host
    /// certifying less than the file claims. The TABLE is drawn, so the eight shapes are exercised
    /// over different data on each pass.
    let private baseSample (iteration: int) (extra: int) (offset: int) : Table * Transform list =
        let rows = extra + 2

        let groupKeys = [| "a"; "b"; "c" |]
        let g = [ for i in 0 .. rows - 1 -> Str groupKeys[i % 3] ]

        let v =
            [ for i in 0 .. rows - 1 ->
                  if (i + offset) % 4 = 0 then
                      Null
                  else
                      Int(i * 3 + offset - 5) ]

        let w = [ for i in 0 .. rows - 1 -> Float(float (i + offset) / 2.0) ]

        let table: Table =
            { Schema = [ "g", StringType; "v", IntType; "w", FloatType ]
              Columns =
                [ Column.create "g" StringType g
                  Column.create "v" IntType v
                  Column.create "w" FloatType w ] }

        table, pipelineOf (iteration % List.length baseShapeNames)

    /// One drawn sample. The first sixteen iterations are the base shapes above; from there each
    /// takes the next decimal shape (Phase 277) over the decimal table, with three rows or more so
    /// that a present decimal exists for each refusal to meet. Both read the same two draws, so the
    /// base vectors are the bytes they were before the decimal shapes were appended.
    let gen (iteration: int) (rng: ConfRng.T) : (Table * Transform list) * ConfRng.T =
        let extra, r1 = ConfRng.intBelow 4 rng
        let offset, r2 = ConfRng.intBelow 7 r1

        if iteration < baseIterations then
            baseSample iteration extra offset, r2
        else
            let rows = extra + 3
            (decimalTable rows offset, decimalPipelineOf (shapeOf iteration) rows offset), r2

    /// The generator in the shape `Conformance.transformLaws` takes — the iteration counter folded
    /// into the state, so the law and the export draw the identical sample.
    let lawGen () : ConfRng.T -> (Table * Transform list) * ConfRng.T =
        let mutable i = -1

        fun rng ->
            i <- i + 1
            gen i rng
