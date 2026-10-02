module Fuaran.Compute.Tests.Program

open Expecto

/// Where an exporter writes: the directory named after the flag, else this repository's
/// committed `conformance/`. A following flag is not a directory.
let private emitTarget (rest: string list) : string =
    match rest with
    | dir :: _ when not (dir.StartsWith "--") -> dir
    | _ -> OwnedConformance.root ()

/// The clock leg's runner (Phase 282): `ScalingTests.clockTests`, alone, then the count check.
/// Phase 285: the calibration baseline first, a per-case summary after, and a distinct exit code when
/// the machine stayed too busy for a verdict.
module private ClockLeg =
    open System.Diagnostics
    open ScalingTests

    /// The leg's exit code when a case ended saturated and nothing was red: "machine saturated, no
    /// verdict". Distinct from Expecto's 1 (a red or an error) and the leg's own 2 (cases missing).
    let saturatedExit = 3

    let private raisePriority () =
        // Above-normal priority, so ordinary work elsewhere on the machine yields to the leg for
        // the seconds it runs. Refused on a host that does not let a process raise itself; the leg
        // still runs, on the three-attempt rule and the saturation guard alone, and says so.
        try
            Process.GetCurrentProcess().PriorityClass <- ProcessPriorityClass.AboveNormal
            printfn "==== clock leg: process priority AboveNormal"
        with e ->
            printfn "==== clock leg: process priority unchanged (%s)" e.Message

    let run (rest: string list) : int =
        raisePriority ()

        printfn
            "==== clock leg: calibration baseline %.3f ms (best of %d readings; saturated = a reading above %.2fx it, or readings disagreeing by more than %.2fx)"
            (clockBaselineMs ())
            Calibration.baselineRuns
            Calibration.k
            Calibration.k

        let code = runTestsWithCLIArgs [] (Array.ofList rest) clockTests
        let ran = clockCasesRun ()
        let expected = clockInventory
        let outcomes = clockOutcomes ()

        for o in outcomes do
            printfn
                "==== clock leg: %-8s %d counted, %d discarded as saturated - %s"
                (match o.Verdict with
                 | ClockGreen -> "GREEN"
                 | ClockRed -> "RED"
                 | ClockSaturated -> "NO-VERDICT"
                 | ClockErrored -> "ERROR")
                o.Counted
                o.Discarded
                o.Name

        printfn
            "==== clock leg: calibration baseline %.3f ms at leg end; %d window(s) discarded as saturated in all"
            (clockBaselineMs ())
            (outcomes |> List.sumBy (fun o -> o.Discarded))

        let verdictFailed =
            outcomes
            |> List.exists (fun o -> o.Verdict = ClockRed || o.Verdict = ClockErrored)

        let saturated = outcomes |> List.filter (fun o -> o.Verdict = ClockSaturated)

        if ran < expected then
            eprintfn
                "==== clock leg: %d of %d clock cases ran — a leg that ran fewer than its inventory proves nothing about the rest"
                ran
                expected

            if code <> 0 then code else 2
        elif not verdictFailed && not saturated.IsEmpty then
            // Never green, never red: no unsaturated verdict was reached for these cases.
            eprintfn
                "==== clock leg: machine saturated, no verdict — %d case(s) found no unsaturated window within the budget (exit %d); this is not a timing red: re-run when the machine is quieter"
                saturated.Length
                saturatedExit

            saturatedExit
        else
            printfn "==== clock leg: %d of %d clock cases ran (exit %d)" ran expected code
            code

    /// The calibration probe (Phase 285): `n` readings of the workload, one per line, with the
    /// baseline. How k was measured — run it quiet and under load and compare the distributions.
    let probe (rest: string list) : int =
        raisePriority ()

        let n =
            match rest with
            | s :: _ ->
                match System.Int32.TryParse s with
                | true, v when v > 0 -> v
                | _ -> 60
            | [] -> 60

        let b = clockBaselineMs ()
        printfn "==== clock calibration: baseline %.3f ms (best of %d)" b Calibration.baselineRuns

        let rs =
            [ for i in 1..n ->
                  let r = Calibration.reading ()
                  printfn "  [calibration] %3d %.3f ms (x%.3f)" i r (r / b)
                  r ]

        let sorted = List.sort rs

        let q (p: float) =
            sorted.[min (n - 1) (int (p * float n))]

        printfn
            "==== clock calibration: %d readings, ratio to baseline min %.3f p50 %.3f p90 %.3f max %.3f; adjacent-pair disagreement max %.3f"
            n
            (List.min rs / b)
            (q 0.5 / b)
            (q 0.9 / b)
            (List.max rs / b)
            (rs |> List.pairwise |> List.map (fun (x, y) -> max x y / min x y) |> List.max)

        0

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // Write the law set this repository is the reference for — the transform-parity family's
    // reference vectors, stamped with this repository's <Version>:
    //   dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws [<dir>]
    // With no argument the target is THIS repository's committed `conformance/` — the source of
    // truth the default suite certifies against. With a directory it writes there instead, which is
    // how the shared corpus's declared copy is refreshed (`copies.json` quotes that form).
    // Deliberately a flag rather than a test side-effect: the corpus is a separate repository, and
    // a suite that wrote into it on every run would dirty a shared clone; the suite COMPARES and
    // names this command.
    | "--emit-laws" :: rest ->
        let dir = emitTarget rest
        LawVectorExport.write dir

        for path, _ in LawVectorExport.emitted dir do
            printfn "Wrote %s" path

        0
    // The clock leg (Phase 282): the cases whose claim is about TIME, alone in this process, run
    // after the main suite by verify.ps1:
    //   dotnet run --project tests/Fuaran.Core.Compute.Tests --no-build -- --clock-leg
    // Further arguments go to Expecto. The leg fails on fewer cases run than the inventory holds, so
    // a filter that matches nothing — or matches some — cannot pass it vacuously.
    | "--clock-leg" :: rest -> ClockLeg.run rest
    // The calibration probe (Phase 285): the saturation guard's workload, `n` readings, quiet or
    // under load — the measurement k in ScalingTests.Calibration is justified from:
    //   dotnet run --project tests/Fuaran.Core.Compute.Tests -c Release --no-build -- --clock-calibration [n]
    | "--clock-calibration" :: rest -> ClockLeg.probe rest
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
