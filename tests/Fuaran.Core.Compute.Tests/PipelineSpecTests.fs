module Fuaran.Compute.Tests.PipelineSpecTests

// ---------------------------------------------------------------------------
// Phase 604 (`DECISIONS.md` D83) — this repository certified against the pipeline wire
// specification's corpus, and `Pipeline.hash` held to its laws.
//
// The corpus is resolved by `PipelineSpecCorpus.resolve` and an absent corpus FAILS here: the gate
// claims the certification, so a machine without the specification beside it is red, never green over
// nothing. The certification runs every vector the manifest enumerates, asserts the count run equals
// the count enumerated, and proves once that a mutated document goes red (the specification's
// conventions, §9.3 of its text).
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Expecto
open Fuaran.Core
open Fuaran.Compute

let private sha256Hex (bytes: byte[]) =
    SHA256.HashData bytes
    |> Array.map (fun b -> b.ToString "x2")
    |> String.concat ""

let private member' (name: string) (v: JVal) : JVal option =
    match v with
    | JObj fields -> fields |> List.tryPick (fun (k, x) -> if k = name then Some x else None)
    | _ -> None

let private str (name: string) (v: JVal) : string =
    match member' name v with
    | Some(JStr s) -> s
    | other -> failwithf "manifest entry has no string member %s (%A)" name other

/// The corpus root, or a failure naming every path tried — never a skip.
let private corpusRoot () : string =
    match PipelineSpecCorpus.resolve () with
    | Ok root -> root
    | Error why -> failtest why

let private manifestOf (root: string) : JVal =
    match Json.parse (File.ReadAllText(Path.Combine(root, "wire-fixtures", "manifest.json"))) with
    | Ok v -> v
    | Error e -> failtestf "the corpus's manifest is not JSON: %A" e

/// Certify ONE vector: `None` when this repository's reader and hash do what the manifest says.
let internal certify (refusals: Map<string, ColumnError -> bool>) (entry: JVal) (bytes: byte[]) : string option =
    let id = str "id" entry
    let text = Encoding.UTF8.GetString bytes

    if sha256Hex bytes <> str "sha256" entry then
        Some(sprintf "%s: the file's bytes are not the ones the manifest indexes" id)
    else
        match str "kind" entry, DataFrameCodec.decodePipeline text with
        | ("hash" | "round-trip"), Ok p ->
            let again = DataFrameCodec.encodePipeline p

            if again <> text then
                Some(sprintf "%s: re-encoded to different bytes\n  file: %s\n  ours: %s" id text again)
            elif str "kind" entry = "hash" then
                match Pipeline.hash p with
                | Ok h when h = str "digest" entry -> None
                | Ok h -> Some(sprintf "%s: hash %s, the manifest records %s" id h (str "digest" entry))
                | Error e -> Some(sprintf "%s: the hash refused a corpus document: %s" id e)
            else
                None
        | "accept", Ok p ->
            let canonical = DataFrameCodec.encodePipeline p

            if canonical <> str "canonical" entry then
                Some(sprintf "%s: read as %s, the manifest records %s" id canonical (str "canonical" entry))
            else
                match Pipeline.hash p with
                | Ok h when h = str "digest" entry -> None
                | Ok h -> Some(sprintf "%s: hash %s, the manifest records %s" id h (str "digest" entry))
                | Error e -> Some(sprintf "%s: the hash refused an accepted document: %s" id e)
        | "reject", Ok _ -> Some(sprintf "%s: a reject vector was READ" id)
        | "reject", Error e ->
            match refusals.TryFind id with
            | Some refused when refused e -> None
            | Some _ -> Some(sprintf "%s: refused for another reason than %s: %A" id (str "reject" entry) e)
            | None -> Some(sprintf "%s: a reject vector this repository declares no refusal for" id)
        | kind, Error e -> Some(sprintf "%s: a %s vector was refused: %A" id kind e)
        | kind, Ok _ -> Some(sprintf "%s: unknown vector kind %s" id kind)

let private refusals () =
    PipelineSpecCorpus.rejectVectors ()
    |> List.choose (fun v -> v.Refused |> Option.map (fun r -> v.Id, r))
    |> Map.ofList

/// Every `$type` a canonical text carries.
let rec private tags (v: JVal) : string list =
    match v with
    | JObj fields ->
        (fields
         |> List.choose (fun (k, x) ->
             match k, x with
             | "$type", JStr t -> Some t
             | _ -> None))
        @ (fields |> List.collect (snd >> tags))
    | JArr xs -> xs |> List.collect tags
    | _ -> []

let private topTags (text: string) =
    match Json.parse text with
    | Ok(JArr steps) ->
        steps
        |> List.choose (fun s ->
            match member' "$type" s with
            | Some(JStr t) -> Some t
            | _ -> None)
    | _ -> []

let private allTags (text: string) =
    match Json.parse text with
    | Ok v -> tags v
    | Error _ -> []

[<Tests>]
let tests =
    testList
        "Phase 604 - the pipeline wire specification"
        [ testCase "the specification's corpus is present: absent, the gate fails"
          <| fun () ->
              let root = corpusRoot ()

              Expect.isTrue
                  (File.Exists(Path.Combine(root, "PIPELINE_WIRE.md")))
                  "the checkout carries the normative text"

          testCase "an absent corpus is a failure naming the variable and the remedy, never a skip"
          <| fun () ->
              let nowhere =
                  Path.Combine(Path.GetTempPath(), "no-pipeline-specification-" + Guid.NewGuid().ToString "N")

              match PipelineSpecCorpus.resolveWith (Some nowhere) (Snapshots.repoFile "") with
              | Ok r -> failtestf "a directory that does not exist resolved as the corpus: %s" r
              | Error why ->
                  Expect.stringContains why PipelineSpecCorpus.variable "the refusal names the variable"
                  Expect.stringContains why "git clone" "the refusal names the remedy"

          testCase "the committed corpus is exactly what the wire-surface harness emits"
          <| fun () ->
              let fixtures = Path.Combine(corpusRoot (), "wire-fixtures")
              let emitted = PipelineSpecCorpus.emission ()

              let stale =
                  [ for rel, text in emitted do
                        let path = Path.Combine(fixtures, rel)

                        if not (File.Exists path) then
                            yield sprintf "missing: %s" rel
                        elif File.ReadAllText path <> text then
                            yield sprintf "differs: %s" rel ]

              let expected = emitted |> List.map fst |> set

              let orphans =
                  [ for family in PipelineSpecCorpus.familyOrder do
                        let dir = Path.Combine(fixtures, family)

                        if Directory.Exists dir then
                            for f in Directory.GetFiles(dir, "*.json") do
                                let rel = family + "/" + Path.GetFileName f

                                if not (expected.Contains rel) then
                                    yield sprintf "not emitted: %s" rel ]

              Expect.isEmpty
                  (stale @ orphans)
                  (sprintf
                      "the specification's corpus is not this repository's emission. A Transform or ColExpr change lands in the codec, the text, the schemas, the corpus and the manifest in ONE change-set (D83): regenerate with\n  dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-pipeline-corpus <specification root>\nthen update PIPELINE_WIRE.md and the schema to match.\n%s"
                      (String.concat "\n" (stale @ orphans)))

          testCase "every vector the manifest enumerates certifies, and the count run is the count enumerated"
          <| fun () ->
              let root = corpusRoot ()
              let manifest = manifestOf root
              let refusals = refusals ()

              let entries =
                  match member' "vectors" manifest with
                  | Some(JArr xs) -> xs
                  | _ -> failtest "the manifest has no vectors"

              let mutable ran = 0

              let failures =
                  [ for e in entries do
                        let bytes = File.ReadAllBytes(Path.Combine(root, "wire-fixtures", str "file" e))

                        ran <- ran + 1

                        match certify refusals e bytes with
                        | Some f -> yield f
                        | None -> () ]

              Expect.isEmpty failures (String.concat "\n" failures)
              Expect.equal ran entries.Length "every enumerated vector ran"
              Expect.isGreaterThan ran 0 "the manifest enumerates vectors"

              Expect.equal
                  (str "hashAlgorithm" manifest)
                  Pipeline.hashAlgorithm
                  "the corpus mints under this repository's identifier"

          testCase "a mutated document makes the certification go red"
          <| fun () ->
              let root = corpusRoot ()

              let entries =
                  match member' "vectors" (manifestOf root) with
                  | Some(JArr xs) -> xs
                  | _ -> failtest "the manifest has no vectors"

              let hashEntry = entries |> List.find (fun e -> str "kind" e = "hash")

              let bytes =
                  File.ReadAllBytes(Path.Combine(root, "wire-fixtures", str "file" hashEntry))

              Expect.isNone (certify (refusals ()) hashEntry bytes) "the unmutated vector certifies"

              // The same document with one member renamed, and re-indexed so only the content can fail.
              let mutated =
                  Encoding.UTF8.GetBytes((Encoding.UTF8.GetString bytes).Replace("\"$type\"", "\"$tyqe\""))

              let reindexed =
                  match hashEntry with
                  | JObj fields ->
                      JObj(
                          fields
                          |> List.map (fun (k, v) -> if k = "sha256" then k, JStr(sha256Hex mutated) else k, v)
                      )
                  | other -> other

              Expect.isSome (certify (refusals ()) reindexed mutated) "a mutated document goes red"

              // And a reject vector that a reader READ would go red too.
              let rejectEntry = entries |> List.find (fun e -> str "kind" e = "reject")
              let readable = Encoding.UTF8.GetBytes """[{"$type":"distinct"}]"""

              let asReject =
                  match rejectEntry with
                  | JObj fields ->
                      JObj(
                          fields
                          |> List.map (fun (k, v) -> if k = "sha256" then k, JStr(sha256Hex readable) else k, v)
                      )
                  | other -> other

              Expect.isSome (certify (refusals ()) asReject readable) "a reject vector that reads goes red"

          testCase "Pipeline.hash is the content address of the incremental state's pipeline key"
          <| fun () ->
              for id, p in PipelineHashSample.pipelines do
                  match Pipeline.hash p with
                  | Ok h -> Expect.equal h ("sha256:" + IncrementalCodec.pipelineHash p) id
                  | Error _ -> ()

          testCase "a pipeline with no canonical bytes has no hash"
          <| fun () ->
              let refused =
                  PipelineHashSample.pipelines
                  |> List.filter (fun (id, _) -> id.StartsWith "refused-")

              Expect.equal refused.Length 3 "the three refused samples"

              for id, p in refused do
                  Expect.isError (Pipeline.hash p) id

          testCase "two spellings of one pipeline mint one hash; two pipelines mint two"
          <| fun () ->
              for v in PipelineSpecCorpus.acceptVectors () do
                  match DataFrameCodec.decodePipeline v.Text with
                  | Ok p ->
                      let canonical = DataFrameCodec.encodePipeline p

                      match DataFrameCodec.decodePipeline canonical with
                      | Ok q -> Expect.equal (Pipeline.hash p) (Pipeline.hash q) v.Id
                      | Error e -> failtestf "%s: the canonical text does not read back: %A" v.Id e
                  | Error e -> failtestf "%s: an accept vector was refused: %A" v.Id e

              let hashes =
                  PipelineHashSample.pipelines
                  |> List.choose (fun (_, p) -> Pipeline.hash p |> Result.toOption)

              Expect.equal (List.distinct hashes).Length hashes.Length "distinct samples, distinct hashes"

          testCase "the declared pipelines reach every step kind and every expression kind the harness reaches"
          <| fun () ->
              let harness = PipelineSpecCorpus.harnessVectors ()

              let stepTags =
                  harness
                  |> List.filter (fun v -> v.Family = "step")
                  |> List.collect (fun v -> topTags v.Text)
                  |> set

              let exprTags =
                  harness
                  |> List.filter (fun v -> v.Family = "expr")
                  |> List.collect (fun v -> allTags v.Text)
                  |> set

              let sampled =
                  PipelineHashSample.pipelines
                  |> List.choose (fun (_, p) -> Pipeline.canonicalText p |> Result.toOption)
                  |> List.collect allTags
                  |> set

              Expect.isEmpty
                  (Set.difference (Set.union stepTags exprTags) sampled)
                  "every step and expression kind is in the declared sample the node leg hashes" ]
