module Fuaran.Core.Tests.DecimalTests

// ---------------------------------------------------------------------------
//  Phase 277 — decimal arithmetic in the transform evaluator. Exact where the operation is closed
//  (`Add`, `Sub`, `Mul`, `Mod`, negation, `Abs`, `Floor`, `Ceil`, the order), refused by name where
//  it is not (`Div` points at `Divide`, a one-argument `Round` asks for its scale and rule, a
//  decimal beside a float asks for a `Cast`), and never through a float in silence.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core

let private dec (text: string) : Cell =
    match Cell.decimal text with
    | Some c -> c
    | None -> failwithf "not decimal text: %s" text

let private table (cols: (string * ColumnType * Cell list) list) : Table =
    { Schema = cols |> List.map (fun (n, t, _) -> n, t)
      Columns = cols |> List.map (fun (n, t, cells) -> Column.create n t cells) }

/// The single cell an expression answers over a one-row table of `cols`.
let private eval1 (cols: (string * ColumnType * Cell list) list) (e: ColExpr) : Result<Cell, EvalError> =
    DataFrame.evalPipeline [ Derive("out", e) ] (table cols)
    |> Result.map (fun t ->
        match Table.tryColumn "out" t with
        | Some c -> List.head c.Cells
        | None -> failwith "no output column")

let private lit (text: string) : ColExpr = Lit(dec text)

let private value (e: ColExpr) : Result<Cell, EvalError> = eval1 [ "x", IntType, [ Int 0 ] ] e

let private message (r: Result<Cell, EvalError>) : string =
    match r with
    | Error(TypeError m) -> m
    | other -> failwithf "expected a TypeError, got %A" other

let private divide (a: string) (b: string) (scale: int) (rule: string) : Result<Cell, EvalError> =
    value (ApplyFn(Divide, [ lit a; lit b; Lit(Int scale); Lit(Str rule) ]))

[<Tests>]
let tests =
    testList
        "Decimal"
        [ testCase "the reconciliation case: decimal lines sum to the header by string equality, where floats do not"
          <| fun _ ->
              // Ten lines of 0.10 against a header of 1.00. As floats the sum is 0.9999999999999999.
              let lines =
                  table
                      [ "order", StringType, List.replicate 10 (Str "A")
                        "amount", DecimalType, List.replicate 10 (dec "0.10")
                        "approx", FloatType, List.replicate 10 (Float 0.1) ]

              let header =
                  table [ "id", StringType, [ Str "A" ]; "total", DecimalType, [ dec "1.00" ] ]

              let pipeline =
                  [ GroupBy(
                        [ "order" ],
                        [ { Name = "sum"
                            Fn = Sum
                            Of = "amount" }
                          { Name = "approxSum"
                            Fn = Sum
                            Of = "approx" } ]
                    )
                    Join(Embedded header, [ "order", "id" ], Inner)
                    Derive("exact", Binary(Eq, Col "sum", Col "total"))
                    Derive("viaFloat", Binary(Eq, Col "approxSum", Cast(FloatType, Col "total"))) ]

              match DataFrame.evalPipeline pipeline lines with
              | Error e -> failtestf "%A" e
              | Ok t ->
                  let cell name =
                      (Table.tryColumn name t |> Option.get).Cells |> List.head

                  Expect.equal (cell "sum") (Decimal "1") "the decimal sum is exact"
                  Expect.equal (DataFrame.cellString (cell "sum")) (DataFrame.cellString (cell "total")) "string-equal"
                  Expect.equal (cell "exact") (Bool true) "the decimal sum equals the header"
                  Expect.equal (cell "viaFloat") (Bool false) "the float sum does not"

          testCase "Add, Sub, Mul, Mod, negation and Abs are exact, and an int promotes"
          <| fun _ ->
              Expect.equal (value (Binary(Add, lit "0.1", lit "0.2"))) (Ok(dec "0.3")) "0.1 + 0.2 = 0.3"

              Expect.equal
                  (value (Binary(Eq, Binary(Add, lit "0.1", lit "0.2"), lit "0.3")))
                  (Ok(Bool true))
                  "and compares equal"

              Expect.equal (value (Binary(Sub, lit "1", lit "1.01"))) (Ok(dec "-0.01")) "sub"
              Expect.equal (value (Binary(Mul, lit "1.25", lit "-0.8"))) (Ok(dec "-1")) "mul"
              Expect.equal (value (Binary(Mul, lit "1.5", Lit(Int 3)))) (Ok(dec "4.5")) "int promotes"
              Expect.equal (value (Binary(Add, Lit(Int 2), lit "0.5"))) (Ok(dec "2.5")) "on either side"
              Expect.equal (value (Binary(Mod, lit "5.5", lit "2"))) (Ok(dec "1.5")) "mod is exact"
              Expect.equal (value (Binary(Mod, lit "-5.5", lit "2"))) (Ok(dec "-1.5")) "mod keeps the dividend's sign"
              Expect.equal (value (Binary(Mod, lit "5.5", lit "0"))) (Ok Null) "mod by zero is null, as an int's"
              Expect.equal (value (Binary(Sub, lit "0", lit "2.5"))) (Ok(dec "-2.5")) "negation"
              Expect.equal (value (ApplyFn(Abs, [ lit "-2.5" ]))) (Ok(dec "2.5")) "abs"
              Expect.equal (value (ApplyFn(Floor, [ lit "-2.5" ]))) (Ok(dec "-3")) "floor"
              Expect.equal (value (ApplyFn(Ceil, [ lit "-2.5" ]))) (Ok(dec "-2")) "ceil"

              Expect.equal
                  (value (Binary(Mul, lit "99999999999999999999", lit "99999999999999999999")))
                  (Ok(dec "9999999999999999999800000000000000000001"))
                  "no overflow: the digits are strings"

              Expect.equal (value (Binary(Add, lit "1.5", Lit Null))) (Ok Null) "null propagates"

          testCase "Least, Greatest and the comparisons use the exact order"
          <| fun _ ->
              // 0.30000000000000001 and 0.3 are one float and two decimals.
              Expect.equal (value (Binary(Gt, lit "0.30000000000000001", lit "0.3"))) (Ok(Bool true)) "exact order"
              Expect.equal (value (ApplyFn(Least, [ lit "2.5"; Lit(Int 2); lit "2.25" ]))) (Ok(Int 2)) "least"
              Expect.equal (value (ApplyFn(Greatest, [ lit "2.5"; Lit(Int 2); lit "2.25" ]))) (Ok(dec "2.5")) "greatest"

              Expect.equal
                  (value (Binary(Eq, lit "2", Lit(Int 2))))
                  (Ok(Bool true))
                  "a decimal equals the int of its value"

          testCase "Divide takes a scale and a rule; each of the seven rules rounds as it names"
          <| fun _ ->
              let cases =
                  [ "half-even", "0.12", "-0.12"
                    "half-up", "0.13", "-0.13"
                    "half-down", "0.12", "-0.12"
                    "up", "0.13", "-0.13"
                    "down", "0.12", "-0.12"
                    "ceiling", "0.13", "-0.12"
                    "floor", "0.12", "-0.13" ]

              for rule, pos, neg in cases do
                  Expect.equal (divide "1" "8" 2 rule) (Ok(dec pos)) (sprintf "1/8 %s" rule)
                  Expect.equal (divide "-1" "8" 2 rule) (Ok(dec neg)) (sprintf "-1/8 %s" rule)

              Expect.equal (divide "1" "3" 4 "half-even") (Ok(dec "0.3333")) "a third, to four places"
              Expect.equal (divide "2" "3" 0 "half-up") (Ok(dec "1")) "scale zero"
              Expect.equal (divide "0.135" "1" 2 "half-even") (Ok(dec "0.14")) "half-even on an odd digit"
              Expect.equal (divide "1" "0" 2 "half-even") (Ok Null) "a zero divisor is null, as Div's is"

              Expect.equal
                  (value (ApplyFn(Divide, [ Lit Null; lit "2"; Lit(Int 2); Lit(Str "down") ])))
                  (Ok Null)
                  "null propagates"

              Expect.equal
                  (value (ApplyFn(Divide, [ lit "7"; Lit(Int 2); Lit(Int 1); Lit(Str "down") ])))
                  (Ok(dec "3.5"))
                  "an int divisor promotes"

          testCase "Round of a decimal takes a scale and a rule, and is exact where nothing is dropped"
          <| fun _ ->
              let round x n rule =
                  value (ApplyFn(Round, [ lit x; Lit(Int n); Lit(Str rule) ]))

              Expect.equal (round "2.345" 2 "half-even") (Ok(dec "2.34")) "half-even"
              Expect.equal (round "2.345" 2 "half-up") (Ok(dec "2.35")) "half-up"
              Expect.equal (round "2.3" 2 "up") (Ok(dec "2.3")) "nothing to drop"

              Expect.equal
                  (value (ApplyFn(Round, [ Lit(Int 7); Lit(Int 0); Lit(Str "down") ])))
                  (Ok(dec "7"))
                  "an int promotes"

          testCase "every refusal names what it needs: the scale, the rounding rule, or the cast"
          <| fun _ ->
              let divMsg = message (value (Binary(Div, lit "1", lit "3")))
              Expect.stringContains divMsg "scale" "Div names the scale"
              Expect.stringContains divMsg "rounding rule" "and the rule"
              Expect.stringContains divMsg "Divide" "and the function that takes them"

              let roundMsg = message (value (ApplyFn(Round, [ lit "1.5" ])))
              Expect.stringContains roundMsg "scale" "Round names the scale"
              Expect.stringContains roundMsg "rounding rule" "and the rule"

              let noRule = message (divide "1" "3" 2 "bankers")
              Expect.stringContains noRule "rounding rule" "an unknown rule is named"
              Expect.stringContains noRule "half-even" "with the rules it could have been"

              let noScale =
                  message (value (ApplyFn(Divide, [ lit "1"; lit "3"; Lit(Int -1); Lit(Str "down") ])))

              Expect.stringContains noScale "scale" "a negative scale is named"

              let mixed = message (value (Binary(Add, lit "1.5", Lit(Float 1.0))))
              Expect.stringContains mixed "decimal" "a decimal beside a float names both"
              Expect.stringContains mixed "float" "both"
              Expect.stringContains mixed "Cast" "and the cast that resolves it"

              let cmp = message (value (Binary(Lt, lit "1.5", Lit(Float 2.0))))
              Expect.stringContains cmp "Cast" "a comparison names the cast too"

              let sqrtMsg = message (value (ApplyFn(Sqrt, [ lit "2" ])))
              Expect.stringContains sqrtMsg "Cast" "sqrt of a decimal names the cast"

          testCase "Cast to and from decimal; from a float is the one place an approximation enters"
          <| fun _ ->
              Expect.equal (value (Cast(DecimalType, Lit(Float 0.1)))) (Ok(dec "0.1")) "the float's shortest digits"
              Expect.equal (value (Cast(DecimalType, Lit(Float 1e21)))) (Ok(dec "1000000000000000000000")) "no exponent"
              Expect.equal (value (Cast(DecimalType, Lit(Float 1.5e-7)))) (Ok(dec "0.00000015")) "small"
              Expect.equal (value (Cast(DecimalType, Lit(Int -12)))) (Ok(dec "-12")) "an int exactly"
              Expect.equal (value (Cast(DecimalType, Lit(Str "012.50")))) (Ok(dec "12.5")) "text, canonicalised"
              Expect.isError (value (Cast(DecimalType, Lit(Str "1e3")))) "not decimal text"
              Expect.isError (value (Cast(DecimalType, Lit(Float nan)))) "not a finite float"
              Expect.equal (value (Cast(FloatType, lit "2.5"))) (Ok(Float 2.5)) "to float"
              Expect.equal (value (Cast(IntType, lit "-2.9"))) (Ok(Int -2)) "to int truncates toward zero"
              Expect.isError (value (Cast(IntType, lit "3000000000"))) "past int32 is an overflow"
              Expect.equal (value (Cast(StringType, lit "2.5"))) (Ok(Str "2.5")) "to string"

          testCase "keys: a decimal groups, dedups and joins on its canonical token, and never matches a float"
          <| fun _ ->
              let t =
                  table
                      [ "k", DecimalType, [ dec "1.5"; dec "1.50"; dec "2"; Null ]
                        "f", FloatType, [ Float 1.5; Float 1.5; Float 2.0; Null ] ]

              match DataFrame.evalPipeline [ Project [ "k", "k" ]; Distinct ] t with
              | Ok r -> Expect.equal (Table.rowCount r) 3 "1.5 and 1.50 are one value"
              | Error e -> failtestf "%A" e

              // A join of the decimal key against the float column of the same values matches nothing.
              let floats = table [ "f", FloatType, [ Float 1.5; Float 2.0 ] ]

              match DataFrame.evalPipeline [ Join(Embedded floats, [ "k", "f" ], Inner) ] t with
              | Ok r -> Expect.equal (Table.rowCount r) 0 "a decimal key never matches a float"
              | Error e -> failtestf "%A" e

              let decs = table [ "d", DecimalType, [ dec "1.5"; dec "2" ] ]

              match DataFrame.evalPipeline [ Join(Embedded decs, [ "k", "d" ], Inner) ] t with
              | Ok r -> Expect.equal (Table.rowCount r) 3 "a decimal key matches a decimal of the same value"
              | Error e -> failtestf "%A" e

              Expect.isError
                  (eval1 [ "k", DecimalType, [ dec "1.5" ] ] (InList(Col "k", [ Lit(Float 1.5) ])))
                  "membership against a float item is a type error, never a match"

              match DataFrame.evalPipeline [ Transform.sortBy [ "k", Desc ] ] t with
              | Ok r ->
                  Expect.equal
                      (Table.tryColumn "k" r |> Option.get).Cells
                      [ dec "2"; dec "1.5"; dec "1.50"; Null ]
                      "the exact order, stable, nulls last"
              | Error e -> failtestf "%A" e

          testCase "a running total over a decimal column stays exact"
          <| fun _ ->
              let t =
                  table
                      [ "i", IntType, [ Int 1; Int 2; Int 3 ]
                        "m", DecimalType, [ dec "0.1"; dec "0.2"; dec "0.3" ] ]

              let spec fn =
                  Window
                      { PartitionBy = []
                        OrderBy = [ "i", Asc ]
                        Fn = fn
                        Of = "m"
                        As = "run" }

              match DataFrame.evalPipeline [ spec CumulSum ] t with
              | Ok r ->
                  Expect.equal
                      (Table.tryColumn "run" r |> Option.get).Cells
                      [ dec "0.1"; dec "0.3"; dec "0.6" ]
                      "cumulSum"
              | Error e -> failtestf "%A" e

              match DataFrame.evalPipeline [ spec RollingSum ] t with
              | Ok r ->
                  Expect.equal
                      (Table.tryColumn "run" r |> Option.get).Cells
                      [ dec "0.1"; dec "0.3"; dec "0.6" ]
                      "rollingSum"
              | Error e -> failtestf "%A" e

          testCase "the typer and the totality verdict over decimal arithmetic"
          <| fun _ ->
              let schema: Schema = [ "m", DecimalType; "i", IntType; "f", FloatType ]
              let ty e = DataFrame.typeOf schema e
              Expect.equal (ty (Binary(Add, Col "m", Col "i"))) (Some DecimalType) "an int promotes"
              Expect.equal (ty (Binary(Add, Col "m", Col "f"))) None "a decimal beside a float is refused"
              Expect.equal (ty (Binary(Lt, Col "m", Col "i"))) (Some BoolType) "an exact comparison"

              Expect.equal
                  (ty (ApplyFn(Divide, [ Col "m"; Col "i"; Lit(Int 2); Lit(Str "down") ])))
                  (Some DecimalType)
                  "divide"

              Expect.equal (ty (Cast(DecimalType, Col "f"))) (Some DecimalType) "cast"

              let total e = Plan.isTotal schema (Derive("x", e))
              Expect.isTrue (total (Binary(Add, Col "m", Col "i"))) "exact addition cannot refuse"
              Expect.isTrue (total (Binary(Mod, Col "m", Col "m"))) "nor a decimal mod"
              Expect.isTrue (total (Binary(Lt, Col "m", Col "i"))) "nor an exact comparison"
              Expect.isTrue (total (Cast(DecimalType, Col "i"))) "nor an int's cast"
              Expect.isFalse (total (Binary(Div, Col "m", Col "i"))) "a decimal Div refuses"
              Expect.isFalse (total (Binary(Add, Col "m", Col "f"))) "a decimal beside a float refuses"

              Expect.isFalse
                  (total (ApplyFn(Divide, [ Col "m"; Col "i"; Lit(Int 2); Lit(Str "down") ])))
                  "divide is declined"

              Expect.isFalse (total (ApplyFn(Round, [ Col "m" ]))) "an unscaled round refuses"

          testCase "a decimal literal round-trips the pipeline codec as decimal text"
          <| fun _ ->
              let p =
                  [ Derive("x", ApplyFn(Divide, [ lit "1.25"; Lit(Int 3); Lit(Int 2); Lit(Str "half-even") ])) ]

              let wire = DataFrameCodec.encodePipeline p
              Expect.stringContains wire "{\"$type\":\"Decimal\",\"value\":\"1.25\"}" "a JSON string"
              Expect.stringContains wire "\"divide\"" "the function's tag"
              Expect.equal (DataFrameCodec.decodePipeline wire) (Ok p) "round-trips" ]
