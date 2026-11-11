/// Phase 591 — the streaming Givens QR timed at the plan's target sizes: n ∈ {10⁵, 10⁶} rows by
/// p ∈ {4, 16, 64} columns, the rotation's portable member against its native (SIMD) one. The rows are
/// generated as they are folded, never stored, so the figure is the kernel's and not a matrix's
/// allocation — which is also how the accumulator is meant to be fed (O(p²) state, rows streamed).
///
/// Compiled into the node harness (`node Program.js <runs> qr [maxRows]`, or
/// `dotnet run -c Release --project benchmarks/Fuaran.Core.Compute.Benchmarks/Node -- <runs> qr`).
/// Under Fable the native member IS the portable one, so node prints one arm.
module Fuaran.Core.Compute.Benchmarks.QrBench

open System
open Fuaran.Compute.LinearAlgebra
open Fuaran.Compute.Regression

/// The wall clock in milliseconds (`DateTime`, for the reason the harness gives).
let private nowMs () : float = float DateTime.UtcNow.Ticks / 10_000.0

/// A cell of the generated design: a fixed integer pattern scaled into `[−0.5, 0.5)`, the same on
/// every host, and dense — no cell is zero often enough to skip a rotation.
let private cell (i: int) (k: int) : float =
    float (((i % 1009) * 7919 + k * 104729 + 13) % 1009) / 1009.0 - 0.5

let private response (i: int) : float = float ((i * 31 + 7) % 101) / 101.0

let private fold rotate (n: int) (p: int) : QRAccumulator =
    match QRAccumulator.foldRowsWith rotate n cell response (fun _ -> 1.0) (QRAccumulator.empty p) with
    | Ok acc -> acc
    | Error e -> failwithf "the benchmark design was refused: %A" e

/// Floating-point operations per row: `p(p + 1)/2` rotated pairs at four products and two sums each,
/// plus per column the radius (two quotients, two squares, two sums, a root, a product) and `c`, `s`.
let private flopsPerRow (p: int) : float =
    3.0 * float (p * (p + 1)) + 10.0 * float p

let private stateBits (acc: QRAccumulator) : int64[] =
    Array.append (Matrix.toColumnMajor (QRAccumulator.r acc)) (Fuaran.Core.Vector.toArray (QRAccumulator.qty acc))
    |> Array.map BitConverter.DoubleToInt64Bits

/// Time `runs` folds (one when the case is a million rows) after a warm-up at 10⁴ rows; the median.
let private time (runs: int) (rotate) (n: int) (p: int) : float =
    fold rotate 10_000 p |> ignore
    let runs = if n >= 1_000_000 then 1 else runs

    let samples =
        Array.init runs (fun _ ->
            let start = nowMs ()
            fold rotate n p |> ignore
            nowMs () - start)

    Array.sortInPlace samples
    samples[runs / 2]

let run (runs: int) (maxRows: int) : unit =
    let native = Kernels.rotateNative
    let portable = Kernels.rotatePortable
#if FABLE_COMPILER
    let arms = [ "portable (= native under Fable)", portable ]
#else
    let arms = [ "portable", portable; "native", native ]
#endif
    printfn ""
    printfn "### Streaming Givens QR (Phase 591): ms per fold, ns per row, GFLOP/s"
    printfn ""
    printfn "| rows | cols | arm | ms | ns/row | GFLOP/s |"
    printfn "|---:|---:|---|---:|---:|---:|"

    for p in [ 4; 16; 64 ] do
        // The arms agree to the bit before either is timed.
        if stateBits (fold native 2_000 p) <> stateBits (fold portable 2_000 p) then
            failwithf "the native rotation disagrees with the portable one at width %d" p

        for n in [ 100_000; 1_000_000 ] do
            if n <= maxRows then
                for name, rotate in arms do
                    let ms = time runs rotate n p
                    let nsRow = ms * 1e6 / float n
                    let gflops = flopsPerRow p * float n / (ms * 1e6)
                    printfn "| %d | %d | %s | %.1f | %.1f | %.2f |" n p name ms nsRow gflops
