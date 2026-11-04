module Fuaran.Compute.Tests.LinearAlgebraTests

// ---------------------------------------------------------------------------
// Phase 590 — Fuaran.Compute.LinearAlgebra held on both hosts.
//
//   * `LinearAlgebraLaws.laws` on .NET, and the probes that show its byte laws can
//     go red (a fused multiply-add answers the witness differently; a perturbed
//     column update is caught).
//   * The committed hard-input vectors (`conformance/laws/linear-algebra-laws.json`)
//     are the package's answers, rendered through the one corpus writer, and a
//     handful are checked against values worked by hand.
//   * The node leg: the same laws and the same document under Fable, the document
//     compared with the committed file byte for byte.
//   * `Operations.exactness` is the published operation set, both ways.
// ---------------------------------------------------------------------------

open System
open System.Diagnostics
open System.IO
open System.Reflection
open Expecto
open Fuaran.Core
open Fuaran.Compute.LinearAlgebra

let private bits (x: float) = BitConverter.DoubleToInt64Bits x

let private committedPath () =
    Snapshots.repoFile (Path.Combine("conformance", "laws", LinearAlgebraVectors.fileName))

let private run (fileName: string) (arguments: string) (cwd: string) : int * string =
    let psi = ChildProcess.redirected fileName arguments
    psi.WorkingDirectory <- cwd
    use p = Process.Start psi
    let err = p.StandardError.ReadToEndAsync()
    let out = p.StandardOutput.ReadToEnd()
    p.WaitForExit()
    p.ExitCode, out + err.Result

/// The vector with this id, as the document's line for it.
let private lineOf (id: string) : string =
    LinearAlgebraVectors.render().Split('\n')
    |> Array.find (fun l -> l.Contains(sprintf "\"id\": \"%s\"" id))

/// The public functions of the package's operation modules, spelled `<Module>.<function>` — the census
/// `Operations.exactness` is held to.
let private publishedOperations () : Set<string> =
    let modules = set [ "Matrix"; "Blas"; "UpperTriangular"; "Givens" ]

    typeof<Matrix>.Assembly.GetExportedTypes()
    |> Array.filter (fun t -> t.IsAbstract && t.IsSealed && t.Namespace = "Fuaran.Compute.LinearAlgebra")
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
        "Phase 590 - linear algebra"
        [ testCase "linearAlgebraLaws hold on .NET"
          <| fun () ->
              for name, verdict in LinearAlgebraLaws.laws 590 200 do
                  match verdict with
                  | Ok() -> ()
                  | Error why -> failtestf "%s: %s" name why

          testCase "the fused-multiply-add witness discriminates: fused, it answers 2^-60; pinned, +0"
          <| fun () ->
              // The probe behind the no-contraction law, run the other way: were a product and a sum
              // fused, the witness would not answer zero, so the law's zero is evidence.
              let a = 1.0 + 1.0 / 1073741824.0
              let c = -(1.0 + 1.0 / 536870912.0)
              Expect.equal (Math.FusedMultiplyAdd(a, a, c)) (1.0 / 1152921504606846976.0) "fused: 2^-60"
              let pinned = Blas.dot (Vector.ofList [ 1.0; a ]) (Vector.ofList [ c; a ])
              Expect.equal (pinned |> Result.map bits) (Ok(bits 0.0)) "pinned: +0"

          testCase "a column update that rounds differently is caught by the native-versus-portable comparison"
          <| fun () ->
              // The witness of the law above, nine rows deep: column 0 holds -(1 + 2^-29) and column 1
              // 1 + 2^-30, so each row is (1 + 2^-30)^2 - (1 + 2^-29), +0 pinned and 2^-60 fused.
              let rows, cols = 9, 2
              let fa = 1.0 + 1.0 / 1073741824.0

              let a =
                  Array.init (rows * cols) (fun k -> if k < rows then -(1.0 + 1.0 / 536870912.0) else fa)

              let x = [| 1.0; fa |]
              let portable = Kernels.gemvWith Kernels.axpyColumnPortable a 0 rows cols x 0

              let fused =
                  Kernels.gemvWith
                      (fun s col co y n ->
                          for i in 0 .. n - 1 do
                              y[i] <- Math.FusedMultiplyAdd(s, col[co + i], y[i]))
                      a
                      0
                      rows
                      cols
                      x
                      0

              Expect.equal
                  (Kernels.gemv a 0 rows cols x 0 |> Array.map bits)
                  (portable |> Array.map bits)
                  "native = portable"

              Expect.notEqual (fused |> Array.map bits) (portable |> Array.map bits) "a fused update differs somewhere"

          testCase "a column is a zero-copy view, and an offset vector is read where it lies"
          <| fun () ->
              let m = Matrix.init 3 2 (fun i j -> float (10 * j + i))
              let col = Matrix.column 1 m

              Expect.isTrue
                  (obj.ReferenceEquals((Vector.Unsafe.borrow col).Array, (Vector.Unsafe.borrow m.Storage).Array))
                  "shared storage"

              Expect.equal (Vector.toList col) [ 10.0; 11.0; 12.0 ] "column 1"
              let x = Vector.slice 1 2 (Vector.ofList [ 99.0; 1.0; -1.0; 99.0 ])
              Expect.equal (Blas.gemv m x |> Result.map Vector.toList) (Ok [ -10.0; -10.0; -10.0 ]) "gemv over a slice"

          testCase "the committed law vectors are the package's answers"
          <| fun () ->
              let committed = File.ReadAllText(committedPath ())

              match CorpusFile.firstDifference committed (LinearAlgebraVectors.render ()) with
              | None -> ()
              | Some d ->
                  failtestf
                      "%s is stale at line %d:\n  committed: %A\n  rendered:  %A\n  Remedy: dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws"
                      LinearAlgebraVectors.fileName
                      d.Line
                      d.Committed
                      d.Rendered

          testCase "the hard inputs answer what was worked by hand"
          <| fun () ->
              let has (id: string) (fragment: string) =
                  Expect.stringContains (lineOf id) fragment (sprintf "%s answers %s" id fragment)

              let h = LinearAlgebraVectors.hex
              let p1000 = List.fold (fun p _ -> p * 2.0) 1.0 [ 1..1000 ]
              // Two roundings, not one: the fused answer would be 2^-60.
              has "dot-fma-witness" (sprintf "\"value\": \"%s\"" (h 0.0))
              has "gemm-fma-witness" (sprintf "\"data\": [\"%s\"]" (h 0.0))
              // Left to right: 1e16 absorbs the 1, then cancels.
              has "dot-absorbed-then-cancelled" (sprintf "\"value\": \"%s\"" (h 0.0))
              // The scaled norm keeps what a plain sum of squares flushes or overflows.
              has "norm2-least-subnormals" (sprintf "\"value\": \"%s\"" (h Double.Epsilon))
              has "norm2-pythagorean-scaled" (sprintf "\"value\": \"%s\"" (h (5.0 * p1000)))
              has "norm2-huge" (sprintf "\"value\": \"%s\"" (h (1e300 * sqrt 2.0)))
              has "norm2-nan-beats-infinity" "\"value\": \"nan\""
              has "norm2-signed-zeros" (sprintf "\"value\": \"%s\"" (h 0.0))
              // The sign convention: r > 0 whatever a's sign; the identity keeps a, sign and all.
              has
                  "givens-negative-a"
                  (sprintf "\"cos\": \"%s\", \"sin\": \"%s\", \"radius\": \"%s\"" (h -0.6) (h 0.8) (h 5.0))

              has "givens-identity" (sprintf "\"radius\": \"%s\", \"identity\": true" (h -2.0))

              has
                  "givens-apply-identity-keeps-infinity"
                  (sprintf "\"value\": [\"%s\", \"%s\"]" (h -0.0) (h Double.PositiveInfinity))
              // The refusals name the lowest offending index.
              has "solveUpper-singular-lowest-index" "{ \"case\": \"singular\", \"index\": 1 }"
              has "ofMatrix-not-triangular" "{ \"case\": \"notTriangular\", \"row\": 1, \"col\": 0 }"
              has "ofMatrix-not-finite" "{ \"case\": \"notFinite\", \"row\": 0, \"col\": 1 }"
              has "ofMatrix-negative-zero-below" "\"verdict\": \"ok\""

          testCase "Operations.exactness classifies exactly the published operations, every one Pinned"
          <| fun () ->
              let declared = Operations.exactness |> List.map fst
              Expect.equal (List.distinct declared) declared "no operation is classified twice"
              let published = publishedOperations ()
              Expect.isNonEmpty (Set.toList published) "the census found the operation modules"
              Expect.equal (Set.ofList declared) published "the table is the published operation set"

              Expect.isTrue
                  (Operations.exactness |> List.forall (fun (_, e) -> e = Exactness.Pinned))
                  "every operation here is Pinned (DECISIONS.md D49 item 7)"

              Expect.equal (Operations.exactnessOf "Blas.gemv") (Some Exactness.Pinned) "a lookup"
              Expect.equal (Operations.exactnessOf "Blas.hypot") None "no such operation"

          // The node leg: the same laws and the same document under the JavaScript host. It needs the
          // repository's own Fable and node on PATH, and fails rather than skips without them, because
          // the gate claims the leg.
          testCase "the laws hold and the vectors render byte for byte under node (Fable)"
          <| fun () ->
              let root = Snapshots.repoFile ""

              let project =
                  Path.Combine(
                      root,
                      "tests",
                      "Fuaran.Core.Compute.Tests",
                      "LinearAlgebraNode",
                      "LinearAlgebraNode.fsproj"
                  )

              let key =
                  Convert
                      .ToHexString(Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes root))
                      .Substring(0, 12)
                      .ToLowerInvariant()

              // Keyed on the checkout, and mirroring the repository's layout under it, for the reasons
              // PooledLawTests gives: the linked vector file lands beside the output directory.
              let scratch = Path.Combine(Path.GetTempPath(), "fuaran-compute-linalg-node-" + key)

              let out =
                  Path.Combine(scratch, "tests", "Fuaran.Core.Compute.Tests", "LinearAlgebraNode")

              if Directory.Exists scratch then
                  Directory.Delete(scratch, true)

              Directory.CreateDirectory out |> ignore

              try
                  let code, transcript =
                      run "dotnet" (sprintf "fable \"%s\" -o \"%s\" --noCache" project out) root

                  Expect.equal code 0 ("the Fable compile of the node leg failed:\n" + transcript)
                  File.WriteAllText(Path.Combine(scratch, "package.json"), "{ \"type\": \"module\" }")
                  let code, transcript = run "node" "LinearAlgebraNode.js" out
                  Expect.equal code 0 ("the node leg's laws failed:\n" + transcript)
                  Expect.stringContains transcript "linear algebra laws under node: held" "the node leg ran to its end"

                  let lines = transcript.Replace("\r\n", "\n").Split('\n')

                  let first =
                      Array.findIndex (fun l -> l = "BEGIN " + LinearAlgebraVectors.fileName) lines

                  let last =
                      Array.findIndex (fun l -> l = "END " + LinearAlgebraVectors.fileName) lines
                  // The document ends in a newline and `printfn` adds one more: drop that one.
                  let rendered = String.Join("\n", lines[first + 1 .. last - 2]) + "\n"
                  let committed = File.ReadAllText(committedPath ())

                  match CorpusFile.firstDifference committed rendered with
                  | None -> ()
                  | Some d ->
                      failtestf
                          "under node, %s differs at line %d:\n  committed: %A\n  node:      %A"
                          LinearAlgebraVectors.fileName
                          d.Line
                          d.Committed
                          d.Rendered
              finally
                  try
                      Directory.Delete(scratch, true)
                  with _ ->
                      () ]
