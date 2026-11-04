module Fuaran.Compute.Tests.FunctionsAndCurvesTests

// ============================================================================
//  Phase 596 — a pipeline carries its own named functions and interpolated
//  curves (`DECISIONS.md` D48).
//
//  The named functions (`Transform.Define`, registered and expanded by
//  `FnDef.expand`): a `Call` evaluates byte for byte as its hand-expanded
//  body, through every entry point that evaluates, types or walks a pipeline;
//  each registration refusal is reached by name; the size bound refuses an
//  exponential nest before building it. The curves: exact at every knot, the
//  two outside policies, the operand rules, the knot rule, the monotone cubic
//  monotone wherever its knots are, and pinned values for the linear and step
//  kinds — the bytes the cross-host corpus carries.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.Compute

let private table (cols: (string * ColumnType * Cell list) list) : Table =
    { Schema = cols |> List.map (fun (n, t, _) -> Field.create n t)
      Columns = cols |> List.map (fun (n, t, cells) -> KitColumn.create n t cells) }

let private spend =
    table
        [ "id", IntType, [ Int 1; Int 2; Int 3; Int 4; Int 5 ]
          "x", FloatType, [ Float 0.0; Float 1.5; Null; Float 4.0; Float 10.0 ]
          "n", IntType, [ Int 0; Int 2; Int 3; Null; Int -1 ] ]

let private wire (r: Result<Table, EvalError>) : string =
    match r with
    | Ok t -> ColumnCodec.encode (Embedded t)
    | Error e -> "error: " + DataFrame.errorString e

let private param (name: string) : FnParam = { Name = name; Default = None }

let private withDefault (name: string) (d: Slot<Cell>) : FnParam = { Name = name; Default = Some d }

/// The Hill saturation curve `x^a / (x^a + k^a)`, `a` and `k` defaulting to pipeline params.
let private hill: FnDef =
    let pow b e = ApplyFn(Pow, [ b; e ])

    { Name = "hill"
      Params =
        [ param "x"
          withDefault "a" (Slot.Param "hill_a")
          withDefault "k" (Slot.Param "hill_k") ]
      Body = Binary(Div, pow (Arg "x") (Arg "a"), Binary(Add, pow (Arg "x") (Arg "a"), pow (Arg "k") (Arg "a"))) }

let private hillBody (x: ColExpr) (a: ColExpr) (k: ColExpr) : ColExpr =
    let pow b e = ApplyFn(Pow, [ b; e ])
    Binary(Div, pow x a, Binary(Add, pow x a, pow k a))

/// `double(v) = v + v` and `quad(v) = double(double(v))`.
let private double': FnDef =
    { Name = "double"
      Params = [ param "v" ]
      Body = Binary(Add, Arg "v", Arg "v") }

let private quad: FnDef =
    { Name = "quad"
      Params = [ param "v" ]
      Body = Call("double", [ Call("double", [ Arg "v" ]) ]) }

let private expectError (label: string) (expected: EvalError -> bool) (r: Result<'a, EvalError>) =
    match r with
    | Error e when expected e -> ()
    | Error e -> failtestf "%s: refused, but as %s" label (DataFrame.errorString e)
    | Ok _ -> failtestf "%s: expected a refusal, got an answer" label

let private curve (knots: (float * float) list) kind outside (x: Cell) : Result<Cell, EvalError> =
    DataFrame.evalExprInRow Map.empty [ Field.create "x" FloatType ] [ x ] (Interpolate(Col "x", knots, kind, outside))

let private floatOf (r: Result<Cell, EvalError>) : float =
    match r with
    | Ok(Float f) -> f
    | other -> failtestf "expected a float, got %A" other

/// A small deterministic generator (an LCG), so a red names its seed.
let private lcg (seed: int) =
    let mutable s = uint32 seed

    fun () ->
        s <- s * 1664525u + 1013904223u
        float (s >>> 8) / float (1u <<< 24)

[<Tests>]
let tests =
    testList
        "Functions and curves (Phase 596)"
        [ testList
              "named functions"
              [ test "a call evaluates byte for byte as its hand-expanded body, defaults bound from the env" {
                    let env = Map.ofList [ "hill_a", Float 2.0; "hill_k", Float 3.0 ]

                    let called = [ Define hill; Derive("s", Call("hill", [ Col "x" ])) ]
                    let byHand = [ Derive("s", hillBody (Col "x") (Param "hill_a") (Param "hill_k")) ]

                    let a = wire (DataFrame.evalPipelineInEnv env called spend)
                    let b = wire (DataFrame.evalPipelineInEnv env byHand spend)
                    Expect.equal a b "the call and its expansion answer the same bytes"
                    Expect.isFalse (a.StartsWith "error") "the pipeline answered"
                }

                test "passed arguments bind before defaults, and nested calls expand inside out" {
                    let called =
                        [ Define double'
                          Define quad
                          Derive("q", Call("quad", [ Col "n" ]))
                          Derive("h", Call("hill", [ Col "x"; Lit(Int 2); Lit(Float 0.5) ]))
                          Define hill ]

                    let byHand =
                        [ Derive("q", Binary(Add, Binary(Add, Col "n", Col "n"), Binary(Add, Col "n", Col "n")))
                          Derive("h", hillBody (Col "x") (Lit(Int 2)) (Lit(Float 0.5))) ]

                    Expect.equal
                        (wire (DataFrame.evalPipeline called spend))
                        (wire (DataFrame.evalPipeline byHand spend))
                        "same bytes"

                    Expect.equal
                        (FnDef.expand called)
                        (Ok byHand)
                        "the expansion is the hand expansion, definitions dropped"
                }

                test "every entry point reads the expansion: as written, the async form, the clock form" {
                    let called =
                        [ Define double'; Filter(Binary(Gt, Call("double", [ Col "n" ]), Lit(Int 2))) ]

                    let byHand = [ Filter(Binary(Gt, Binary(Add, Col "n", Col "n"), Lit(Int 2))) ]
                    let expected = wire (DataFrame.evalPipeline byHand spend)

                    Expect.equal (wire (DataFrame.evalPipeline called spend)) expected "planned"
                    Expect.equal (wire (DataFrame.evalPipelineAsWritten called spend)) expected "as written"

                    Expect.equal
                        (wire (DataFrame.evalPipelineAt (fun _ -> Null) called spend))
                        expected
                        "with a pinned clock"

                    let asyncAnswer =
                        DataFrame.evalToPreparedAsync DataFrame.noResolve Map.empty called (DataFrame.prepare spend)
                        |> Async.RunSynchronously
                        |> Result.map DataFrame.toTable

                    Expect.equal (wire asyncAnswer) expected "the asynchronous entry point"
                }

                test "a default param is a param of the pipeline; unbound, it is refused by name at evaluation" {
                    let p = [ Define hill; Derive("s", Call("hill", [ Col "x" ])) ]
                    Expect.equal (Transform.paramsOf p) [ "hill_a"; "hill_k" ] "the defaults' params"

                    DataFrame.evalPipeline p spend
                    |> expectError "unbound default" (function
                        | UnboundParam("hill_a", _) -> true
                        | _ -> false)

                    let bound =
                        Transform.substitute (Map.ofList [ "hill_a", Float 1.0; "hill_k", Float 2.0 ]) p

                    Expect.equal (Transform.paramsOf bound) [] "substitution binds a default"
                }

                test "recursion is refused by name, the cycle in call order" {
                    let self =
                        { Name = "f"
                          Params = [ param "v" ]
                          Body = Call("f", [ Arg "v" ]) }

                    FnDef.expand [ Define self ]
                    |> expectError "self" (function
                        | RecursiveFunction [ "f"; "f" ] -> true
                        | _ -> false)

                    let f =
                        { Name = "f"
                          Params = [ param "v" ]
                          Body = Call("g", [ Arg "v" ]) }

                    let g =
                        { Name = "g"
                          Params = [ param "w" ]
                          Body = Binary(Add, Call("f", [ Arg "w" ]), Lit(Int 1)) }

                    DataFrame.evalPipeline [ Define f; Define g; Derive("y", Call("f", [ Col "n" ])) ] spend
                    |> expectError "mutual" (function
                        | RecursiveFunction [ "f"; "g"; "f" ] -> true
                        | _ -> false)
                }

                test "an expansion past the bound is refused by name before it is built" {
                    // f0(v) = v + v; f(i)(v) = f(i-1)(v) + f(i-1)(v): 2^(i+2) - 1 nodes, so
                    // f8 (1,023) is past 512 and f60 would not fit in memory — it is refused from the sizes alone.
                    let chain (depth: int) =
                        [ for i in 0..depth ->
                              Define
                                  { Name = sprintf "f%d" i
                                    Params = [ param "v" ]
                                    Body =
                                      if i = 0 then
                                          Binary(Add, Arg "v", Arg "v")
                                      else
                                          let inner = Call(sprintf "f%d" (i - 1), [ Arg "v" ])
                                          Binary(Add, inner, inner) } ]

                    FnDef.expand (chain 60)
                    |> expectError "deep nest" (function
                        | ExpansionTooLarge("function 'f8'", 512) -> true
                        | _ -> false)

                    // Within the bound per definition, past it at the call: a large argument read
                    // eight times.
                    let eight =
                        { Name = "eight"
                          Params = [ param "v" ]
                          Body =
                            List.init 7 (fun _ -> Arg "v")
                            |> List.fold (fun acc a -> Binary(Add, acc, a)) (Arg "v") }

                    let big =
                        List.init 40 (fun i -> Lit(Int i))
                        |> List.fold (fun acc a -> Binary(Add, acc, a)) (Col "n")

                    FnDef.expand [ Define eight; Derive("y", Call("eight", [ big ])) ]
                    |> expectError "at the step" (function
                        | ExpansionTooLarge("derive 'y'", 512) -> true
                        | _ -> false)

                    Expect.isOk (FnDef.expand (chain 7)) "f7 (511 nodes) is within the bound"
                }

                test "registration refuses unknown functions, arity, and malformed definitions by name" {
                    let derive e = Derive("y", e)

                    FnDef.expand [ derive (Call("nope", [ Col "n" ])) ]
                    |> expectError "unknown" (function
                        | FunctionError("nope", _) -> true
                        | _ -> false)

                    FnDef.expand [ Define double'; derive (Call("double", [ Col "n"; Col "n" ])) ]
                    |> expectError "too many" (function
                        | ArityError("double", 1, 2) -> true
                        | _ -> false)

                    FnDef.expand [ Define hill; derive (Call("hill", [])) ]
                    |> expectError "too few" (function
                        | ArityError("hill", 1, 0) -> true
                        | _ -> false)

                    FnDef.expand [ Define double'; Define double' ]
                    |> expectError "twice" (function
                        | FunctionError("double", "defined twice") -> true
                        | _ -> false)

                    FnDef.expand
                        [ Define
                              { double' with
                                  Params = [ param "v"; param "v" ] } ]
                    |> expectError "parameter twice" (function
                        | FunctionError("double", _) -> true
                        | _ -> false)

                    FnDef.expand [ Define { double' with Body = Col "n" } ]
                    |> expectError "reads a column" (function
                        | FunctionError("double", d) -> d.Contains "column 'n'"
                        | _ -> false)

                    FnDef.expand [ Define { double' with Body = Arg "w" } ]
                    |> expectError "an Arg naming no parameter" (function
                        | FunctionError("double", d) -> d.Contains "'w'"
                        | _ -> false)

                    DataFrame.evalPipeline [ derive (Arg "v") ] spend
                    |> expectError "an Arg in a step" (function
                        | FunctionError("v", _) -> true
                        | _ -> false)

                    // A call reaching the evaluator without the pipeline's definitions.
                    DataFrame.evalExprInRow Map.empty [] [] (Call("double", [ Lit(Int 1) ]))
                    |> expectError "unexpanded" (function
                        | FunctionError("double", _) -> true
                        | _ -> false)
                }

                test "the static walk reads through: typing, and the exactness of a float power" {
                    let schema = spend.Schema

                    let half =
                        { double' with
                            Name = "half"
                            Body = ApplyFn(Pow, [ Arg "v"; Lit(Float 0.5) ]) }

                    let p =
                        [ Define double'
                          Define half
                          Derive("d", Call("double", [ Col "n" ]))
                          Derive("r", Call("half", [ Col "x" ]))
                          Filter(Binary(Gt, Col "r", Lit(Float 1.0))) ]

                    let byHand =
                        [ Derive("d", Binary(Add, Col "n", Col "n"))
                          Derive("r", ApplyFn(Pow, [ Col "x"; Lit(Float 0.5) ]))
                          Filter(Binary(Gt, Col "r", Lit(Float 1.0))) ]

                    let cols k =
                        match k with
                        | SchemaKnowledge.Closed cs -> cs
                        | SchemaKnowledge.AtLeast(cs, _) -> cs

                    let called = cols (SchemaWalk.ofPipeline schema p)
                    Expect.equal called (cols (SchemaWalk.ofPipeline schema byHand)) "the walk is the expansion's"

                    let d = called |> List.find (fun c -> c.Name = "d")
                    let r = called |> List.find (fun c -> c.Name = "r")
                    Expect.equal d.Type (Some IntType) "an int doubled is an int"
                    Expect.equal d.Exactness Exactness.Pinned "exact arithmetic is pinned"
                    Expect.equal r.Exactness Exactness.Pinned "a float power is pinned (D54), and the walk says so"
                    Expect.isEmpty (SchemaWalk.hostRoundedDecisions schema p) "a pinned pipeline reaches no decision"

                    // Over an input whose `x` is declared host-rounded, the filter reading the call's
                    // column is a decision at the WRITTEN step 4, not at the expanded step 2.
                    let marked =
                        match SchemaWalk.ofSchema schema with
                        | SchemaKnowledge.Closed cs ->
                            SchemaKnowledge.Closed(
                                cs
                                |> List.map (fun c ->
                                    if c.Name = "x" then
                                        ColumnKnowledge.withExactness Exactness.HostRounded c
                                    else
                                        c)
                            )
                        | other -> failtestf "an input schema walks closed: %A" other

                    let decisions = SchemaWalk.hostRoundedDecisionsFrom SchemaWalk.noSources marked p
                    Expect.equal (decisions |> List.map (fun d -> d.Step, d.Verb)) [ 4, "filter" ] "written index"
                }

                test "the incremental seam classifies the expansion, so a call is as incremental as its body" {
                    let p = [ Define double'; Derive("d", Call("double", [ Col "n" ])) ]
                    let plan, report = Incremental.planOver spend.Schema p
                    Expect.isTrue (Incremental.isIncremental plan) "row-local once expanded"
                    Expect.equal report.Planned [ Derive("d", Binary(Add, Col "n", Col "n")) ] "planned expanded"
                }

                test "the wire round-trips definitions, calls, arguments and defaults" {
                    let p =
                        [ Define hill
                          Define
                              { double' with
                                  Params = [ withDefault "v" (Slot.Lit(Int 3)) ] }
                          Derive("s", Call("hill", [ Col "x"; Call("double", []) ])) ]

                    let text = DataFrameCodec.encodePipeline p
                    Expect.equal (DataFrameCodec.decodePipeline text) (Ok p) "round trip"
                    Expect.stringContains text "\"$type\":\"define\"" "the define tag"
                    Expect.stringContains text "\"$type\":\"invoke\"" "the call tag"
                    Expect.stringContains text "\"$type\":\"arg\"" "the arg tag"
                    Expect.stringContains text "{\"$param\":\"hill_a\"}" "a param default in the slot form"
                } ]

          testList
              "interpolated curves"
              [ test "every kind is exact at every knot" {
                    let knots = [ -2.0, 0.1; 0.3, 7.25; 1.0, -3.5; 4.5, -3.5; 9.0, 1e10 ]

                    for kind in [ InterpKind.Linear; InterpKind.Step; InterpKind.MonotoneCubic ] do
                        for x, y in knots do
                            Expect.equal
                                (floatOf (curve knots kind InterpOutside.Null (Float x)))
                                y
                                (sprintf "%A at %g" kind x)
                }

                test "linear and step answer pinned values" {
                    let knots = [ 0.0, 0.0; 4.0, 1.0; 6.0, -1.0 ]

                    let at kind x =
                        floatOf (curve knots kind InterpOutside.Clamp (Float x))

                    Expect.equal (at InterpKind.Linear 1.0) 0.25 "a quarter of the way"
                    Expect.equal (at InterpKind.Linear 5.0) 0.0 "half way down"

                    Expect.equal
                        (at InterpKind.Linear 0.1)
                        (0.0 + (1.0 - 0.0) * ((0.1 - 0.0) / (4.0 - 0.0)))
                        "the pinned order"

                    Expect.equal (at InterpKind.Step 3.999) 0.0 "held from the left knot"
                    Expect.equal (at InterpKind.Step 4.5) 1.0 "the next knot's value from its x"
                }

                test "outside the knots: clamp to the end knot, or null" {
                    let knots = [ 1.0, 10.0; 2.0, 20.0 ]

                    for kind in [ InterpKind.Linear; InterpKind.Step; InterpKind.MonotoneCubic ] do
                        Expect.equal (curve knots kind InterpOutside.Clamp (Float 0.0)) (Ok(Float 10.0)) "clamped below"

                        Expect.equal
                            (curve knots kind InterpOutside.Clamp (Float infinity))
                            (Ok(Float 20.0))
                            "clamped above"

                        Expect.equal (curve knots kind InterpOutside.Null (Float 2.5)) (Ok Null) "null above"
                        Expect.equal (curve knots kind InterpOutside.Null (Float -infinity)) (Ok Null) "null below"
                }

                test "the operand: an int promotes, a null propagates, NaN answers NaN, a decimal or text is refused" {
                    let knots = [ 0.0, 0.0; 10.0, 5.0 ]
                    let at = curve knots InterpKind.Linear InterpOutside.Clamp
                    Expect.equal (at (Int 4)) (Ok(Float 2.0)) "an int"
                    Expect.equal (at Null) (Ok Null) "a null"
                    Expect.isTrue (System.Double.IsNaN(floatOf (at (Float nan)))) "NaN"

                    at (Cell.decimal "1.5" |> Option.get)
                    |> expectError "decimal" (function
                        | TypeError d -> d.Contains "decimal"
                        | _ -> false)

                    at (Str "1") |> expectError "text" (fun _ -> true)
                }

                test "knots that break the rule are refused by name, on an empty frame too" {
                    let refused (label: string) (knots: (float * float) list) =
                        let empty = table [ "x", FloatType, [] ]

                        DataFrame.evalPipeline
                            [ Derive("y", Interpolate(Col "x", knots, InterpKind.Linear, InterpOutside.Clamp)) ]
                            empty
                        |> expectError label (function
                            | InvalidKnots _ -> true
                            | _ -> false)

                    refused "one knot" [ 0.0, 1.0 ]
                    refused "no knots" []
                    refused "not increasing" [ 0.0, 1.0; 0.0, 2.0 ]
                    refused "decreasing" [ 1.0, 1.0; 0.0, 2.0 ]
                    refused "NaN" [ 0.0, nan; 1.0, 2.0 ]
                    refused "infinite" [ 0.0, 1.0; infinity, 2.0 ]
                    refused "a rise that overflows" [ 0.0, -1e308; 1.0, 1e308 ]
                }

                test "the compiled form answers the reference's bytes" {
                    let knots = [ 0.0, 1.0; 2.0, 3.0; 3.0, 3.0; 7.0, 0.5 ]

                    let exprs =
                        [ for kind in [ InterpKind.Linear; InterpKind.Step; InterpKind.MonotoneCubic ] do
                              for outside in [ InterpOutside.Clamp; InterpOutside.Null ] ->
                                  Interpolate(Col "x", knots, kind, outside) ]

                    let t =
                        table
                            [ "x",
                              FloatType,
                              [ Float -1.0
                                Float 0.5
                                Null
                                Float 2.0
                                Float 2.5
                                Float 5.25
                                Float 8.0
                                Float nan ] ]

                    for e in exprs do
                        let compiled =
                            match DataFrame.evalPipeline [ Derive("y", e) ] t with
                            | Ok r -> Table.tryColumn "y" r |> Option.get |> Column.toCells
                            | Error err -> failtestf "%s" (DataFrame.errorString err)

                        let reference =
                            Table.tryColumn "x" t
                            |> Option.get
                            |> Column.toCells
                            |> List.map (fun x ->
                                match DataFrame.evalExprInRow Map.empty t.Schema [ x ] e with
                                | Ok c -> c
                                | Error err -> failtestf "%s" (DataFrame.errorString err))

                        Expect.equal
                            (compiled
                             |> List.map (fun c -> ColumnCodec.encode (Embedded(table [ "c", FloatType, [ c ] ]))))
                            (reference
                             |> List.map (fun c -> ColumnCodec.encode (Embedded(table [ "c", FloatType, [ c ] ]))))
                            (sprintf "%A" e)
                }

                test "a monotone cubic is monotone wherever its knots are, and flat on a flat segment" {
                    let next = lcg 20261104

                    for trial in 1..200 do
                        let n = 2 + int (next () * 8.0)

                        let xs =
                            List.init n (fun _ -> 0.05 + next () * 3.0) |> List.scan (+) 0.0 |> List.tail

                        // Non-decreasing data with repeated values, then mirrored for non-increasing.
                        let steps =
                            List.init n (fun _ -> if next () < 0.25 then 0.0 else next () * next () * 50.0)

                        let ys = steps |> List.scan (+) (next () * 10.0 - 5.0) |> List.tail

                        for sign in [ 1.0; -1.0 ] do
                            let knots = List.zip xs (ys |> List.map ((*) sign))

                            let samples =
                                [ for i in 0..400 -> List.head xs + (List.last xs - List.head xs) * float i / 400.0 ]
                                @ xs
                                |> List.sort

                            let values =
                                samples
                                |> List.map (fun x ->
                                    floatOf (curve knots InterpKind.MonotoneCubic InterpOutside.Clamp (Float x)))

                            for (xa, a), (xb, b) in List.pairwise (List.zip samples values) do
                                if (sign > 0.0 && b < a) || (sign < 0.0 && b > a) then
                                    failtestf
                                        "trial %d: not monotone between %.17g (%.17g) and %.17g (%.17g) over %A"
                                        trial
                                        xa
                                        a
                                        xb
                                        b
                                        knots

                            // A flat segment answers its knots' value everywhere inside it.
                            for (x0, y0), (x1, y1) in List.pairwise knots do
                                if y0 = y1 then
                                    let mid =
                                        floatOf (
                                            curve
                                                knots
                                                InterpKind.MonotoneCubic
                                                InterpOutside.Clamp
                                                (Float((x0 + x1) / 2.0))
                                        )

                                    Expect.equal mid y0 "flat"
                }

                test "the wire round-trips a curve, its kind and its outside policy" {
                    for kind in [ InterpKind.Linear; InterpKind.Step; InterpKind.MonotoneCubic ] do
                        for outside in [ InterpOutside.Clamp; InterpOutside.Null ] do
                            let p =
                                [ Derive("y", Interpolate(Col "x", [ -1.5, 2.0; 0.0, 0.0; 3.0, 1e-3 ], kind, outside)) ]

                            Expect.equal
                                (DataFrameCodec.decodePipeline (DataFrameCodec.encodePipeline p))
                                (Ok p)
                                "round trip"

                    let text =
                        DataFrameCodec.encodePipeline
                            [ Filter(
                                  Interpolate(
                                      Col "x",
                                      [ 0.0, 1.0; 1.0, 2.0 ],
                                      InterpKind.MonotoneCubic,
                                      InterpOutside.Null
                                  )
                              ) ]

                    Expect.stringContains text "\"kind\":\"monotoneCubic\"" "the kind tag"
                    Expect.stringContains text "\"outside\":\"null\"" "the outside tag"
                    Expect.stringContains text "\"$type\":\"interpolate\"" "the expression tag"
                } ] ]
