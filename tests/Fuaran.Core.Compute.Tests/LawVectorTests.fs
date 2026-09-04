module Fuaran.Core.Tests.LawVectorTests

open System.IO
open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  The exported `transformLaws` reference vectors, checked from both ends.
//
//  `LawVectorExport` renders them; nothing here writes into the shared corpus.
//  Emitting is an explicit command (`--emit-laws <dir>`), because the corpus is
//  a separate repository and a suite that wrote into it on every run would
//  dirty a shared clone.
//
//  Three claims, and the order matters:
//
//   1. The sample IS one the law certifies. The export publishes the answers
//      `transformLaws` compares a host against, so a sample the law would not
//      pass must never reach the corpus. The law is run here over the exact
//      generator and seed the file declares.
//   2. Every vector is CONSUMABLE and TRUE. Each is decoded back through the
//      public wire codecs — the ones a host has — evaluated with the reference,
//      and its recorded answer checked. This is the leg that would catch a
//      perturbed `expected`, and it names the vector.
//   3. The committed file is CURRENT. When the corpus is checked out beside
//      this repository, the bytes it holds must be the bytes the emitter
//      renders now; otherwise a kit change silently leaves the corpus
//      describing an evaluator that no longer exists.
// ---------------------------------------------------------------------------

/// The corpus's `laws/` directory, when it is checked out alongside — the same climb
/// `IdlSpikeTests` uses for the `nodes/` family.
let private tryFindLawsDir () : string option =
    let candidates (root: string) =
        [ Path.Combine(root, "Fuaran-UI", "wire-format-fixtures", "laws")
          Path.Combine(root, "wire-format-fixtures", "laws") ]

    let rec climb (dir: string) (budget: int) =
        if budget < 0 || isNull dir then
            None
        else
            match candidates dir |> List.tryFind Directory.Exists with
            | Some d -> Some d
            | None ->
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); System.AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)

let private field (name: string) (el: JVal) : JVal option =
    match el with
    | JObj ms -> ms |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
    | _ -> None

let private str (name: string) (el: JVal) : string option =
    match field name el with
    | Some(JStr s) -> Some s
    | _ -> None

/// One parsed vector, reduced to the four things a check reads.
type private ParsedVector =
    { Id: string
      Pipeline: string
      Source: string
      Verdict: string
      Table: string option }

let private parseVectors (json: string) : Result<ParsedVector list, string> =
    match Json.parse json with
    | Error m -> Error("the vector file did not parse: " + m)
    | Ok doc ->
        match field "vectors" doc with
        | Some(JArr items) ->
            let parsed =
                items
                |> List.map (fun v ->
                    match str "id" v, field "input" v, field "expected" v with
                    | Some id, Some input, Some expected ->
                        match str "pipeline" input, str "source" input, str "verdict" expected with
                        | Some p, Some s, Some verdict ->
                            Ok
                                { Id = id
                                  Pipeline = p
                                  Source = s
                                  Verdict = verdict
                                  Table = str "table" expected }
                        | _ -> Error("vector " + id + ": input.pipeline / input.source / expected.verdict missing")
                    | _ -> Error "a vector is missing id / input / expected")

            match
                parsed
                |> List.tryPick (function
                    | Error m -> Some m
                    | Ok _ -> None)
            with
            | Some m -> Error m
            | None ->
                Ok
                    [ for p in parsed do
                          match p with
                          | Ok v -> yield v
                          | Error _ -> () ]
        | _ -> Error "the vector file carries no `vectors` array"

/// Run one parsed vector the way a host would: decode both halves with the public codecs,
/// evaluate with the reference, and report what disagreed with the recorded answer.
let private checkVector (v: ParsedVector) : string option =
    match DataFrameCodec.decodePipeline v.Pipeline, ColumnCodec.decode v.Source with
    | Error e, _ -> Some(sprintf "%s: input.pipeline did not decode (%s)" v.Id (ColumnCodec.errorString e))
    | _, Error e -> Some(sprintf "%s: input.source did not decode (%s)" v.Id (ColumnCodec.errorString e))
    | Ok pipeline, Ok src ->
        match src with
        | Embedded table ->
            match DataFrame.evalPipeline pipeline table, v.Verdict, v.Table with
            | Ok t, "ok", Some expected ->
                let actual = ColumnCodec.encode (Embedded t)

                if actual = expected then
                    None
                else
                    Some(sprintf "%s: the reference answered\n  %s\nbut the vector records\n  %s" v.Id actual expected)
            | Ok t, "ok", None -> Some(sprintf "%s: an `ok` vector carries no expected table (reference: %A)" v.Id t)
            | Ok _, verdict, _ ->
                Some(sprintf "%s: the reference evaluated the pipeline, but the vector says `%s`" v.Id verdict)
            | Error _, "error", _ -> None
            | Error e, verdict, _ ->
                Some(sprintf "%s: the reference refused the pipeline (%A), but the vector says `%s`" v.Id e verdict)
        | other -> Some(sprintf "%s: input.source is not an embedded table (%A)" v.Id other)

[<Tests>]
let tests =
    testList
        "LawVectors"
        [

          testCase "the exported sample is one transformLaws certifies"
          <| fun _ ->
              // The file publishes the answers the law compares a host against, so publishing a
              // sample the law itself would not pass would be publishing an oracle nobody checked.
              let results =
                  Conformance.transformLaws
                      DataFrame.evalPipeline
                      (LawVectorExport.lawGen ())
                      LawVectorExport.seed
                      LawVectorExport.iterations

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "every rendered vector decodes with the public codecs and records what the reference answered"
          <| fun _ ->
              // The leg that makes the file an oracle rather than a blob: it is read back exactly
              // as a host reads it — `decodePipeline` + `ColumnCodec.decode`, no private access —
              // and the recorded answer is recomputed rather than trusted.
              match parseVectors (LawVectorExport.renderTransformVectors ()) with
              | Error m -> failtest m
              | Ok vectors ->
                  Expect.equal (List.length vectors) LawVectorExport.iterations "one vector per declared iteration"

                  let failures = vectors |> List.choose checkVector
                  Expect.isEmpty failures (sprintf "%A" failures)

          testCase "the rendered vectors reach every declared shape, and both verdicts"
          <| fun _ ->
              // A corpus artefact that silently stopped carrying a shape would leave a host
              // certifying less than the file's description claims — the adequacy lesson applied to
              // an exported sample.
              match parseVectors (LawVectorExport.renderTransformVectors ()) with
              | Error m -> failtest m
              | Ok vectors ->
                  for shape in LawVectorExport.shapeNames do
                      Expect.isTrue
                          (vectors |> List.exists (fun v -> v.Id.EndsWith shape))
                          (sprintf "no vector was rendered for the `%s` shape" shape)

                  Expect.isTrue (vectors |> List.exists (fun v -> v.Verdict = "ok")) "an accepted pipeline was rendered"

                  Expect.isTrue
                      (vectors |> List.exists (fun v -> v.Verdict = "error"))
                      "a refused pipeline was rendered — the law requires the host to refuse it too"

          testCase "the rendered artefact is LF-only and byte-stable across renders"
          <| fun _ ->
              let once = LawVectorExport.renderTransformVectors ()
              let twice = LawVectorExport.renderTransformVectors ()
              Expect.equal once twice "two renders of the same pin produce the same bytes"

              Expect.isFalse
                  (once.Contains "\r")
                  "the corpus is byte-compared across three operating systems — no CR may reach it"

          testCase "the committed corpus vectors are the ones this kit renders"
          <| fun _ ->
              match tryFindLawsDir () with
              | None -> skiptest "wire-format-fixtures not checked out alongside — corpus comparison skipped"
              | Some dir ->
                  let path = Path.Combine(dir, LawVectorExport.transformFileName)

                  if not (File.Exists path) then
                      skiptest "the corpus carries no transform-laws.json yet — comparison skipped"
                  else
                      // Read as bytes-to-text without newline translation: the file is LF and the
                      // comparison is about bytes.
                      let committed = File.ReadAllText path

                      match parseVectors committed with
                      | Error m -> failtest ("the committed vectors did not read: " + m)
                      | Ok vectors ->
                          // First the oracle question — is what the corpus records still true of
                          // this kit? — because that is the failure a host would suffer.
                          let failures = vectors |> List.choose checkVector

                          Expect.isEmpty
                              failures
                              (sprintf
                                  "the committed corpus vectors disagree with this kit's reference evaluator: %A"
                                  failures)

                          // Then the freshness question. Distinct from the above: a rendering
                          // change (a new shape, a reworded description) leaves every vector true
                          // and the file stale.
                          Expect.equal
                              committed
                              (LawVectorExport.renderTransformVectors ())
                              "the committed corpus file is not what this kit renders — re-run `--emit-laws <corpus dir>`" ]
