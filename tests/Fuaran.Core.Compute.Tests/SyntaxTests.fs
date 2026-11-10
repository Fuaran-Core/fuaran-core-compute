module Fuaran.Compute.Tests.SyntaxTests

// ============================================================================
//  Phase 626 — the `ColExpr` authoring helpers (`Fuaran.Compute.Syntax`,
//  `DECISIONS.md` D66) build exactly the nodes the constructors build.
//
//  Totality: a closed match from every `BinOp` case to its helper, so a case
//  the algebra gains stops the build (FS0025 is an error here) until its
//  spelling is decided. The identity law: every helper's node is structurally
//  equal to the hand-written constructor and renders to the same wire bytes,
//  and a deliberately wrong mapping is reported. The roster: every public
//  member of the module is under the law. Precedence: the operators bind as
//  the F# operators they spell.
// ============================================================================

open Expecto
open Microsoft.FSharp.Reflection
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Syntax

/// The closed match: every `BinOp` case to the helper that spells it. A case added to `BinOp` fails
/// to compile here until it has a spelling (FS0025 is an error in this repository).
let private helperOf (op: BinOp) : ColExpr -> ColExpr -> ColExpr =
    match op with
    | Add -> (.+)
    | Sub -> (.-)
    | Mul -> (.*)
    | Div -> (./)
    | Mod -> (.%)
    | Eq -> (.=)
    | Ne -> (.<>)
    | Lt -> (.<)
    | Le -> (.<=)
    | Gt -> (.>)
    | Ge -> (.>=)
    | And -> and'
    | Or -> or'
    | Contains -> contains
    | StartsWith -> startsWith
    | EndsWith -> endsWith

/// Every `BinOp` case, read off the union itself, so a new case joins the law without a list to edit.
let private allBinOps: BinOp list =
    FSharpType.GetUnionCases(typeof<BinOp>)
    |> Array.map (fun c -> FSharpValue.MakeUnion(c, [||]) :?> BinOp)
    |> List.ofArray

let private a = Col "a"
let private b = Param "p"

let private wire (e: ColExpr) : string =
    Json.render (DataFrameCodec.encodeExpr e)

/// One row of the identity law: the helper's name, the node the helper built, and the node the
/// constructor builds.
type private LawRow = string * ColExpr * ColExpr

/// The rows whose helper node differs from the constructor node, structurally or on the wire.
let private failures (rows: LawRow list) : string list =
    rows
    |> List.choose (fun (name, helper, ctor) ->
        if helper = ctor && wire helper = wire ctor then
            None
        else
            Some name)

/// The binary rows, built through a helper table (the shipped one, or a deliberately wrong one).
let private binaryRows (table: BinOp -> ColExpr -> ColExpr -> ColExpr) : LawRow list =
    allBinOps
    |> List.map (fun op -> sprintf "%A" op, table op a b, Binary(op, a, b))

/// Every non-binary helper beside its constructor.
let private leafRows: LawRow list =
    [ "col", col "a", Col "a"
      "param", param "p", Param "p"
      "lit", lit (Int 7), Lit(Int 7)
      "litInt", litInt -3, Lit(Int -3)
      "litFloat", litFloat 2.5, Lit(Float 2.5)
      "litBool", litBool true, Lit(Bool true)
      "litStr", litStr "x", Lit(Str "x")
      "litDate", litDate "2026-11-10", Lit(Date "2026-11-10")
      "litTimestamp", litTimestamp "2026-11-10T09:30:00Z", Lit(Timestamp "2026-11-10T09:30:00Z")
      "litDecimal", litDecimal "12.5", Lit(Decimal "12.5")
      "litNull", litNull, Lit Null
      "now", now NowGrain.Date, Now NowGrain.Date
      "not'", not' a, Not a
      "isNull'", isNull' a, IsNull a
      "coalesce", coalesce [ a; b ], Coalesce [ a; b ]
      "inList", inList a [ Lit(Int 1); b ], InList(a, [ Lit(Int 1); b ])
      "inParam", inParam a "ps", InParam(a, "ps")
      "cast", cast FloatType a, Cast(FloatType, a) ]

/// The compiled name of every public member the module exposes, read by reflection.
let private moduleMembers: Set<string> =
    let syntax = typeof<ColExpr>.Assembly.GetType("Fuaran.Compute.Syntax", true)

    syntax.GetMembers(System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.Static)
    |> Array.choose (fun m ->
        match m with
        // An operator is a special-name method; a module value's getter is one too, and the value is
        // read through its property below.
        | :? System.Reflection.MethodInfo as mi when not (mi.IsSpecialName && mi.Name.StartsWith "get_") ->
            Some mi.Name
        | :? System.Reflection.PropertyInfo as pi -> Some pi.Name
        | _ -> None)
    |> Set.ofArray

/// The compiled name each `BinOp` helper carries.
let private compiledNameOf (op: BinOp) : string =
    match op with
    | Add -> "op_DotPlus"
    | Sub -> "op_DotMinus"
    | Mul -> "op_DotMultiply"
    | Div -> "op_DotDivide"
    | Mod -> "op_DotPercent"
    | Eq -> "op_DotEquals"
    | Ne -> "op_DotLessGreater"
    | Lt -> "op_DotLess"
    | Le -> "op_DotLessEquals"
    | Gt -> "op_DotGreater"
    | Ge -> "op_DotGreaterEquals"
    | And -> "and'"
    | Or -> "or'"
    | Contains -> "contains"
    | StartsWith -> "startsWith"
    | EndsWith -> "endsWith"

[<Tests>]
let tests =
    testList
        "Phase 626 — ColExpr authoring helpers (Fuaran.Compute.Syntax)"
        [ testCase "every BinOp helper builds Binary(op, a, b), structurally and on the wire"
          <| fun _ ->
              Expect.equal (List.length allBinOps) 16 "the union read by reflection names every BinOp case"
              Expect.isEmpty (failures (binaryRows helperOf)) "no binary helper differs from its constructor"

          testCase "every other helper builds its constructor's node, structurally and on the wire"
          <| fun _ -> Expect.isEmpty (failures leafRows) "no leaf or named helper differs from its constructor"

          testCase "go-red: a deliberately wrong mapping is reported, by name"
          <| fun _ ->
              let swapped op =
                  match op with
                  | Lt -> helperOf Gt
                  | Gt -> helperOf Lt
                  | other -> helperOf other

              Expect.equal (failures (binaryRows swapped)) [ "Lt"; "Gt" ] "a swapped pair fails the law on both rows"

              let wrongLeaf: LawRow list = [ "litStr", litStr "x", Lit(Str "y") ]
              Expect.equal (failures wrongLeaf) [ "litStr" ] "a wrong leaf fails the law"

          testCase "the law covers every public member of the module"
          <| fun _ ->
              let underLaw =
                  Set.union
                      (leafRows |> List.map (fun (n, _, _) -> n) |> Set.ofList)
                      (allBinOps |> List.map compiledNameOf |> Set.ofList)

              Expect.equal
                  (Set.difference moduleMembers underLaw |> Set.toList)
                  []
                  "every public member is under the law"

              Expect.equal
                  (Set.difference underLaw moduleMembers |> Set.toList)
                  []
                  "every row of the law names a public member"

          testCase "the operators bind as the F# operators they spell"
          <| fun _ ->
              let x, y, z = Col "x", Col "y", Col "z"
              Expect.equal (x .+ y .* z) (Binary(Add, x, Binary(Mul, y, z))) "`.*` binds tighter than `.+`"
              Expect.equal (x .- y .- z) (Binary(Sub, Binary(Sub, x, y), z)) "`.-` is left-associative"

              Expect.equal (x .+ y .> z) (Binary(Gt, Binary(Add, x, y), z)) "arithmetic binds tighter than comparison"

              Expect.equal
                  (and' (x .> y) (y .<= z))
                  (Binary(And, Binary(Gt, x, y), Binary(Le, y, z)))
                  "the logical pair takes its operands as arguments"

          testCase "the README's two examples build the constructors' trees they are shown beside"
          <| fun _ ->
              Expect.equal
                  (Filter(col "salary" .> litInt 95))
                  (Filter(Binary(Gt, Col "salary", Lit(Int 95))))
                  "the first example"

              Expect.equal
                  (Filter(and' (col "dept" .= param "dept") (not' (isNull' (col "bonus")))))
                  (Filter(Binary(And, Binary(Eq, Col "dept", Param "dept"), Not(IsNull(Col "bonus")))))
                  "the second example"

          testCase "opening the module captures no FSharp.Core name"
          <| fun _ ->
              // Each of these would fail to compile, or answer differently, if an `open` had captured it.
              Expect.isFalse (not true) "not"
              Expect.isTrue (isNull (null: string)) "isNull"
              Expect.equal (int "3") 3 "int"
              Expect.equal (float 2) 2.0 "float"
              Expect.equal (string 4) "4" "string" ]
