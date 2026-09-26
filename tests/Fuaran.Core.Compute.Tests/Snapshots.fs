module Fuaran.Core.Tests.Snapshots

// The repository root, found the one way every leg that reads a committed file finds it: climb from
// the working directory and from the test binary to the directory holding `Fuaran.Core.Compute.slnx`.
// (The module keeps the name it has in the Fuaran.Core suite these tests were carried from, so the
// carried files read it unchanged; the golden-snapshot store it also held there is not needed here.)

open System.IO

/// Resolve a repo-relative path by climbing to the repository root (`Fuaran.Core.Compute.slnx`
/// marker).
let repoFile (relPath: string) : string =
    let rec climb (dir: string) (budget: int) : string option =
        if budget < 0 || isNull dir then
            None
        elif File.Exists(Path.Combine(dir, "Fuaran.Core.Compute.slnx")) then
            Some dir
        else
            match Directory.GetParent dir with
            | null -> None
            | parent -> climb parent.FullName (budget - 1)

    let root =
        [ Directory.GetCurrentDirectory(); System.AppContext.BaseDirectory ]
        |> List.tryPick (fun start -> climb start 12)
        |> Option.defaultWith (fun () ->
            failwith "Snapshots.repoFile: the repository root (Fuaran.Core.Compute.slnx) not found")

    Path.Combine(root, relPath)
