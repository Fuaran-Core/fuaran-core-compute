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
//
// Phase 591 adds the streaming QR, least squares and the codec to the same laws,
// vectors and node leg, and the probes that show the new byte laws discriminate:
// a fused rotation and a reordered fold each answer different bits.
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
    let modules =
        set
            [ "Matrix"
              "Blas"
              "UpperTriangular"
              "Givens"
              "QRAccumulator"
              "QR"
              "LeastSquares"
              "LinAlgWire"
              "Elementary" ]

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

          testCase "a rotation that rounds differently is caught by the native-versus-portable comparison"
          <| fun () ->
              // The pair (1 + 2^-30, -(1 + 2^-29)) rotated by c = 1 + 2^-30, s = 1: the first output is
              // c·x + s·y, +0 rounded twice and 2^-60 fused — nine pairs, so the SIMD lanes are reached.
              let fa = 1.0 + 1.0 / 1073741824.0
              let fc = -(1.0 + 1.0 / 536870912.0)
              let fresh () = Array.create 9 fa, Array.create 9 fc
              let pu, pv = fresh ()
              Kernels.rotatePortable fa 1.0 pu 0 pv 0 9
              let nu, nv = fresh ()
              Kernels.rotateNative fa 1.0 nu 0 nv 0 9
              let fu, fv = fresh ()

              for k in 0..8 do
                  let x, y = fu[k], fv[k]
                  fu[k] <- Math.FusedMultiplyAdd(fa, x, 1.0 * y)
                  fv[k] <- fa * y - 1.0 * x

              Expect.equal (Array.map bits nu) (Array.map bits pu) "native = portable"
              Expect.equal (Array.map bits pu) (Array.create 9 (bits 0.0)) "pinned: +0 in every pair"
              Expect.notEqual (Array.map bits fu) (Array.map bits pu) "a fused rotation differs"

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
              // A Householder R is normalised to a non-negative diagonal: |(-3, 4, 0)| = 5.
              has "householder-signs-normalised" (sprintf "\"data\": [\"%s\"" (h 5.0))
              // Pivoting takes the largest column first, the lower index of two of norm 5.
              has "pivoted-largest-first" "\"permutation\": [1, 2, 0]"

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

          // Phase 593 — the elementary functions.
          testCase "every elementary vector with a reference is within one ulp of it, and says so in its ulps cell"
          <| fun () ->
              let elementaryOps = set [ "exp"; "ln"; "log10"; "pow" ]

              let referenced =
                  LinearAlgebraVectors.cases
                  |> List.filter (fun c -> elementaryOps.Contains c.Op)
                  |> List.choose (fun c ->
                      LinearAlgebraVectors.references
                      |> List.tryFind (fun (id, _) -> id = c.Id)
                      |> Option.map (fun (_, h) -> c, h))

              Expect.equal referenced.Length LinearAlgebraVectors.references.Length "every reference names a vector"
              Expect.isGreaterThan referenced.Length 60 "the hard inputs are referenced"

              for c, h in referenced do
                  let line = lineOf c.Id
                  Expect.stringContains line (sprintf "\"reference\": \"%s\"" h) (c.Id + " carries its reference")

                  let ulps =
                      line.Substring(line.IndexOf "\"ulps\": " + 8).TrimEnd(' ', '}', ',', '\r')
                      |> int

                  Expect.isLessThanOrEqual
                      ulps
                      LinearAlgebraVectors.referenceUlps
                      (sprintf "%s is %d ulps from the reference" c.Id ulps)

          testCase "the elementary vectors answer what was worked by hand"
          <| fun () ->
              let has (id: string) (fragment: string) =
                  Expect.stringContains (lineOf id) fragment (sprintf "%s answers %s" id fragment)

              let h = LinearAlgebraVectors.hex
              has "exp-zero" (sprintf "\"value\": \"%s\", \"table\": \"compute\"" (h 1.0))

              has
                  "exp-above-overflow-edge"
                  (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h Double.PositiveInfinity))

              has "exp-minus-thousand" (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h 0.0))
              has "exp-nan" "\"value\": \"nan\", \"table\": \"nan\""
              has "ln-zero" "\"value\": \"nan\", \"table\": \"undefined\""
              has "ln-one" (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h 0.0))
              has "ln-two" (sprintf "\"value\": \"%s\"" (h 0.6931471805599453))
              has "log10-thousand" (sprintf "\"value\": \"%s\"" (h 3.0))
              has "pow-square-shortcut" (sprintf "\"value\": \"%s\"" (h 100.0))
              has "pow-root-shortcut" (sprintf "\"value\": \"%s\"" (h (sqrt 2.0)))
              has "pow-negative-zero-odd" (sprintf "\"value\": \"%s\", \"table\": \"compute\"" (h -0.0))
              has "pow-negative-base-non-integer" "\"value\": \"nan\", \"table\": \"undefined\""
              has "pow-one-nan" "\"value\": \"nan\", \"table\": \"nan\""
              has "pow-zero-zero" (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h 1.0))
              has "pow-two-cubed" (sprintf "\"value\": \"%s\"" (h 8.0))

          testCase
              "the host's own libm answers other bits on at least one elementary vector, so the byte comparison sees a port that cheats"
          <| fun () ->
              let differing =
                  LinearAlgebraVectors.cases
                  |> List.choose (fun c ->
                      let scalar name =
                          match c.Input |> List.find (fun (n, _) -> n = name) with
                          | _, LinearAlgebraVectors.Scalar x -> x
                          | _ -> failtestf "%s: %s is not a scalar" c.Id name

                      match c.Op with
                      | "exp" -> Some(c.Id, Elementary.exp (scalar "x"), Math.Exp(scalar "x"))
                      | "ln" -> Some(c.Id, Elementary.ln (scalar "x"), Math.Log(scalar "x"))
                      | "log10" -> Some(c.Id, Elementary.log10 (scalar "x"), Math.Log10(scalar "x"))
                      | "pow" -> Some(c.Id, Elementary.pow (scalar "x") (scalar "y"), Math.Pow(scalar "x", scalar "y"))
                      | _ -> None)
                  |> List.filter (fun (_, pinned, host) ->
                      not (Double.IsNaN pinned) && not (Double.IsNaN host) && bits pinned <> bits host)

              Expect.isNonEmpty differing "the host's libm and the pinned routine differ somewhere on the vectors"

          testCase "Elementary's source reaches no host mathematics call"
          <| fun () ->
              let source =
                  File.ReadAllText(
                      Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.LinearAlgebra", "Elementary.fs"))
                  )

              let hostCall =
                  Text.RegularExpressions.Regex(
                      @"Math\.(Exp|Log|Log10|Log2|Pow|Floor|Ceiling|Round|Truncate|FusedMultiplyAdd|ScaleB|ILogB|Sqrt|Cbrt)\b|\*\*"
                  )

              let hits =
                  hostCall.Matches source
                  |> Seq.map (fun m -> m.Value)
                  |> Seq.distinct
                  |> List.ofSeq

              Expect.isEmpty
                  hits
                  "no host mathematics call in Elementary.fs (the square root is the F# operator over the premise's correctly rounded root)"

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
