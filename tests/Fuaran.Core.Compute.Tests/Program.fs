module Fuaran.Core.Tests.Program

open Expecto

/// Where an exporter writes: the directory named after the flag, else this repository's
/// committed `conformance/`. A following flag is not a directory.
let private emitTarget (rest: string list) : string =
    match rest with
    | dir :: _ when not (dir.StartsWith "--") -> dir
    | _ -> OwnedConformance.root ()

/// The clock leg's runner (Phase 282): `ScalingTests.clockTests`, alone, then the count check.
module private ClockLeg =
    open System.Diagnostics

    let run (rest: string list) : int =
        // Above-normal priority, so ordinary work elsewhere on the machine yields to the leg for
        // the seconds it runs. Refused on a host that does not let a process raise itself; the leg
        // still runs, on the three-attempt rule alone, and says so.
        try
            Process.GetCurrentProcess().PriorityClass <- ProcessPriorityClass.AboveNormal
            printfn "==== clock leg: process priority AboveNormal"
        with e ->
            printfn "==== clock leg: process priority unchanged (%s)" e.Message

        let code = runTestsWithCLIArgs [] (Array.ofList rest) ScalingTests.clockTests
        let ran = ScalingTests.clockCasesRun ()
        let expected = ScalingTests.clockInventory

        if ran < expected then
            eprintfn
                "==== clock leg: %d of %d clock cases ran — a leg that ran fewer than its inventory proves nothing about the rest"
                ran
                expected

            if code <> 0 then code else 2
        else
            printfn "==== clock leg: %d of %d clock cases ran (exit %d)" ran expected code
            code

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
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
