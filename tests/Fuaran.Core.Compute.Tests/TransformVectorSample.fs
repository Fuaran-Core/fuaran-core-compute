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

    /// The unit shapes (Phase 429), one vector each after the typing ones, over the unit table: a
    /// column's unit through every verb that keeps, composes or drops one, a conversion in each of
    /// its three accepted forms, and seven refusals — metres beside seconds in a derive, a filter, a
    /// `Greatest`, a join key and a union column, a scale of one dimension beside another, and a
    /// conversion across dimensions.
    let private unitShapeNames =
        [ "unit-divide"
          "unit-product-literal"
          "unit-convert"
          "unit-coalesce-case"
          "unit-compare-filter"
          "unit-group-agg"
          "unit-window"
          "unit-join-keep"
          "unit-union"
          "unit-pivot"
          "unit-unpivot-shared"
          "unit-unpivot-mixed"
          "unit-project-label"
          "unit-add-refused"
          "unit-scale-refused"
          "unit-convert-refused"
          "unit-compare-refused"
          "unit-greatest-refused"
          "unit-join-key-refused"
          "unit-union-refused" ]

    /// The repeated-name shapes (Phase 468, D35), one vector each after the unit ones, over the
    /// decimal table: a `Project`, a `GroupBy`, an `Unpivot` and a `Pivot` whose output would name a
    /// column twice, each refused.
    let private repeatShapeNames =
        [ "repeat-project-refused"
          "repeat-group-by-refused"
          "repeat-unpivot-refused"
          "repeat-pivot-refused" ]

    /// The power shapes (Phase 557, D43), one vector each after the repeat shapes, over the decimal
    /// table. An integer-exponent `Pow` is a BYTE-EXACT vector — the pinned routine, held equal bit
    /// for bit to the repeated-multiplication spelling up to the cube — and so is every row of the
    /// special-value table; a float-exponent `Pow`, `Exp`, `Ln` and `Log10` are HOST-ROUNDED, and
    /// their vectors carry the per-column tolerance the exactness mark names; a decimal operand is
    /// refused.
    let private powerShapeNames =
        [ "power-int-exponent"
          "power-int-base"
          "power-cube-is-repeated-multiply"
          "power-special-values"
          "power-float-exponent"
          "power-exp"
          "power-ln-log10"
          "power-decimal-refused" ]

    /// The int32 overflow shapes (Phase 572, `DECISIONS.md` D45), one vector each after the power
    /// shapes, over the decimal table. Each of `Add`, `Sub`, `Mul` and integer `Pow` carries a vector
    /// past each edge of the int32 band, refused as the named `OverflowError`, and one in-band
    /// neighbour landing on the edges themselves, so a host that wraps or widens is red and a host that
    /// refuses too eagerly is red as well.
    let private overflowShapeNames =
        [ "overflow-add-above-refused"
          "overflow-add-below-refused"
          "overflow-add-in-band"
          "overflow-sub-above-refused"
          "overflow-sub-below-refused"
          "overflow-sub-in-band"
          "overflow-mul-above-refused"
          "overflow-mul-below-refused"
          "overflow-mul-in-band"
          "overflow-pow-above-refused"
          "overflow-pow-below-refused"
          "overflow-pow-in-band" ]

    /// Every shape after the base draws, in the order the iterations take them.
    let private drawnShapeNames =
        decimalShapeNames
        @ typingShapeNames
        @ unitShapeNames
        @ repeatShapeNames
        @ powerShapeNames
        @ overflowShapeNames

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

        { Schema =
            [ Field.create "g" StringType
              Field.create "m" DecimalType
              Field.create "v" IntType
              Field.create "w" FloatType ]
          Columns =
            [ KitColumn.create "g" StringType g
              KitColumn.create "m" DecimalType m
              KitColumn.create "v" IntType v
              KitColumn.create "w" FloatType w ] }

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
        | "repeat-project-refused" -> [ Project [ "m", "x"; "v", "x" ] ]
        | "repeat-group-by-refused" -> [ GroupBy([ "g" ], [ agg "g" Sum "v" ]) ]
        | "repeat-unpivot-refused" -> [ Derive("variable", Col "g"); Unpivot([ "variable" ], [ "v"; "w" ]) ]
        | "repeat-pivot-refused" ->
            [ Derive("a", Col "v")
              Pivot
                  { Index = [ "a" ]
                    On = "g"
                    Values = "w"
                    Agg = Sum } ]
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

    let private unitOf (text: string) : UnitOfMeasure =
        match Unit.parse text with
        | Ok u -> u
        | Error _ -> Unit.dimensionless

    /// The unit table (Phase 429): a key, a distance in metres (labelled), two elapsed times in
    /// seconds, a mass in kilograms, the distance again in kilometres, and a unitless count that
    /// carries a label and a description. `distanceUnit` is the unit `d` is declared in — `m` for
    /// every shape but the union refusal's second operand, which declares it in `km`.
    let unitTable (distanceUnit: string) (rows: int) (offset: int) : Table =
        let keys = [| "a"; "b"; "c" |]
        let g = [ for i in 0 .. rows - 1 -> Str keys[i % 3] ]

        let d =
            [ for i in 0 .. rows - 1 ->
                  if (i + offset) % 4 = 3 then
                      Null
                  else
                      Int((i + 1) * 100 + offset * 10) ]

        let t =
            [ for i in 0 .. rows - 1 ->
                  if (i + offset) % 5 = 4 then
                      Null
                  else
                      Float(float (i + 1 + offset) * 2.5) ]

        let h = [ for i in 0 .. rows - 1 -> Float(float (i + offset) + 0.5) ]
        let w = [ for i in 0 .. rows - 1 -> Float(float (i * 3 + offset) / 4.0) ]

        let dk =
            [ for i in 0 .. rows - 1 -> Float(float ((i + 1) * 100 + offset * 10) / 1000.0) ]

        let n = [ for i in 0 .. rows - 1 -> Int(i + offset) ]

        { Schema =
            [ Field.create "g" StringType
              Field.create "d" IntType
              |> Field.withUnit (unitOf distanceUnit)
              |> Field.withLabel "Distance"
              Field.create "t" FloatType |> Field.withUnit (unitOf "s")
              Field.create "h" FloatType |> Field.withUnit (unitOf "s")
              Field.create "w" FloatType |> Field.withUnit (unitOf "kg")
              Field.create "dk" FloatType |> Field.withUnit (unitOf "km")
              Field.create "n" IntType
              |> Field.withLabel "Count"
              |> Field.withDescription "How many" ]
          Columns =
            [ KitColumn.create "g" StringType g
              KitColumn.create "d" IntType d
              KitColumn.create "t" FloatType t
              KitColumn.create "h" FloatType h
              KitColumn.create "w" FloatType w
              KitColumn.create "dk" FloatType dk
              KitColumn.create "n" IntType n ] }

    let private unitPipelineOf (shape: string) (rows: int) (offset: int) : Transform list =
        let d = Col "d"
        let t = Col "t"
        // The second operand of a join or a set verb: the same table less its last row.
        let other = Embedded(unitTable "m" (rows - 1) offset)

        let win fn ofCol asCol : Transform =
            Window
                { PartitionBy = [ "g" ]
                  OrderBy = [ "n", Asc ]
                  Fn = fn
                  Of = ofCol
                  As = asCol }

        match shape with
        | "unit-divide" -> [ Derive("v", Binary(Div, d, t)); Derive("r", Binary(Div, Lit(Float 1.0), t)) ]
        | "unit-product-literal" -> [ Derive("a", Binary(Mul, d, Lit(Int 2))); Derive("b", Binary(Mul, d, d)) ]
        | "unit-convert" ->
            [ Derive("k", Convert(d, unitOf "km"))
              Derive("s", Binary(Add, Convert(Col "dk", unitOf "m"), d))
              Derive("tag", Convert(Lit(Int 5), unitOf "m"))
              Derive("same", Convert(d, unitOf "m")) ]
        | "unit-coalesce-case" ->
            [ Derive("c", Coalesce [ d; Lit(Int 0) ])
              Derive("e", Case([ Binary(Gt, Col "n", Lit(Int 1)), d ], Binary(Sub, d, Lit(Int 1))))
              Derive("lo", ApplyFn(Least, [ t; Col "h" ])) ]
        | "unit-compare-filter" ->
            [ Filter(Binary(Gt, d, Convert(Col "dk", unitOf "m")))
              Filter(Binary(Lt, t, Lit(Float 100.0))) ]
        | "unit-group-agg" ->
            [ GroupBy([ "g" ], [ agg "s" Sum "w"; agg "n" Count "n"; agg "m" Mean "d"; agg "x" Max "t" ]) ]
        | "unit-window" -> [ win Lag "w" "prev"; win CumulSum "d" "run"; win RowNumber "n" "rn" ]
        | "unit-join-keep" -> [ Join(other, [ "g", "g" ], Inner) ]
        | "unit-union" -> [ Union other ]
        | "unit-pivot" ->
            [ Pivot
                  { Index = [ "n" ]
                    On = "g"
                    Values = "w"
                    Agg = Sum } ]
        | "unit-unpivot-shared" -> [ Unpivot([ "g" ], [ "t"; "h" ]) ]
        | "unit-unpivot-mixed" -> [ Unpivot([ "g" ], [ "d"; "t" ]) ]
        | "unit-project-label" ->
            [ Transform.sortBy [ "n", Desc ]
              Transform.limit 2 0
              Project [ "d", "dist"; "n", "n" ] ]
        | "unit-add-refused" -> [ Derive("x", Binary(Add, d, t)) ]
        | "unit-scale-refused" -> [ Derive("x", Binary(Add, Col "dk", d)) ]
        | "unit-convert-refused" -> [ Derive("x", Convert(d, unitOf "s")) ]
        | "unit-compare-refused" -> [ Filter(Binary(Gt, d, t)) ]
        | "unit-greatest-refused" -> [ Derive("x", ApplyFn(Greatest, [ Col "w"; t ])) ]
        | "unit-join-key-refused" -> [ Join(other, [ "d", "t" ], Inner) ]
        | _ -> [ Union(Embedded(unitTable "km" (rows - 1) offset)) ]

    /// The power shapes (Phase 557, D43) over the decimal table: `v` an int, `w` a float at or above
    /// zero, `m` a decimal. No vector answers a non-finite float — the wire carries none — so the
    /// table's `Inf` rows (an `exp` overflow, `ln Inf`) are held by the evaluator's own suite alone.
    let private powerPipelineOf (shape: string) : Transform list =
        let w = Col "w"
        let fl (f: float) = Lit(Float f)
        let pow b e = ApplyFn(Pow, [ b; e ])

        match shape with
        | "power-int-exponent" -> [ Derive("cube", pow w (Lit(Int 3))); Derive("recip", pow w (Lit(Int -2))) ]
        | "power-int-base" ->
            [ Derive("sq", pow (Col "v") (Lit(Int 2)))
              Derive("inv", pow (Col "v") (Lit(Int -1))) ]
        | "power-cube-is-repeated-multiply" ->
            [ Derive("routine", pow w (Lit(Int 3)))
              Derive("spelled", Binary(Mul, Binary(Mul, w, w), w))
              Derive("same", Binary(Eq, Col "routine", Col "spelled")) ]
        | "power-special-values" ->
            [ Derive("zeroZero", pow (fl 0.0) (fl 0.0))
              Derive("oneToAny", pow (fl 1.0) (fl 2.5))
              Derive("zeroNeg", pow (fl 0.0) (fl -1.0))
              Derive("negNonInt", pow (fl -8.0) (fl (1.0 / 3.0)))
              Derive("negIntegral", pow (fl -2.0) (fl 2.0))
              Derive("intRecip", pow (Lit(Int 2)) (Lit(Int -1)))
              Derive("intZeroNeg", pow (Lit(Int 0)) (Lit(Int -2)))
              Derive("expUnder", ApplyFn(Exp, [ fl -746.0 ]))
              Derive("lnOne", ApplyFn(Ln, [ fl 1.0 ]))
              Derive("lnZero", ApplyFn(Ln, [ fl 0.0 ]))
              Derive("lnNeg", ApplyFn(Ln, [ fl -1.0 ]))
              Derive("log10Zero", ApplyFn(Log10, [ Lit(Int 0) ])) ]
        | "power-float-exponent" -> [ Derive("p", pow w (Binary(Add, w, fl 0.5))) ]
        | "power-exp" -> [ Derive("e", ApplyFn(Exp, [ Binary(Sub, Binary(Mul, w, fl 3.0), fl 2.0) ])) ]
        | "power-ln-log10" ->
            [ Derive("l", ApplyFn(Ln, [ Binary(Add, w, fl 0.25) ]))
              Derive("l10", ApplyFn(Log10, [ Binary(Add, w, fl 1.0) ])) ]
        | _ -> [ Derive("p", pow (Col "m") (Lit(Int 2))) ]

    /// The int32 overflow shapes (Phase 572, D45). Each operand is first derived into a column, so the
    /// operation runs over a column as a host's kernel would rather than over two literals; every row
    /// carries the same values, so the answer does not depend on the draw. An in-band shape lands
    /// exactly on `Int32.MaxValue` and `Int32.MinValue` where the operator can reach them.
    let private overflowPipelineOf (shape: string) : Transform list =
        let i (n: int) = Lit(Int n)
        let hi = System.Int32.MaxValue
        let lo = System.Int32.MinValue
        let pow b e = ApplyFn(Pow, [ b; e ])

        let past (a: int) (op: BinOp) (b: int) =
            [ Derive("a", i a); Derive("o", Binary(op, Col "a", i b)) ]

        let inBand (op: BinOp) (up: int) (down: int) =
            [ Derive("hi", i (hi - 1))
              Derive("lo", i (lo + 1))
              Derive("top", Binary(op, Col "hi", i up))
              Derive("bottom", Binary(op, Col "lo", i down)) ]

        match shape with
        | "overflow-add-above-refused" -> past hi Add 1
        | "overflow-add-below-refused" -> past lo Add -1
        | "overflow-add-in-band" -> inBand Add 1 -1
        | "overflow-sub-above-refused" -> past hi Sub -1
        | "overflow-sub-below-refused" -> past lo Sub 1
        | "overflow-sub-in-band" -> inBand Sub -1 1
        // `MinValue * -1` is 2^31, the negation no int32 holds; the other edge is a product past it.
        | "overflow-mul-above-refused" -> past lo Mul -1
        | "overflow-mul-below-refused" -> past -65536 Mul 32769
        | "overflow-mul-in-band" ->
            [ Derive("a", i 65536)
              Derive("top", Binary(Mul, Col "a", i 32767))
              Derive("bottom", Binary(Mul, Col "a", i -32768))
              Derive("max", i hi)
              Derive("negated", Binary(Mul, Col "max", i -1)) ]
        // 2^31 is one past the band; (-1291)^3 is past its lower edge by its final product.
        | "overflow-pow-above-refused" -> [ Derive("a", i 2); Derive("o", pow (Col "a") (i 31)) ]
        | "overflow-pow-below-refused" -> [ Derive("a", i -1291); Derive("o", pow (Col "a") (i 3)) ]
        | _ ->
            [ Derive("two", i -2)
              Derive("bottom", pow (Col "two") (i 31))
              Derive("c", i 1290)
              Derive("top", pow (Col "c") (i 3))
              Derive("nc", i -1290)
              Derive("low", pow (Col "nc") (i 3)) ]

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
            { Schema =
                [ Field.create "g" StringType
                  Field.create "v" IntType
                  Field.create "w" FloatType ]
              Columns =
                [ KitColumn.create "g" StringType g
                  KitColumn.create "v" IntType v
                  KitColumn.create "w" FloatType w ] }

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
            let shape = shapeOf iteration

            if shape.StartsWith "unit-" then
                (unitTable "m" rows offset, unitPipelineOf shape rows offset), r2
            elif shape.StartsWith "power-" then
                (decimalTable rows offset, powerPipelineOf shape), r2
            elif shape.StartsWith "overflow-" then
                (decimalTable rows offset, overflowPipelineOf shape), r2
            else
                (decimalTable rows offset, decimalPipelineOf shape rows offset), r2

    /// The generator in the shape `Conformance.transformLaws` takes — the iteration counter folded
    /// into the state, so the law and the export draw the identical sample.
    let lawGen () : ConfRng.T -> (Table * Transform list) * ConfRng.T =
        let mutable i = -1

        fun rng ->
            i <- i + 1
            gen i rng
