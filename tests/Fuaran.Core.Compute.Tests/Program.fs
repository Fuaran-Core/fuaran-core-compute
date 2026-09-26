module Fuaran.Core.Tests.Program

open Expecto

/// Where an exporter writes: the directory named after the flag, else this repository's
/// committed `conformance/`. A following flag is not a directory.
let private emitTarget (rest: string list) : string =
    match rest with
    | dir :: _ when not (dir.StartsWith "--") -> dir
    | _ -> OwnedConformance.root ()

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
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
