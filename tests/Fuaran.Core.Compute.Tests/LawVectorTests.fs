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

/// Report the reading through the channel its severity earns. Printed either way: a finding that
/// only a failure would have shown is a finding the ordinary local run does not make.
let private announce (text: string) (fatal: bool) =
    printfn "%s" text
    Console.Out.Flush()

    if fatal then
        failtest text

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
                      () ]
