/// Phase 375 — the step-as-data laws on this host, and what the hand-off costs on one thread.
///
///   node Program.js [runs]      (after `dotnet fable handoff.fsproj -o <out>`)
///   dotnet run -c Release -- [runs]
///
/// First the laws (`MorselHandOffLaw`): every honest runner, and one that sends every morsel's
/// hand-off through a structured clone and its results back through another (the copies `postMessage`
/// makes; on .NET there is no clone and the record is passed), answers what the sequential member
/// answers over the transform vectors at 1, 2, 3 and 8,192 rows a morsel and over the corpus at
/// 20,000 rows; the two perturbed runners go red. Exit 1 if any of that fails.
///
/// Then the costs, medians of `runs` (default 11) after two warm-ups, for the two steps Layer 6 and
/// Phase 346 time — `filter` (`a + b > 500` over `rowTable`) and `derive` (`amount = qty * price`
/// over `orders`) — at 10,000, 100,000 and 1,000,000 rows: the sequential member; the hand-off's plan
/// (the step and environment through the wire, the masks to bytes); its rebuild (what a runner pays
/// before its first morsel: the wire decoded, the masks back to the evaluator's arrays); its morsels
/// run on one thread; and its finish.
module Fuaran.Compute.Tests.HandOffNode

open System.Diagnostics
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests.MorselHandOffLaw

#if FABLE_COMPILER
/// The copy `postMessage` makes of what it is given.
[<Fable.Core.Emit("structuredClone($0)")>]
let private clone (h: MorselHandOff) : MorselHandOff = Fable.Core.Util.jsNative

let private host = "node (Fable)"

/// The host's high-resolution clock, in milliseconds.
[<Fable.Core.Emit("performance.now()")>]
let private nowMs () : float = Fable.Core.Util.jsNative
#else
let private clone (h: MorselHandOff) : MorselHandOff = h

let private host = ".NET"

let private nowMs () : float =
    float (Stopwatch.GetTimestamp()) * 1000.0 / float Stopwatch.Frequency
#endif

/// Each morsel as a worker would take it: the record narrowed to the morsel and cloned in, run on
/// the clone, and the clone's slots cloned back and copied into the caller's for the morsel's rows.
let private cloned (h: MorselHandOff) : unit =
    let n = h.Rows.Length

    for j in 0 .. MorselHandOff.morselCount h - 1 do
        let inbound = clone (range h j (j + 1))
        MorselHandOff.run inbound
        let back = clone inbound
        let lo = j * h.MorselRows
        let hi = min n (lo + h.MorselRows)
        h.OutFailed[j] <- back.OutFailed[j]

        if h.Root = 0 then
            h.OutCounts[j] <- back.OutCounts[j]
            Array.blit back.OutRows lo h.OutRows lo (hi - lo)
        else
            for i in lo .. hi - 1 do
                let p = h.Rows[i]
                h.OutMask[p] <- back.OutMask[p]

                match h.Root with
                | 1 -> h.OutInts[p] <- back.OutInts[p]
                | 2 -> h.OutFloats[p] <- back.OutFloats[p]
                | _ -> h.OutBools[p] <- back.OutBools[p]

let private laws () : bool =
    let vectors = vectorCases ()
    let corpus = corpusCases 20_000
    let runners = honest @ [ "cloned", cloned ]
    let mutable ok = true

    for name, runner in runners do
        for morselRows in [ 1; 2; 3; Kernels.MorselRows ] do
            let failures, handed = disagreements runner morselRows vectors

            printfn
                "law: %-9s vectors at %5d rows a morsel: %d handed off, %d disagree"
                name
                morselRows
                handed
                failures.Length

            if handed = 0 || not failures.IsEmpty then
                ok <- false
                failures |> List.truncate 3 |> List.iter (printfn "  %s")

        let failures, handed = disagreements runner Kernels.MorselRows corpus

        printfn
            "law: %-9s corpus at 20,000 rows: %d of %d handed off, %d disagree"
            name
            handed
            corpus.Length
            failures.Length

        if handed <> corpus.Length || not failures.IsEmpty then
            ok <- false
            failures |> List.truncate 3 |> List.iter (printfn "  %s")

    let answered = corpus |> List.filter (refuses >> not)

    for name, runner in [ "shifted", shifted; "swapped", swappedPairs ] do
        let failures, _ = disagreements runner Kernels.MorselRows answered
        printfn "law: %-9s (perturbed) corpus: %d of %d disagree" name failures.Length answered.Length

        if failures.IsEmpty then
            ok <- false

    ok

let private median (xs: float list) : float =
    let a = xs |> List.sort |> List.toArray
    a[a.Length / 2]

let private time (runs: int) (f: unit -> 'a) : float =
    for _ in 1..2 do
        f () |> ignore

    median
        [ for _ in 1..runs do
              let t0 = nowMs ()
              f () |> ignore
              nowMs () - t0 ]

let private costs (runs: int) =
    printfn ""
    printfn "| step | rows | sequential | plan | rebuild | morsels, one thread | finish |"
    printfn "|---|---|---|---|---|---|---|"

    for n in [ 10_000; 100_000; 1_000_000 ] do
        for name, env, table, step in
            [ "filter", Map.empty, rowTable n, Filter(Binary(Gt, Binary(Add, Col "a", Col "b"), Lit(Int 500)))
              "derive", sheetEnv, orders n false, List.head lines ] do
            let f = Frame.ofTable table

            let sequential =
                time runs (fun () -> DataFrame.evalStepWith Kernels.oneThread DataFrame.noResolve env f step)

            let plan = time runs (fun () -> MorselHandOff.plan env f step)
            let h = (MorselHandOff.plan env f step).Value
            let rebuild = time runs (fun () -> MorselHandOff.rebuild h)
            let run = time runs (fun () -> MorselHandOff.run h)
            let finish = time runs (fun () -> MorselHandOff.finish env f step h)

            printfn
                "| %s | %d | %.2f ms | %.2f ms | %.2f ms | %.2f ms | %.2f ms |"
                name
                n
                sequential
                plan
                rebuild
                run
                finish

[<EntryPoint>]
let main argv =
    let runs =
        match argv |> Array.tryHead with
        | Some a -> int a
        | None -> 11

    printfn "Phase 375 hand-off harness on %s, %d measured runs a case" host runs

    if not (laws ()) then
        printfn "law: FAILED"
        1
    else
        costs runs
        0
