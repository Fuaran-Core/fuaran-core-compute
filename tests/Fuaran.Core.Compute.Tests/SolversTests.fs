module Fuaran.Compute.Tests.SolversTests

// ---------------------------------------------------------------------------
// Phase 633 — Fuaran.Compute.Solvers: the laws on .NET, the committed law
// vectors, what was worked by hand, the operation table held to the public
// surface, the source held to no host mathematics call, and the package's
// module names held apart from every other Compute package's (DECISIONS.md
// D77). The node leg (LinearAlgebraTests) runs the same laws and renders the
// same document under JavaScript.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Reflection
open Expecto
open Fuaran.Core
open Fuaran.Compute.Solvers

let private committedPath () =
    Snapshots.repoFile (Path.Combine("conformance", "laws", SolversVectors.fileName))

/// The vector with this id, as the document's line for it.
let private lineOf (id: string) : string =
    SolversVectors.render().Split('\n')
    |> Array.find (fun l -> l.Contains(sprintf "\"id\": \"%s\"" id))

let private solversAssembly = typeof<StoppingRule>.Assembly

/// The modules this package publishes at namespace level, by their source names.
let private moduleNames (assembly: Assembly) : string list =
    assembly.GetExportedTypes()
    |> Array.filter (fun t -> t.IsAbstract && t.IsSealed && not t.IsNested)
    |> Array.map (fun t ->
        if t.Name.EndsWith "Module" then
            t.Name.Substring(0, t.Name.Length - "Module".Length)
        else
            t.Name)
    |> List.ofArray

/// The public functions of the package's method modules, spelled `<Module>.<function>` — the census
/// `SolversOperations.exactness` is held to.
let private publishedOperations () : Set<string> =
    solversAssembly.GetExportedTypes()
    |> Array.filter (fun t -> t.IsAbstract && t.IsSealed && not t.IsNested)
    |> Array.collect (fun t ->
        let name =
            if t.Name.EndsWith "Module" then
                t.Name.Substring(0, t.Name.Length - "Module".Length)
            else
                t.Name

        if List.contains name [ "StoppingRule"; "Root"; "Minimise"; "Damped" ] then
            t.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
            |> Array.filter (fun m -> not m.IsSpecialName)
            |> Array.map (fun m -> name + "." + m.Name)
        else
            [||])
    |> Set.ofArray

[<Tests>]
let tests =
    testList
        "Phase 633 - solvers"
        [ testCase "solversLaws hold on .NET"
          <| fun () ->
              for name, verdict in SolversLaws.laws 590 200 do
                  match verdict with
                  | Ok() -> ()
                  | Error why -> failtestf "%s: %s" name why

          testCase "the committed law vectors are the package's answers"
          <| fun () ->
              let committed = File.ReadAllText(committedPath ())

              match CorpusFile.firstDifference committed (SolversVectors.render ()) with
              | None -> ()
              | Some d ->
                  failtestf
                      "%s is stale at line %d:\n  committed: %A\n  rendered:  %A\n  Remedy: dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws"
                      SolversVectors.fileName
                      d.Line
                      d.Committed
                      d.Rendered

          testCase "the solver vectors answer what was worked by hand"
          <| fun () ->
              let has (id: string) (fragment: string) =
                  Expect.stringContains (lineOf id) fragment (sprintf "%s answers %s" id fragment)

              let h = ElementaryVectors.hex

              // A root at an end is answered before any iteration.
              has
                  "bisect-root-at-low-end"
                  (sprintf "\"state\": \"%s\", \"value\": \"%s\", \"iterations\": 0" (h 1.5) (h 0.0))

              has
                  "bisect-root-at-high-end"
                  (sprintf "\"state\": \"%s\", \"value\": \"%s\", \"iterations\": 0" (h 1.5) (h 0.0))

              has "bisect-root-at-first-midpoint" (sprintf "\"state\": \"%s\"" (h 1.5))
              has "bisect-root-at-first-midpoint" "\"iterations\": 1, \"arm\": \"exactZero\""
              // [-1, 2.5]: 0.75 is below the plateau, 1.625 is on it.
              has "bisect-flat-region" (sprintf "\"state\": \"%s\"" (h 1.625))
              has "bisect-flat-region" "\"iterations\": 2, \"arm\": \"exactZero\""
              // The k-th midpoint of [0, 1] moves by 2^-k: a tolerance of 2^-10 stops the tenth, and the
              // float below it lets the tenth pass.
              has "bisect-tolerance-at-step" "\"iterations\": 10, \"arm\": \"absoluteStep\""
              has "bisect-tolerance-one-ulp-below-step" "\"iterations\": 11, \"arm\": \"absoluteStep\""
              has "bisect-converges-at-its-cap" "\"verdict\": \"ok\""
              has "bisect-converges-at-its-cap" "\"iterations\": 10, \"arm\": \"absoluteStep\""
              has "bisect-cut-at-its-cap" "\"error\": \"NotConverged\""
              has "bisect-cut-at-its-cap" "\"iterations\": 9, \"arm\": \"cap\""
              has "bisect-one-sign-refused" "\"error\": \"BracketInvalid\""
              has "bisect-cap-zero-refused" "\"error\": \"RuleInvalid\""
              has "bisect-not-finite-refused" (sprintf "\"error\": \"NotFinite\", \"at\": \"%s\"" (h 1.0))
              // Newton from zero on the cycle steps to one, outside [-3, 0]: the first proposal is the
              // midpoint -1.5 instead, and the run still closes on the root.
              has "newton-step-leaves-bracket" "\"verdict\": \"ok\""
              has "newton-zero-derivative" "\"verdict\": \"ok\""

              has
                  "newton-start-is-the-root"
                  (sprintf "\"state\": \"%s\", \"value\": \"%s\", \"iterations\": 0" (h 1.5) (h 0.0))

              has "newton-cut-at-its-cap" "\"iterations\": 2, \"arm\": \"cap\""
              has "newton-shrink-zero-refused" "\"error\": \"RuleInvalid\""
              has "newton-start-outside-refused" "\"error\": \"BracketInvalid\""
              // 8 * 8 = 64 is the first square at or above 50.
              has "integer-square" (sprintf "\"state\": 8, \"value\": \"%s\"" (h 64.0))
              has "integer-target-met-exactly" (sprintf "\"state\": 7, \"value\": \"%s\"" (h 49.0))
              has "integer-answer-at-low-end" "\"state\": 0"
              has "integer-answer-at-low-end" "\"iterations\": 0"
              has "integer-answer-at-high-end" "\"state\": 100"
              has "integer-one-point-bracket" "\"state\": 7"
              has "integer-negative-bracket" "\"state\": -7"
              has "integer-no-answer-refused" "\"error\": \"BracketInvalid\""
              has "golden-cut-at-its-cap" "\"iterations\": 3, \"arm\": \"cap\""
              has "golden-empty-bracket-refused" "\"error\": \"BracketInvalid\""
              has "golden-not-finite-refused" "\"error\": \"NotFinite\""
              // A gain of one lands on the fixed point in one step, where the objective is zero.
              has
                  "damped-lands-on-zero"
                  (sprintf
                      "\"state\": \"%s\", \"value\": \"%s\", \"iterations\": 1, \"arm\": \"exactZero\""
                      (h 2.0)
                      (h 0.0))
              // 10 -> 6 -> 4 -> 3: three iterations at a gain of a half.
              has "damped-cut-at-its-cap" (sprintf "\"error\": \"NotConverged\", \"state\": \"%s\"" (h 3.0))
              has "damped-step-refused" "\"error\": \"StepRefused\", \"reason\": \"below three\""

              has
                  "damped-rise-no-halving-removes"
                  "\"error\": \"HalvingExhausted\", \"iteration\": 1, \"reason\": \"none\""

              has "damped-inadmissible-no-halving-removes" "\"iteration\": 1, \"reason\": \"below twelve\""
              has "damped-seeded-inadmissible-refused-at-once" "\"iteration\": 1, \"reason\": \"below twelve\""
              has "damped-bracket-rule-refused" "\"error\": \"RuleInvalid\""
              has "damped-negative-halvings-refused" "\"error\": \"RuleInvalid\""

          testCase "the closing estimates are where the functions say"
          <| fun () ->
              let near
                  (what: string)
                  (r: Result<Stopped<float>, SolverError<float, unit>>)
                  (expected: float)
                  (within: float)
                  =
                  match r with
                  | Ok s ->
                      Expect.isTrue
                          (abs (s.State - expected) <= within)
                          (sprintf "%s: %.17g against %.17g" what s.State expected)
                  | Error e -> failtestf "%s was refused: %A" what e

              let cubic (x: float) = x * x * x - 2.0 * x - 5.0
              let cubicDf (x: float) = cubic x, 3.0 * x * x - 2.0
              let rule = StoppingRule.create 200 (StoppingTest.AbsoluteStep 1e-13)
              let root = 2.0945514815423265
              near "bisect" (Root.bisect rule cubic 2.0 3.0) root 1e-12
              near "bracketedNewton, bisecting" (Root.bracketedNewton rule 0.5 cubicDf 2.0 3.0 2.5) root 1e-12
              near "bracketedNewton, plain" (Root.bracketedNewton rule 1.0 cubicDf 2.0 3.0 2.5) root 1e-12

              let cycleDf (x: float) =
                  x * x * x - 2.0 * x + 2.0, 3.0 * x * x - 2.0

              near "the cycle" (Root.bracketedNewton rule 0.5 cycleDf -3.0 0.5 0.0) -1.7692923542386314 1e-12

              let parabola (x: float) = (x - 2.0) * (x - 2.0)
              let golden = StoppingRule.create 200 (StoppingTest.AbsoluteStep 1e-9)
              near "goldenSection" (Minimise.goldenSection golden parabola 0.0 5.0) 2.0 1e-7
              near "goldenSection at its low end" (Minimise.goldenSection golden (fun x -> x) 2.0 3.0) 2.0 1e-8
              near "goldenSection at its high end" (Minimise.goldenSection golden parabola 0.0 1.0) 1.0 1e-8

          testCase "the package's sources reach no host mathematics call"
          <| fun () ->
              let source =
                  [ "StoppingRule.fs"; "Root.fs"; "Minimise.fs"; "Damped.fs" ]
                  |> List.map (fun file ->
                      File.ReadAllText(Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.Solvers", file))))
                  |> String.concat "\n"

              let hostCall =
                  Text.RegularExpressions.Regex(
                      @"Math\.\w+|\b(sqrt|exp|log|log10|pown|floor|ceil|round|truncate|abs|sin|cos|tan)\b\s*[\w(]|\*\*"
                  )

              let hits =
                  source.Split('\n')
                  |> Array.filter (fun l -> not (l.TrimStart().StartsWith "//"))
                  |> Array.collect (fun l -> [| for m in hostCall.Matches l -> m.Value |])
                  |> Array.distinct
                  |> List.ofArray

              Expect.isEmpty hits "a solver's own arithmetic is + - * / and comparisons (DECISIONS.md D77 item 1)"

          testCase "SolversOperations.exactness classifies exactly the published operations, every one Pinned"
          <| fun () ->
              let declared = SolversOperations.exactness |> List.map fst
              Expect.equal (List.distinct declared) declared "no operation is classified twice"
              let published = publishedOperations ()
              Expect.isNonEmpty (Set.toList published) "the census found the method modules"
              Expect.equal (Set.ofList declared) published "the table is the published operation set"

              Expect.isTrue
                  (SolversOperations.exactness
                   |> List.forall (fun (_, e) -> e = SolversOperations.Exactness.Pinned))
                  "every operation here is Pinned (DECISIONS.md D77)"

              Expect.equal
                  (SolversOperations.exactnessOf "Root.bisect")
                  (Some SolversOperations.Exactness.Pinned)
                  "a lookup"

              Expect.equal (SolversOperations.exactnessOf "Root.brent") None "Brent is not shipped (D77 item 8)"

          testCase "no other Compute package declares a module or type the solvers' names would shadow"
          <| fun () ->
              // `open Fuaran.Compute.Solvers` beside another package's namespace must not shadow either
              // side's names (DECISIONS.md D74 item 2): every top-level name here is this package's alone.
              let mine =
                  solversAssembly.GetExportedTypes()
                  |> Array.filter (fun t -> not t.IsNested)
                  |> Array.map (fun t ->
                      let n = t.Name.Split('`').[0]

                      if n.EndsWith "Module" then
                          n.Substring(0, n.Length - 6)
                      else
                          n)
                  |> Set.ofArray

              Expect.isTrue
                  (Set.isSubset (set [ "StoppingRule"; "Root"; "Minimise"; "Damped"; "SolversOperations" ]) mine)
                  "the census found the package's modules"

              let others =
                  [ typeof<Fuaran.Compute.Elementary.SpecialValue>.Assembly
                    typeof<Fuaran.Compute.LinearAlgebra.Matrix>.Assembly
                    typeof<Fuaran.Compute.Regression.GlmFit>.Assembly
                    typeof<Fuaran.Compute.Transform>.Assembly
                    typeof<Fuaran.Compute.ColumnOp>.Assembly ]

              Expect.equal
                  (others |> List.map (fun a -> a.GetName().Name) |> List.distinct |> List.length)
                  5
                  "five other packages are read"

              let clashes =
                  [ for a in others do
                        for t in a.GetExportedTypes() do
                            if not t.IsNested then
                                let n = t.Name.Split('`').[0]

                                let n =
                                    if n.EndsWith "Module" then
                                        n.Substring(0, n.Length - 6)
                                    else
                                        n

                                if Set.contains n mine then
                                    yield a.GetName().Name + ": " + n ]

              Expect.isEmpty clashes "no top-level name of the solvers is declared by another Compute package"
              Expect.isNonEmpty (moduleNames solversAssembly) "the module census is not empty" ]
