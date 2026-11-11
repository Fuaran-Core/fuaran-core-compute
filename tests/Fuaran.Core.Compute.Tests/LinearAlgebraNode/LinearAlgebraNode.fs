/// Phase 590 — the node leg of the linear-algebra package: `LinearAlgebraLaws.laws` at the seed and
/// count the .NET suite runs, then the hard-input law-vector document rendered under JavaScript. Since
/// Phase 630 it runs the elementary package's leg too: `ElementaryLaws.laws` at the same seed and
/// count, and `elementary-laws.json` rendered beside the linear-algebra document; and since Phase 632
/// the regression package's: `RegressionLaws.laws` and `regression-laws.json`; and since Phase 633 the
/// solvers package's: `SolversLaws.laws` and `solvers-laws.json`.
///
///   dotnet fable LinearAlgebraNode.fsproj -o <out>; node <out>/LinearAlgebraNode.js
///
/// Prints each law's verdict, each document between `BEGIN` and `END` marker lines, and "linear
/// algebra laws under node: held"; exits 0 when every law held, 1 otherwise. The suite compares the
/// documents with the committed files, so the vectors are held on this host byte for byte.
module Fuaran.Compute.Tests.LinearAlgebraNode

#if FABLE_COMPILER
open Fable.Core
open Fuaran.Compute.Elementary
open Fuaran.Compute.LinearAlgebra
open Fuaran.Compute.Regression
open Fuaran.Compute.Solvers
open Fuaran.Compute.Tests

[<Emit("process.exit($0)")>]
let private exit (code: int) : unit = jsNative

let private failures =
    LinearAlgebraLaws.laws 590 200
    @ ElementaryLaws.laws 590 200
    @ RegressionLaws.laws 590 200
    @ SolversLaws.laws 590 200
    |> List.choose (fun (name, verdict) ->
        match verdict with
        | Ok() -> None
        | Error why -> Some(name + ": " + why))

for f in failures do
    printfn "FAILED %s" f

printfn "BEGIN %s" LinearAlgebraVectors.fileName
printfn "%s" (LinearAlgebraVectors.render ())
printfn "END %s" LinearAlgebraVectors.fileName

printfn "BEGIN %s" ElementaryVectors.fileName
printfn "%s" (ElementaryVectors.render ())
printfn "END %s" ElementaryVectors.fileName

printfn "BEGIN %s" RegressionVectors.fileName
printfn "%s" (RegressionVectors.render ())
printfn "END %s" RegressionVectors.fileName

printfn "BEGIN %s" SolversVectors.fileName
printfn "%s" (SolversVectors.render ())
printfn "END %s" SolversVectors.fileName

printfn "linear algebra laws under node: %s" (if List.isEmpty failures then "held" else "FAILED")
exit (if List.isEmpty failures then 0 else 1)
#endif
