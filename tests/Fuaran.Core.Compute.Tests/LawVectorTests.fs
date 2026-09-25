module Fuaran.Core.Tests.LawVectorTests

open System
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

// ---------------------------------------------------------------------------
//  Phase 216 — the corpus copy's TWO readings, told apart.
// ---------------------------------------------------------------------------
//  `kitVersion` is DERIVED from `<Version>`, so a version move restales the corpus copy without
//  any vector changing. That is a different fact from the copy describing an evaluator this kit no
//  longer is, and until this phase both arrived as one sentence — "the corpus copy is STALE" — in
//  a run belonging to somebody else.
//
//   * STAMP ONLY. The vectors are byte-identical and only the derived stamp differs. Nothing about
//     what a host certifies has changed; the two repositories are out of lockstep and the remedy is
//     the re-stamp. This is the common case and the one a version move causes every single time.
//   * THE VECTORS DIFFER. The copy records answers this kit's reference evaluator no longer gives,
//     or carries vectors it no longer renders. The five hosts certify against that copy, so what is
//     published is an oracle nobody checked.
//
//  Both are reported here and both are fatal where the leg is asked for; they are never reported as
//  the same thing. The classification is deliberately the REGISTRY's equality (`fingerprint`,
//  `roadmapctl copies`) REFINED rather than a second notion of freshness beside it: a reading is
//  taken only once the fingerprints have already disagreed, and it is taken from the registry's own
//  normalisation, which is why the function below is that one and not a copy of it.

/// The rendered member carrying the derived stamp.
[<Literal>]
let private stampMember = "kitVersion"

let private emitHere = "dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws"

let private emitCopy =
    "dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws <corpus dir>"

/// The registry's own normalisation, not a second one beside it: `OwnedConformance.fingerprint` IS
/// `String.concat "\n"` of these lines, so the reading below refines the equality
/// `roadmapctl copies` applies rather than inventing a neighbouring one that could drift from it.
let private fingerprintLines = OwnedConformance.fingerprintLines

// Ordinal throughout, for the reason `OwnedConformance.fingerprintLines` records: the
// culture-sensitive overload of `StartsWith` is not the question anyone means to ask of a
// rendered JSON member.
let private isStampLine (line: string) =
    line.TrimStart().StartsWith("\"" + stampMember + "\"", StringComparison.Ordinal)

/// The stamp as the file itself declares it — read back through the parser rather than off the
/// line, so a report never quotes a version the document does not actually carry.
let private stampOf (text: string) : string option =
    match Json.parse text with
    | Ok doc -> str stampMember doc
    | Error _ -> None

/// A readable window onto one long line, centred on `col`. Centred rather than truncated because
/// a vector line is a whole wire string: two of them shown from the left look identical for a
/// hundred characters and the reader learns nothing about where they parted.
let private window (col: int) (line: string) : string =
    let start = max 0 (col - 40)
    let len = min 110 (line.Length - start)

    (if start > 0 then "… " else "")
    + line.Substring(start, len)
    + (if start + len < line.Length then " …" else "")

/// The first character position at which two lines part.
let private firstDivergence (a: string) (b: string) : int =
    let shared = min a.Length b.Length

    match Seq.tryFindIndex (fun i -> a[i] <> b[i]) (seq { 0 .. shared - 1 }) with
    | Some i -> i
    | None -> shared

/// What the corpus copy is, relative to what this kit renders now.
type private CopyReading =
    /// The registry's equality holds: nothing to say.
    | Fresh
    /// The corpus is there and carries no copy at all.
    | CopyMissing
    /// Byte-identical vectors; the derived stamp alone differs.
    | StampOnly of copyStamp: string * kitStamp: string
    /// A content divergence, with what was seen.
    | VectorsDiffer of detail: string

let private classify (copyText: string) (kitText: string) : CopyReading =
    let copy = fingerprintLines copyText
    let kit = fingerprintLines kitText

    if copy = kit then
        Fresh
    elif copy.Length <> kit.Length then
        VectorsDiffer(
            sprintf
                "the two files are different LENGTHS — the corpus copy has %d lines, this kit renders %d"
                copy.Length
                kit.Length
        )
    else
        let differing =
            [ for i in 0 .. copy.Length - 1 do
                  if copy[i] <> kit[i] then
                      yield i ]

        if differing |> List.forall (fun i -> isStampLine copy[i] && isStampLine kit[i]) then
            // Every differing line is the stamp's. Read both stamps back through the parser; a
            // document whose stamp cannot be read is not the lockstep case, whatever its lines say.
            match stampOf copyText, stampOf kitText with
            | Some c, Some k when c <> k -> StampOnly(c, k)
            | _ ->
                VectorsDiffer(
                    sprintf
                        "only the %s line differs, but the stamp could not be read back from both documents — treated as a content divergence rather than as the derived-lockstep case"
                        stampMember
                )
        else
            let first = List.head differing
            let col = firstDivergence copy[first] kit[first]

            VectorsDiffer(
                sprintf
                    "%d of %d lines differ; the first is line %d, which parts at character %d —\n    corpus copy: %s\n    this kit:    %s"
                    (List.length differing)
                    copy.Length
                    (first + 1)
                    (col + 1)
                    (window col copy[first])
                    (window col kit[first])
            )

let private banner (heading: string) (body: string list) : string =
    let rule = String.replicate 78 "="

    String.concat
        "\n"
        ([ ""; rule; "  " + heading; rule ]
         @ [ for l in body -> "  " + l ]
         @ [ rule; "" ])

/// What the reading says out loud, or `None` when there is nothing to say. `fatal` changes only
/// the last line: the finding, the two stamps and the two commands are the same text either way,
/// so a local run and a CI run are read the same way by the same person.
let private describe (copyPath: string) (fatal: bool) (reading: CopyReading) : string option =
    let fatality =
        if fatal then
            [ sprintf
                  "FATAL: %s is set, so this fails the run — as it does in CI, on every push."
                  SiblingCorpus.askVariable ]
        else
            [ sprintf
                  "Not fatal here — %s is unset, so this is REPORTED and the run continues."
                  SiblingCorpus.askVariable
              "It IS fatal in CI. You are reading this now so that it is not read there instead." ]

    let bothCommands =
        [ "Re-emit BOTH halves, in the same sitting:"
          "    " + emitHere
          "        — this repository's conformance/, which the default suite certifies"
          "    " + emitCopy
          "        — the corpus copy; then commit AND PUSH it, in that separate public repository" ]

    match reading with
    | Fresh -> None
    | CopyMissing ->
        Some(
            banner
                (sprintf "CORPUS COPY MISSING — %s is not published there" LawVectorExport.transformFileName)
                ([ sprintf "expected at  %s" copyPath
                   ""
                   sprintf
                       "The corpus is present but carries no copy of this file, so the %s hosts have nothing to"
                       LawVectorExport.familyDirName
                   "certify against. This is not the stamp being behind; there is no copy at all."
                   "" ]
                 @ bothCommands
                 @ [ "" ]
                 @ fatality)
        )
    | StampOnly(copyStamp, kitStamp) ->
        Some(
            banner
                (sprintf "CORPUS COPY STALE — the %s STAMP ONLY (the derived-lockstep case)" stampMember)
                ([ sprintf "copy             %s" copyPath
                   sprintf "its %s   %s" stampMember copyStamp
                   sprintf "this kit renders %s" kitStamp
                   ""
                   sprintf
                       "Every vector is IDENTICAL. `%s` is DERIVED from <Version>, so a version move restales"
                       stampMember
                   "this copy every single time — in a repository this gate cannot write to, which is why"
                   "nothing used to go red for it here."
                   "" ]
                 @ bothCommands
                 @ [ ""
                     "(`copies.json` at this repository's root names the same command, and"
                     " `roadmapctl copies <workspace-root>` names this file on the next estate sweep.)"
                     "" ]
                 @ fatality)
        )
    | VectorsDiffer detail ->
        Some(
            banner
                "CORPUS COPY STALE — the VECTORS DIFFER (a content divergence, not the stamp)"
                ([ sprintf "copy  %s" copyPath
                   ""
                   detail
                   ""
                   "This is NOT the derived stamp. The published copy records answers this kit's reference"
                   "evaluator no longer gives, or carries vectors it no longer renders — and the hosts"
                   "certify against that copy, so what is published is an oracle nobody checked."
                   "" ]
                 @ bothCommands
                 @ [ "" ]
                 @ fatality)
        )

// ---------------------------------------------------------------------------
//  Phase 235 — the capabilityLaws vectors, checked the way a host checks them.
// ---------------------------------------------------------------------------
//  Moved here from the UI tier, which rendered them against its own Core pin. Each vector is read
//  back through the public codecs a host has — the capability declaration through
//  `CapabilityCodec.decode`, the args as plain `{addr, value}` pairs — and its recorded answer is
//  recomputed rather than trusted, so a perturbed `expected` is named by its id.

let private jstrOf (v: JVal) : string option =
    match v with
    | JStr s -> Some s
    | _ -> None

let private argsOf (v: JVal option) : (string * string) list option =
    match v with
    | Some(JArr items) ->
        let pairs =
            items
            |> List.map (fun a ->
                match str "addr" a, str "value" a with
                | Some addr, Some value -> Some(addr, value)
                | _ -> None)

        if List.forall Option.isSome pairs then
            Some(List.choose id pairs)
        else
            None
    | _ -> None

/// The verdict members `validateArgs` answers with, in the file's host-neutral words.
let private capabilityVerdict (r: Result<unit, InvokeError>) : (string * JVal) list =
    match r with
    | Ok() -> [ "verdict", JStr "accept" ]
    | Error(ArgOutOfSpace(addr, _, _)) -> [ "verdict", JStr "reject"; "error", JStr "argOutOfSpace"; "addr", JStr addr ]
    | Error(UnknownArg(addr, _)) -> [ "verdict", JStr "reject"; "error", JStr "unknownArg"; "addr", JStr addr ]
    | Error _ -> [ "verdict", JStr "reject"; "error", JStr "unexpected" ]

let private checkCapabilityVector (v: JVal) : string option =
    let id = str "id" v |> Option.defaultValue "<no id>"
    let input = field "input" v |> Option.defaultValue (JObj [])

    let expected =
        match field "expected" v with
        | Some(JObj ms) -> ms
        | _ -> []

    let decl (name: string) =
        match str name input with
        | None -> Error(sprintf "%s: input.%s missing" id name)
        | Some d ->
            match CapabilityCodec.decode d with
            | Ok c -> Ok(d, c)
            | Error m -> Error(sprintf "%s: input.%s did not decode (%s)" id name m)

    let differs (actual: (string * JVal) list) =
        if actual = expected then
            None
        else
            Some(sprintf "%s: this kit answers %A but the vector records %A" id actual expected)

    match str "case" v, argsOf (field "args" input) with
    | Some "validateArgs", Some args ->
        match decl "capability" with
        | Error m -> Some m
        | Ok(_, c) -> differs (capabilityVerdict (Capability.validateArgs c args))
    | Some "invocationKey", Some args ->
        match decl "capability" with
        | Error m -> Some m
        | Ok(_, c) ->
            // `capturedValue` is the sample's own draw, not something the kit computes from the
            // inputs; it is carried through and checked against the draw separately.
            let captured = expected |> List.filter (fun (k, _) -> k = "capturedValue")

            differs (
                [ "key", JStr(Capability.invocationKey c args)
                  "determinismTag", JStr(Capability.determinismTag c) ]
                @ captured
            )
    | Some "declarationRoundTrip", _ ->
        match decl "declaration" with
        | Error m -> Some m
        | Ok(d, c) ->
            let back = CapabilityCodec.encode c

            if back <> d then
                Some(sprintf "%s: decode-then-encode did not return the input bytes" id)
            else
                differs [ "declaration", JStr back ]
    | Some "registryEnumerate", _ ->
        match field "declarations" input with
        | Some(JArr ds) ->
            let decoded =
                ds |> List.map (fun d -> jstrOf d |> Option.map CapabilityCodec.decode)

            match
                decoded
                |> List.fold
                    (fun acc d ->
                        match acc, d with
                        | Ok r, Some(Ok c) -> Registry.register c r |> Result.mapError (sprintf "%A")
                        | Error e, _ -> Error e
                        | _, Some(Error m) -> Error m
                        | _, None -> Error "a declaration is not a string")
                    (Ok Registry.empty)
            with
            | Error m -> Some(sprintf "%s: the declarations did not register (%s)" id m)
            | Ok r -> differs [ "ids", JArr(Registry.enumerate r |> List.map (fun c -> JStr c.Id)) ]
        | _ -> Some(sprintf "%s: input.declarations missing" id)
    | Some other, _ -> Some(sprintf "%s: unknown case `%s`" id other)
    | None, _ -> Some(sprintf "%s: no case" id)

let private capabilityVectorsOf (json: string) : Result<JVal list, string> =
    match Json.parse json with
    | Error m -> Error("the capability vector file did not parse: " + m)
    | Ok doc ->
        match field "vectors" doc with
        | Some(JArr items) -> Ok items
        | _ -> Error "the capability vector file carries no `vectors` array"

/// Report the reading through the channel its severity earns. Printed either way: a finding that
/// only a failure would have shown is a finding the ordinary local run does not make.
let private announce (text: string) (fatal: bool) =
    printfn "%s" text
    Console.Out.Flush()

    if fatal then
        failtest text

// ---------------------------------------------------------------------------
//  Phase 235 — the corpus copy of capability-laws.json, pinned byte for byte.
// ---------------------------------------------------------------------------
//  The move carried the renderer over unchanged, and this is the pin that says so: the corpus copy
//  (written by the UI tier's exporter at its Core pin) must be what THIS renderer produces, line for
//  line and byte for byte — with exactly one recorded exception. Phase 225 made `invocationKey`
//  injective, which changed every key VALUE; the copy is re-synced with the TS and Go ports at the
//  UI tier's Core pin raise (fuaran#1860), and until then it legitimately carries the old keys under
//  the old stamp. So a differing line is admitted only if it is the `kitVersion` line or an
//  `invocationKey` vector's line that becomes identical once its `key` value is put back. Anything
//  else — a verdict, a declaration, the description, a vector added or dropped — is a real
//  divergence and is reported as one. Once the copy is re-synced the reading is simply `Fresh`, and
//  the exception admits nothing.

type private CapabilityCopyReading =
    | CapabilityFresh
    | CapabilityCopyMissing
    /// Identical but for the stamp and the invocation keys Phase 225 moved: `keys` lines differ.
    | KeyLag of keys: int * copyStamp: string * kitStamp: string
    | CapabilityDiffers of detail: string

let private keyPattern =
    System.Text.RegularExpressions.Regex(
        "\"key\": \"[^\"]*\"",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    )

let private classifyCapabilityCopy (copyText: string) (kitText: string) : CapabilityCopyReading =
    let copy = fingerprintLines copyText
    let kit = fingerprintLines kitText

    if copy = kit then
        CapabilityFresh
    elif copy.Length <> kit.Length then
        CapabilityDiffers(sprintf "the corpus copy has %d lines, this kit renders %d" copy.Length kit.Length)
    else
        let explained (i: int) =
            if isStampLine copy[i] && isStampLine kit[i] then
                Some false
            elif copy[i].Contains "\"case\": \"invocationKey\"" then
                match keyPattern.Match copy[i] with
                | m when m.Success && keyPattern.Replace(kit[i], m.Value.Replace("$", "$$"), 1) = copy[i] -> Some true
                | _ -> None
            else
                None

        let differing =
            [ for i in 0 .. copy.Length - 1 do
                  if copy[i] <> kit[i] then
                      yield i ]

        match differing |> List.tryFind (fun i -> (explained i).IsNone) with
        | Some i ->
            let col = firstDivergence copy[i] kit[i]

            CapabilityDiffers(
                sprintf
                    "line %d parts at character %d, and it is neither the stamp nor an invocation key —\n    corpus copy: %s\n    this kit:    %s"
                    (i + 1)
                    (col + 1)
                    (window col copy[i])
                    (window col kit[i])
            )
        | None ->
            let keys =
                differing |> List.filter (fun i -> explained i = Some true) |> List.length

            KeyLag(keys, stampOf copyText |> Option.defaultValue "?", stampOf kitText |> Option.defaultValue "?")

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

          testCase
              "the committed conformance/laws vectors are the ones this kit renders, and each is true of this evaluator"
          <| fun _ ->
              // Phase 172: the vectors are AUTHORED here — `conformance/laws/transform-laws.json`
              // at this repository's root — and the shared corpus carries a declared copy. So the
              // oracle question and the freshness question are both asked of the committed file
              // in this checkout, and the suite needs no corpus to certify its own contract.
              let root = OwnedConformance.root ()
              let path = LawVectorExport.transformPath root

              if not (File.Exists path) then
                  failtestf
                      "this repository carries no %s at '%s' — re-run `--emit-laws` (no argument writes into conformance/) and commit the result"
                      LawVectorExport.transformFileName
                      path
              else
                  // Read as bytes-to-text without newline translation: the file is LF and the
                  // comparison is about bytes.
                  let committed = File.ReadAllText path

                  match parseVectors committed with
                  | Error m -> failtest ("the committed vectors did not read: " + m)
                  | Ok vectors ->
                      // First the oracle question — is what the file records still true of
                      // this kit? — because that is the failure a host would suffer.
                      let failures = vectors |> List.choose checkVector

                      Expect.isEmpty
                          failures
                          (sprintf "the committed vectors disagree with this kit's reference evaluator: %A" failures)

                      // Then the freshness question. Distinct from the above: a rendering
                      // change (a new shape, a reworded description, a `<Version>` move behind
                      // the `kitVersion` stamp) leaves every vector true and the file stale. The
                      // stamp lives in THIS file, so a version cut re-emits it in the same commit
                      // and never reaches across a repository boundary to go red.
                      Expect.equal
                          committed
                          (LawVectorExport.renderTransformVectors ())
                          "the committed conformance/laws file is not what this kit renders — re-run `--emit-laws` (no argument) and commit conformance/"

          testCase
              "the corpus copy of laws/transform-laws.json is fresh — always checked; FUARAN_CORE_CORPUS_FRESHNESS decides only whether a finding is fatal"
          <| fun _ ->
              // Phase 130 decided that an absent corpus FAILS where the leg was asked for; Phase
              // 172 kept that on the leg it was written for. Phase 216 moves the DISCOVERY: this
              // leg is decided by the corpus's PRESENCE, so a version move that restales the copy
              // is heard here, on the run that caused it, rather than in CI minutes later. The
              // comparison is still the workspace copy registry's `fingerprint` equality
              // (`roadmapctl copies`), refined into the two readings above.
              match SiblingCorpus.freshness LawVectorExport.familyDirName with
              | SiblingCorpus.NotChecked(why, true) -> failtest why
              | SiblingCorpus.NotChecked(why, false) ->
                  // Never a quiet pass: a single-repository checkout has no corpus at all, and
                  // "nothing to check" must not read as "everything checked".
                  printfn "%s" (banner "CORPUS COPY NOT CHECKED" [ why ])
                  Console.Out.Flush()
                  skiptest why
              | SiblingCorpus.Compare(root, fatal) ->
                  let copy = LawVectorExport.transformPath root
                  let owned = LawVectorExport.transformPath (OwnedConformance.root ())

                  if not (File.Exists owned) then
                      // A defect in THIS repository, and nothing to do with the corpus — so it is
                      // fatal whatever the ask says.
                      failtestf
                          "this repository carries no %s at '%s' — nothing to compare the copy against; re-run `%s` and commit conformance/"
                          LawVectorExport.transformFileName
                          owned
                          emitHere

                  let reading =
                      if File.Exists copy then
                          classify (File.ReadAllText copy) (File.ReadAllText owned)
                      else
                          CopyMissing

                  match describe copy fatal reading with
                  | None -> ()
                  | Some text -> announce text fatal

          testCase "a fingerprint drops a BOM and NOTHING else - the equality has no hole in its first character"
          <| fun _ ->
              // Found by taking the reading from the registry's own normalisation rather than from
              // a copy of it: `StartsWith(string)` compares by the current culture, under which
              // U+FEFF is IGNORABLE, so the obvious spelling answered true for every text and
              // quietly removed the first character of any document that had no BOM. Both sides of
              // a comparison lost the same character, so nothing was ever reported wrongly - but
              // the one equality this repository's published copies are held to was blind to their
              // first byte, and could not tell an empty document from a crash.
              Expect.equal (OwnedConformance.fingerprint "{ \"a\": 1 }") "{ \"a\": 1 }" "no BOM, nothing dropped"

              Expect.equal
                  (OwnedConformance.fingerprint "\uFEFF{ \"a\": 1 }")
                  "{ \"a\": 1 }"
                  "a leading BOM is dropped, and only it"

              Expect.equal (OwnedConformance.fingerprint "") "" "an empty document fingerprints rather than throwing"
              Expect.equal (OwnedConformance.fingerprint "\n\n  \n") "" "a document of blank lines fingerprints empty"

              Expect.notEqual
                  (OwnedConformance.fingerprint "{ \"a\": 1 }")
                  (OwnedConformance.fingerprint "[ \"a\", 1 ]")
                  "two documents differing only in their first character are not the same copy"

              Expect.equal
                  (String.concat "\n" (fingerprintLines (LawVectorExport.renderTransformVectors ())))
                  (OwnedConformance.fingerprint (LawVectorExport.renderTransformVectors ()))
                  "the reading's lines ARE the fingerprint's, by construction"

          testCase "the two readings are told apart, and a stamp move never masks a content divergence"
          <| fun _ ->
              // The go-red both ways. A perturbed VECTOR and a perturbed STAMP must not produce the
              // same report, and the pair of them must read as the divergence — otherwise the
              // separation would be a way of hiding one behind the other, which is worse than not
              // separating them at all.
              let kit = LawVectorExport.renderTransformVectors ()

              let replaceFirst (needle: string) (replacement: string) (text: string) =
                  match text.IndexOf(needle, StringComparison.Ordinal) with
                  | -1 -> failtestf "the fixture could not be perturbed: '%s' is not in the rendered file" needle
                  | i -> text.Substring(0, i) + replacement + text.Substring(i + needle.Length)

              let kitStamp =
                  match stampOf kit with
                  | Some s -> s
                  | None -> failtest "the rendered file carries no readable stamp"

              let stampPerturbed =
                  replaceFirst
                      (sprintf "\"%s\": \"%s\"" stampMember kitStamp)
                      (sprintf "\"%s\": \"%s-perturbed\"" stampMember kitStamp)
                      kit

              // Inside a vector, and valid JSON: the copy claims the reference refused a pipeline
              // it in fact evaluated.
              let vectorPerturbed =
                  replaceFirst "\"verdict\": \"ok\"" "\"verdict\": \"error\"" kit

              let bothPerturbed =
                  replaceFirst
                      (sprintf "\"%s\": \"%s\"" stampMember kitStamp)
                      (sprintf "\"%s\": \"%s-perturbed\"" stampMember kitStamp)
                      vectorPerturbed

              Expect.equal (classify kit kit) Fresh "an unperturbed copy is fresh"

              match classify stampPerturbed kit with
              | StampOnly(copyStamp, k) ->
                  Expect.equal copyStamp (kitStamp + "-perturbed") "the copy's own stamp is quoted"
                  Expect.equal k kitStamp "this kit's stamp is quoted beside it"
              | other -> failtestf "a stamp-only perturbation read as %A" other

              match classify vectorPerturbed kit with
              | VectorsDiffer _ -> ()
              | other -> failtestf "a perturbed VECTOR read as %A — the content divergence was not seen" other

              match classify bothPerturbed kit with
              | VectorsDiffer _ -> ()
              | other ->
                  failtestf
                      "a copy whose stamp AND vectors both moved read as %A — a version move would mask a real divergence"
                      other

              // And the reports themselves are different text, each naming its own reading and both
              // naming both commands. A separation only the type sees is not one a reader gets.
              let reportOf reading =
                  match describe "C:\\corpus\\laws\\transform-laws.json" false reading with
                  | Some t -> t
                  | None -> failtestf "%A produced no report" reading

              let stampReport = reportOf (classify stampPerturbed kit)
              let vectorReport = reportOf (classify vectorPerturbed kit)

              Expect.notEqual stampReport vectorReport "the two readings are reported differently"
              Expect.stringContains stampReport "STAMP ONLY" "the stamp reading says which reading it is"
              Expect.stringContains stampReport kitStamp "the stamp reading names both stamps"
              Expect.stringContains vectorReport "VECTORS DIFFER" "the divergence says which reading it is"

              Expect.isFalse
                  (vectorReport.Contains "STAMP ONLY")
                  "a content divergence is never reported as the derived-lockstep case"

              for report in [ stampReport; vectorReport ] do
                  Expect.stringContains report emitHere "the report names the in-repository re-emit"
                  Expect.stringContains report emitCopy "the report names the corpus re-emit"

          testCase "presence decides whether the copy is compared; the ask decides only what a finding costs"
          <| fun _ ->
              // The four quadrants, hermetically: a synthesised corpus root and a directory that is
              // not one, so nothing here depends on whether this machine happens to hold the corpus.
              let scratch =
                  Path.Combine(Path.GetTempPath(), "fuaran-core-216-" + Guid.NewGuid().ToString("N"))

              let corpus = Path.Combine(scratch, "corpus")
              let notCorpus = Path.Combine(scratch, "not-a-corpus")

              try
                  Directory.CreateDirectory(Path.Combine(corpus, LawVectorExport.familyDirName))
                  |> ignore

                  Directory.CreateDirectory notCorpus |> ignore

                  File.WriteAllText(Path.Combine(corpus, "manifest.json"), "{ \"schema\": \"s\", \"idl\": \"i\" }")

                  let ask = Some "1"
                  let dont = None

                  let at (askValue: string option) (dir: string) =
                      SiblingCorpus.freshnessWith askValue (Some dir) LawVectorExport.familyDirName scratch

                  match at dont corpus with
                  | SiblingCorpus.Compare(root, fatal) ->
                      Expect.equal root corpus "a present corpus is compared even when the leg was not asked for"
                      Expect.isFalse fatal "and a finding against it is not fatal there"
                  | other -> failtestf "an unasked-for run with a corpus present read as %A" other

                  match at ask corpus with
                  | SiblingCorpus.Compare(_, fatal) -> Expect.isTrue fatal "the ask is what makes a finding fatal"
                  | other -> failtestf "an asked-for run with a corpus present read as %A" other

                  match at dont notCorpus with
                  | SiblingCorpus.NotChecked(why, fatal) ->
                      Expect.isFalse fatal "no corpus and no ask is reported, not failed"
                      Expect.stringContains why "NOT CHECKED" "and it says so in those words"
                  | other -> failtestf "an unasked-for run with no corpus read as %A" other

                  match at ask notCorpus with
                  | SiblingCorpus.NotChecked(_, fatal) ->
                      Expect.isTrue fatal "asked for and absent still FAILS — D31 is unchanged"
                  | other -> failtestf "an asked-for run with no corpus read as %A" other
              finally
                  try
                      Directory.Delete(scratch, true)
                  with _ ->
                      ()

          // ---- Phase 235: capabilityLaws, emitted here rather than by the UI tier ----------------

          testCase
              "the exported capability sample is one capabilityLaws certifies, and each draw gets the verdict the law demands"
          <| fun _ ->
              // The law over the exported seed: the sample is a sample of a PASSING run.
              for r in
                  Conformance.capabilityLaws LawVectorExport.Capabilities.seed LawVectorExport.Capabilities.iterations do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

              // And each computed expectation is the one the law demands, asserted directly rather than
              // through the renderer, so a renderer that recorded the wrong class could not pass.
              for d in LawVectorExport.Capabilities.draws () do
                  Expect.equal
                      (Capability.validateArgs d.Cap [ "h0", string d.Lo ])
                      (Ok())
                      (sprintf "iteration %d: the in-space arg is accepted" d.Iteration)

                  match Capability.validateArgs d.Cap [ "h0", string (d.Hi + 1) ] with
                  | Error(ArgOutOfSpace _) -> ()
                  | other -> failtestf "iteration %d: out-of-space was not ArgOutOfSpace (%A)" d.Iteration other

                  match Capability.validateArgs d.Cap [ "nope", string d.Lo ] with
                  | Error(UnknownArg _) -> ()
                  | other -> failtestf "iteration %d: an unknown arg was not UnknownArg (%A)" d.Iteration other

              Expect.isFalse
                  ((LawVectorExport.Capabilities.render ()).Contains "\"unexpected\"")
                  "a vector carried a refusal outside the two the law distinguishes"

          testCase "every rendered capability vector reads back through the public codecs and is true of this kit"
          <| fun _ ->
              match capabilityVectorsOf (LawVectorExport.Capabilities.render ()) with
              | Error m -> failtest m
              | Ok vectors ->
                  Expect.equal
                      (List.length vectors)
                      (6 * LawVectorExport.Capabilities.iterations)
                      "six vectors per declared iteration"

                  let failures = vectors |> List.choose checkCapabilityVector
                  Expect.isEmpty failures (sprintf "%A" failures)

                  // `capturedValue` is the draw, which the kit does not compute from the inputs — so it
                  // is held to the draw here.
                  let captured =
                      vectors
                      |> List.choose (fun v ->
                          match str "case" v, field "expected" v with
                          | Some "invocationKey", Some e ->
                              match field "capturedValue" e with
                              | Some(JInt n) -> Some n
                              | _ -> None
                          | _ -> None)

                  Expect.equal
                      captured
                      (LawVectorExport.Capabilities.draws () |> List.map (fun d -> d.Realized))
                      "each invocation-key vector carries its iteration's drawn capture value"

          testCase "the capability checker names a perturbed vector — the oracle leg can go red"
          <| fun _ ->
              let kit = LawVectorExport.Capabilities.render ()

              let perturbed =
                  match kit.IndexOf("\"verdict\": \"accept\"", StringComparison.Ordinal) with
                  | -1 -> failtest "the rendered file carries no accept verdict to perturb"
                  | i -> kit.Substring(0, i) + "\"verdict\": \"reject\"" + kit.Substring(i + 19)

              match capabilityVectorsOf perturbed with
              | Error m -> failtest m
              | Ok vectors ->
                  let failures = vectors |> List.choose checkCapabilityVector
                  Expect.equal (List.length failures) 1 "exactly the perturbed vector is named"
                  Expect.stringContains (List.head failures) "capability-0-accept" "and by its id"

          testCase "the rendered capability artefact is LF-only and byte-stable across renders"
          <| fun _ ->
              let once = LawVectorExport.Capabilities.render ()
              Expect.equal once (LawVectorExport.Capabilities.render ()) "two renders produce the same bytes"
              Expect.isFalse (once.Contains "\r") "no CR may reach a corpus byte-compared across three OSes"

          testCase "the committed conformance/laws/capability-laws.json is the one this kit renders"
          <| fun _ ->
              let path = LawVectorExport.capabilityPath (OwnedConformance.root ())

              if not (File.Exists path) then
                  failtestf
                      "this repository carries no %s at '%s' — re-run `--emit-laws` (no argument writes into conformance/) and commit the result"
                      LawVectorExport.Capabilities.fileName
                      path
              else
                  let committed = File.ReadAllText path

                  match capabilityVectorsOf committed with
                  | Error m -> failtest ("the committed capability vectors did not read: " + m)
                  | Ok vectors ->
                      let failures = vectors |> List.choose checkCapabilityVector

                      Expect.isEmpty
                          failures
                          (sprintf "the committed capability vectors disagree with this kit: %A" failures)

                  Expect.equal
                      committed
                      (LawVectorExport.Capabilities.render ())
                      "the committed conformance/laws/capability-laws.json is not what this kit renders — re-run `--emit-laws` (no argument) and commit conformance/"

          testCase
              "the corpus copy of laws/capability-laws.json is this renderer's output byte for byte, but for the invocation keys fuaran#1860 re-syncs"
          <| fun _ ->
              match SiblingCorpus.freshness LawVectorExport.familyDirName with
              | SiblingCorpus.NotChecked(why, true) -> failtest why
              | SiblingCorpus.NotChecked(why, false) ->
                  printfn "%s" (banner "CAPABILITY CORPUS COPY NOT CHECKED" [ why ])
                  Console.Out.Flush()
                  skiptest why
              | SiblingCorpus.Compare(root, fatal) ->
                  let copy = LawVectorExport.capabilityPath root

                  let reading =
                      if File.Exists copy then
                          classifyCapabilityCopy (File.ReadAllText copy) (LawVectorExport.Capabilities.render ())
                      else
                          CapabilityCopyMissing

                  match reading with
                  | CapabilityFresh -> ()
                  | KeyLag(keys, copyStamp, kitStamp) ->
                      // The recorded lag, never fatal: the copy is the file five hosts certify against,
                      // and re-syncing it ahead of the TS and Go ports would redden them. It is still
                      // SAID, on every run that can see it.
                      printfn
                          "%s"
                          (banner
                              "CAPABILITY CORPUS COPY LAGS — the invocation keys Phase 225 moved (fuaran#1860 re-syncs it)"
                              [ sprintf "copy             %s" copy
                                sprintf "its kitVersion   %s" copyStamp
                                sprintf "this kit renders %s" kitStamp
                                sprintf "%d invocation-key lines differ in their `key` value and in nothing else;" keys
                                "every other line is byte-identical to this renderer's output."
                                "The re-sync lands with the TS and Go ports at the UI tier's Core pin raise:"
                                "    " + emitCopy ])

                      Console.Out.Flush()
                  | CapabilityCopyMissing ->
                      announce
                          (banner "CAPABILITY CORPUS COPY MISSING" [ sprintf "expected at  %s" copy; "    " + emitCopy ])
                          fatal
                  | CapabilityDiffers detail ->
                      announce
                          (banner
                              "CAPABILITY CORPUS COPY DIFFERS — beyond the recorded invocation-key lag"
                              [ sprintf "copy  %s" copy
                                ""
                                detail
                                ""
                                "    " + emitHere
                                "    " + emitCopy ])
                          fatal

          testCase "the capability copy reading admits the key lag and nothing else"
          <| fun _ ->
              // The go-red both ways, hermetically: a copy whose invocation keys (and stamp) moved reads
              // as the lag; the same copy with one verdict perturbed as well reads as a divergence.
              let kit = LawVectorExport.Capabilities.render ()

              let keysMoved =
                  keyPattern
                      .Replace(kit, (fun (m: System.Text.RegularExpressions.Match) -> m.Value.Replace("#", "#00")))
                      .Replace("\"kitVersion\": \"", "\"kitVersion\": \"old-")

              Expect.equal (classifyCapabilityCopy kit kit) CapabilityFresh "an unperturbed copy is fresh"

              match classifyCapabilityCopy keysMoved kit with
              | KeyLag(keys, _, _) ->
                  Expect.equal keys LawVectorExport.Capabilities.iterations "one moved key per iteration"
              | other -> failtestf "a key-and-stamp-only copy read as %A" other

              let alsoVerdict =
                  match keysMoved.IndexOf("\"verdict\": \"accept\"", StringComparison.Ordinal) with
                  | -1 -> failtest "no accept verdict to perturb"
                  | i ->
                      keysMoved.Substring(0, i)
                      + "\"verdict\": \"reject\""
                      + keysMoved.Substring(i + 19)

              match classifyCapabilityCopy alsoVerdict kit with
              | CapabilityDiffers _ -> ()
              | other -> failtestf "a perturbed verdict hidden behind the key lag read as %A" other

              // A key moved on a line that is NOT an invocation-key vector is not the lag either.
              let declarationMoved =
                  kit.Replace("\"id\": \"capability-0-accept\"", "\"id\": \"capability-0-accepted\"")

              match classifyCapabilityCopy declarationMoved kit with
              | CapabilityDiffers _ -> ()
              | other -> failtestf "a moved vector id read as %A" other ]
