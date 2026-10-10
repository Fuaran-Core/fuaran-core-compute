/// Phase 375 — the step as data (`MorselHandOff`) held to its laws (`MorselHandOffLaw`): a row-local
/// step handed to a runner that holds no closure of the caller's, compiled by the runner from the data
/// through the evaluator's own compile path, and finished by the caller, answers what the sequential
/// member answers byte for byte, whichever order and whichever threads run its morsels; and the
/// perturbed runners go red.
module Fuaran.Compute.Tests.MorselHandOffTests

open System.Threading.Tasks
open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests.MorselHandOffLaw

/// One morsel a task on the thread pool, each through its own rebuild and compile — the shape a
/// pool of workers takes, every worker compiling the step from the data.
let private pooled (h: MorselHandOff) : unit =
    Parallel.For(0, MorselHandOff.morselCount h, (fun j -> MorselHandOff.run (range h j (j + 1))))
    |> ignore

let private honest: (string * (MorselHandOff -> unit)) list =
    MorselHandOffLaw.honest @ [ "pooled", pooled ]

[<Tests>]
let tests =
    testList
        "Phase 375 - the step as data"
        [ testCase "every runner answers the sequential member over the transform vectors, at every morsel size"
          <| fun () ->
              let cases = vectorCases ()

              for morselRows in [ 1; 2; 3; Kernels.MorselRows ] do
                  for name, runner in honest do
                      let failures, handed = disagreements runner morselRows cases
                      Expect.isEmpty failures (sprintf "%s runner, %d rows a morsel" name morselRows)
                      Expect.isGreaterThan handed 0 "the sample hands some step off"

          testCase
              "the transform vectors reach every kind of hand-off: filter, the three typed roots, more than one morsel, a refusal"
          <| fun () ->
              let handed = handedOff 2 (vectorCases ())

              let roots =
                  handed
                  |> List.map (fun c -> (MorselHandOff.planAt 2 c.Env c.Frame c.Step).Value.Root)
                  |> set

              Expect.isTrue (handed |> List.exists isFilter) "a filter is handed off"
              Expect.isTrue (roots.Contains 1 || roots.Contains 2) "a numeric derive is handed off"

              Expect.isTrue
                  (handed |> List.exists (fun c -> Frame.physical c.Frame |> Array.length > 2))
                  "a step spans more than one morsel"

          testCase "every runner answers the sequential member over the corpus's row-local pipelines"
          <| fun () ->
              let cases = corpusCases 20_000

              for name, runner in honest do
                  let failures, handed = disagreements runner Kernels.MorselRows cases
                  Expect.isEmpty failures (sprintf "%s runner" name)
                  Expect.equal handed (List.length cases) "every corpus step is handed off"

          testCase "the corpus reaches the filter, int, float and bool roots, a selection, and refusals in two morsels"
          <| fun () ->
              let cases = corpusCases 20_000
              let plans = cases |> List.choose (fun c -> MorselHandOff.plan c.Env c.Frame c.Step)
              Expect.equal (plans |> List.map (fun h -> h.Root) |> set) (set [ 0; 1; 2; 3 ]) "every root"
              // 20,000 rows are three morsels; the chain's steps after its filter read under 8,192.
              Expect.isTrue (plans |> List.exists (fun h -> MorselHandOff.morselCount h = 3)) "steps span three morsels"
              Expect.isTrue (cases |> List.exists (fun c -> Option.isSome c.Frame.Sel)) "a step over a selection"
              Expect.equal (cases |> List.filter refuses |> List.length) 2 "two refusals"

          testCase "a shifted morsel boundary goes red, on a filter and on a derive"
          <| fun () ->
              let corpus = corpusCases 20_000 |> List.filter (refuses >> not)
              let failures, _ = disagreements shifted Kernels.MorselRows corpus

              let red (pick: Case -> bool) =
                  corpus
                  |> List.filter pick
                  |> List.exists (fun c -> fst (disagreements shifted Kernels.MorselRows [ c ]) <> [])

              Expect.isNonEmpty failures "the shifted runner disagrees"
              Expect.isTrue (red isFilter) "red on a filter"
              Expect.isTrue (red (isFilter >> not)) "red on a derive"

              let vectorFailures, _ = disagreements shifted 2 (vectorCases ())
              Expect.isNonEmpty vectorFailures "red over the transform vectors too"

          testCase "concatenation out of morsel order goes red"
          <| fun () ->
              let failures, _ = disagreements swappedPairs Kernels.MorselRows (corpusCases 20_000)
              Expect.isNonEmpty failures "over the corpus"

          testCase "a step the hand-off cannot carry is not handed off, and runs as it always ran"
          <| fun () ->
              let o = Frame.ofTable (orders 100 false)
              let reads (t: Transform) = MorselHandOff.plan Map.empty o t

              Expect.isNone (reads (Derive("r", ApplyFn(Upper, [ Col "region" ])))) "a string column"
              Expect.isNone (reads (Filter(Binary(Ge, Col "qty", Lit(Int 3))))) "a comparison-kernel filter"
              Expect.isNone (reads (Derive("t", Lit(Str "x")))) "a string root"
              Expect.isNone (reads (Project [ "id", "id" ])) "a step that is not row-local"
              Expect.isSome (reads (Derive("u", Binary(Add, Col "qty", Col "id")))) "an int derive is handed off"

          testCase "a hand-off holds the frame only as data: strings, numbers and arrays of numbers"
          <| fun () ->
              let h =
                  (MorselHandOff.plan sheetEnv (Frame.ofTable (orders 100 false)) (List.head lines)).Value

              let f, env, t = MorselHandOff.rebuild h
              Expect.equal t (List.head lines) "the step decodes back to itself"
              Expect.equal env sheetEnv "the environment decodes back to itself"

              Expect.equal
                  (f.Cols |> List.map (fun (f: Field) -> f.Name))
                  [ "id"; "region"; "qty"; "price" ]
                  "the schema"

              Expect.equal h.Kinds [| 0; 0; 1; 2 |] "only the columns the step reads are carried" ]
