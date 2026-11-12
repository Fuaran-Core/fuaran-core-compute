/// Phase 637 — the cost of calling the matrix kernels across the package boundary (DECISIONS.md D75).
///
/// Phase 632 moved `Glm` and least squares into `Fuaran.Compute.Regression`, and `Kernels.canonical` and
/// `Kernels.lend` lost `inline` because F# cannot inline an `internal` value into another assembly. This
/// times the paths that call them, so a build of the shipped tree and a build of the same tree with both
/// kernels inlined at Regression's call sites (a scratch copy, never committed) are compared on one table:
///
///   - `solve`: `LeastSquares.solve` over a dense design (the OLS path, called directly);
///   - `glm`: `Glm.fit` for a Poisson response under the log link (IRLS, called directly);
///   - `Fit ols`, `Fit poisson`, `Predict ols`: the DataFrame verbs over a table, which add the evaluator.
///
/// Every input is built before the clock starts. A case is warmed by a fixed number of calls, then `runs`
/// samples of a fixed number of calls each are timed; the row states their median and their minimum and
/// maximum, in microseconds per call. Each row is also printed as a `BOUNDARY|case|rows|median|min|max`
/// line for the script that aggregates several processes. `node Program.js <runs> boundary`, or the same
/// program on .NET.
module Fuaran.Core.Compute.Benchmarks.BoundaryBench

open System
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.LinearAlgebra
open Fuaran.Compute.Regression

/// The wall clock in milliseconds (`DateTime`, for the reason the harness gives).
let private nowMs () : float = float DateTime.UtcNow.Ticks / 10_000.0

/// A design cell: a fixed integer pattern scaled into `[−0.5, 0.5)`, the same on every host.
let private cell (i: int) (k: int) : float =
    float (((i % 1009) * 7919 + k * 104729 + 13) % 1009) / 1009.0 - 0.5

/// Predictors per design (an intercept comes with each fit).
let private width = 4

/// A linear response with a patterned residual.
let private gaussian (i: int) : float =
    1.0 + 2.0 * cell i 0 - 1.5 * cell i 1
    + 0.5 * cell i 2
    + float ((i * 31 + 7) % 101) / 1010.0

/// A count whose log mean is linear in the first two predictors.
let private count (i: int) : float =
    floor (exp (0.6 + 0.8 * cell i 0 - 0.5 * cell i 1) + float ((i * 17 + 3) % 5) / 5.0)

let private table (n: int) : Table =
    let col (name: string) (cells: Cell list) =
        match Column.ofCells name FloatType cells with
        | Ok c -> c
        | Error e -> failwithf "%s: %A" name e

    let names = [ for k in 0 .. width - 1 -> "x" + string k ]

    { Schema =
        [ for nm in names -> Field.create nm FloatType ]
        @ [ Field.create "y" FloatType; Field.create "c" FloatType ]
      Columns =
        [ for k in 0 .. width - 1 -> col names[k] [ for i in 0 .. n - 1 -> Float(cell i k) ] ]
        @ [ col "y" [ for i in 0 .. n - 1 -> Float(gaussian i) ]
            col "c" [ for i in 0 .. n - 1 -> Float(count i) ] ] }

let private terms =
    [ for k in 0 .. width - 1 -> "x" + string k, Col("x" + string k) ]

let private poissonSpec: FitSpec =
    { FitSpec.ols "c" terms with
        Family = FitFamily.Poisson
        Link = FitLink.Log }

let private expectOk (what: string) =
    function
    | Ok _ -> ()
    | Error e -> failwithf "benchmark boundary: %s was refused: %A" what e

/// Warm-up calls, calls per sample: fewer calls per sample as a call grows, so a sample spans tens of
/// milliseconds at least; both are fixed counts, never read from the clock.
let private schedule (n: int) : int * int =
    if n <= 100 then 2000, 1000
    elif n <= 10_000 then 30, 20
    else 5, 1

/// The median, minimum and maximum of `runs` samples, in microseconds per call.
let private measure (runs: int) (n: int) (f: unit -> unit) : float * float * float =
    let warm, calls = schedule n

    for _ in 1..warm do
        f ()

    let samples =
        Array.init runs (fun _ ->
            let start = nowMs ()

            for _ in 1..calls do
                f ()

            (nowMs () - start) * 1000.0 / float calls)

    Array.sortInPlace samples
    samples[runs / 2], samples[0], samples[runs - 1]

let run (runs: int) : unit =
    printfn ""
    printfn "### Calling the kernels across the package boundary (Phase 637): microseconds per call"
    printfn ""
    printfn "| case | rows | median us | min us | max us |"
    printfn "|---|---:|---:|---:|---:|"

    for n in [ 100; 10_000; 100_000 ] do
        let x = Matrix.init n (width + 1) (fun i j -> if j = 0 then 1.0 else cell i (j - 1))
        let y = Vector.adopt (Array.init n gaussian)
        let c = Vector.adopt (Array.init n count)
        let ones = Vector.adopt (Array.create n 1.0)
        let zeros = Vector.adopt (Array.zeroCreate n)
        let t = table n

        let cases: (string * (unit -> unit)) list =
            [ "solve", (fun () -> LeastSquares.solve x y |> expectOk "solve")
              "glm poisson",
              (fun () ->
                  Glm.fit GlmFamily.Poisson GlmLink.Log GlmControl.defaults x c ones zeros
                  |> expectOk "glm")
              "Fit ols", (fun () -> DataFrame.evalPipeline [ Fit(FitSpec.ols "y" terms) ] t |> expectOk "Fit ols")
              "Fit poisson", (fun () -> DataFrame.evalPipeline [ Fit poissonSpec ] t |> expectOk "Fit poisson")
              "Predict ols",
              (fun () ->
                  DataFrame.evalPipeline [ Predict(PredictSpec.ofTraining (FitSpec.ols "y" terms) (Embedded t)) ] t
                  |> expectOk "Predict ols") ]

        for name, f in cases do
            let median, lo, hi = measure runs n f
            printfn "| %s | %d | %.1f | %.1f | %.1f |" name n median lo hi
            printfn "BOUNDARY|%s|%d|%.3f|%.3f|%.3f" name n median lo hi
