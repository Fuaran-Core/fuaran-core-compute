module Fuaran.Compute.Tests.UnitPropagationTests

// ============================================================================
//  Phase 429 — the evaluator propagates and checks units (`DECISIONS.md` D29).
//
//  One test per rule of D29's table, every refusal class reached by name, and a
//  go-red per rule: each positive assertion names the unit the rule derives and
//  each refusal asserts the `UnitMismatch` the rule raises, so removing a rule
//  from the evaluator reddens its row here. The conformance go-red at the end
//  runs the derive-typing laws over an evaluator that drops every unit and
//  expects the unit law, and only the unit law, to go red.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Conformance

let private u (text: string) : UnitOfMeasure =
    match Unit.parse text with
    | Ok unit -> unit
    | Error e -> failtestf "'%s' is not a unit: %A" text e

let private ok (r: Result<'a, EvalError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got %s" (DataFrame.errorString e)

let private unitText (f: Field) : string option = f.Unit |> Option.map Unit.render

let private fieldOf (name: string) (t: Table) : Field =
    match Table.tryField name t with
    | Some f -> f
    | None -> failtestf "no column '%s' in %A" name (t.Schema |> List.map _.Name)

let private unitOfColumn (name: string) (t: Table) : string option = fieldOf name t |> unitText

let private cellsOf (name: string) (t: Table) : Cell list =
    Table.tryColumn name t |> Option.map Column.toCells |> Option.defaultValue []

/// The mismatch, as (step, left unit, right unit), or the Ok the test did not expect.
let private mismatch (r: Result<'a, EvalError>) : string * string * string =
    match r with
    | Error(UnitMismatch(step, _, l, r)) -> step, l, r
    | Error e -> failtestf "expected a UnitMismatch, got %s" (DataFrame.errorString e)
    | Ok _ -> failtest "expected a UnitMismatch, got Ok"

let private col name ty cells : Column = KitColumn.create name ty cells

/// Distances in metres, elapsed seconds, a mass in kilograms, the same distance in kilometres, a
/// labelled, described and unit-less count, and a key.
let private measures: Table =
    { Schema =
        [ Field.create "id" StringType
          Field.create "d" IntType |> Field.withUnit (u "m") |> Field.withLabel "Distance"
          Field.create "t" FloatType |> Field.withUnit (u "s")
          Field.create "w" FloatType
          |> Field.withUnit (u "kg")
          |> Field.withDescription "Mass at the gate"
          Field.create "dk" FloatType |> Field.withUnit (u "km")
          Field.create "n" IntType
          |> Field.withLabel "Count"
          |> Field.withDescription "How many"
          Field.create "g" StringType ]
      Columns =
        [ col "id" StringType [ Str "a"; Str "b"; Str "c"; Str "d" ]
          col "d" IntType [ Int 100; Int 250; Null; Int 400 ]
          col "t" FloatType [ Float 10.0; Float 25.0; Float 5.0; Null ]
          col "w" FloatType [ Float 1.5; Float 2.5; Float 3.0; Float 4.0 ]
          col "dk" FloatType [ Float 0.1; Float 0.25; Float 0.3; Float 0.4 ]
          col "n" IntType [ Int 1; Int 2; Int 3; Int 4 ]
          col "g" StringType [ Str "x"; Str "y"; Str "x"; Str "y" ] ] }

let private derive name expr = [ Derive(name, expr) ]

let private run pipeline =
    DataFrame.evalPipeline pipeline measures

let private unitOfDerive expr =
    run (derive "out" expr) |> ok |> unitOfColumn "out"

let private win fn ofCol asCol : Transform =
    Window
        { PartitionBy = [ "g" ]
          OrderBy = [ "id", Asc ]
          Fn = fn
          Of = ofCol
          As = asCol }

let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

/// A query declaration over `measures` as a named source, declaring `result`.
let private query (result: Schema) : Query =
    { Id = "speeds"
      Params = []
      ResultSchema = result
      Effect =
        { Host = ReadsHost
          Determinism = Effect.network }
      Source = Ref "measures"
      TimeoutMs = None
      PageSize = None
      Where = []
      OrderBy = [] }

let private registered (result: Schema) (pipeline: Transform list) : PipelineQuery =
    { Query = query result
      Pipeline = pipeline
      Sources = Map.ofList [ "measures", measures.Schema ] }

let private idw = RowIdentity.byColumn "id"

[<Tests>]
let tests =
    testList
        "UnitPropagation"
        [ // ---- addition, subtraction, comparison: one dimension, one scale ----
          testCase "metres plus seconds is refused, naming the derive, the expression and both units"
          <| fun _ ->
              let expr = Binary(Add, Col "d", Col "t")
              let step, l, r = mismatch (run (derive "x" expr))
              Expect.equal (step, l, r) ("derive 'x'", "m", "s") "the step and both units"

              match run (derive "x" expr) with
              | Error(UnitMismatch(_, e, _, _)) -> Expect.equal e expr "the expression the units met in"
              | _ -> ()

          testCase "metres plus metres keeps metres, and a sum against an unconstrained literal keeps the unit"
          <| fun _ ->
              Expect.equal (unitOfDerive (Binary(Add, Col "d", Col "d"))) (Some "m") "m + m"
              Expect.equal (unitOfDerive (Binary(Sub, Col "d", Lit(Int 1)))) (Some "m") "m - 1"
              Expect.equal (unitOfDerive (Binary(Add, Lit(Int 1), Col "d"))) (Some "m") "1 + m"

          testCase "a different scale of one dimension is refused until converted: kilometres beside metres"
          <| fun _ ->
              let _, l, r = mismatch (run (derive "x" (Binary(Add, Col "dk", Col "d"))))
              Expect.equal (l, r) ("km", "m") "km beside m"

              let converted =
                  run (derive "x" (Binary(Add, Convert(Col "dk", u "m"), Col "d"))) |> ok

              Expect.equal (unitOfColumn "x" converted) (Some "m") "converted, the sum is in metres"
              Expect.equal (cellsOf "x" converted) [ Float 200.0; Float 500.0; Null; Float 800.0 ] "0.1 km is 100 m"

          testCase "a comparison requires one dimension, in a filter as in a derive, and answers no unit"
          <| fun _ ->
              let step, l, r = mismatch (run [ Filter(Binary(Gt, Col "d", Col "t")) ])
              Expect.equal (step, l, r) ("filter", "m", "s") "the filter's mismatch"
              Expect.equal (unitOfDerive (Binary(Gt, Col "d", Col "d"))) None "a boolean carries none"

              Expect.equal
                  (run [ Filter(Binary(Gt, Col "d", Lit(Int 150))) ] |> ok).Schema.Length
                  7
                  "a literal matches anything"

          testCase "Coalesce, a Case's arms, Least, Greatest and InList require one unit and carry it"
          <| fun _ ->
              Expect.equal (unitOfDerive (Coalesce [ Col "d"; Lit(Int 0) ])) (Some "m") "coalesce"
              Expect.equal (unitOfDerive (Coalesce [ Col "d"; Col "d" ])) (Some "m") "coalesce of two"
              let _, l, r = mismatch (run (derive "x" (Coalesce [ Col "d"; Col "t" ])))
              Expect.equal (l, r) ("m", "s") "coalesce across units"

              let gt = Binary(Gt, Col "n", Lit(Int 2))
              Expect.equal (unitOfDerive (Case([ gt, Col "d" ], Lit Null))) (Some "m") "case arm"
              // The else is the base the arms must agree with, so the mismatch names it first.
              let _, l, r = mismatch (run (derive "x" (Case([ gt, Col "d" ], Col "w"))))
              Expect.equal (l, r) ("kg", "m") "case arms across units"

              Expect.equal (unitOfDerive (ApplyFn(Least, [ Col "d"; Col "d" ]))) (Some "m") "least"
              let _, l, r = mismatch (run (derive "x" (ApplyFn(Greatest, [ Col "w"; Col "t" ]))))
              Expect.equal (l, r) ("kg", "s") "greatest across units"

              Expect.equal (unitOfDerive (InList(Col "d", [ Lit(Int 100); Lit(Int 250) ]))) None "in answers a boolean"
              let _, l, r = mismatch (run (derive "x" (InList(Col "d", [ Col "t" ]))))
              Expect.equal (l, r) ("m", "s") "in across units"

          // ---- multiplication, division, powers: the algebra ----
          testCase "metres over seconds derives metres per second; a product composes; a literal is dimensionless"
          <| fun _ ->
              let speed = run (derive "v" (Binary(Div, Col "d", Col "t"))) |> ok
              Expect.equal (unitOfColumn "v" speed) (Some "m/s") "m / s"
              Expect.equal (cellsOf "v" speed) [ Float 10.0; Float 10.0; Null; Null ] "the cells are the quotient"
              Expect.equal (unitOfDerive (Binary(Mul, Col "d", Col "d"))) (Some "m2") "m * m"
              Expect.equal (unitOfDerive (Binary(Mul, Col "d", Lit(Int 2)))) (Some "m") "m * 2"
              Expect.equal (unitOfDerive (Binary(Div, Lit(Float 1.0), Col "t"))) (Some "/s") "1 / s"
              Expect.equal (unitOfDerive (Binary(Div, Col "d", Lit(Int 2)))) (Some "m") "m / 2"
              Expect.equal (unitOfDerive (Binary(Mul, Binary(Div, Col "d", Col "t"), Col "t"))) (Some "m") "m/s * s"
              Expect.equal (unitOfDerive (Binary(Mod, Col "d", Lit(Int 7)))) (Some "m") "m mod 7"

              Expect.equal
                  (unitOfDerive (
                      Quotient(
                          Cast(DecimalType, Col "d"),
                          Cast(DecimalType, Col "n"),
                          { Scale = Slot.Lit 2
                            Mode = RoundingMode.HalfEven }
                      )
                  ))
                  (Some "m")
                  "an exact quotient composes as a division"

          testCase "Cast, Abs, Round, Floor, Ceil and Rounded keep the unit; text, date and Sqrt carry none"
          <| fun _ ->
              Expect.equal (unitOfDerive (Cast(FloatType, Col "d"))) (Some "m") "cast"
              Expect.equal (unitOfDerive (ApplyFn(Abs, [ Col "d" ]))) (Some "m") "abs"
              Expect.equal (unitOfDerive (ApplyFn(Round, [ Col "t" ]))) (Some "s") "round"
              Expect.equal (unitOfDerive (ApplyFn(Floor, [ Col "t" ]))) (Some "s") "floor"
              Expect.equal (unitOfDerive (ApplyFn(Ceil, [ Col "t" ]))) (Some "s") "ceil"

              Expect.equal
                  (unitOfDerive (
                      Rounded(
                          Cast(DecimalType, Col "d"),
                          { Scale = Slot.Lit 0
                            Mode = RoundingMode.HalfUp }
                      )
                  ))
                  (Some "m")
                  "rounded"

              Expect.equal (unitOfDerive (ApplyFn(Sqrt, [ Col "w" ]))) None "sqrt"
              Expect.equal (unitOfDerive (Cast(StringType, Col "d"))) (Some "m") "a cast to text keeps the unit"
              Expect.equal (unitOfDerive (ApplyFn(Length, [ Cast(StringType, Col "d") ]))) None "length"
              Expect.equal (unitOfDerive (IsNull(Col "d"))) None "isNull"
              Expect.equal (unitOfDerive (Not(IsNull(Col "d")))) None "not"

          // ---- Phase 557 (D43): the power functions ----
          testCase
              "Pow under an integer literal composes Unit.pow; any other Pow, Exp, Ln and Log10 take a dimensionless operand"
          <| fun _ ->
              Expect.equal (unitOfDerive (ApplyFn(Pow, [ Col "d"; Lit(Int 2) ]))) (Some "m2") "m squared"
              Expect.equal (unitOfDerive (ApplyFn(Pow, [ Col "t"; Lit(Int -1) ]))) (Some "/s") "per second"

              Expect.equal
                  (unitOfDerive (ApplyFn(Pow, [ Col "d"; Lit(Int 0) ])))
                  None
                  "to the power zero: unconstrained"

              Expect.equal (unitOfDerive (ApplyFn(Pow, [ Col "n"; Lit(Int 3) ]))) None "a unitless base stays unitless"
              Expect.equal (unitOfDerive (ApplyFn(Pow, [ Col "n"; Col "n" ]))) None "two unitless operands"

              // A ratio of one unit is dimensionless (`s/s` renders `1`), and a dimensionless operand
              // is admitted exactly as an unconstrained one.
              Expect.equal (unitOfDerive (ApplyFn(Exp, [ Binary(Div, Col "t", Col "t") ]))) None "exp of a ratio"
              Expect.equal (unitOfDerive (ApplyFn(Ln, [ Col "n" ]))) None "ln of a count"
              Expect.equal (unitOfDerive (ApplyFn(Log10, [ Lit(Float 100.0) ]))) None "log10 of a literal"

              let step, l, r = mismatch (run (derive "x" (ApplyFn(Exp, [ Col "t" ]))))
              Expect.equal (step, l, r) ("derive 'x'", "s", "1") "exp of seconds is refused against 1"

              let _, l, r = mismatch (run (derive "x" (ApplyFn(Ln, [ Col "d" ]))))
              Expect.equal (l, r) ("m", "1") "ln of metres is refused against 1"

              let _, l, r = mismatch (run (derive "x" (ApplyFn(Pow, [ Col "d"; Col "n" ]))))
              Expect.equal (l, r) ("m", "1") "a power of metres under a column exponent is refused against 1"

              let _, l, r = mismatch (run (derive "x" (ApplyFn(Pow, [ Col "n"; Col "t" ]))))
              Expect.equal (l, r) ("s", "1") "a dimensioned exponent is refused against 1"

              // An exponent product the algebra cannot carry: `m` to a power that leaves the
              // exponent range is refused naming the power asked for, never thrown.
              let _, l, r =
                  mismatch (
                      run (derive "x" (ApplyFn(Pow, [ ApplyFn(Pow, [ Col "d"; Lit(Int 2) ]); Lit(Int 2000000000) ])))
                  )

              Expect.equal
                  (l, r)
                  ("m2", "a power the algebra can carry")
                  "the algebra's exponent range is refused by name"

          // ---- Convert ----
          testCase
              "Convert scales by the exact factor as a float, tags an unconstrained operand, and refuses across dimensions"
          <| fun _ ->
              let km = run (derive "x" (Convert(Col "d", u "km"))) |> ok
              Expect.equal (unitOfColumn "x" km) (Some "km") "m to km"

              Expect.equal
                  (cellsOf "x" km)
                  [ Float 0.1; Float 0.25; Null; Float 0.4 ]
                  "100 m is 0.1 km, null propagates"

              Expect.equal (fieldOf "x" km).Type FloatType "a conversion answers a float"

              let tagged = run (derive "x" (Convert(Lit(Int 5), u "m"))) |> ok
              Expect.equal (unitOfColumn "x" tagged) (Some "m") "a literal given a unit"

              Expect.equal
                  (cellsOf "x" tagged)
                  [ Float 5.0; Float 5.0; Float 5.0; Float 5.0 ]
                  "a tag casts to float and scales by nothing"

              let same = run (derive "x" (Convert(Col "d", u "m"))) |> ok
              Expect.equal (cellsOf "x" same) [ Float 100.0; Float 250.0; Null; Float 400.0 ] "the identity conversion"

              let step, l, r = mismatch (run (derive "x" (Convert(Col "d", u "s"))))
              Expect.equal (step, l, r) ("derive 'x'", "m", "s") "metres are not seconds"

              match run (derive "x" (Convert(Col "d", u "s"))) with
              | Error(UnitMismatch(_, e, _, _)) ->
                  Expect.equal e (Convert(Col "d", u "s")) "the conversion itself is named"
              | _ -> ()

              Expect.equal
                  (unitOfDerive (Convert(Binary(Mul, Col "d", Col "d"), u "km2")))
                  (Some "km2")
                  "a derived unit converts"

          testCase "Convert rides the wire, the substitutions and the reads as every other node does"
          <| fun _ ->
              let p =
                  [ Derive("x", Convert(Binary(Add, Col "d", Param "p"), u "km"))
                    Filter(Convert(Now NowGrain.Date, u "m")) ]

              let text = DataFrameCodec.encodePipeline p

              Expect.stringContains text "\"$type\":\"convert\"" "the wire tag"
              Expect.stringContains text "\"unit\":\"km\"" "the unit's canonical text"

              match DataFrameCodec.decodePipeline text with
              | Ok decoded -> Expect.equal decoded p "round trip"
              | Error e -> failtestf "decode failed: %A" e

              Expect.equal (Transform.paramsOf p) [ "p" ] "the param inside a conversion is read"
              Expect.isTrue (Transform.usesNow p) "the now inside a conversion is read"

              Expect.equal
                  (Transform.substitute (Map.ofList [ "p", Int 1 ]) p |> List.head)
                  (Derive("x", Convert(Binary(Add, Col "d", Lit(Int 1)), u "km")))
                  "substitution walks through"

              Expect.equal
                  (DataFrame.typeOf measures.Schema (Convert(Col "d", u "km")))
                  (Some FloatType)
                  "the static typer types a conversion as the float it answers"

          // ---- aggregates and windows ----
          testCase
              "Sum, Mean, Min, Max, Median, First, Last and StdDev keep the unit; Count and CountDistinct are dimensionless"
          <| fun _ ->
              let keeps = [ Sum; Mean; Min; Max; Median; First; Last; StdDev ]
              let drops = [ Count; CountDistinct ]

              let out =
                  run [ GroupBy([ "g" ], (keeps @ drops) |> List.mapi (fun i fn -> agg ("a" + string i) fn "w")) ]
                  |> ok

              keeps
              |> List.iteri (fun i fn ->
                  Expect.equal (unitOfColumn ("a" + string i) out) (Some "kg") (sprintf "%A keeps kg" fn))

              drops
              |> List.iteri (fun i fn ->
                  Expect.equal
                      (unitOfColumn ("a" + string (i + keeps.Length)) out)
                      None
                      (sprintf "%A is dimensionless" fn))

              Expect.equal (DataFrame.aggregateUnit Count (Some(u "kg"))) None "the rule as a function"

              Expect.equal
                  (DataFrame.aggregateUnit Sum (Some(u "kg")) |> Option.map Unit.render)
                  (Some "kg")
                  "the rule as a function"

          testCase "a window that shifts, runs or rolls keeps the unit; a rank or a row number carries none"
          <| fun _ ->
              let out =
                  run
                      [ win Lag "w" "prev"
                        win CumulSum "w" "running"
                        win RowNumber "w" "rn"
                        win DenseRank "w" "rk" ]
                  |> ok

              Expect.equal (unitOfColumn "prev" out) (Some "kg") "lag"
              Expect.equal (unitOfColumn "running" out) (Some "kg") "cumulative sum"
              Expect.equal (unitOfColumn "rn" out) None "row number"
              Expect.equal (unitOfColumn "rk" out) None "dense rank"
              Expect.equal (unitOfColumn "d" out) (Some "m") "the input columns keep theirs"
              Expect.equal (fieldOf "d" out).Label (Some "Distance") "and their labels"

          // ---- joins and set ops ----
          testCase
              "join keys of two units are refused; an unconstrained key matches anything; non-key columns keep their own metadata"
          <| fun _ ->
              let right: Table =
                  { Schema =
                      [ Field.create "k" FloatType |> Field.withUnit (u "s")
                        Field.create "mass" FloatType
                        |> Field.withUnit (u "g")
                        |> Field.withLabel "Mass" ]
                    Columns =
                      [ col "k" FloatType [ Float 10.0; Float 25.0 ]
                        col "mass" FloatType [ Float 1.0; Float 2.0 ] ] }

              let step, l, r = mismatch (run [ Join(Embedded right, [ "d", "k" ], Inner) ])
              Expect.equal (step, l, r) ("join key 'd' = 'k'", "m", "s") "the key pair"

              let joined = run [ Join(Embedded right, [ "t", "k" ], Inner) ] |> ok
              Expect.equal (unitOfColumn "mass" joined) (Some "g") "a right column keeps its unit"
              Expect.equal (fieldOf "mass" joined).Label (Some "Mass") "and its label"
              Expect.equal (unitOfColumn "d" joined) (Some "m") "a left column keeps its unit"

              let byCount = run [ Join(Embedded right, [ "n", "k" ], Left) ] |> ok

              Expect.equal
                  (unitOfColumn "k" byCount)
                  (Some "s")
                  "an unconstrained key joins a stated one, which keeps its unit"

          testCase "a union, an intersect and an except refuse a column pair of two units and keep the left's fields"
          <| fun _ ->
              let peer (unit: string) : Table =
                  { Schema =
                      measures.Schema
                      |> List.map (fun f ->
                          if f.Name = "d" then
                              Field.create "d" IntType |> Field.withUnit (u unit)
                          else
                              f)
                    Columns = measures.Columns }

              let step, l, r = mismatch (run [ Union(Embedded(peer "km")) ])
              Expect.equal (step, l, r) ("union column 'd'", "m", "km") "union"
              let step, _, _ = mismatch (run [ Intersect(Embedded(peer "s")) ])
              Expect.equal step "intersect column 'd'" "intersect"
              let step, _, _ = mismatch (run [ Except(Embedded(peer "s")) ])
              Expect.equal step "except column 'd'" "except"

              let unioned = run [ Union(Embedded(peer "m")) ] |> ok
              Expect.equal (unitOfColumn "d" unioned) (Some "m") "one unit, kept"
              Expect.equal (fieldOf "d" unioned).Label (Some "Distance") "the left's label"
              Expect.equal unioned.Schema.Length 7 "the schema"

              let unconstrainedLeft: Table =
                  { Schema =
                      measures.Schema
                      |> List.map (fun f -> if f.Name = "d" then Field.create "d" IntType else f)
                    Columns = measures.Columns }

              let stated =
                  DataFrame.evalPipeline [ Union(Embedded measures) ] unconstrainedLeft |> ok

              Expect.equal (unitOfColumn "d" stated) (Some "m") "an unconstrained left takes the right's stated unit"

          // ---- labels and descriptions through the verbs ----
          testCase
              "Project, Filter, Sort, Limit, Distinct and Window keep labels and descriptions; a derive and an aggregate output carry none"
          <| fun _ ->
              let kept =
                  run
                      [ Filter(Binary(Gt, Col "n", Lit(Int 0)))
                        Transform.sortBy [ "n", Desc ]
                        Transform.limit 3 0
                        Distinct
                        win RowNumber "n" "rn"
                        Project [ "d", "dist"; "n", "n"; "w", "w" ] ]
                  |> ok

              let dist = fieldOf "dist" kept

              Expect.equal
                  (dist.Label, dist.Unit |> Option.map Unit.render)
                  (Some "Distance", Some "m")
                  "a renamed column keeps its metadata"

              Expect.equal (fieldOf "n" kept).Description (Some "How many") "a description rides the verbs"
              Expect.equal (fieldOf "w" kept).Description (Some "Mass at the gate") "so does a unit-bearing column's"

              let fresh =
                  run
                      [ Derive("d", Binary(Add, Col "d", Lit(Int 1)))
                        GroupBy([ "g" ], [ agg "total" Sum "w" ]) ]
                  |> ok

              Expect.equal (fieldOf "total" fresh).Description None "an aggregate output states no description"

              let overwritten = run [ Derive("d", Binary(Add, Col "d", Lit(Int 1))) ] |> ok

              Expect.equal
                  ((fieldOf "d" overwritten).Label, unitOfColumn "d" overwritten)
                  (None, Some "m")
                  "a derive onto a name keeps the derived unit and no label"

          testCase
              "Pivot: the index columns keep their fields and every value column carries the aggregate's unit; Unpivot's value carries the unit its columns share"
          <| fun _ ->
              let pivoted =
                  run
                      [ Pivot
                            { Index = [ "n" ]
                              On = "g"
                              Values = "w"
                              Agg = Sum } ]
                  |> ok

              Expect.equal (fieldOf "n" pivoted).Label (Some "Count") "the index keeps its label"
              Expect.equal (unitOfColumn "x" pivoted) (Some "kg") "a value column carries the sum's unit"
              Expect.equal (unitOfColumn "y" pivoted) (Some "kg") "every value column does"

              let counted =
                  run
                      [ Pivot
                            { Index = [ "n" ]
                              On = "g"
                              Values = "w"
                              Agg = Count } ]
                  |> ok

              Expect.equal (unitOfColumn "x" counted) None "a counted pivot carries none"

              let shared = run [ Unpivot([ "id" ], [ "t"; "t" ]) ] |> ok
              Expect.equal (unitOfColumn "value" shared) (Some "s") "one unit shared"
              let mixed = run [ Unpivot([ "id" ], [ "d"; "t" ]) ] |> ok
              Expect.equal (unitOfColumn "value" mixed) None "two units, none stated"
              Expect.equal (fieldOf "id" mixed).Name "id" "the id columns keep their fields"

          // ---- unitless pipelines ----
          testCase
              "a pipeline over unitless columns answers exactly as before: no unit anywhere, the planner and the as-written evaluator agree"
          <| fun _ ->
              let people: Table =
                  { Schema =
                      [ Field.create "a" IntType
                        Field.create "b" FloatType
                        Field.create "g" StringType ]
                    Columns =
                      [ col "a" IntType [ Int 1; Int 2; Int 3 ]
                        col "b" FloatType [ Float 1.5; Float 2.5; Null ]
                        col "g" StringType [ Str "x"; Str "y"; Str "x" ] ] }

              let p =
                  [ Derive("c", Binary(Add, Col "a", Col "b"))
                    Derive("q", Binary(Div, Col "a", Col "b"))
                    Filter(Binary(Gt, Col "c", Lit(Int 1)))
                    GroupBy([ "g" ], [ agg "s" Sum "c"; agg "n" Count "a" ]) ]

              let out = DataFrame.evalPipeline p people |> ok

              Expect.isTrue
                  (out.Schema |> List.forall (fun f -> f.Unit.IsNone && not (Field.hasMetadata f)))
                  "no unit, no metadata"

              Expect.equal (DataFrame.evalPipelineAsWritten p people) (Ok out) "as written agrees"

              let encoded = ColumnCodec.encode (Embedded out)
              Expect.isFalse (encoded.Contains "\"unit\"") "the wire carries no unit member"

          // ---- the readers: the schema walk, the planner, the incremental seam, the registered query ----
          testCase "the schema walk states the output's units, labels and descriptions as the evaluator gives them"
          <| fun _ ->
              let p =
                  [ Derive("v", Binary(Div, Col "d", Col "t"))
                    Derive("vk", Convert(Col "v", u "km/h"))
                    win Lag "w" "prev"
                    GroupBy([ "g" ], [ agg "total" Sum "w"; agg "n" Count "id"; agg "fastest" Max "vk" ]) ]

              let out = run p |> ok
              let walked = SchemaWalk.ofPipeline measures.Schema p

              Expect.isTrue (SchemaWalk.isClosed walked) "closed"

              List.zip (SchemaWalk.columns walked) out.Schema
              |> List.iter (fun (c, f) ->
                  Expect.equal c.Name f.Name "name"
                  Expect.equal (c.Unit |> Option.map Unit.render) (unitText f) ("unit of " + f.Name)
                  Expect.equal c.Label f.Label ("label of " + f.Name)
                  Expect.equal c.Description f.Description ("description of " + f.Name))

              Expect.equal (SchemaWalk.unitOf "fastest" walked |> Option.map Unit.render) (Some "km/h") "the reader"
              Expect.equal (SchemaWalk.unitOf "n" walked) None "a count"

              let before = SchemaWalk.ofPipeline measures.Schema [ Project [ "d", "dist" ] ]

              Expect.equal
                  (SchemaWalk.columns before |> List.head).Label
                  (Some "Distance")
                  "a projection keeps the label"

              // A refused derive is a column stating no unit: the walk carries no refusal verdict (D26).
              let refused =
                  SchemaWalk.ofPipeline measures.Schema [ Derive("x", Binary(Add, Col "d", Col "t")) ]

              Expect.equal (SchemaWalk.unitOf "x" refused) None "refused, so none"

          testCase "the planner's rewrites preserve metadata, and its verdict declines a step the unit check refuses"
          <| fun _ ->
              let p =
                  [ Derive("v", Binary(Div, Col "d", Col "t"))
                    Filter(Binary(Gt, Col "n", Lit(Int 1)))
                    Transform.sortBy [ "n", Asc ]
                    Transform.limit 2 0
                    Project [ "v", "speed"; "n", "n" ] ]

              let planned = Plan.rewrite measures.Schema p
              Expect.notEqual planned p "the planner rewrote something"
              let a = DataFrame.evalPipeline p measures |> ok
              let b = DataFrame.evalPipelineAsWritten p measures |> ok
              Expect.equal a.Schema b.Schema "the planned schema is the reference's, metadata included"
              Expect.equal (unitOfColumn "speed" a) (Some "m/s") "through the rename"
              Expect.equal (fieldOf "n" a).Label (Some "Count") "through the prune"

              Expect.isFalse
                  (Plan.isTotal measures.Schema (Filter(Binary(Gt, Col "d", Col "t"))))
                  "a mismatched filter is not total"

              Expect.isTrue
                  (Plan.isTotal measures.Schema (Filter(Binary(Gt, Col "n", Lit(Int 1)))))
                  "an unconstrained one is"

              Expect.isFalse
                  (Plan.isTotal measures.Schema (Derive("x", Binary(Add, Col "d", Col "t"))))
                  "a mismatched derive is not total"

          testCase "the incremental seam carries metadata unchanged through a refresh and through its state wire"
          <| fun _ ->
              let p =
                  [ Derive("v", Binary(Div, Col "d", Col "t"))
                    Filter(Binary(Gt, Col "n", Lit(Int 0)))
                    GroupBy([ "g" ], [ agg "total" Sum "w"; agg "top" Max "v" ]) ]

              let after: Table =
                  { measures with
                      Columns =
                          measures.Columns
                          |> List.map (fun c ->
                              if c.Name = "w" then
                                  col "w" FloatType [ Float 1.5; Float 9.5; Float 3.0; Float 4.0 ]
                              else
                                  c) }

              let state = ok (Incremental.primeOn idw p measures)

              let delta =
                  match Delta.diff idw measures after with
                  | Ok d -> d
                  | Error e -> failtestf "diff: %A" e

              let next = ok (Incremental.refreshOn idw p state delta after)
              let reference = DataFrame.evalPipeline p after |> ok

              Expect.equal
                  (Incremental.result next).Schema
                  reference.Schema
                  "the refreshed schema is the reference's, units included"

              Expect.equal
                  (unitOfColumn "top" (Incremental.result next))
                  (Some "m/s")
                  "a derived unit through an aggregate"

              match IncrementalCodec.encode state with
              | Error e -> failtestf "encode: %A" e
              | Ok text ->
                  Expect.stringContains text "\"unit\":\"kg\"" "a stated unit crosses the wire"

                  match IncrementalCodec.decode text with
                  | Error e -> failtestf "decode: %A" e
                  | Ok decoded ->
                      Expect.equal
                          (Incremental.result decoded).Schema
                          (Incremental.result state).Schema
                          "decoded, the schema is the same"

                      let next' = ok (Incremental.refreshOn idw p decoded delta after)

                      Expect.equal
                          (Incremental.result next').Schema
                          reference.Schema
                          "and a refresh from the decoded state agrees"

          testCase
              "a registered query's output schema carries its fields, and a declared unit the pipeline does not produce is refused"
          <| fun _ ->
              let p =
                  [ Derive("v", Binary(Div, Col "d", Col "t")); Project [ "id", "id"; "v", "v" ] ]

              let produced = Field.create "v" FloatType |> Field.withUnit (u "m/s")
              let declared = [ Field.create "id" StringType; produced ]
              Expect.equal (PipelineQuery.check (registered declared p)) (Ok()) "declared as produced"

              let knowledge = PipelineQuery.resultKnowledge (registered declared p)

              Expect.equal
                  (SchemaWalk.unitOf "v" knowledge |> Option.map Unit.render)
                  (Some "m/s")
                  "the knowledge carries the unit"

              let wrong =
                  [ Field.create "id" StringType
                    Field.create "v" FloatType |> Field.withUnit (u "km/h") ]

              Expect.equal
                  (PipelineQuery.check (registered wrong p))
                  (Error(ResultColumn("v", ResultDisagreement.UnitDiffers(Some(u "km/h"), Some(u "m/s")))))
                  "declared km/h, produced m/s"

              let none = [ Field.create "id" StringType; Field.create "v" FloatType ]

              Expect.equal
                  (PipelineQuery.check (registered none p))
                  (Error(ResultColumn("v", ResultDisagreement.UnitDiffers(None, Some(u "m/s")))))
                  "declared none, produced m/s"

              let pq = registered declared p

              match PipelineQueryCodec.decode (PipelineQueryCodec.encode pq) with
              | Ok back ->
                  Expect.equal back.Sources pq.Sources "the declared sources' units round-trip the registry wire"
              | Error e -> failtestf "decode: %s" e

          testCase "every refusal reads as a unit mismatch naming the step"
          <| fun _ ->
              let text =
                  DataFrame.errorString (UnitMismatch("derive 'x'", Binary(Add, Col "d", Col "t"), "m", "s"))

              Expect.equal text "unit mismatch in derive 'x': m against s in add('d', 't')" "the text"

          // ---- the conformance go-red ----
          testCase "the derive-typing laws go red on the unit law over an evaluator that drops every unit"
          <| fun _ ->
              let dropping env pipeline table =
                  DataFrame.evalPipelineInEnv env pipeline table
                  |> Result.map (fun (t: Table) ->
                      { t with
                          Schema = t.Schema |> List.map Field.withoutUnit })

              let failed =
                  DeriveTypingConformance.lawsWith dropping 429 60
                  |> List.filter (fun r -> not r.Passed)
                  |> List.map _.Law

              let unitLaw =
                  "a derive's column carries the unit the check derives over the schema, on a full, an empty and an all-null frame alike"

              Expect.contains failed unitLaw "the unit law goes red"

              // The refresh law reads the perturbed evaluator's schema against the seam's, so it
              // catches the dropped unit too; no other law moves.
              Expect.isEmpty
                  (failed
                   |> List.filter (fun l ->
                       l <> unitLaw
                       && l <> "prime, refresh and the full evaluation type a derived column identically"))
                  "nothing else went red" ]
