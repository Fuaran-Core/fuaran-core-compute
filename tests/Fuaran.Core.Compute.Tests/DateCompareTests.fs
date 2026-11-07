module Fuaran.Compute.Tests.DateCompareTests

// ---------------------------------------------------------------------------
//  A date comparison reads day counts instead of rendering text per row. The reference orders dates
//  by their text, and the two orders agree only for four-digit years, so the oracle here IS the
//  text: every row's answer is computed by rendering the day and comparing ordinally. The edges are
//  the point: days past the four-digit range, whose text sorts elsewhere than their count; constants
//  that are not canonical; absent rows; constants on either side.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Compute

let private day (s: string) : int = (TemporalText.tryDays s).Value

/// Ordinary days, the ends of the four-digit range, and days past both ends.
let private days: int[] =
    let first = day "0000-01-01"
    let last = day "9999-12-31"

    [| day "2026-04-01"
       day "2026-03-31"
       day "2026-10-01"
       day "1999-12-31"
       first
       first - 1
       first - 400
       last
       last + 1
       last + 400
       0 |]

/// The days inside the four-digit range only, where the filter kernel may answer.
let private inRange: int[] =
    [| day "2026-04-01"
       day "2026-03-31"
       day "2026-10-01"
       day "1999-12-31"
       day "0000-01-01"
       day "9999-12-31"
       0 |]

let private tableOf (ds: int[]) : Table =
    let n = ds.Length

    { Schema =
        [ Field.create "id" IntType
          Field.create "d" DateType
          Field.create "e" DateType ]
      Columns =
        [ Column.ofInts "id" (Vector.ofArray (Array.init n id)) AllValid
          Column.ofDates "d" (Vector.ofArray ds) (Validity.ofArray (Array.init n (fun i -> i % 5 <> 3)))
          Column.ofDates "e" (Vector.ofArray (Array.rev ds)) AllValid ] }

let private ops: (BinOp * (int -> bool)) list =
    [ Lt, (fun c -> c < 0)
      Le, (fun c -> c <= 0)
      Gt, (fun c -> c > 0)
      Ge, (fun c -> c >= 0)
      Eq, (fun c -> c = 0)
      Ne, (fun c -> c <> 0) ]

/// Canonical at both ends of the range and within it, then text with no day count.
let private constants: string list =
    [ "2026-04-01"
      "0000-01-01"
      "9999-12-31"
      "1970-01-01"
      "2026-4-1"
      "2026-02-30"
      "zzzz"
      "" ]

let private oracle (test: int -> bool) (x: string) (y: string) : bool =
    test (System.String.CompareOrdinal(x, y))

let private text (d: int) : string = TemporalText.dateText d

let private keptIds (t: Table) (pred: ColExpr) : int list =
    match DataFrame.evalPipeline [ Filter pred ] t with
    | Error e -> failtestf "the filter refused: %A" e
    | Ok out ->
        out.Columns
        |> List.find (fun c -> c.Name = "id")
        |> Column.toCells
        |> List.map (function
            | Int i -> i
            | c -> failtestf "an id read %A" c)

let private derived (t: Table) (expr: ColExpr) : Cell list =
    match DataFrame.evalPipeline [ Derive("x", expr) ] t with
    | Error e -> failtestf "the derive refused: %A" e
    | Ok out -> out.Columns |> List.find (fun c -> c.Name = "x") |> Column.toCells

let private filterAgreesWithText (ds: int[]) =
    let t = tableOf ds

    for op, test in ops do
        for k in constants do
            let present i = i % 5 <> 3

            let wantRight =
                [ for i in 0 .. ds.Length - 1 do
                      if present i && oracle test (text ds[i]) k then
                          i ]

            let wantLeft =
                [ for i in 0 .. ds.Length - 1 do
                      if present i && oracle test k (text ds[i]) then
                          i ]

            Expect.equal (keptIds t (Binary(op, Col "d", Lit(Date k)))) wantRight (sprintf "d %A %s" op k)
            Expect.equal (keptIds t (Binary(op, Lit(Date k), Col "d"))) wantLeft (sprintf "%s %A d" k op)

[<Tests>]
let tests =
    testList
        "date comparison on day counts"
        [ testCase "a filter against a constant answers what the text answers, past the four-digit range too"
          <| fun () -> filterAgreesWithText days

          testCase "a filter inside the four-digit range answers what the text answers"
          <| fun () -> filterAgreesWithText inRange

          testCase "a derive against a constant answers what the text answers, an absent row null"
          <| fun () ->
              let t = tableOf days

              for op, test in ops do
                  for k in constants do
                      let want =
                          [ for i in 0 .. days.Length - 1 ->
                                if i % 5 <> 3 then
                                    Bool(oracle test (text days[i]) k)
                                else
                                    Null ]

                      Expect.equal (derived t (Binary(op, Col "d", Lit(Date k)))) want (sprintf "%A %s" op k)

          testCase "two date columns answer what the text answers"
          <| fun () ->
              let t = tableOf days
              let e = Array.rev days

              for op, test in ops do
                  let want =
                      [ for i in 0 .. days.Length - 1 ->
                            if i % 5 <> 3 then
                                Bool(oracle test (text days[i]) (text e[i]))
                            else
                                Null ]

                  Expect.equal (derived t (Binary(op, Col "d", Col "e"))) want (sprintf "%A" op)

          testCase "a day past the four-digit range sorts by its text, not its count"
          <| fun () ->
              let beyond = day "9999-12-31" + 1
              Expect.equal (text beyond) "10000-01-01" "the premise: five digits"

              let t =
                  { Schema = [ Field.create "d" DateType ]
                    Columns = [ Column.ofDates "d" (Vector.ofArray [| beyond |]) AllValid ] }

              Expect.equal
                  (derived t (Binary(Lt, Col "d", Lit(Date "2026-01-01"))))
                  [ Bool true ]
                  "ordinally, 10000-01-01 precedes 2026-01-01; by count it follows"

          testCase "the filter kernel takes a canonical constant over a four-digit vector, and declines the rest"
          <| fun () ->
              let takes (ds: int[]) (k: string) =
                  let f = Frame.ofTable (tableOf ds)

                  DataFrame.filterBits
                      Kernels.host
                      f
                      (DataFrame.resolveExpr Map.empty f.Cols (Binary(Lt, Col "d", Lit(Date k))))
                  |> Option.isSome

              Expect.isTrue (takes inRange "2026-04-01") "canonical, in range"
              Expect.isFalse (takes days "2026-04-01") "a day past the range"
              Expect.isFalse (takes inRange "2026-4-1") "a constant with no day count"

          testCase "the compiled comparison renders no text for a row inside the range"
          <| fun () ->
              let n = 10_000
              let first = day "2026-01-01"

              let t =
                  { Schema = [ Field.create "d" DateType ]
                    Columns = [ Column.ofDates "d" (Vector.ofArray (Array.init n (fun i -> first + i % 365))) AllValid ] }

              let f = Frame.ofTable t

              let resolved =
                  DataFrame.resolveExpr Map.empty f.Cols (Binary(Ge, Col "d", Lit(Date "2026-04-01")))

              match (DataFrame.compileExpr f resolved).Node with
              | DataFrame.NBool r ->
                  // Run once so the JIT's own allocation is outside the measure.
                  for p in 0 .. n - 1 do
                      r p |> ignore

                  let before = System.GC.GetAllocatedBytesForCurrentThread()

                  for p in 0 .. n - 1 do
                      r p |> ignore

                  let bytes = System.GC.GetAllocatedBytesForCurrentThread() - before
                  Expect.isLessThan bytes (int64 n) "under a byte a row; a rendered date is dozens"
              | other -> failtestf "the comparison compiled to %A, not a typed boolean" other ]
