// Phase 345 evidence — exactly-rounded float summation (CPython's `math.fsum` algorithm, Shewchuk's
// non-overlapping partials with the correctly-rounded final collapse) against the left-to-right fold.
// Generates the inputs, writes them as raw little-endian doubles for the node and Python legs, times
// both sums, and checks permutation / partition invariance and the parallel member's bytes.
module FsumBench

open System
open System.IO
open System.Diagnostics
open System.Threading.Tasks

/// CPython `math.fsum` (Modules/mathmodule.c, `math_fsum_impl`), finite inputs.
let fsumPartials (xs: float[]) (lo0: int) (hi0: int) (partials: ResizeArray<float>) =
    for k in lo0 .. hi0 - 1 do
        let mutable x = xs[k]
        let mutable i = 0

        for j in 0 .. partials.Count - 1 do
            let mutable y = partials[j]

            if abs x < abs y then
                let t = x
                x <- y
                y <- t

            let hi = x + y
            let lo = y - (hi - x)

            if lo <> 0.0 then
                partials[i] <- lo
                i <- i + 1

            x <- hi

        partials.RemoveRange(i, partials.Count - i)
        partials.Add x

let collapse (partials: ResizeArray<float>) : float =
    let mutable hi = 0.0

    if partials.Count > 0 then
        let mutable n = partials.Count
        n <- n - 1
        hi <- partials[n]
        let mutable lo = 0.0
        let mutable fin = false

        while not fin && n > 0 do
            let x = hi
            n <- n - 1
            let y = partials[n]
            hi <- x + y
            let yr = hi - x
            lo <- y - yr

            if lo <> 0.0 then
                fin <- true

        if
            n > 0
            && ((lo < 0.0 && partials[n - 1] < 0.0) || (lo > 0.0 && partials[n - 1] > 0.0))
        then
            let y = lo * 2.0
            let x = hi + y
            let yr = x - hi

            if y = yr then
                hi <- x

    hi

let fsum (xs: float[]) =
    let p = ResizeArray<float>(32)
    fsumPartials xs 0 xs.Length p
    collapse p

let fold (xs: float[]) =
    let mutable t = 0.0

    for i in 0 .. xs.Length - 1 do
        t <- t + xs[i]

    t

/// The parallel member: each range's partials are that range's exact sum; the partials of every
/// range fed through the same algorithm give the correctly rounded total.
let fsumParallel (xs: float[]) (ranges: int) =
    let n = xs.Length
    let step = (n + ranges - 1) / ranges
    let parts = Array.init ranges (fun _ -> ResizeArray<float>(32))

    Parallel.For(
        0,
        ranges,
        fun r ->
            let lo = r * step
            let hi = min n (lo + step)
            fsumPartials xs lo hi parts[r]
    )
    |> ignore

    let all = Array.concat (parts |> Array.map (fun p -> p.ToArray()))
    fsum all

// xorshift64* — the same stream in every leg.
type Rng(seed: uint64) =
    let mutable s = seed

    member _.Next() =
        s <- s ^^^ (s >>> 12)
        s <- s ^^^ (s <<< 25)
        s <- s ^^^ (s >>> 27)
        s * 2685821657736338717UL

    member r.Unit() =
        float (r.Next() >>> 11) * (1.0 / 9007199254740992.0)

let dataset (kind: string) (n: int) =
    let rng = Rng(0x9E3779B97F4A7C15UL + uint64 n)

    Array.init n (fun _ ->
        match kind with
        | "quarters" -> floor (rng.Unit() * 4000.0) / 4.0
        | "uniform" -> rng.Unit()
        | "wide" ->
            let sign = if rng.Next() &&& 1UL = 0UL then 1.0 else -1.0
            let exp = rng.Unit() * 16.0 - 8.0
            sign * Math.Pow(10.0, exp) * rng.Unit()
        | k -> failwithf "unknown dataset %s" k)

let median (xs: float[]) =
    let s = Array.sort xs
    s[s.Length / 2]

let time reps (f: unit -> float) =
    let mutable sink = 0.0

    for _ in 1..3 do
        sink <- sink + f ()

    let ts =
        Array.init reps (fun _ ->
            let sw = Stopwatch.StartNew()
            sink <- sink + f ()
            sw.Elapsed.TotalMilliseconds)

    GC.KeepAlive sink
    median ts

let shuffle (rng: Rng) (xs: float[]) =
    let a = Array.copy xs

    for i in a.Length - 1 .. -1 .. 1 do
        let j = int (rng.Next() % uint64 (i + 1))
        let t = a[i]
        a[i] <- a[j]
        a[j] <- t

    a

[<EntryPoint>]
let main argv =
    let outDir = argv[0]
    Directory.CreateDirectory outDir |> ignore

    printfn
        "arch=%A cores=%d runtime=%s"
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
        Environment.ProcessorCount
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription

    printfn ""

    printfn
        "| dataset | n | fold ms | fsum ms | fsum/fold | fsum 8-way ms | 8-way/fold | partials (max) | fold bits == fsum bits |"

    printfn "|---|---|---|---|---|---|---|---|---|"

    let results = ResizeArray<string>()

    for kind in [ "quarters"; "uniform"; "wide" ] do
        for n in [ 10_000; 100_000; 1_000_000 ] do
            let xs = dataset kind n

            let reps =
                if n = 1_000_000 then 15
                elif n = 100_000 then 51
                else 201

            let raw = Array.zeroCreate<byte> (n * 8)
            Buffer.BlockCopy(xs, 0, raw, 0, raw.Length)
            File.WriteAllBytes(Path.Combine(outDir, sprintf "%s-%d.f64" kind n), raw)
            let tFold = time reps (fun () -> fold xs)
            let tFsum = time reps (fun () -> fsum xs)
            let tPar = time reps (fun () -> fsumParallel xs 8)
            let vFold = fold xs
            let vFsum = fsum xs
            let vPar = fsumParallel xs 8
            // invariance: permutation, and partition into 3 / 5 / 8 / 64 ranges
            let rng = Rng(7UL)
            let perms = [ for _ in 1..5 -> fsum (shuffle rng xs) ]
            let parts = [ for r in [ 3; 5; 8; 64; 1000 ] -> fsumParallel xs r ]
            let bits (v: float) = BitConverter.DoubleToInt64Bits v

            let invariant =
                (perms @ parts @ [ vPar ]) |> List.forall (fun v -> bits v = bits vFsum)

            let foldPerm = fold (shuffle (Rng(11UL)) xs)
            let p = ResizeArray<float>()
            fsumPartials xs 0 n p
            let same = bits vFold = bits vFsum

            printfn
                "| %s | %d | %.3f | %.3f | %.1fx | %.3f | %.2fx | %d | %b |"
                kind
                n
                tFold
                tFsum
                (tFsum / tFold)
                tPar
                (tPar / tFold)
                p.Count
                same

            results.Add(
                sprintf
                    "%s %d fsum=%016x fold=%016x foldShuffled=%016x invariant=%b"
                    kind
                    n
                    (bits vFsum)
                    (bits vFold)
                    (bits foldPerm)
                    invariant
            )

            if not invariant then
                failwithf "NOT invariant: %s %d" kind n

    printfn ""

    for r in results do
        printfn "%s" r

    File.WriteAllLines(Path.Combine(outDir, "dotnet-results.txt"), results)
    0
