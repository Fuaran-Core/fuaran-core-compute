/// Phase 280 — the law that the scaled-integer decimal vector changes no answer: every pipeline
/// over a decimal column the dense frame carries as a `Decs` vector answers, byte for byte, what it
/// answers over the same values on the text path (the boxed `Cells` vector).
///
/// The text path is reached through the public surface alone, so the law runs on every host that
/// compiles this file: the suite runs it on .NET, and the node benchmark harness compiles it with
/// Fable and runs it under node before it times anything. Each generated table carries two decimal
/// columns with the same value in every row but the first: `v`, which fits the vector, and `w`,
/// whose first row is past the vector's width, so the frame packs `w` boxed. Every pipeline starts
/// by dropping that first row, so what follows reads the same values through the two vectors, and
/// its answer over `v` must equal its answer over `w` as canonical wire bytes. The suite asserts
/// separately that `v` is packed as the vector and `w` boxed, so the law cannot pass by comparing
/// the text path with itself.
///
/// Overflow is among the cases by construction: values up to the width's fifteen significant
/// digits, so group totals leave the float carrier's exact range and spill to the text path
/// mid-group; columns past the width, which take the text path on both sides; integer constants
/// too large at the column's scale and decimal constants finer than it, which the filter kernel
/// hands back to the compiled path.
///
/// FSharp.Core and the dataframe package only, Fable-clean: the generator draws from a
/// multiplicative congruential generator computed in float64, exact on both hosts.
module Fuaran.Compute.Tests.DecimalVectorLaw

open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

/// A deterministic generator: `next s` is the Park-Miller step, every product below 2^53.
type private Rng(seed: int) =
    let mutable s = float (abs seed % 2147483646 + 1)

    member _.Int(bound: int) : int =
        s <- (s * 48271.0) % 2147483647.0
        int (s % float bound)

    member this.Chance(percent: int) : bool = this.Int 100 < percent

/// A value past the vector's width: seventeen significant digits.
let poison = "1234567890123456.5"

/// Decimal text with `intDigits` integer digits and `fracDigits` fraction digits, either sign,
/// in canonical form where `canonical` is set and otherwise sometimes with a leading integer zero,
/// a trailing fraction zero or a negative zero — forms the grammar reads and the text path
/// normalises on the way.
let private text (r: Rng) (intDigits: int) (fracDigits: int) (canonical: bool) : string =
    let digits (n: int) =
        String.init n (fun _ -> string (r.Int 10))

    let ip =
        let raw = digits intDigits
        let trimmed = raw.TrimStart '0'
        if trimmed = "" then "0" else trimmed

    let fp = (digits fracDigits).TrimEnd '0'
    let body = if fp = "" then ip else ip + "." + fp
    let negative = r.Chance 40 && body <> "0"
    let signed = if negative then "-" + body else body

    if canonical then
        signed
    else
        match r.Int 4 with
        | 0 -> (if negative then "-0" else "0") + body
        | 1 -> if fp = "" then signed + ".0" else signed + "0"
        | 2 when body = "0" -> "-0"
        | _ -> signed

/// One generated table: `id:int, grp:string, v:decimal, w:decimal` with `w = v` but in row 0.
type Case =
    { Seed: int
      Table: Table
      Right: Table
      DecimalConstants: string list
      IntConstants: int list }

let private column (name: string) (ty: ColumnType) (cells: Cell list) : Column = KitColumn.create name ty cells

/// The table and the constants for seed `seed`.
let generate (seed: int) : Case =
    let r = Rng(seed)
    let n = 1 + r.Int 60
    // The column's shape: most seeds fit the width; some sit at it, so sums overflow the carrier;
    // some are past it, so both columns take the text path.
    let maxInt, maxFrac =
        match r.Int 6 with
        | 0 -> 1, 0
        | 1 -> 3, 2
        | 2 -> 6, 4
        | 3 -> 12, 3
        | 4 -> 15, 0
        | _ -> 10, 8

    let canonical = r.Chance 70
    let pool = ResizeArray<string>()

    let draw () =
        if pool.Count > 0 && r.Chance 30 then
            pool[r.Int pool.Count]
        else
            let t = text r (r.Int(maxInt + 1)) (r.Int(maxFrac + 1)) canonical
            pool.Add t
            t

    let values =
        [ for i in 0 .. n - 1 -> if i > 0 && r.Chance 10 then None else Some(draw ()) ]

    let cell (v: string option) =
        match v with
        | Some t -> Decimal t
        | None -> Null

    let vCells = values |> List.map cell
    let wCells = vCells |> List.mapi (fun i c -> if i = 0 then Decimal poison else c)

    let table =
        { Schema =
            [ Field.create "id" IntType
              Field.create "grp" StringType
              Field.create "v" DecimalType
              Field.create "w" DecimalType ]
          Columns =
            [ column "id" IntType [ for i in 0 .. n - 1 -> Int i ]
              column "grp" StringType [ for _ in 0 .. n - 1 -> Str("g" + string (r.Int 3)) ]
              column "v" DecimalType vCells
              column "w" DecimalType wCells ] }

    let rightKeys =
        [ for _ in 0 .. r.Int 20 ->
              if pool.Count > 0 && r.Chance 70 then
                  Decimal pool[r.Int pool.Count]
              else
                  Null ]

    let right =
        { Schema = [ Field.create "rk" DecimalType; Field.create "b" IntType ]
          Columns =
            [ column "rk" DecimalType rightKeys
              column "b" IntType (rightKeys |> List.mapi (fun i _ -> Int i)) ] }

    let constants =
        [ for _ in 0..2 ->
              if pool.Count > 0 && r.Chance 60 then
                  pool[r.Int pool.Count]
              else
                  // Sometimes finer than the column's scale, so the kernel declines it.
                  text r (r.Int(maxInt + 1)) (r.Int(maxFrac + 3)) true ]

    let ints = [ r.Int 2001 - 1000; 2147483647; -2147483647 + r.Int 5 ]

    { Seed = seed
      Table = table
      Right = right
      DecimalConstants = constants
      IntConstants = ints }

/// The pipelines the law runs over decimal column `c`, by name.
let pipelines (case: Case) (c: string) : (string * Transform list) list =
    let agg (name: string) (fn: AggFn) = { Name = name; Fn = fn; Of = c }

    [ for k in case.DecimalConstants do
          yield "filter " + c + " > " + k, [ Filter(Binary(Gt, Col c, Lit(Decimal k))) ]
          yield "filter " + k + " = " + c, [ Filter(Binary(Eq, Lit(Decimal k), Col c)) ]

          yield
              "filter " + c + " <> " + k + " or id < 3",
              [ Filter(Binary(Or, Binary(Ne, Col c, Lit(Decimal k)), Binary(Lt, Col "id", Lit(Int 3)))) ]
      for k in case.IntConstants do
          yield "filter " + c + " <= " + string k, [ Filter(Binary(Le, Col c, Lit(Int k))) ]
      yield
          "group-and-sum",
          [ GroupBy(
                [ "grp" ],
                [ agg "s" Sum
                  agg "n" Count
                  agg "lo" Min
                  agg "hi" Max
                  agg "f" First
                  agg "l" Last ]
            ) ]
      yield "sum, one group", [ GroupBy([], [ agg "s" Sum ]) ]
      yield "sort ascending", [ Transform.sortBy [ c, Asc ] ]
      yield "sort descending, id", [ Transform.sortBy [ c, Desc; "id", Asc ] ]
      yield "top 5", [ Transform.sortBy [ c, Asc ]; Transform.limit 5 0 ]
      yield "join", [ Join(Embedded case.Right, [ c, "rk" ], Inner) ]
      yield
          "group by the decimal",
          [ GroupBy([ c ], [ { Name = "n"; Fn = Count; Of = "id" } ])
            Project [ c, "key"; "n", "n" ] ]
      yield
          "derive and sort",
          [ Derive("d", Binary(Add, Col c, Col c))
            Transform.sortBy [ "d", Desc; "id", Asc ] ]
      yield
          "running total",
          [ Window
                { PartitionBy = [ "grp" ]
                  OrderBy = [ "id", Asc ]
                  Fn = CumulSum
                  Of = c
                  As = "t" } ] ]

/// An answer as the bytes the law compares: the canonical wire string, or the refusal's text.
let private wire (r: Result<Table, EvalError>) : string =
    match r with
    | Ok t -> "ok " + ColumnCodec.encode (Embedded t)
    | Error e -> "error " + DataFrame.errorString e

/// The first row dropped, then the pipeline: over `v` this reads the vector, over `w` the text path.
let private run (case: Case) (pipeline: Transform list) : string =
    DataFrame.evalPipeline (Filter(Binary(Gt, Col "id", Lit(Int 0))) :: pipeline) case.Table
    |> wire

/// Every disagreement for `seed`, named; an empty list is a pass. Also answers how many pipelines
/// it compared, so a caller can tell a pass from a law that ran nothing.
let check (seed: int) : string list * int =
    let case = generate seed
    let overV = pipelines case "v"
    let overW = pipelines case "w"

    let failures =
        List.zip overV overW
        |> List.choose (fun ((name, pv), (_, pw)) ->
            let a = run case pv
            let b = run case pw

            if a = b then
                None
            else
                Some("seed " + string seed + ", " + name + ": vector " + a + " / text " + b))

    failures, overV.Length

/// The law over `seeds` seeds from `first`: every disagreement, and the pipelines compared.
let checkAll (first: int) (seeds: int) : string list * int =
    [ for s in first .. first + seeds - 1 -> check s ]
    |> List.fold (fun (fs, n) (f, k) -> fs @ f, n + k) ([], 0)
