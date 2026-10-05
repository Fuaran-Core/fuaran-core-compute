/// Phase 346 prototype — the sequential member as it ships: the Fable-compiled evaluator, under node,
/// over the two row-local cases the worker prototype carries (Layer 6's `compiled` Filter and the
/// sheet's `lines` Derives), at the prototype's sizes. Under Fable the host kernels ARE the portable
/// member, so `DataFrame.evalPrepared` runs the morsels one after another on node's one thread.
/// Each case ends in `Limit 10`, as Layer 6's do, so the `Table` boundary out costs the same at every
/// size. Before timing, the Filter without its `Limit` is counted against the prototype's kept rows
/// (the same columns: `a = i * 7919 % 1000`, `b = i * 104729 % 1000`, the products taken in int64 —
/// Layer 6's int32 products wrap on .NET and not under Fable, so its `b` differs between the hosts
/// from 20,506 rows on).
module Phase346Seq

open Fuaran.Core
open Fuaran.Compute

let private col (name: string) (ty: ColumnType) (cells: Cell list) : Column = Column.create name ty cells

let rowTable (n: int) : Table =
    { Schema = [ "a", IntType; "b", IntType; "x", FloatType ]
      Columns =
        [ col "a" IntType [ for i in 0 .. n - 1 -> Int(int ((int64 i * 7919L) % 1000L)) ]
          col "b" IntType [ for i in 0 .. n - 1 -> Int(int ((int64 i * 104729L) % 1000L)) ]
          col "x" FloatType [ for i in 0 .. n - 1 -> Float(float ((i * 31) % 1000) * 0.25) ] ] }

let ordersTable (n: int) : Table =
    let regions = [| "north"; "south"; "east"; "west"; "central" |]

    { Schema = [ "id", IntType; "region", StringType; "qty", IntType; "price", FloatType ]
      Columns =
        [ col "id" IntType [ for i in 0 .. n - 1 -> Int i ]
          col "region" StringType [ for i in 0 .. n - 1 -> Str regions.[(i * 3 + i / 7) % regions.Length] ]
          col "qty" IntType [ for i in 0 .. n - 1 -> Int(1 + (i * 7 + i / 3) % 20) ]
          col "price" FloatType [ for i in 0 .. n - 1 -> Float(float (4 + (i * 13) % 397) * 0.25) ] ] }

let env: Map<string, Cell> = Map.ofList [ "threshold", Float 500.0 ]
let limit10 = Transform.limit 10 0
let compiledFilter = Filter(Binary(Gt, Binary(Add, Col "a", Col "b"), Lit(Int 500)))

let linesPipeline =
    [ Derive("amount", Binary(Mul, Col "qty", Col "price"))
      Derive("big", Binary(Ge, Col "amount", Param "threshold")) ]

let orFail (r: Result<Table, EvalError>) : Table =
    match r with
    | Ok t -> t
    | Error e -> failwithf "phase 346 seq: %A" e

/// The wall clock in milliseconds: `DateTime`, as the node harness does (Fable maps no `Stopwatch`).
let nowMs () : float =
    float System.DateTime.UtcNow.Ticks / 10_000.0

/// The node harness's sampling: three warm-ups; calls per sample doubled until a sample spans at
/// least 50 ms (the clock is a millisecond under node); `runs` samples; the median per call.
let time (runs: int) (f: unit -> Table) : float =
    for _ in 1..3 do
        f () |> ignore

    let sample (calls: int) =
        let start = nowMs ()

        for _ in 1..calls do
            f () |> ignore

        nowMs () - start

    let rec calibrate calls =
        if calls >= 1_000_000 || sample calls >= 50.0 then
            calls
        else
            calibrate (calls * 2)

    let calls = calibrate 1
    let times = Array.init runs (fun _ -> sample calls / float calls)
    Array.sortInPlace times
    times.[runs / 2]

/// Which host this run is: under Fable the portable member, on .NET the native one.
let hostName =
#if FABLE_COMPILER
    "fable/node"
#else
    ".NET"
#endif

[<EntryPoint>]
let main _ =
    for n in [ 10_000; 100_000; 1_000_000 ] do
        let reps = 11
        let rows = DataFrame.prepare (rowTable n)
        let orders = DataFrame.prepare (ordersTable n)

        let kept =
            DataFrame.evalPrepared DataFrame.noResolve env [ compiledFilter ] rows
            |> orFail
            |> Table.rowCount

        let filterMs =
            time reps (fun () ->
                DataFrame.evalPrepared DataFrame.noResolve env [ compiledFilter; limit10 ] rows
                |> orFail)

        let deriveMs =
            time reps (fun () ->
                DataFrame.evalPrepared DataFrame.noResolve env (linesPipeline @ [ limit10 ]) orders
                |> orFail)

        printfn
            "{\"host\":\"%s\",\"step\":\"filter\",\"rows\":%d,\"arm\":\"evaluator\",\"threads\":1,\"ms\":%.4f,\"kept\":%d}"
            hostName
            n
            filterMs
            kept

        printfn
            "{\"host\":\"%s\",\"step\":\"derive\",\"rows\":%d,\"arm\":\"evaluator\",\"threads\":1,\"ms\":%.4f}"
            hostName
            n
            deriveMs

    0
