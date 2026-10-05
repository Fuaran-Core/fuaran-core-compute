/// Phase 343 — independent pipelines evaluated concurrently over one prepared source:
/// `DataFrame.evalManyToPrepared` held to `evalToPrepared` pipeline for pipeline, and the kernel
/// pair's batch runner held to its contract (each pipeline once, the work threshold, the nesting
/// bound).
module Fuaran.Compute.Tests.ConcurrentPipelinesTests

open System.Threading
open Expecto
open Fuaran.Core
open Fuaran.Compute

/// The native member running every batch of two or more alongside, whatever its work, so that the
/// law's small sources run the concurrent path rather than the threshold's sequential one.
let private alongsideAlways: KernelSet =
    { Kernels.native with
        RunPipelines = Kernels.Native.runPipelinesAt 0 }

/// The members a batch is run through: both members of the pair, the native member alongside at
/// any work (and the same partitioning from two rows, so a pipeline that WOULD partition is run
/// alongside), and the one-thread set a pipeline alongside others evaluates through.
let private members: (string * KernelSet) list =
    [ "portable", Kernels.portable
      "native", Kernels.native
      "native alongside at any work", alongsideAlways
      "native from 2 rows, alongside at any work",
      { Kernels.nativeFrom 2 with
          RunPipelines = Kernels.Native.runPipelinesAt 0 }
      "one thread", Kernels.oneThread ]

/// An answer as the text the law compares: the kept result's table as canonical wire, or the
/// refusal's text.
let private wire (r: Result<Prepared, EvalError>) : string =
    match r with
    | Ok p -> "ok " + ColumnCodec.encode (Embedded(DataFrame.toTable p))
    | Error e -> "error " + DataFrame.errorString e

/// The generated lists: for each transform vector's source, a list of the sample's pipelines drawn
/// by a fixed linear congruential generator — empty, one, or up to 23 long, repeats allowed, the
/// vector's own pipeline always among them — so most lists mix answers with refusals (a pipeline
/// drawn from another vector usually names columns this source lacks).
let private generatedLists () : (Prepared * Transform list list) list =
    let pairs = PreparedResultLaw.sample () |> Array.ofList
    let pipelines = pairs |> Array.map snd
    let mutable state = 343u

    let next (bound: int) =
        state <- state * 1664525u + 1013904223u
        int ((state >>> 8) % uint32 bound)

    [ for i, (table, own) in Array.indexed pairs do
          let length = if i % 11 = 0 then 0 else next 24

          let drawn = List.init length (fun _ -> pipelines[next pipelines.Length])

          let list =
              if length = 0 then
                  []
              else
                  let at = next (length + 1)
                  List.take at drawn @ [ own ] @ List.skip at drawn

          yield DataFrame.prepare table, list ]

[<Tests>]
let concurrentPipelinesTests =
    testList
        "independent pipelines evaluated concurrently (Phase 343)"
        [ testCase
              "the law: over generated lists of the transform vectors' pipelines, every member's batch answers evalToPrepared's answers element for element, each refusal its own pipeline's"
          <| fun _ ->
              let lists = generatedLists ()
              let mutable compared = 0
              let mutable answered = 0
              let mutable refused = 0
              let mutable ranAlongside = 0

              for li, (source, list) in List.indexed lists do
                  let reference =
                      list
                      |> List.map (fun p -> DataFrame.evalToPrepared DataFrame.noResolve Map.empty p source)

                  let expected = reference |> List.map wire

                  for r in reference do
                      match r with
                      | Ok _ -> answered <- answered + 1
                      | Error _ -> refused <- refused + 1

                  let batches =
                      ("the entry point", DataFrame.evalManyToPrepared DataFrame.noResolve Map.empty list source)
                      :: [ for name, k in members ->
                               name, DataFrame.evalManyToPreparedWith k DataFrame.noResolve Map.empty list source ]

                  for name, results in batches do
                      Expect.equal results.Length list.Length (sprintf "list %d, %s: one result a pipeline" li name)

                      // The errors as values, not only as text: the same refusal, the first one.
                      for i, (got, want) in List.indexed (List.zip results reference) do
                          match got, want with
                          | Error a, Error b ->
                              Expect.equal a b (sprintf "list %d, %s, pipeline %d: the same refusal" li name i)
                          | _ -> ()

                      Expect.equal
                          (List.map wire results)
                          expected
                          (sprintf "list %d, %s: the batch answers evalToPrepared's answers in list order" li name)

                      compared <- compared + results.Length

                  if list.Length >= 2 then
                      ranAlongside <- ranAlongside + 1

              Expect.isGreaterThan answered 100 "the lists carry answers, not only refusals"
              Expect.isGreaterThan refused 100 "the lists carry refusals, so per-pipeline errors are exercised"
              Expect.isGreaterThan ranAlongside (lists.Length / 2) "most lists ran alongside on the native member"
              Expect.isGreaterThan compared (lists.Length * 20) "the law compared the lists, not a handful"

          testCase "a result kept by the batch serves as the next pipeline's source as evalToPrepared's does"
          <| fun _ ->
              let table =
                  { Schema = [ "k", IntType; "v", FloatType ]
                    Columns =
                      [ Column.create "k" IntType [ for i in 0..199 -> Int(i % 7) ]
                        Column.create "v" FloatType [ for i in 0..199 -> Float(float i * 0.5) ] ] }

              let source = DataFrame.prepare table

              let list =
                  [ [ Filter(Binary(Gt, Col "v", Lit(Float 20.0))) ]
                    [ Transform.sortBy [ "v", Desc ]; Transform.limit 5 0 ]
                    [ GroupBy([ "k" ], [ { Name = "n"; Fn = Count; Of = "v" } ]) ] ]

              let next = [ Derive("w", Binary(Mul, Col "v", Lit(Float 2.0))) ]

              for name, k in members do
                  let results =
                      DataFrame.evalManyToPreparedWith k DataFrame.noResolve Map.empty list source

                  for i, (r, p) in List.indexed (List.zip results list) do
                      let kept =
                          DataFrame.evalToPrepared DataFrame.noResolve Map.empty p source
                          |> Result.defaultWith (fun e -> failtestf "%s" (DataFrame.errorString e))

                      let mine =
                          r |> Result.defaultWith (fun e -> failtestf "%s" (DataFrame.errorString e))

                      Expect.equal
                          (wire (DataFrame.evalToPrepared DataFrame.noResolve Map.empty next mine))
                          (wire (DataFrame.evalToPrepared DataFrame.noResolve Map.empty next kept))
                          (sprintf "%s, pipeline %d: the next hop answers alike" name i)

          testCase
              "the batch runner runs every pipeline once; the portable member and the threshold run them in order on the caller's thread"
          <| fun _ ->
              let caller = Thread.CurrentThread.ManagedThreadId

              for n in [ 0; 1; 2; 7; 64 ] do
                  for name, k, work, expectAlongside in
                      [ "portable", Kernels.portable, System.Int32.MaxValue, false
                        "native below the threshold", Kernels.native, Kernels.PipelineRows - 1, false
                        "native at the threshold", Kernels.native, Kernels.PipelineRows, n >= 2
                        "one thread", Kernels.oneThread, System.Int32.MaxValue, false ] do
                      let ran: int[] = Array.zeroCreate n
                      let flags: bool[] = Array.zeroCreate n
                      let order = System.Collections.Concurrent.ConcurrentQueue<int>()
                      let threads = System.Collections.Concurrent.ConcurrentBag<int>()

                      k.RunPipelines n work (fun alongside i ->
                          Interlocked.Increment(&ran[i]) |> ignore
                          flags[i] <- alongside
                          order.Enqueue i
                          threads.Add Thread.CurrentThread.ManagedThreadId)

                      Expect.equal ran (Array.create n 1) (sprintf "%s, %d pipelines: each run once" name n)

                      Expect.equal
                          flags
                          (Array.create n expectAlongside)
                          (sprintf "%s, %d pipelines: alongside exactly when the batch runs on the pool" name n)

                      if not expectAlongside then
                          Expect.equal
                              (List.ofSeq order)
                              [ 0 .. n - 1 ]
                              (sprintf "%s, %d pipelines: in list order" name n)

                          Expect.isTrue
                              (threads |> Seq.forall ((=) caller))
                              (sprintf "%s, %d pipelines: on the caller's thread" name n)

          testCase
              "the nesting bound: a batch alongside holds at most one pipeline per logical processor, and the set it runs through fans out nothing"
          <| fun _ ->
              let inFlight = ref 0
              let most = ref 0

              alongsideAlways.RunPipelines 64 System.Int32.MaxValue (fun alongside _ ->
                  Expect.isTrue alongside "a batch of 64 runs alongside"
                  let now = Interlocked.Increment(&inFlight.contents)

                  let mutable seen = most.Value

                  while now > seen && Interlocked.CompareExchange(&most.contents, now, seen) <> seen do
                      seen <- most.Value

                  Thread.Sleep 2
                  Interlocked.Decrement(&inFlight.contents) |> ignore)

              Expect.isLessThanOrEqual
                  most.Value
                  System.Environment.ProcessorCount
                  "at most one pipeline per logical processor at once"

              // The set a pipeline alongside others runs through: no partitions at any size, its
              // morsels and any batch of its own on the caller's thread.
              let k = Kernels.oneThread
              Expect.equal (k.Partitions System.Int32.MaxValue) 1 "one thread never partitions"
              let caller = Thread.CurrentThread.ManagedThreadId
              let threads = System.Collections.Concurrent.ConcurrentBag<int>()

              k.RunMorsels 64 (fun _ ->
                  threads.Add Thread.CurrentThread.ManagedThreadId
                  true)

              Expect.equal threads.Count 64 "every morsel ran"
              Expect.isTrue (threads |> Seq.forall ((=) caller)) "one thread runs its morsels on the caller's thread"
              let keys = [| 5.0; 1.0; 3.0; 2.0; 4.0 |]
              k.SortFinite keys
              Expect.equal keys [| 1.0; 2.0; 3.0; 4.0; 5.0 |] "one thread sorts as every member does" ]
