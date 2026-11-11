module Fuaran.Compute.Tests.RegressionTests

// ---------------------------------------------------------------------------
// Phase 632 — Fuaran.Compute.Regression held on .NET; the node leg that runs its
// laws and renders its document under Fable is LinearAlgebraTests' (one Fable
// compile for the three packages).
//
//   * `RegressionLaws.laws` on .NET, at the seed and count the linear-algebra
//     laws run at, and the probe that shows the fold's byte laws see an order.
//   * The committed hard-input vectors (`conformance/laws/regression-laws.json`)
//     are the package's answers, and a handful are checked against values worked
//     by hand.
//   * `RegressionOperations.exactness` is the published operation set, both ways.
//
// Every test here left LinearAlgebraTests with the code it holds (Phases 591,
// 595, 598 and 600), its body unchanged.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Reflection
open Expecto
open Fuaran.Core
open Fuaran.Compute.LinearAlgebra
open Fuaran.Compute.Regression

let private bits (x: float) = BitConverter.DoubleToInt64Bits x

let private committedPath () =
    Snapshots.repoFile (Path.Combine("conformance", "laws", RegressionVectors.fileName))

/// The vector with this id, as the document's line for it.
let private lineOf (id: string) : string =
    RegressionVectors.render().Split('\n')
    |> Array.find (fun l -> l.Contains(sprintf "\"id\": \"%s\"" id))

/// The public functions of the package's operation modules, spelled `<Module>.<function>` — the census
/// `RegressionOperations.exactness` is held to.
let private publishedOperations () : Set<string> =
    let modules =
        set [ "QRAccumulator"; "LeastSquares"; "RegressionWire"; "GlmControl"; "Glm" ]

    typeof<QRAccumulator>.Assembly.GetExportedTypes()
    |> Array.filter (fun t -> t.IsAbstract && t.IsSealed && t.Namespace = "Fuaran.Compute.Regression")
    |> Array.collect (fun t ->
        let name =
            if t.Name.EndsWith "Module" then
                t.Name.Substring(0, t.Name.Length - "Module".Length)
            else
                t.Name

        if modules.Contains name then
            t.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
            |> Array.filter (fun m -> not m.IsSpecialName)
            |> Array.map (fun m -> name + "." + m.Name)
        else
            [||])
    |> Set.ofArray

[<Tests>]
let tests =
    testList
        "Phase 632 - regression"
        [ testCase "regressionLaws hold on .NET"
          <| fun () ->
              for name, verdict in RegressionLaws.laws 590 200 do
                  match verdict with
                  | Ok() -> ()
                  | Error why -> failtestf "%s: %s" name why

          testCase "rows folded in another order answer other bits, so the byte laws see an order"
          <| fun () ->
              // The fold is the contract (DECISIONS.md D49 items 1 and 9): reversing the rows of a
              // generic design moves the last bits of R, which is why the append and codec laws can fail.
              let x = Matrix.init 7 3 (fun i j -> float ((i * 7 + j * 3) % 11) / 7.0 - 0.6)
              let y = Vector.init 7 (fun i -> float (i * i % 5) / 3.0)
              let w = Vector.init 7 (fun _ -> 1.0)
              let rev = Matrix.init 7 3 (fun i j -> x[6 - i, j])
              let ry = Vector.init 7 (fun i -> y[6 - i])

              let fold a b =
                  QRAccumulator.empty 3 |> QRAccumulator.addRows a b w

              let state (r: Result<QRAccumulator, LinAlgError>) =
                  match r with
                  | Ok acc ->
                      Array.append (Matrix.toColumnMajor (QRAccumulator.r acc)) (Vector.toArray (QRAccumulator.qty acc))
                      |> Array.map bits
                  | Error e -> failtestf "refused: %A" e

              Expect.notEqual (state (fold rev ry)) (state (fold x y)) "a reversed fold differs in its bits"

          testCase "the least-squares vectors answer what was worked by hand"
          <| fun () ->
              let has (id: string) (fragment: string) =
                  Expect.stringContains (lineOf id) fragment (sprintf "%s answers %s" id fragment)

              let h = LinearAlgebraVectors.hex
              // y = 2 + 3t exactly: the coefficients are 2 and 3 to the bit.
              has "lsq-exact-line" (sprintf "\"coefficients\": [\"%s\", \"%s\"]" (h 2.0) (h 3.0))
              // The later copy of a column is aliased, its coefficient the token.
              has "lsq-duplicate-column-aliased" "\"aliased\"], \"rank\": 2, \"aliased\": [2]"
              has "lsq-zero-column-aliased" "\"rank\": 1, \"aliased\": [0]"
              has "lsq-no-rows" "\"rank\": 0, \"aliased\": [0, 1]"
              // A zero weight is not counted; a negative or infinite one is refused by row.
              has "lsq-zero-weight-outlier-ignored" "\"count\": 3"
              has "qr-refuses-negative-weight" "{ \"case\": \"invalidWeight\", \"row\": 1 }"
              has "qr-refuses-nan-cell" "{ \"case\": \"notFinite\", \"row\": 1, \"col\": 1 }"
              has "qr-refuses-infinite-response" "{ \"case\": \"notFinite\", \"row\": 0, \"col\": 1 }"

          testCase "the committed law vectors are the package's answers"
          <| fun () ->
              let committed = File.ReadAllText(committedPath ())

              match CorpusFile.firstDifference committed (RegressionVectors.render ()) with
              | None -> ()
              | Some d ->
                  failtestf
                      "%s is stale at line %d:\n  committed: %A\n  rendered:  %A\n  Remedy: dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws"
                      RegressionVectors.fileName
                      d.Line
                      d.Committed
                      d.Rendered

          testCase "RegressionOperations.exactness classifies exactly the published operations, every one Pinned"
          <| fun () ->
              let declared = RegressionOperations.exactness |> List.map fst
              Expect.equal (List.distinct declared) declared "no operation is classified twice"
              let published = publishedOperations ()
              Expect.isNonEmpty (Set.toList published) "the census found the operation modules"
              Expect.equal (Set.ofList declared) published "the table is the published operation set"

              Expect.isTrue
                  (RegressionOperations.exactness
                   |> List.forall (fun (_, e) -> e = RegressionOperations.Exactness.Pinned))
                  "every operation here is Pinned (DECISIONS.md D49 item 7)"

              Expect.equal
                  (RegressionOperations.exactnessOf "Glm.fit")
                  (Some RegressionOperations.Exactness.Pinned)
                  "a lookup"

              Expect.equal
                  (RegressionOperations.exactnessOf "Blas.gemv")
                  None
                  "a matrix operation is not this package's" ]
