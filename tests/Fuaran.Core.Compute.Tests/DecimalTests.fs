module Fuaran.Compute.Tests.DecimalTests

// ---------------------------------------------------------------------------
//  Phase 277 — decimal arithmetic in the transform evaluator. Exact where the operation is closed
//  (`Add`, `Sub`, `Mul`, `Mod`, negation, `Abs`, `Least`, `Greatest`, the order), a STATED rounding
//  where it is not (`Quotient` and `Rounded` carry a typed `Rounding`; `Round` / `Floor` / `Ceil`
//  are their scale-0 specialisations), refused by name where a pipeline has not said (`Div` names
//  `Quotient`, a decimal beside a float names the `Cast`), and never through a float in silence.
// ---------------------------------------------------------------------------

open System.Numerics
open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

let private dec (text: string) : Cell =
    match Cell.decimal text with
    | Some c -> c
    | None -> failwithf "not decimal text: %s" text

let private table (cols: (string * ColumnType * Cell list) list) : Table =
    { Schema = cols |> List.map (fun (n, t, _) -> Field.create n t)
      Columns = cols |> List.map (fun (n, t, cells) -> KitColumn.create n t cells) }

/// The single cell an expression answers over a one-row table of `cols`.
let private eval1 (cols: (string * ColumnType * Cell list) list) (e: ColExpr) : Result<Cell, EvalError> =
    DataFrame.evalPipeline [ Derive("out", e) ] (table cols)
    |> Result.map (fun t ->
        match Table.tryColumn "out" t with
        | Some c -> List.head (Column.toCells c)
        | None -> failwith "no output column")

let private lit (text: string) : ColExpr = Lit(dec text)

let private value (e: ColExpr) : Result<Cell, EvalError> = eval1 [ "x", IntType, [ Int 0 ] ] e

/// An expression over no columns under a param env — the reference expression evaluator.
let private valueIn (env: Map<string, Cell>) (e: ColExpr) : Result<Cell, EvalError> =
    DataFrame.evalExprInRow env [] [] e

let private message (r: Result<Cell, EvalError>) : string =
    match r with
    | Error(TypeError m) -> m
    | other -> failwithf "expected a TypeError, got %A" other

let private allModes =
    [ RoundingMode.HalfEven
      RoundingMode.HalfUp
      RoundingMode.HalfDown
      RoundingMode.Up
      RoundingMode.Down
      RoundingMode.Ceiling
      RoundingMode.Floor ]

let private at (scale: int) (mode: RoundingMode) : Rounding = { Scale = Slot.Lit scale; Mode = mode }

let private rounded (x: string) (scale: int) (mode: RoundingMode) : Result<Cell, EvalError> =
    value (Rounded(lit x, at scale mode))

let private quotient (a: string) (b: string) (scale: int) (mode: RoundingMode) : Result<Cell, EvalError> =
    value (Quotient(lit a, lit b, at scale mode))

// ---- an independent reference: rationals over BigInteger --------------------------------------

/// A decimal text as the rational `p / q`, `q` a power of ten.
let private ratOf (text: string) : BigInteger * BigInteger =
    let negative = text.StartsWith "-"
    let body = if negative then text.Substring 1 else text
    let dot = body.IndexOf '.'

    let digits, places =
        if dot < 0 then
            body, 0
        else
            body.Remove(dot, 1), body.Length - dot - 1

    let p = BigInteger.Parse digits
    (if negative then -p else p), BigInteger.Pow(BigInteger 10, places)

/// The decimal text of `r / 10^scale`, canonical.
let private textOf (r: BigInteger) (scale: int) : Cell =
    let digits = BigInteger.Abs(r).ToString().PadLeft(scale + 1, '0')
    let ip = digits.Substring(0, digits.Length - scale)
    let fp = digits.Substring(digits.Length - scale)
    dec ((if r.Sign < 0 then "-" else "") + ip + (if scale > 0 then "." + fp else ""))

/// `p / q` (`q > 0`) to `scale` places under `mode`, from the floor and the ceiling of the scaled
/// value and the fraction between them — a formulation the production kernel does not share.
let private reference (p: BigInteger) (q: BigInteger) (scale: int) (mode: RoundingMode) : Cell =
    let n = p * BigInteger.Pow(BigInteger 10, scale)

    let fl =
        BigInteger.Divide(n - (if n.Sign < 0 then q - BigInteger.One else BigInteger.Zero), q)

    let ce = if fl * q = n then fl else fl + BigInteger.One
    let frac2 = BigInteger.Compare(BigInteger 2 * (n - fl * q), q) // the fraction against one half
    let towardZero = if n.Sign >= 0 then fl else ce
    let awayFromZero = if n.Sign >= 0 then ce else fl

    let r =
        if fl = ce then
            fl
        else
            match mode with
            | RoundingMode.Floor -> fl
            | RoundingMode.Ceiling -> ce
            | RoundingMode.Down -> towardZero
            | RoundingMode.Up -> awayFromZero
            | RoundingMode.HalfUp ->
                if frac2 < 0 then fl
                elif frac2 > 0 then ce
                else awayFromZero
            | RoundingMode.HalfDown ->
                if frac2 < 0 then fl
                elif frac2 > 0 then ce
                else towardZero
            | RoundingMode.HalfEven ->
                if frac2 < 0 then fl
                elif frac2 > 0 then ce
                elif fl.IsEven then fl
                else ce

    textOf r scale

/// A drawn decimal text: up to nine digits at zero to four places, either sign.
let private drawDecimal (rng: System.Random) : string =
    let places = rng.Next 5
    let digits = string (rng.Next(0, 1000000000))
    let padded = digits.PadLeft(places + 1, '0')

    let text =
        padded.Substring(0, padded.Length - places)
        + (if places > 0 then
               "." + padded.Substring(padded.Length - places)
           else
               "")

    let signed = if rng.Next 2 = 0 then "-" + text else text

    match Cell.decimal signed with
    | Some(Decimal t) -> t
    | _ -> failwith "unreachable"

let private placesOf (text: string) : int =
    let dot = text.IndexOf '.'
    if dot < 0 then 0 else text.Length - dot - 1

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
                      Column.toCells (Table.tryColumn name t |> Option.get) |> List.head

                  Expect.equal (cell "sum") (Decimal "1") "the decimal sum is exact"
                  Expect.equal (DataFrame.cellString (cell "sum")) (DataFrame.cellString (cell "total")) "string-equal"
                  Expect.equal (cell "exact") (Bool true) "the decimal sum equals the header"
                  Expect.equal (cell "viaFloat") (Bool false) "the float sum does not"

          testCase "the allocation case: 100.00 split three ways at two places, half-even, is 33.33"
          <| fun _ -> Expect.equal (quotient "100.00" "3" 2 RoundingMode.HalfEven) (Ok(dec "33.33")) "33.33"

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

          testCase "each mode's tie behaviour: the classic table at 2.5, 3.5, -2.5 and -3.5"
          <| fun _ ->
              let table =
                  [ RoundingMode.HalfEven, [ "2"; "4"; "-2"; "-4" ]
                    RoundingMode.HalfUp, [ "3"; "4"; "-3"; "-4" ]
                    RoundingMode.HalfDown, [ "2"; "3"; "-2"; "-3" ]
                    RoundingMode.Up, [ "3"; "4"; "-3"; "-4" ]
                    RoundingMode.Down, [ "2"; "3"; "-2"; "-3" ]
                    RoundingMode.Ceiling, [ "3"; "4"; "-2"; "-3" ]
                    RoundingMode.Floor, [ "2"; "3"; "-3"; "-4" ] ]

              for mode, expected in table do
                  for x, e in List.zip [ "2.5"; "3.5"; "-2.5"; "-3.5" ] expected do
                      Expect.equal (rounded x 0 mode) (Ok(dec e)) (sprintf "%A of %s" mode x)
                      // The same tie through a quotient: x * 2 / 2 at scale 0.
                      Expect.equal
                          (quotient (string (float x * 2.0)) "2" 0 mode)
                          (Ok(dec e))
                          (sprintf "%A quotient of %s" mode x)

          testCase "Round, Floor and Ceil over a decimal are Rounded at scale 0 under HalfUp, Floor and Ceiling"
          <| fun _ ->
              let rng = System.Random 2771

              for _ in 1..400 do
                  let x = drawDecimal rng

                  for fn, mode in
                      [ Round, RoundingMode.HalfUp
                        Floor, RoundingMode.Floor
                        Ceil, RoundingMode.Ceiling ] do
                      Expect.equal (value (ApplyFn(fn, [ lit x ]))) (rounded x 0 mode) (sprintf "%A of %s" fn x)

              Expect.equal (value (ApplyFn(Round, [ lit "-2.5" ]))) (Ok(dec "-3")) "round: ties away from zero"
              Expect.equal (value (ApplyFn(Floor, [ lit "-2.5" ]))) (Ok(dec "-3")) "floor"
              Expect.equal (value (ApplyFn(Ceil, [ lit "-2.5" ]))) (Ok(dec "-2")) "ceil"
              Expect.equal (value (ApplyFn(Round, [ Lit(Float 2.5) ]))) (Ok(Float 3.0)) "over a float, unchanged"

          testCase "Quotient is the exact quotient correctly rounded, against an independent BigInteger reference"
          <| fun _ ->
              let rng = System.Random 2772
              let mutable terminating = 0

              // Half the divisors are drawn from powers of two and five, so a terminating quotient
              // is common rather than rare.
              let terminatingDivisors = [| "2"; "-4"; "0.5"; "8"; "1.25"; "-0.04"; "625"; "1.6" |]

              for i in 1..600 do
                  let a = drawDecimal rng

                  let b =
                      if i % 2 = 0 then
                          terminatingDivisors[rng.Next terminatingDivisors.Length]
                      else
                          drawDecimal rng

                  let scale = rng.Next 7
                  let mode = allModes[rng.Next allModes.Length]
                  let pa, qa = ratOf a
                  let pb, qb = ratOf b

                  if pb.IsZero then
                      Expect.equal (quotient a b scale mode) (Ok Null) "a zero divisor is null"
                  else
                      // a / b = (pa * qb) / (qa * pb), the denominator made positive.
                      let p0 = pa * qb
                      let q0 = qa * pb
                      let p, q = if q0.Sign < 0 then -p0, -q0 else p0, q0
                      let expected = reference p q scale mode
                      Expect.equal (quotient a b scale mode) (Ok expected) (sprintf "%s / %s at %d %A" a b scale mode)

                      // On a terminating quotient, it is `Rounded` of the exact quotient.
                      let mutable den = q / BigInteger.GreatestCommonDivisor(p, q)

                      while den % BigInteger 2 = BigInteger.Zero do
                          den <- den / BigInteger 2

                      while den % BigInteger 5 = BigInteger.Zero do
                          den <- den / BigInteger 5

                      if den.IsOne then
                          terminating <- terminating + 1
                          let exact = reference p q 40 RoundingMode.Down

                          match exact with
                          | Decimal t ->
                              Expect.equal (quotient a b scale mode) (rounded t scale mode) "Rounded(exact a/b, r)"
                          | other -> failtestf "%A" other

              Expect.isGreaterThan terminating 20 "terminating quotients were reached"

          testCase "Quotient(a*b, b, {scale(a); m}) = a, for every mode and a non-zero b"
          <| fun _ ->
              let rng = System.Random 2773

              for _ in 1..300 do
                  let a = drawDecimal rng
                  let b = drawDecimal rng
                  let mode = allModes[rng.Next allModes.Length]

                  if (ratOf b |> fst).IsZero |> not then
                      let ab = value (Binary(Mul, lit a, lit b))

                      match ab with
                      | Ok(Decimal t) ->
                          Expect.equal (quotient t b (placesOf a) mode) (Ok(dec a)) (sprintf "(%s*%s)/%s" a b b)
                      | other -> failtestf "%A" other

          testCase "Rounded is idempotent at a scale, and the identity at a scale no smaller than the operand's places"
          <| fun _ ->
              let rng = System.Random 2774

              for _ in 1..400 do
                  let x = drawDecimal rng
                  let scale = rng.Next 6
                  let mode = allModes[rng.Next allModes.Length]

                  match rounded x scale mode with
                  | Ok(Decimal once) -> Expect.equal (rounded once scale mode) (Ok(Decimal once)) "idempotent"
                  | other -> failtestf "%A" other

                  Expect.equal (rounded x (placesOf x + rng.Next 3) mode) (Ok(dec x)) "the identity"

          testCase "Rounded and Quotient: an int promotes, null propagates, a float names the cast"
          <| fun _ ->
              Expect.equal (value (Rounded(Lit(Int 7), at 0 RoundingMode.Down))) (Ok(dec "7")) "an int promotes"

              Expect.equal
                  (value (Quotient(lit "7", Lit(Int 2), at 1 RoundingMode.Down)))
                  (Ok(dec "3.5"))
                  "an int divisor"

              Expect.equal (value (Rounded(Lit Null, at 2 RoundingMode.Up))) (Ok Null) "null"
              Expect.equal (value (Quotient(Lit Null, lit "2", at 2 RoundingMode.Up))) (Ok Null) "null dividend"
              Expect.equal (quotient "1" "0" 2 RoundingMode.HalfEven) (Ok Null) "a zero divisor is null, as Div's is"
              Expect.stringContains (message (value (Rounded(Lit(Float 1.5), at 0 RoundingMode.Up)))) "Cast" "a float"

              Expect.stringContains
                  (message (value (Quotient(lit "1", Lit(Float 2.0), at 0 RoundingMode.Up))))
                  "Cast"
                  "a float divisor"

          testCase "the scale: a literal outside 0..1000 and a param of the wrong shape are refused by name"
          <| fun _ ->
              Expect.stringContains (message (rounded "1.5" 1001 RoundingMode.Up)) "rounding scale" "past 1000"
              Expect.stringContains (message (rounded "1.5" -1 RoundingMode.Up)) "rounding scale" "negative"

              let byParam =
                  Rounded(
                      lit "1.255",
                      { Scale = Slot.Param "places"
                        Mode = RoundingMode.HalfEven }
                  )

              Expect.equal (valueIn (Map.ofList [ "places", Int 2 ]) byParam) (Ok(dec "1.26")) "a bound param"

              Expect.stringContains
                  (message (valueIn (Map.ofList [ "places", Str "2" ]) byParam))
                  "rounding scale"
                  "a param bound to a string names the slot"

              match valueIn Map.empty byParam with
              | Error(UnboundParam("places", _)) -> ()
              | other -> failtestf "an unbound scale param is UnboundParam, got %A" other

              Expect.equal (ColExpr.paramsOf byParam) [ "places" ] "the scale param is reported"

              Expect.equal
                  (ColExpr.substitute (Map.ofList [ "places", Int 2 ]) byParam)
                  (Rounded(lit "1.255", at 2 RoundingMode.HalfEven))
                  "and substituted, as Limit's slot is"

          testCase "every refusal names what it needs: the rounding, or the cast"
          <| fun _ ->
              let divMsg = message (value (Binary(Div, lit "1", lit "3")))
              Expect.stringContains divMsg "rounding" "Div names the rounding"
              Expect.stringContains divMsg "Quotient" "and the node that takes it"

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
                      (Column.toCells (Table.tryColumn "k" r |> Option.get))
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

              for fn in [ CumulSum; RollingSum ] do
                  match DataFrame.evalPipeline [ spec fn ] t with
                  | Ok r ->
                      Expect.equal
                          (Column.toCells (Table.tryColumn "run" r |> Option.get))
                          [ dec "0.1"; dec "0.3"; dec "0.6" ]
                          (sprintf "%A" fn)
                  | Error e -> failtestf "%A" e

          testCase "the typer and the totality verdict over decimal arithmetic and the rounding nodes"
          <| fun _ ->
              let schema: Schema =
                  [ Field.create "m" DecimalType
                    Field.create "i" IntType
                    Field.create "f" FloatType ]

              let ty e = DataFrame.typeOf schema e
              Expect.equal (ty (Binary(Add, Col "m", Col "i"))) (Some DecimalType) "an int promotes"
              Expect.equal (ty (Binary(Add, Col "m", Col "f"))) None "a decimal beside a float is refused"
              Expect.equal (ty (Binary(Lt, Col "m", Col "i"))) (Some BoolType) "an exact comparison"
              Expect.equal (ty (Quotient(Col "m", Col "i", at 2 RoundingMode.Down))) (Some DecimalType) "quotient"
              Expect.equal (ty (Rounded(Col "i", at 2 RoundingMode.Down))) (Some DecimalType) "rounded"
              Expect.equal (ty (Rounded(Col "f", at 2 RoundingMode.Down))) None "a float rounded is refused"
              Expect.equal (ty (ApplyFn(Round, [ Col "m" ]))) (Some DecimalType) "round of a decimal"
              Expect.equal (ty (Cast(DecimalType, Col "f"))) (Some DecimalType) "cast"

              let total e = Plan.isTotal schema (Derive("x", e))
              Expect.isTrue (total (Binary(Add, Col "m", Col "i"))) "exact addition cannot refuse"
              Expect.isTrue (total (Binary(Mod, Col "m", Col "m"))) "nor a decimal mod"
              Expect.isTrue (total (Binary(Lt, Col "m", Col "i"))) "nor an exact comparison"
              Expect.isTrue (total (Cast(DecimalType, Col "i"))) "nor an int's cast"

              Expect.isTrue
                  (total (Quotient(Col "m", Col "i", at 2 RoundingMode.HalfEven)))
                  "nor a literal-scale quotient"

              Expect.isTrue (total (Rounded(Col "m", at 0 RoundingMode.Floor))) "nor a literal-scale rounding"
              Expect.isTrue (total (ApplyFn(Round, [ Col "m" ]))) "nor round of a decimal"
              Expect.isFalse (total (Binary(Div, Col "m", Col "i"))) "a decimal Div refuses"
              Expect.isFalse (total (Binary(Add, Col "m", Col "f"))) "a decimal beside a float refuses"
              Expect.isFalse (total (Quotient(Col "m", Col "f", at 2 RoundingMode.HalfEven))) "a float divisor refuses"
              Expect.isFalse (total (Rounded(Col "m", at 1001 RoundingMode.Floor))) "a scale past 1000 refuses"

              Expect.isFalse
                  (total (
                      Rounded(
                          Col "m",
                          { Scale = Slot.Param "p"
                            Mode = RoundingMode.Floor }
                      )
                  ))
                  "a param scale is the env's"

          testCase "the rounding nodes round-trip the pipeline codec; the mode is spelled only there"
          <| fun _ ->
              let p =
                  [ Derive("q", Quotient(lit "1.25", Lit(Int 3), at 2 RoundingMode.HalfEven))
                    Derive(
                        "r",
                        Rounded(
                            Col "q",
                            { Scale = Slot.Param "places"
                              Mode = RoundingMode.Ceiling }
                        )
                    ) ]

              let wire = DataFrameCodec.encodePipeline p

              Expect.stringContains
                  wire
                  "{\"$type\":\"Decimal\",\"value\":\"1.25\"}"
                  "a decimal literal is a JSON string"

              Expect.stringContains wire "\"$type\":\"quotient\"" "the quotient's tag"
              Expect.stringContains wire "\"rounding\":{\"mode\":\"half-even\",\"scale\":2}" "the rounding"
              Expect.stringContains wire "{\"$param\":\"places\"}" "a param scale in the slot's wire form"
              Expect.equal (DataFrameCodec.decodePipeline wire) (Ok p) "round-trips"

              for mode in allModes do
                  let one = [ Derive("r", Rounded(Col "x", at 1 mode)) ]

                  Expect.equal
                      (DataFrameCodec.decodePipeline (DataFrameCodec.encodePipeline one))
                      (Ok one)
                      (sprintf "%A" mode)

              let unknown = wire.Replace("half-even", "bankers")

              match DataFrameCodec.decodePipeline unknown with
              | Error(UnknownTag("bankers", allowed)) ->
                  Expect.contains allowed "half-even" "the decode names the modes"
              | other -> failtestf "an unknown mode is a decode error, got %A" other ]
