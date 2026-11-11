/// Phase 376 — the worker pool held to its laws (`PooledLaw`) on both hosts the gate runs: on .NET
/// over workers on the thread pool, and under node over `worker_threads` (the node leg: the harness
/// beside this file in `Node/` compiled with Fable and run, every job and reply through a structured
/// clone). Both run every law: the pool's answers are the sequential member's, step by step and over
/// whole pipelines, with the caller draining beside the workers and with the workers alone; the
/// shifted and swapped pools go red; and, under node, a realm without cross-origin isolation refuses
/// the opt-in and answers as the sequential member does.
module Fuaran.Compute.Tests.PooledLawTests

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open Expecto
open Fuaran.Core
open Fuaran.Compute

/// A worker on the .NET thread pool: each job runs on a task, through the worker's side of the pool
/// (`WorkerPool.serveWith`) with the span named `span`, and replies from that task.
let private inProcess (span: string) () : MorselWorker =
    let handler = ref (fun (_: obj) -> ())

    { new MorselWorker with
        member _.Post message =
            let handle = handler.Value
            Task.Run(fun () -> handle message) |> ignore

        member _.Listen hear =
            handler.Value <- WorkerPool.serveWith (PooledLaw.spanOf span) hear

        member _.Stop() = () }

/// Run `fileName arguments` in `cwd`, answering its exit code and its output.
let private run (fileName: string) (arguments: string) (cwd: string) : int * string =
    let psi = ChildProcess.redirected fileName arguments
    psi.WorkingDirectory <- cwd
    use p = Process.Start psi
    let err = p.StandardError.ReadToEndAsync()
    let out = p.StandardOutput.ReadToEnd()
    p.WaitForExit()
    p.ExitCode, out + err.Result

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 376 - the worker pool"
        [ testCase "the pool's laws hold on .NET, over workers on the thread pool"
          <| fun () ->
              let lines = ResizeArray<string>()
              let held = PooledLaw.laws inProcess 3 lines.Add |> Async.RunSynchronously
              Expect.isTrue held (String.Join("\n", lines))

          testCase "a host that has not opted in reaches no pool"
          <| fun () ->
              WorkerPool.optOut ()
              Expect.isFalse (WorkerPool.isActive ()) "not opted in"

              let frame = Frame.ofTable (MorselHandOffLaw.rowTable 100_000)
              let step = Filter(Binary(Gt, Binary(Add, Col "a", Col "b"), Lit(Int 500)))
              Expect.isNone (DataFrame.pooledStep Map.empty frame step) "no pool, no pooled step"

          // The node leg (Phase 376): the same laws under the JavaScript host, over real workers and
          // shared memory. It needs the repository's own Fable (the tool manifest pins it) and node
          // on PATH; it fails, not skips, without them, because the gate claims the leg.
          testCase "the pool's laws hold under node, over worker_threads (Fable)"
          <| fun () ->
              let root = Snapshots.repoFile ""

              let project =
                  Path.Combine(root, "tests", "Fuaran.Core.Compute.Tests", "Node", "PooledLawNode.fsproj")

              let key =
                  Convert
                      .ToHexString(Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes root))
                      .Substring(0, 12)
                      .ToLowerInvariant()

              // Keyed on the checkout, so two checkouts never wipe each other's output. The layout
              // under it mirrors the repository's, because Fable writes each linked source at its
              // path relative to the project: the suite's law files land beside the output
              // directory, not under it, and a mirrored root keeps them inside the scratch area.
              let scratch = Path.Combine(Path.GetTempPath(), "fuaran-compute-pooled-node-" + key)
              let out = Path.Combine(scratch, "tests", "Fuaran.Core.Compute.Tests", "Node")

              if Directory.Exists scratch then
                  Directory.Delete(scratch, true)

              Directory.CreateDirectory out |> ignore

              try
                  let code, transcript =
                      run "dotnet" (sprintf "fable \"%s\" -o \"%s\" --noCache" project out) root

                  Expect.equal code 0 ("the Fable compile of the node leg failed:\n" + transcript)
                  // The emitted modules are ECMAScript modules, whatever node version runs them.
                  File.WriteAllText(Path.Combine(scratch, "package.json"), "{ \"type\": \"module\" }")
                  let code, transcript = run "node" "PooledLawNode.js" out
                  Expect.equal code 0 ("the node leg's laws failed:\n" + transcript)
                  Expect.stringContains transcript "pooled laws under node: held" "the node leg ran to its end"

                  // Phase 592 — the regression vectors' answers under node are this host's, line for
                  // line: the estimates, the statistics, the decimal outputs and the refusals.
                  let lines = transcript.Replace("\r\n", "\n").Split('\n')
                  let first = Array.IndexOf(lines, "BEGIN fit-vectors")
                  let last = Array.IndexOf(lines, "END fit-vectors")
                  Expect.isTrue (first >= 0 && last > first) "the node leg printed the fit vectors"
                  let underNode = lines[first + 1 .. last - 1]
                  let onDotnet = TransformVectorSample.fitRender().Split('\n')
                  Expect.equal underNode.Length onDotnet.Length "one line per fit shape"

                  for i in 0 .. onDotnet.Length - 1 do
                      Expect.equal underNode[i] onDotnet[i] (sprintf "under node, fit vector %d differs from .NET" i)

                  // Phase 621 — and the seeded steps' vectors: every draw, assignment, centre, replicate
                  // and interval under node is this host's, line for line.
                  let first = Array.IndexOf(lines, "BEGIN seeded-vectors")
                  let last = Array.IndexOf(lines, "END seeded-vectors")
                  Expect.isTrue (first >= 0 && last > first) "the node leg printed the seeded vectors"
                  let underNode = lines[first + 1 .. last - 1]
                  let onDotnet = TransformVectorSample.seededRender().Split('\n')
                  Expect.equal underNode.Length onDotnet.Length "one line per seeded shape"

                  Expect.isTrue
                      (onDotnet |> Array.forall (fun line -> line.Contains " round-trips "))
                      "every seeded pipeline round-trips its wire on .NET"

                  for i in 0 .. onDotnet.Length - 1 do
                      Expect.equal underNode[i] onDotnet[i] (sprintf "under node, seeded vector %d differs from .NET" i)
              finally
                  try
                      Directory.Delete(scratch, true)
                  with _ ->
                      () ]
