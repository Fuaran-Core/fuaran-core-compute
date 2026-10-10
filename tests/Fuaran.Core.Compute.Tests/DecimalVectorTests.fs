module Fuaran.Compute.Tests.DecimalVectorTests

// ---------------------------------------------------------------------------
//  Phase 280 — the scaled-integer decimal vector behind the `Table` boundary. A decimal column
//  whose values fit fifteen significant digits at one scale is packed as `Decs`: its cells as they
//  came in, beside each value as an exact integer in a float64. The filter, sort and sum kernels
//  read the integers; everything else reads the cells. A column past the width, or holding any
//  cell that is not well-formed decimal text, takes the text path, which is a path and not a
//  refusal. `DecimalVectorLaw` holds the two paths byte-identical; this file holds the law
//  non-vacuous and pins the edges it relies on.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

let private decs (cells: Cell list) : Vec =
    Vec.pack DecimalType (List.toArray cells)

let private isDecs (v: Vec) : bool =
    match v with
    | Decs _ -> true
    | _ -> false

let private scaleOfVec (v: Vec) : int option =
    match v with
    | Decs(_, s, _, _) -> Some s
    | _ -> None

let private d (s: string) : Cell = Decimal s

let private sumOver (v: Vec) : Cell =
    let n = Vec.length v
    let s = DataFrame.GroupAgg.Stream(Sum, DecimalType, v, 1, DataFrame.GroupAgg.Exact)
    s.FeedAll(Array.zeroCreate n, Array.init n id)

    match s.TryCell 0 with
    | ValueSome c -> c
    | ValueNone -> failtest "the decimal Sum deferred"

/// The reference sum: `Column.aggregate` over the cells, which adds `DecimalText`.
let private referenceSum (cells: Cell list) : Cell =
    match Column.aggregate Sum (KitColumn.create "v" DecimalType cells) with
    | Ok c -> c
    | Error e -> failtestf "the reference refused: %A" e

[<Tests>]
let tests =
    testList
        "Phase 280 — the decimal vector"
        [ testCase "the scaled reading: shape, scale, value and canonical rendering"
          <| fun _ ->
              let shape s =
                  match ScaledDecimal.shape s with
                  | ValueSome(struct (i, f)) -> Some(i, f)
                  | ValueNone -> None

              Expect.equal (shape "012.50") (Some(2, 1)) "leading and trailing zeros carry no digit"
              Expect.equal (shape "-0") (Some(0, 0)) "negative zero"
              Expect.equal (shape "0.05") (Some(0, 2)) "a fraction below one"

              for bad in [ ""; "-"; ".5"; "5."; "+1"; "1e5"; "1.2.3"; "1 2"; "-.5"; "--1" ] do
                  Expect.isNone (shape bad) (sprintf "'%s' is outside the grammar" bad)

              Expect.equal (ScaledDecimal.tryScaled 3 "-12.5") (ValueSome -12500.0) "scaled up to the column's scale"
              Expect.equal (ScaledDecimal.tryScaled 0 "-0") (ValueSome 0.0) "no negative zero in the carrier"

              Expect.isFalse
                  (System.Double.IsNegative(ScaledDecimal.tryScaled 2 "-0.00" |> ValueOption.get))
                  "-0.00 is +0.0"

              Expect.equal (ScaledDecimal.tryScaled 1 "1.25") ValueNone "finer than the scale"
              Expect.equal (ScaledDecimal.tryScaled 0 "999999999999999") (ValueSome 999999999999999.0) "the width"
              Expect.equal (ScaledDecimal.tryScaled 0 "1000000000000000") ValueNone "past the width"
              Expect.equal (ScaledDecimal.tryScaled 2 "99999999999999") ValueNone "past the width at the scale"

              for u, s, want in
                  [ 0.0, 3, "0"
                    125.0, 1, "12.5"
                    -12500.0, 3, "-12.5"
                    5.0, 2, "0.05"
                    -5.0, 2, "-0.05"
                    100.0, 2, "1"
                    9007199254740991.0, 4, "900719925474.0991" ] do
                  Expect.equal (ScaledDecimal.render s u) want (sprintf "render %g at scale %d" u s)

          testCase "the boundary packs a decimal column as the vector where it fits, and boxed where it does not"
          <| fun _ ->
              let fits = decs [ d "1.5"; Null; d "-20"; d "0.125" ]
              Expect.equal (scaleOfVec fits) (Some 3) "the column's scale is its finest value's"
              Expect.equal (Vec.cellAt fits 0) (d "1.5") "a cell reads back as it came in"
              Expect.equal (Vec.cellAt fits 1) Null "an absent row is null"

              let raw = decs [ d "012.50"; d "-0" ]
              Expect.isTrue (isDecs raw) "non-canonical forms in the grammar are carried"
              Expect.equal (Vec.cellAt raw 0) (d "012.50") "and read back untouched"

              let pastWidth = [ d "123456789012.5"; d "0.0001" ]
              Expect.isFalse (isDecs (decs pastWidth)) "twelve integer digits at scale four is past the width"
              Expect.isFalse (isDecs (decs [ d DecimalVectorLaw.poison ])) "the law's poison value"
              // Core `1.0.0` (Phase 423): an int in a decimal column is the decimal of its value, so
              // the column is the vector; it was boxed while the column was a cell list.
              Expect.isTrue (isDecs (decs [ d "1"; Int 2 ])) "an int cell in the column is its decimal"
              Expect.isFalse (isDecs (decs [ d "1"; d "1e3" ])) "malformed decimal text"
              Expect.isTrue (isDecs (decs [ Null; Null ])) "an all-null column is an empty vector"

          testCase "a decimal Sum stays in the carrier while exact and spills to the text path past 2^53"
          <| fun _ ->
              // Forty fifteen-digit values: the total leaves 2^53 after the tenth.
              let big = List.replicate 40 (d "999999999999999")
              // The spill mid-group at scale one, then values on both sides of it.
              let mixed =
                  List.replicate 100 (d "99999999999999.9")
                  @ [ d "-99999999999999.9"; d "0.1"; Null ]

              let near = [ d "90071992547.0991"; d "0.0001"; d "-0.0002"; d "0.0001" ]

              for cells in [ big; mixed; near; [ Null; Null ]; [ d "1.25"; Null; d "-1.25" ] ] do
                  let v = decs cells
                  Expect.isTrue (isDecs v) (sprintf "carried: %A" (List.truncate 2 cells))
                  Expect.equal (sumOver v) (referenceSum cells) (sprintf "the sum over %A" (List.truncate 2 cells))

          testCase "setAt, append and concat keep a decimal vector typed at the scale that fits"
          <| fun _ ->
              let v = decs [ d "1.5"; d "2" ]
              let same = Vec.setAt v 1 (d "3.5")
              Expect.equal (scaleOfVec same) (Some 1) "a value at the scale stays at it"
              Expect.equal (Vec.cellAt same 1) (d "3.5") "the edit"

              let finer = Vec.setAt v 1 (d "3.25")
              Expect.equal (scaleOfVec finer) (Some 2) "a finer value repacks at its scale"
              Expect.equal (sumOver finer) (d "4.75") "and sums at it"

              Expect.isFalse (isDecs (Vec.setAt v 0 (d DecimalVectorLaw.poison))) "past the width: the text path"
              Expect.equal (Vec.cellAt (Vec.setAt v 0 Null) 0) Null "a null edit"

              let a = decs [ d "1.5" ]
              let b = decs [ d "0.25" ]
              let joined = Vec.append a b
              Expect.equal (scaleOfVec joined) (Some 2) "two scales append at the finer"
              Expect.equal (sumOver joined) (d "1.75") "and sum exactly"

              let cat = Vec.concat DecimalType [| a; b; decs [ d "3" ] |]
              Expect.equal (scaleOfVec cat) (Some 2) "concat likewise"
              Expect.equal [ for i in 0..2 -> Vec.cellAt cat i ] [ d "1.5"; d "0.25"; d "3" ] "cells in order"

          testCase "the filter kernel reads a constant at the column's scale, and hands back one it cannot"
          <| fun _ ->
              let t =
                  { Schema = [ Field.create "v" DecimalType ]
                    Columns = [ KitColumn.create "v" DecimalType [ d "1.5"; d "2.25"; Null; d "-3"; d "0.0000001" ] ] }

              let frame = Frame.ofTable t
              Expect.isTrue (isDecs frame.Vecs[0]) "the column is carried"

              let bits (pred: ColExpr) =
                  DataFrame.filterBits Kernels.host frame (DataFrame.resolveExpr Map.empty frame.Cols pred)

              Expect.isSome (bits (Binary(Gt, Col "v", Lit(d "1.5")))) "a constant at the scale"
              Expect.isSome (bits (Binary(Le, Lit(Int 2), Col "v"))) "an int constant"
              Expect.isNone (bits (Binary(Gt, Col "v", Lit(d "1.00000001")))) "a constant finer than the scale"
              Expect.isNone (bits (Binary(Gt, Col "v", Lit(Int 2147483647)))) "an int past 2^53 at scale seven"
              Expect.isNone (bits (Binary(Gt, Col "v", Lit(Float 1.0)))) "a float: refused on the compiled path"

              match DataFrame.evalPipeline [ Filter(Binary(Gt, Col "v", Lit(Float 1.0))) ] t with
              | Error _ -> ()
              | Ok _ -> failtest "a decimal beside a float is refused by name, vector or not"

          testCase "the law is not vacuous: v is carried, w is boxed, and the shapes past the width occur"
          <| fun _ ->
              let mutable carried = 0
              let mutable pastWidth = 0

              for seed in 1..400 do
                  let case = DecimalVectorLaw.generate seed
                  let frame = Frame.ofTable case.Table

                  let idx name =
                      frame.Cols |> List.findIndex (fun f -> f.Name = name)

                  Expect.isFalse (isDecs frame.Vecs[idx "w"]) (sprintf "seed %d: w takes the text path" seed)

                  if isDecs frame.Vecs[idx "v"] then
                      carried <- carried + 1
                  else
                      pastWidth <- pastWidth + 1

              Expect.isGreaterThan carried 200 "most seeds carry v as the vector"
              Expect.isGreaterThan pastWidth 10 "some seeds put v past the width, so both sides are text"

          testCase "the law: every pipeline over the vector answers the text path's bytes"
          <| fun _ ->
              let failures, compared = DecimalVectorLaw.checkAll 1 400
              Expect.isGreaterThan compared 4000 "the law compared pipelines"
              Expect.isEmpty (List.truncate 5 failures) "the vector path is the text path, byte for byte" ]
