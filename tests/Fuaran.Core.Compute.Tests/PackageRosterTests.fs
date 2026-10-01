module Fuaran.Core.Tests.PackageRosterTests

// ---------------------------------------------------------------------------
// Phase 199 — the package roster is DERIVED, and STABILITY.md's header is held to
// `<Version>`.
//
// `README.md`'s package table and `STABILITY.md`'s entry headers are hand-maintained
// documents sitting beside the mechanism they describe, so they drift silently and are
// discovered by a reader rather than by a gate. Measured 2026-09-17: the table listed
// twelve packages against twenty-one packable projects, and two RELEASED slots still
// carried the "no `vX.Y.Z` tag exists yet" sentence they were cut with.
//
// The derived-registry rule — declare local, project central — applies inside one
// repository too: the project files declare what ships, and the table is a projection of
// them. So this file asserts the ROSTER and nothing else. What a package is FOR stays
// prose a person writes; a gate that generated that column would be describing the file
// layout rather than the design.
//
// Five properties, each with a go-red case beside it over synthetic input. The live case
// alone cannot show a classifier works — every one of these reads a real file that is
// expected to be correct, so a classifier that matched nothing would pass all five.
//
//   1. README rows = the packable set.
//   2. No packable project sits outside `src/` — the SCOPE property 1's derivation assumes,
//      checked rather than trusted. Without it a package added elsewhere is invisible to
//      property 1 by being out of frame, which is the same drift wearing a different hat.
//   3. Some entry header in STABILITY.md names the standing `<Version>`.
//   4. No "no `vX.Y.Z` tag exists" sentence survives the tag it denies.
//   5. Every release TAG at or above a declared floor has an entry header naming it.
//
// Property 5 is Phase 205, and it exists because property 3 structurally cannot see what
// it caught. Property 3 quantifies over the STANDING `<Version>` — one number — so a slot
// that was cut, tagged, released and then left behind as `<Version>` moved on is invisible
// to it: `v0.25.0` was tagged 2026-09-15 and this document carried no entry for it at all,
// while property 3 read green against `0.26.0` the whole time. Quantifying over the tags
// is what closes that, and the FLOOR is what keeps it honest — every release below
// `0.25.0` predates this document's per-slot classes, so demanding entries for them would
// demand invention rather than record.
//
// Properties 4 and 5 read `git tag` LOCALLY, which is the honest limit: they answer "has
// the release gesture been made in this clone", never "is this version published". A clone
// with no git at all skips by name with the reason printed, on the
// `WorkingCopyEolTests` precedent — a check that reads as green when its instrument is
// missing is worse than an absent one.
// ---------------------------------------------------------------------------

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

/// A project under `src/` that `pack` would produce a package from.
type internal PackableProject =
    {
        /// The package id — `<PackageId>` where declared, else the project file's base name.
        PackageId: string
        /// Repo-relative, forward-slashed.
        ProjectFile: string
    }

let private isPackableRe =
    Regex(@"<IsPackable>([^<]*)</IsPackable>", RegexOptions.Compiled)

let private packageIdRe =
    Regex(@"<PackageId>([^<]*)</PackageId>", RegexOptions.Compiled)

let private firstGroup (re: Regex) (text: string) : string option =
    let m = re.Match text

    if m.Success then
        let v = m.Groups[1].Value.Trim()
        if v = "" then None else Some v
    else
        None

/// `IsPackable` for one project: what the project declares, else what
/// `Directory.Build.props` declares, else MSBuild's own default (true). Only the literal
/// `false` excludes — anything else, including a value this reader does not understand,
/// leaves the project IN the roster, so a misread widens the table rather than silently
/// dropping a shipping package from it.
let internal isPackable (fallback: string option) (projectText: string) : bool =
    match firstGroup isPackableRe projectText |> Option.orElse fallback with
    | Some v -> not (String.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
    | None -> true

/// THE derivation, kept in one small function on purpose: the Fable surface's completeness
/// check (Phase 185) derives the same set, and two spellings of "what ships" would drift
/// exactly the way the documents this file gates drifted. `src/*/*.fsproj` +
/// `src/*/*.csproj` — a C# project under `src/` is a published package too, and a roster scoped
/// to F# would leave it undocumented by construction, which is the same excusal-by-the-shape-of-
/// the-derivation that 185's own header names. Checked against that sibling after both
/// landed: it covers every project type under `src/` and names any package off the Fable surface
/// as an explicit exclusion, so the two rosters range over one set.
let internal packableProjects (root: string) : PackableProject list =
    let srcDir = Path.Combine(root, "src")

    let fallback =
        let props = Path.Combine(root, "Directory.Build.props")

        if File.Exists props then
            firstGroup isPackableRe (File.ReadAllText props)
        else
            None

    if not (Directory.Exists srcDir) then
        []
    else
        Directory.GetDirectories srcDir
        |> Array.collect (fun d -> Array.append (Directory.GetFiles(d, "*.fsproj")) (Directory.GetFiles(d, "*.csproj")))
        |> Array.toList
        |> List.choose (fun path ->
            let text = File.ReadAllText path

            if isPackable fallback text then
                Some
                    { PackageId =
                        firstGroup packageIdRe text
                        |> Option.defaultValue (Path.GetFileNameWithoutExtension path)
                      ProjectFile = Path.GetRelativePath(root, path).Replace('\\', '/') }
            else
                None)
        |> List.sortBy _.PackageId

/// Every project file in the tree that is NOT under `src/`, paired with its packability —
/// the instrument for the scope property.
let internal projectsOutsideSrc (root: string) : (string * bool) list =
    let fallback =
        let props = Path.Combine(root, "Directory.Build.props")

        if File.Exists props then
            firstGroup isPackableRe (File.ReadAllText props)
        else
            None

    [ yield! Directory.GetFiles(root, "*.fsproj", SearchOption.AllDirectories)
      yield! Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories) ]
    |> List.map (fun p -> Path.GetRelativePath(root, p).Replace('\\', '/'), p)
    |> List.filter (fun (rel, _) -> not (rel.StartsWith("src/", StringComparison.Ordinal)))
    |> List.map (fun (rel, full) -> rel, isPackable fallback (File.ReadAllText full))
    |> List.sortBy fst

// ---- README ---------------------------------------------------------------

/// The package ids the README's `## Packages` table lists, in file order. The first cell's
/// backticked token is the id; the header row and the `|---|` separator carry no backticks
/// and drop out on their own.
let internal readmePackageIds (readme: string) : string list =
    let lines = readme.Replace("\r\n", "\n").Split('\n') |> Array.toList

    let rec collect (acc: string list) (inSection: bool) (ls: string list) =
        match ls with
        | [] -> List.rev acc
        | (l: string) :: rest ->
            if l.StartsWith("## ", StringComparison.Ordinal) then
                if l.Trim() = "## Packages" then collect acc true rest
                elif inSection then List.rev acc
                else collect acc false rest
            elif inSection then
                collect (l :: acc) true rest
            else
                collect acc false rest

    collect [] false lines
    |> List.filter (fun l -> l.TrimStart().StartsWith("|", StringComparison.Ordinal))
    |> List.choose (fun l ->
        let cells = l.Trim().Trim('|').Split('|')

        if cells.Length = 0 then
            None
        else
            let m = Regex.Match(cells[0], "`([^`]+)`")
            if m.Success then Some(m.Groups[1].Value.Trim()) else None)

/// `(shipping but undocumented, documented but not shipping)`.
let internal rosterDiff (packable: string list) (documented: string list) : string list * string list =
    let p = Set.ofList packable
    let d = Set.ofList documented
    Set.difference p d |> Set.toList, Set.difference d p |> Set.toList

// ---- STABILITY ------------------------------------------------------------

/// The standing `<Version>` from `Directory.Build.props`.
let internal standingVersion (propsText: string) : string option =
    firstGroup (Regex @"<Version>([^<]*)</Version>") propsText

/// The first level-2-or-3 header naming `version`, if any. A version's entry may be its own
/// `## 0.26.0 (draft)` section or a `### … (Phase N, \`0.26.0\`)` entry under an older
/// grouping — both spellings are live in this document, so the property is "an entry header
/// names it", not "a header equals it".
let internal entryHeaderNaming (version: string) (stability: string) : string option =
    stability.Replace("\r\n", "\n").Split('\n')
    |> Array.tryFind (fun l -> Regex.IsMatch(l, @"^#{2,3}\s") && l.Contains(version, StringComparison.Ordinal))

let private noTagRe =
    Regex(@"no `v(?<v>\d+\.\d+\.\d+)` tag exists", RegexOptions.Compiled)

/// Draft-slot preambles that deny a tag the repository holds — `(line number, version)`.
/// The sentence is true when a slot is cut and false the instant the release gesture is
/// made, and nothing but this retires it.
let internal staleDraftSentences (tags: Set<string>) (lines: string list) : (int * string) list =
    lines
    |> List.indexed
    |> List.choose (fun (i, l) ->
        let m = noTagRe.Match l

        if m.Success && tags.Contains("v" + m.Groups["v"].Value) then
            Some(i + 1, m.Groups["v"].Value)
        else
            None)

/// The oldest released slot the per-tag entry rule covers (Phase 205), as
/// `(major, minor, patch)`. ONE named constant, because the number is a judgement — it is
/// the oldest slot whose record can still be written from evidence — and a judgement spelt
/// in two places is a judgement that will disagree with itself. In this repository it is the
/// FIRST version it emits (Phase 259): every earlier version of these packages was released
/// from the Fuaran.Core repository and is recorded in that repository's STABILITY.md, which
/// this one's points to rather than restates.
let internal entryHeaderFloor = (0, 33, 0)

let private versionHeaderRe =
    Regex(@"^##\s+(?<v>\d+\.\d+\.\d+)\b(?<rest>.*)$", RegexOptions.Compiled ||| RegexOptions.Multiline)

/// True when every level-2 entry header in STABILITY.md that names a version marks it DRAFT —
/// the document's own statement that this repository has released nothing yet (Phase 259). It is
/// what lets a clone holding NO release tag be read as "nothing released" rather than as "the
/// tags were not fetched": the two are told apart by the record, not by the absence. Once any
/// header records a release, an empty tag list is a clone missing its tags, and the tag legs
/// refuse it as the substrate's do. False for a document with no version header at all, which
/// is not a statement of anything.
let internal releasedNothingYet (stability: string) : bool =
    let headers = versionHeaderRe.Matches(stability.Replace("\r\n", "\n")) |> Seq.toList

    not headers.IsEmpty
    && headers |> List.forall (fun m -> m.Groups["rest"].Value.Contains "DRAFT")

let private releaseTagRe = Regex(@"^v(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled)

/// `(major, minor, patch)` for a plain release tag, else `None`. The shape is deliberately
/// STRICT: a pre-release spelling (`v0.0.1-alpha.8`) names no released slot in the sense
/// this rule is about, and every such tag this repository holds sits below the floor in any
/// case. A tag shape this does not parse is out of frame rather than reported — the
/// alternative is reddening on someone else's tagging convention.
let internal releaseTagVersion (tag: string) : (int * int * int) option =
    let m = releaseTagRe.Match tag

    if m.Success then
        Some(int m.Groups[1].Value, int m.Groups[2].Value, int m.Groups[3].Value)
    else
        None

/// Release tags at or above `floor` that no entry header names, oldest first.
///
/// Ordering is NUMERIC, over the parsed triple, not lexical over the tag string: `0.9.0`
/// sorts BELOW `0.25.0` and a string comparison gets that backwards, which would silently
/// admit every pre-`0.10` tag the floor exists to exclude. It reuses `entryHeaderNaming` —
/// the same seam property 3 asserts the standing version through — so the two properties
/// cannot disagree about what counts as an entry.
let internal tagsMissingEntryHeader (floor: int * int * int) (tags: Set<string>) (stability: string) : string list =
    tags
    |> Set.toList
    |> List.choose (fun t -> releaseTagVersion t |> Option.map (fun v -> v, t))
    |> List.filter (fun (v, _) -> v >= floor)
    |> List.sortBy fst
    |> List.filter (fun (_, t) -> (entryHeaderNaming (t.Substring 1) stability).IsNone)
    |> List.map snd

// ---- the instruments ------------------------------------------------------

let private repoRoot () : string option =
    let rec climb (dir: string) (budget: int) : string option =
        if budget < 0 || isNull dir then
            None
        elif File.Exists(Path.Combine(dir, "Fuaran.Core.Compute.slnx")) then
            Some dir
        else
            match Directory.GetParent dir with
            | null -> None
            | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)

/// `git tag --list` from `root`, or why it could not be run. Spawned through
/// `ChildProcess.redirected` so the child's stdout decodes as UTF-8 whatever code page the
/// runner inherited.
let private gitTags (root: string) : Result<Set<string>, string> =
    try
        let psi = ChildProcess.redirected "git" "tag --list"
        psi.WorkingDirectory <- root
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            Error(sprintf "`git tag --list` exited %d: %s" p.ExitCode (err.Trim()))
        else
            out.Split('\n')
            |> Array.toList
            |> List.map (fun l -> l.Trim())
            |> List.filter (fun l -> l <> "")
            |> Set.ofList
            |> Ok
    with e ->
        Error("`git` could not be run: " + e.Message)

let private withRoot (f: string -> unit) =
    match repoRoot () with
    | None -> failtest "could not locate the repository root (Fuaran.Core.Compute.slnx) from the CWD or the test binary"
    | Some root -> f root

let private fileLines (path: string) =
    File.ReadAllText(path).Replace("\r\n", "\n").Split('\n') |> Array.toList

// ---- the Fable-surface cross-check ---------------------------------------

/// The package ids `fable-exclusions.json` declares (Phase 185; at the repository
/// root since Phase 217), or `None` when its shape is not one this reader recognises.
/// Tolerant of the shapes it was written against before that file existed; the live check fails
/// on `None`.
let internal declaredExclusions (json: string) : Set<string> option =
    let stringsOf (el: JsonElement) =
        if el.ValueKind = JsonValueKind.Array then
            let items = el.EnumerateArray() |> Seq.toList

            if items |> List.forall (fun i -> i.ValueKind = JsonValueKind.String) then
                Some(items |> List.map _.GetString() |> Set.ofList)
            else
                let named =
                    items
                    |> List.choose (fun i ->
                        if i.ValueKind <> JsonValueKind.Object then
                            None
                        else
                            [ "package"; "packageId"; "id"; "project" ]
                            |> List.tryPick (fun k ->
                                match i.TryGetProperty k with
                                | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                                | _ -> None))

                if named.Length = items.Length && not items.IsEmpty then
                    Some(Set.ofList named)
                else
                    None
        else
            None

    try
        use doc = JsonDocument.Parse json
        let root = doc.RootElement

        match stringsOf root with
        | Some s -> Some s
        | None when root.ValueKind = JsonValueKind.Object ->
            root.EnumerateObject() |> Seq.tryPick (fun p -> stringsOf p.Value)
        | None -> None
    with _ ->
        None

// ---- the suite ------------------------------------------------------------

[<Tests>]
let tests =
    testList
        "Package roster"
        [

          test "the README package table lists exactly the packable projects" {
              withRoot (fun root ->
                  let packable = packableProjects root |> List.map _.PackageId

                  let documented =
                      readmePackageIds (File.ReadAllText(Path.Combine(root, "README.md")))

                  Expect.isNonEmpty packable "at least one packable project was found under src/"

                  Expect.isNonEmpty
                      documented
                      "the README's `## Packages` table was located and at least one row parsed — an empty read here would make the comparison below vacuous"

                  match rosterDiff packable documented with
                  | [], [] -> ()
                  | missing, surplus ->
                      let part label items =
                          if List.isEmpty items then
                              ""
                          else
                              sprintf "\n       %s: %s" label (String.concat ", " items)

                      failtestf
                          "the README package table is not the packable set.%s%s\n       Remedy: edit the table in README.md — its rows are a projection of `src/*/*.fsproj` + `src/*/*.csproj` whose `IsPackable` is not `false`. A project that genuinely should not ship declares `<IsPackable>false</IsPackable>` instead of being left out of the table."
                          (part "shipping but undocumented" missing)
                          (part "documented but not shipping" surplus))
          }

          test "no packable project sits outside src/" {
              // The scope property 1 assumes. Left unchecked, a packable project added
              // anywhere else is outside the roster's frame and so can never be reported
              // missing from the README — the same drift, invisible to the check written
              // to catch it.
              withRoot (fun root ->
                  let outside = projectsOutsideSrc root
                  Expect.isNonEmpty outside "project files outside src/ were found at all (tests, samples, proofs)"

                  match outside |> List.filter snd |> List.map fst with
                  | [] -> ()
                  | packable ->
                      failtestf
                          "%d project(s) outside src/ are packable, so the README roster derivation cannot see them: %s\n       Remedy: declare `<IsPackable>false</IsPackable>`, or move the project under src/ and add its README row."
                          packable.Length
                          (String.concat ", " packable))
          }

          test "STABILITY.md carries an entry header naming the standing <Version>" {
              withRoot (fun root ->
                  let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

                  match standingVersion props with
                  | None -> failtest "Directory.Build.props declares no <Version>"
                  | Some version ->
                      let stability = File.ReadAllText(Path.Combine(root, "STABILITY.md"))

                      match entryHeaderNaming version stability with
                      | Some _ -> ()
                      | None ->
                          failtestf
                              "`<Version>` is `%s` and no STABILITY.md entry header names it.\n       Remedy: open the slot — a `## %s (draft)` header with the draft-slot preamble — and append this change's entry under it. The version is what tells a consumer what adopting it costs; a number with no entry says nothing."
                              version
                              version)
          }

          test "no 'no tag exists' sentence survives the tag it denies" {
              withRoot (fun root ->
                  match gitTags root with
                  | Error why ->
                      // Skipped by NAME with the reason printed, never passed silently.
                      // This is the source-archive / no-git case, not a defect in the tree.
                      printfn "STABILITY draft-slot tag check SKIPPED: %s" why
                      skiptestf "STABILITY draft-slot tag check skipped — %s" why
                  | Ok tags ->
                      if Set.isEmpty tags then
                          // No tag at all is legitimate only while the record says nothing was
                          // released; otherwise this clone is missing its tags (fetch them).
                          Expect.isTrue
                              (releasedNothingYet (File.ReadAllText(Path.Combine(root, "STABILITY.md"))))
                              "`git tag --list` listed no tag, and STABILITY.md records a release — this clone is missing its tags (`git fetch --tags`), so the comparison below would pass vacuously"

                      let lines = fileLines (Path.Combine(root, "STABILITY.md"))

                      match staleDraftSentences tags lines with
                      | [] -> ()
                      | stale ->
                          let shown =
                              stale
                              |> List.map (fun (n, v) ->
                                  sprintf "STABILITY.md:%d says no `v%s` tag exists — it does" n v)
                              |> String.concat "\n       "

                          failtestf
                              "%d draft-slot preamble(s) deny a tag this repository holds:\n       %s\n       Remedy: retire the sentence — the slot is released, so say so and say when. Do not delete the entries under it."
                              stale.Length
                              shown)
          }

          test "every release tag at or above the floor has a STABILITY entry header" {
              // Phase 205. Property 3 asserts the STANDING version only, so a released slot
              // that `<Version>` has moved past is outside its frame entirely — which is
              // how `v0.25.0` came to be tagged with no entry while this file read green.
              withRoot (fun root ->
                  match gitTags root with
                  | Error why ->
                      printfn "STABILITY per-tag entry check SKIPPED: %s" why
                      skiptestf "STABILITY per-tag entry check skipped — %s" why
                  | Ok tags ->
                      let covered =
                          tags
                          |> Set.toList
                          |> List.choose releaseTagVersion
                          |> List.filter (fun v -> v >= entryHeaderFloor)

                      let stability = File.ReadAllText(Path.Combine(root, "STABILITY.md"))

                      if List.isEmpty covered then
                          // Nothing at or above the floor is legitimate only while the record says
                          // this repository has released nothing (Phase 259); otherwise the empty set
                          // is the shape a mis-parsed or unfetched tag takes.
                          Expect.isTrue
                              (releasedNothingYet stability)
                              "no release tag sits at or above the floor, and STABILITY.md records a release — with none the comparison below would pass vacuously, which is the shape a mis-parsed or unfetched tag would take"

                      match tagsMissingEntryHeader entryHeaderFloor tags stability with
                      | [] -> ()
                      | missing ->
                          failtestf
                              "%d released tag(s) at or above the floor have no STABILITY.md entry header: %s\n       Remedy: open a level-2 section whose header names the version — the shape the released slots use is '0.25.0 — released <date> as v0.25.0' — and record under it what shipped in that slot, each change classed. A released version with no entry tells a consumer nothing about what adopting it costs.\n       The floor is `entryHeaderFloor` in this file, and lowering it is a decision about which old slots can still be written from evidence, not a tidy-up."
                              missing.Length
                              (String.concat ", " missing))
          }

          test "the Fable surface's declared roster — packable minus its exclusions — is documented in the README" {
              // Phase 185's cross-check, over the DECLARED surface since Phase 217 moved the Fable
              // compile out of this repository: the packages a Fable consumer can compile are the
              // packable ones minus `fable-exclusions.json`, and each is a package a
              // reader must be able to find. The file is this repository's own now, so its absence
              // or an unrecognised shape is a failure rather than a skip.
              withRoot (fun root ->
                  let exclusionsPath = Path.Combine(root, "fable-exclusions.json")

                  Expect.isTrue (File.Exists exclusionsPath) "fable-exclusions.json exists"

                  match declaredExclusions (File.ReadAllText exclusionsPath) with
                  | None -> failtest "fable-exclusions.json holds no array of package ids this reader recognises"
                  | Some excluded ->
                      let documented =
                          readmePackageIds (File.ReadAllText(Path.Combine(root, "README.md")))
                          |> Set.ofList

                      let declared =
                          packableProjects root
                          |> List.map _.PackageId
                          |> List.filter (excluded.Contains >> not)

                      Expect.isNonEmpty declared "the declared Fable surface is not empty"

                      match declared |> List.filter (documented.Contains >> not) with
                      | [] -> ()
                      | absent ->
                          failtestf
                              "%d package(s) on the declared Fable surface are absent from the README table: %s"
                              absent.Length
                              (String.concat ", " absent))
          }

          // ---- the go-red controls ------------------------------------------
          //
          // Each live case above reads a file expected to be correct, so all four would
          // pass against a classifier that matched nothing at all. These feed the same
          // functions input that must be refused.

          test "the roster diff reports a removed README row and a surplus one" {
              let packable = [ "Fuaran.Core.Tree"; "Fuaran.Core.Ops"; "Fuaran.Core.Wire" ]

              Expect.equal
                  (rosterDiff packable [ "Fuaran.Core.Tree"; "Fuaran.Core.Ops"; "Fuaran.Core.Wire" ])
                  ([], [])
                  "an exact table reports nothing"

              Expect.equal
                  (rosterDiff packable [ "Fuaran.Core.Tree"; "Fuaran.Core.Ops" ])
                  ([ "Fuaran.Core.Wire" ], [])
                  "a packable project removed from the table is reported as undocumented"

              Expect.equal
                  (rosterDiff
                      packable
                      [ "Fuaran.Core.Tree"
                        "Fuaran.Core.Ops"
                        "Fuaran.Core.Wire"
                        "Fuaran.Core.Gone" ])
                  ([], [ "Fuaran.Core.Gone" ])
                  "a row for a package that no longer ships is reported as surplus"
          }

          test "packability reads the project, then the props fallback, then MSBuild's default" {
              Expect.isFalse (isPackable None "<IsPackable>false</IsPackable>") "an explicit false excludes"
              Expect.isFalse (isPackable None "<IsPackable>FALSE</IsPackable>") "case does not matter"
              Expect.isTrue (isPackable None "<IsPackable>true</IsPackable>") "an explicit true includes"
              Expect.isTrue (isPackable None "<PropertyGroup></PropertyGroup>") "undeclared with no fallback includes"
              Expect.isFalse (isPackable (Some "false") "<PropertyGroup></PropertyGroup>") "the props fallback applies"

              Expect.isTrue
                  (isPackable (Some "false") "<IsPackable>true</IsPackable>")
                  "the project's own declaration wins over the fallback"
          }

          test "the README reader takes the Packages table and stops at the next heading" {
              let readme =
                  String.concat
                      "\n"
                      [ "# Title"
                        "Prose with a `Fuaran.Core.NotARow` backtick in it."
                        ""
                        "## Packages"
                        ""
                        "| Package | What it owns | Generic over |"
                        "|---|---|---|"
                        "| **`Fuaran.Core.Tree`** | addressing | `'Node` + `'Id` |"
                        "| **`Fuaran.Core.Ops`** | the ops | node witness |"
                        ""
                        "## The witness pattern"
                        ""
                        "| **`Fuaran.Core.Beyond`** | not a package row | — |" ]

              Expect.equal
                  (readmePackageIds readme)
                  [ "Fuaran.Core.Tree"; "Fuaran.Core.Ops" ]
                  "the header row, the separator, surrounding prose and a table in a later section are all excluded"

              Expect.isEmpty
                  (readmePackageIds "# Title\n\nno packages section here\n")
                  "a document with no Packages section yields nothing — which is why the live case asserts the read is non-empty before comparing"
          }

          test "the STABILITY header check finds a slot header and refuses a missing one" {
              let stability =
                  String.concat
                      "\n"
                      [ "# Stability"
                        "Prose mentioning 0.27.0 outside a heading."
                        "## 0.26.0 (draft)"
                        "### Something (Phase 1, `0.26.0`)"
                        "## 0.24.0 — released" ]

              Expect.isSome (entryHeaderNaming "0.26.0" stability) "the standing version's own header is found"
              Expect.isSome (entryHeaderNaming "0.24.0" stability) "an older released slot is found too"

              Expect.isNone
                  (entryHeaderNaming "0.27.0" stability)
                  "a version named only in prose is NOT an entry — this is the `<Version>` bumped without an entry case"
          }

          test "the draft-slot check reports a denied tag and stays quiet on a genuine draft" {
              let lines =
                  [ "## 0.26.0 (draft)"
                    "**This section describes a DRAFT slot.** `<Version>` reads `0.26.0` and no `v0.26.0` tag exists"
                    "yet."
                    "## 0.24.0 — released"
                    "**This section describes a DRAFT slot.** `<Version>` reads `0.24.0` and no `v0.24.0` tag exists"
                    "yet." ]

              Expect.equal
                  (staleDraftSentences (Set.ofList [ "v0.24.0"; "v0.23.0" ]) lines)
                  [ (5, "0.24.0") ]
                  "the sentence denying a tag that exists is reported, by line, and the genuine draft is not"

              Expect.isEmpty
                  (staleDraftSentences (Set.ofList [ "v0.23.0" ]) lines)
                  "with neither version tagged, both sentences are true and nothing is reported"
          }

          test "an empty tag list is admitted only while every version header is a DRAFT (Phase 259)" {
              Expect.isTrue
                  (releasedNothingYet "# Stability\n\n## 0.33.0 — DRAFT\n\nProse naming 0.32.0.\n")
                  "a record whose only version header is a draft has released nothing"

              Expect.isFalse
                  (releasedNothingYet
                      "# Stability\n\n## 0.34.0 — DRAFT\n\n## 0.33.0 — released 2026-10-01 as `v0.33.0`\n")
                  "one released header means an empty tag list is a clone missing its tags"

              Expect.isFalse
                  (releasedNothingYet "# Stability\n\nNo version headers at all.\n")
                  "a record with no version header states nothing, so it cannot excuse an empty tag list"
          }

          test "the per-tag entry check reports an un-entried release and respects the floor" {
              let stability =
                  String.concat
                      "\n"
                      [ "# Stability"
                        "## 0.26.0 — released 2026-09-17 as `v0.26.0`"
                        "## 0.24.0 — released 2026-09-15 as `v0.24.0`"
                        "Prose mentioning 0.25.0 outside a heading." ]

              let tags =
                  Set.ofList [ "v0.0.1-alpha.8"; "v0.17.0"; "v0.24.0"; "v0.25.0"; "v0.26.0" ]

              Expect.equal
                  (tagsMissingEntryHeader (0, 25, 0) tags stability)
                  [ "v0.25.0" ]
                  "the released tag with no entry header is reported — and a version named only in prose is not an entry"

              Expect.isEmpty
                  (tagsMissingEntryHeader (0, 27, 0) tags stability)
                  "a floor above every tag reports nothing, which is exactly why the live case asserts the covered set is non-empty first"

              Expect.equal
                  (tagsMissingEntryHeader (0, 17, 0) tags stability)
                  [ "v0.17.0"; "v0.25.0" ]
                  "lowering the floor pulls older un-entried slots in, oldest first — the FLOOR is what excludes them, not a gap in the reader"

              Expect.equal
                  (releaseTagVersion "v0.9.0" |> Option.map (fun v -> v < (0, 25, 0)))
                  (Some true)
                  "ordering is numeric: 0.9.0 is BELOW 0.25.0, which a lexical comparison on the tag string gets backwards"

              Expect.isNone
                  (releaseTagVersion "v0.0.1-alpha.8")
                  "a pre-release spelling parses to nothing, so it is out of frame rather than reported"

              Expect.isNone (releaseTagVersion "release/v1.2.3") "a foreign tagging convention is out of frame too"
          }

          test "the exclusions reader accepts the plausible shapes and refuses the rest" {
              Expect.equal
                  (declaredExclusions """["Fuaran.Core.Idl.Codegen","Fuaran.Core.Observer"]""")
                  (Some(Set.ofList [ "Fuaran.Core.Idl.Codegen"; "Fuaran.Core.Observer" ]))
                  "a bare array of ids"

              Expect.equal
                  (declaredExclusions """{"kind":"fableSmokeExclusions","exclusions":["Fuaran.Core.Observer"]}""")
                  (Some(Set.ofList [ "Fuaran.Core.Observer" ]))
                  "an object whose first array-of-strings property carries them"

              Expect.equal
                  (declaredExclusions """{"exclusions":[{"package":"Fuaran.Core.Observer","why":"build-time"}]}""")
                  (Some(Set.ofList [ "Fuaran.Core.Observer" ]))
                  "an array of objects naming the package"

              Expect.isNone (declaredExclusions "{ not json") "unparseable input is a named skip, never a failure"

              Expect.isNone
                  (declaredExclusions """{"note":"nothing excluded yet"}""")
                  "a shape carrying no id array is a named skip too — a sibling's newer format must not redden this"
          } ]
