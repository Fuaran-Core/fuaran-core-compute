/// Phase 375 — the laws of the step as data (`MorselHandOff`), as plain functions: the runners, the
/// cases and the comparison. Every step a runner takes answers what the sequential member
/// (`Kernels.oneThread`) answers, byte for byte as canonical wire (refusals included), over the
/// transform vectors' sample (`PreparedResultLaw.sample`, at several morsel sizes so the small
/// sources split) and over the benchmark corpus's row-local pipelines (the sheet's `lines` and its
/// chain, `Corpus.fs`; Layer 6's compiled `Filter`, `Layer6.fs`) at the evaluator's own morsel size,
/// whichever order the runner runs the morsels in. A perturbed runner goes red: one whose morsels
/// after the first start a row late (a shifted boundary), and one whose kept rows are concatenated out
/// of morsel order.
///
/// FSharp.Core, the dataframe package and the conformance kit's generator only, Fable-clean: the
/// suite runs it on .NET (`MorselHandOffTests`, which adds a runner on the thread pool), and the node
/// harness beside `benchmarks/results/2026-10-06-i7-9700-phase-375.md` compiles it with Fable and
/// runs it under node, every hand-off through a structured clone.
module internal Fuaran.Compute.Tests.MorselHandOffLaw

open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

// ---- runners ---------------------------------------------------------------------------------

/// A copy of the hand-off narrowed to the morsels `first .. last - 1`: the record a pool hands each
/// worker. The slots are shared, as typed arrays over shared memory are.
let range (h: MorselHandOff) (first: int) (last: int) : MorselHandOff = { h with First = first; Last = last }

/// The whole range on the caller's thread, in order.
let inOrder (h: MorselHandOff) : unit = MorselHandOff.run h

/// One morsel at a time, last first, each through its own rebuild and compile.
let reversed (h: MorselHandOff) : unit =
    for j in MorselHandOff.morselCount h - 1 .. -1 .. 0 do
        MorselHandOff.run (range h j (j + 1))

/// PERTURBED: every morsel after the first starts one row late, so its first row is never run.
let shifted (h: MorselHandOff) : unit =
    let f, env, t = MorselHandOff.rebuild h
    let resolved = MorselHandOff.resolve f env t
    let n = h.Rows.Length

    for j in 0 .. MorselHandOff.morselCount h - 1 do
        let lo = j * h.MorselRows
        let hi = min n (lo + h.MorselRows)
        MorselHandOff.runSpan f resolved h j (if j = 0 then lo else min hi (lo + 1)) hi

/// PERTURBED: run in order, then hand the morsels' kept rows back in swapped pairs — morsel 0's
/// rows where morsel 1's belong and the reverse, then 2 and 3, … — among the full morsels, whose
/// slots are equally long. The caller then concatenates out of morsel order.
let swappedPairs (h: MorselHandOff) : unit =
    MorselHandOff.run h
    let m = MorselHandOff.morselCount h
    let r = h.MorselRows
    let full = if h.Rows.Length % r = 0 then m else m - 1

    if h.Root = 0 then
        for pair in 0 .. full / 2 - 1 do
            let a = 2 * pair
            let b = a + 1
            let blockA = Array.sub h.OutRows (a * r) h.OutCounts[a]
            let blockB = Array.sub h.OutRows (b * r) h.OutCounts[b]
            Array.blit blockB 0 h.OutRows (a * r) blockB.Length
            Array.blit blockA 0 h.OutRows (b * r) blockA.Length
            h.OutCounts[a] <- blockB.Length
            h.OutCounts[b] <- blockA.Length

/// The honest runners every host can run: no threads.
let honest: (string * (MorselHandOff -> unit)) list =
    [ "in order", inOrder; "reversed", reversed ]

// ---- the comparison --------------------------------------------------------------------------

/// An answer as the text the law compares: the frame's table as canonical wire, or the refusal.
let wire (r: Result<Frame, EvalError>) : string =
    match r with
    | Ok f -> "ok " + ColumnCodec.encode (Embedded(Frame.toTable f))
    | Error e -> "error " + DataFrame.errorString e

/// A row-local step whose sequential answer a runner is held to.
type Case =
    { Name: string
      Env: Map<string, Cell>
      Frame: Frame
      Step: Transform }

/// Every row-local step of `pipeline` over `table`, each over the frame the sequential member left
/// before it, up to the first step it refuses.
let casesOf (name: string) (env: Map<string, Cell>) (table: Table) (pipeline: Transform list) : Case list =
    let rec go (i: int) (f: Frame) (steps: Transform list) acc =
        match steps with
        | [] -> List.rev acc
        | t :: rest ->
            let acc =
                match t with
                | Filter _
                | Derive _ ->
                    { Name = sprintf "%s, step %d" name i
                      Env = env
                      Frame = f
                      Step = t }
                    :: acc
                | _ -> acc

            match DataFrame.evalStepWith Kernels.oneThread DataFrame.noResolve env f t with
            | Ok next -> go (i + 1) next rest acc
            | Error _ -> List.rev acc

    go 0 (Frame.ofTable table) pipeline []

/// The transform vectors' sample, step by step.
let vectorCases () : Case list =
    PreparedResultLaw.sample ()
    |> List.indexed
    |> List.collect (fun (i, (table, pipeline)) -> casesOf (sprintf "vector %d" i) Map.empty table pipeline)

// ---- the corpus (benchmarks/Fuaran.Core.Compute.Benchmarks/Corpus.fs, Layer6.fs) --------------

let private col (name: string) (ty: ColumnType) (cells: Cell list) : Column = KitColumn.create name ty cells

let private regions = [| "north"; "south"; "east"; "west"; "central" |]

/// `Corpus.ordersTable (Corpus.ordersArrays n)`: `id:int, region:string, qty:int, price:float`, with
/// a missing price every 97 rows. With `poison`, the quantity at two rows in two morsels is large
/// enough that doubling it overflows an int, for the cases that refuse.
let orders (n: int) (poison: bool) : Table =
    let qty (i: int) =
        if poison && (i = 9_000 || i = 17_000) then
            2_000_000_000 + i
        else
            1 + (i * 7 + i / 3) % 20

    let price (i: int) = float (4 + (i * 13) % 397) * 0.25

    { Schema =
        [ Field.create "id" IntType
          Field.create "region" StringType
          Field.create "qty" IntType
          Field.create "price" FloatType ]
      Columns =
        [ col "id" IntType [ for i in 0 .. n - 1 -> Int i ]
          col "region" StringType [ for i in 0 .. n - 1 -> Str regions.[(i * 3 + i / 7) % regions.Length] ]
          col "qty" IntType [ for i in 0 .. n - 1 -> Int(qty i) ]
          col "price" FloatType [ for i in 0 .. n - 1 -> (if i % 97 = 13 then Null else Float(price i)) ] ] }

/// `Layer6.rowTable n`: `a` and `b` ints over 0 .. 999 and `x` a float in quarters.
let rowTable (n: int) : Table =
    { Schema =
        [ Field.create "a" IntType
          Field.create "b" IntType
          Field.create "x" FloatType ]
      Columns =
        [ col "a" IntType [ for i in 0 .. n - 1 -> Int((i * 7919) % 1000) ]
          col "b" IntType [ for i in 0 .. n - 1 -> (if i % 89 = 5 then Null else Int((i * 104729) % 1000)) ]
          col "x" FloatType [ for i in 0 .. n - 1 -> Float(float ((i * 31) % 1000) * 0.25) ] ] }

let sheetEnv = Map.ofList [ "threshold", Float 500.0 ]

let lines =
    [ Derive("amount", Binary(Mul, Col "qty", Col "price"))
      Derive("big", Binary(Ge, Col "amount", Param "threshold")) ]

/// The corpus's row-local pipelines at `n` rows, whole: each its name, environment, source and steps.
let corpusPipelines (n: int) : (string * Map<string, Cell> * Table * Transform list) list =
    let o = orders n false
    let poisoned = orders n true

    [ "lines", sheetEnv, o, lines
      // The chain's second and third hops, over `lines`' answer.
      "chain",
      sheetEnv,
      o,
      lines
      @ [ Filter(Col "big")
          Derive("net", Binary(Mul, Col "amount", Lit(Float 0.8)))
          Derive("tax", Binary(Sub, Col "amount", Col "net"))
          Derive("units", Binary(Add, Col "qty", Col "id")) ]
      // Layer 6's compiled filter, and a filter over a selection.
      "compiled filter", Map.empty, rowTable n, [ Filter(Binary(Gt, Binary(Add, Col "a", Col "b"), Lit(Int 500))) ]
      "filter after filter",
      Map.empty,
      rowTable n,
      [ Filter(Binary(Gt, Binary(Add, Col "a", Col "b"), Lit(Int 500)))
        Filter(Binary(Lt, Binary(Mul, Col "a", Col "x"), Lit(Float 40000.0)))
        Derive("y", Binary(Add, Col "x", Col "a")) ]
      // Refusals in two morsels: the first in row order answers.
      "derive refuses", sheetEnv, poisoned, [ Derive("twice", Binary(Add, Col "qty", Col "qty")) ]
      "filter refuses", sheetEnv, poisoned, [ Filter(Binary(Gt, Binary(Add, Col "qty", Col "qty"), Lit(Int 0))) ] ]

/// The corpus's row-local pipelines at `n` rows, step by step.
let corpusCases (n: int) : Case list =
    corpusPipelines n
    |> List.collect (fun (name, env, table, pipeline) -> casesOf name env table pipeline)

/// The disagreements of `runner` with the sequential member over `cases` at `morselRows`, and how
/// many cases it was handed.
let disagreements (runner: MorselHandOff -> unit) (morselRows: int) (cases: Case list) : string list * int =
    let mutable handed = 0

    let failures =
        [ for c in cases do
              match MorselHandOff.evalWith runner morselRows c.Env c.Frame c.Step with
              | None -> ()
              | Some answer ->
                  handed <- handed + 1

                  let expected =
                      DataFrame.evalStepWith Kernels.oneThread DataFrame.noResolve c.Env c.Frame c.Step

                  if wire answer <> wire expected then
                      yield sprintf "%s at %d rows a morsel: %s / %s" c.Name morselRows (wire answer) (wire expected) ]

    failures, handed

let handedOff (morselRows: int) (cases: Case list) : Case list =
    cases
    |> List.filter (fun c -> Option.isSome (MorselHandOff.planAt morselRows c.Env c.Frame c.Step))

let isFilter (c: Case) =
    match c.Step with
    | Filter _ -> true
    | _ -> false

let refuses (c: Case) =
    match DataFrame.evalStepWith Kernels.oneThread DataFrame.noResolve c.Env c.Frame c.Step with
    | Error _ -> true
    | Ok _ -> false
