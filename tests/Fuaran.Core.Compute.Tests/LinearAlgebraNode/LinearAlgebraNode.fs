/// Phase 590 — the node leg of the linear-algebra package: `LinearAlgebraLaws.laws` at the seed and
/// count the .NET suite runs, then the hard-input law-vector document rendered under JavaScript.
///
///   dotnet fable LinearAlgebraNode.fsproj -o <out>; node <out>/LinearAlgebraNode.js
///
/// Prints each law's verdict, the document between `BEGIN` and `END` marker lines, and "linear
/// algebra laws under node: held"; exits 0 when every law held, 1 otherwise. The suite compares the
/// document with the committed file, so the vectors are held on this host byte for byte.
module Fuaran.Compute.Tests.LinearAlgebraNode

#if FABLE_COMPILER
open Fable.Core
open Fuaran.Compute.LinearAlgebra
open Fuaran.Compute.Tests

[<Emit("process.exit($0)")>]
let private exit (code: int) : unit = jsNative

let private failures =
    LinearAlgebraLaws.laws 590 200
    |> List.choose (fun (name, verdict) ->
        match verdict with
        | Ok() -> None
        | Error why -> Some(name + ": " + why))

for f in failures do
    printfn "FAILED %s" f

printfn "BEGIN %s" LinearAlgebraVectors.fileName
printfn "%s" (LinearAlgebraVectors.render ())
printfn "END %s" LinearAlgebraVectors.fileName

printfn "linear algebra laws under node: %s" (if List.isEmpty failures then "held" else "FAILED")
exit (if List.isEmpty failures then 0 else 1)
#endif
