module Fuaran.Compute.Tests.PackageRosterTests

// ---------------------------------------------------------------------------
// Phase 199 — the package roster is DERIVED, and the release record is held to
// `<Version>` and to the tags.
//
// `README.md`'s package table and the release record's headings are hand-maintained
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
// Ten properties, each with a go-red case beside it over synthetic input. The live case
// alone cannot show a classifier works — every one of these reads a real file that is
// expected to be correct, so a classifier that matched nothing would pass all of them.
//
//   1. README rows = the packable set.
//   2. No packable project sits outside `src/` — the SCOPE property 1's derivation assumes,
//      checked rather than trusted. Without it a package added elsewhere is invisible to
//      property 1 by being out of frame, which is the same drift wearing a different hat.
//   3. The standing `<Version>` has a release slot: `docs/releases/<v>/`, headed by it.
//   4. No "no `vX.Y.Z` tag exists" sentence survives the tag it denies.
//   5. Every release TAG at or above a declared floor has a slot.
//   6. Each slot's heading agrees with the tags: a DRAFT is untagged, a released version is
//      tagged, and the untagged standing `<Version>` is the DRAFT.
//   7. Every slot is headed by its own version, and holds only entry files.
//   8. STABILITY.md holds the contract and no release heading.
//   9. DECISIONS.md is closed: it holds D1 to D91, each once, and every `## ` heading in it is one
//      of those entries.
//  10. Every name under `docs/decisions/` is a decision file, `D<phase>.<n>.md`, headed by its own
//      name and phase, and a phase's decisions run from 1 with no gap.
//
// Properties 9 and 10 are Phase 735's (D735.1): the decision record left the one file every
// change prepended to, for a file per decision named for the phase that made it, and they make
// the old habit fail the gate rather than a review.
//
// Phase 734 moved the record out of STABILITY.md into a directory per version with a file per
// entry (DECISIONS.md D91), and properties 3 to 5 read it there. Properties 6 to 8 are that
// phase's: 6 because `0.47.0` was found tagged under a DRAFT heading with properties 3 to 5
// green, and 7 and 8 because a layout nothing reads is a layout that drifts back.
//
// Property 5 is Phase 205, and it exists because property 3 structurally cannot see what
// it caught. Property 3 quantifies over the STANDING `<Version>` — one number — so a slot
// that was cut, tagged, released and then left behind as `<Version>` moved on is invisible
// to it: `v0.25.0` was tagged 2026-09-15 and the record carried no entry for it at all,
// while property 3 read green against `0.26.0` the whole time. Quantifying over the tags
// is what closes that, and the FLOOR is what keeps it honest — every release below
// `0.25.0` predates this document's per-slot classes, so demanding entries for them would
// demand invention rather than record.
//
// Properties 4, 5 and 6 read `git tag` LOCALLY, which is the honest limit: they answer "has
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

// ---- the release record ---------------------------------------------------

/// The standing `<Version>` from `Directory.Build.props`.
let internal standingVersion (propsText: string) : string option =
    firstGroup (Regex @"<Version>([^<]*)</Version>") propsText

/// The release record's directory, relative to the repository root (Phase 734, DECISIONS.md D91).
let internal releasesDir = Path.Combine("docs", "releases")

/// One version's directory under `docs/releases/`, as read from the tree.
type internal ReleaseSlot =
    {
        /// The directory's name, which is the version it records.
        Version: string
        /// The first line of the directory's `README.md` — the version's heading — or `None` when it has
        /// no README.
        Heading: string option
        /// Every other name in the directory, a subdirectory with a trailing `/`.
        Entries: string list
    }

/// What a slot's heading says of its version.
type internal SlotStatus =
    | Draft
    | Released of tag: string
    | NeverReleased

/// The three headings a slot may open with, and only those: `## <v> — DRAFT`,
/// `## <v> — released <date> as \`v<v>\`` and `## <v> — never released; its entries ship in \`<w>\``, each
/// naming the directory's own version. Anything else is a heading this record cannot read, and is
/// reported rather than guessed at.
let internal slotStatus (slot: ReleaseSlot) : Result<SlotStatus, string> =
    let v = Regex.Escape slot.Version

    match slot.Heading with
    | None -> Error "has no README.md, so nothing heads it"
    | Some h when Regex.IsMatch(h, sprintf @"^## %s — DRAFT$" v) -> Ok Draft
    | Some h when Regex.IsMatch(h, sprintf @"^## %s — released \d{4}-\d{2}-\d{2} as `v%s`$" v v) ->
        Ok(Released("v" + slot.Version))
    | Some h when Regex.IsMatch(h, sprintf @"^## %s — never released; its entries ship in `\d+\.\d+\.\d+`$" v) ->
        Ok NeverReleased
    | Some h ->
        Error(
            sprintf
                "opens with `%s`, which is not `## %s — DRAFT`, `## %s — released <date> as `v%s`` or `## %s — never released; its entries ship in `<version>``"
                h
                slot.Version
                slot.Version
                slot.Version
                slot.Version
        )

let private entryNameRe =
    Regex(@"^(phase-\d{4,}|change-[a-z0-9][a-z0-9.-]*)\.md$", RegexOptions.Compiled)

/// Findings that need no tag: a slot whose heading cannot be read, a DRAFT that is not the standing
/// `<Version>` (one draft at a time, and it is the number the packages build at), and a name in a slot
/// that is not an entry file (`phase-<n>.md`, `<n>` four digits or more, or `change-<slug>.md`), or an entry
/// under a never-released heading — the case a branch in flight makes when its draft advanced under it.
let internal slotLayoutFindings (standing: string) (slots: ReleaseSlot list) : string list =
    [ for s in slots do
          match slotStatus s with
          | Error why -> yield sprintf "docs/releases/%s/ %s" s.Version why
          | Ok Draft when s.Version <> standing ->
              yield sprintf "docs/releases/%s/ is headed DRAFT, and the standing `<Version>` is `%s`" s.Version standing
          | Ok NeverReleased when not s.Entries.IsEmpty ->
              yield
                  sprintf
                      "docs/releases/%s/ is headed never released and holds entries: its entries ship in the version it names, and belong there"
                      s.Version
          | Ok _ -> ()

          for e in s.Entries do
              if not (entryNameRe.IsMatch e) then
                  yield
                      sprintf
                          "docs/releases/%s/%s is not an entry file: an entry is `phase-<n>.md` (four digits or more) or `change-<slug>.md`"
                          s.Version
                          e ]

/// Findings against the tags: a DRAFT heading over a version that is tagged (the release was made and
/// the heading not moved — how `0.47.0` read when Phase 734 found it), a released or never-released
/// heading that disagrees with the tag list, and a standing `<Version>` that is untagged and not headed
/// DRAFT.
let internal slotTagFindings (standing: string) (tags: Set<string>) (slots: ReleaseSlot list) : string list =
    [ for s in slots do
          let tagged = tags.Contains("v" + s.Version)

          match slotStatus s with
          | Ok Draft when tagged ->
              yield
                  sprintf
                      "docs/releases/%s/ is headed DRAFT, and `v%s` is tagged: the release was made and its heading was not moved"
                      s.Version
                      s.Version
          | Ok(Released tag) when not tagged ->
              yield sprintf "docs/releases/%s/ is headed released as `%s`, and no such tag exists" s.Version tag
          | Ok NeverReleased when tagged ->
              yield sprintf "docs/releases/%s/ is headed never released, and `v%s` is tagged" s.Version s.Version
          | Ok status when s.Version = standing && not tagged && status <> Draft ->
              yield
                  sprintf
                      "the standing `<Version>` `%s` is untagged, so its slot is the draft, and docs/releases/%s/ is not headed DRAFT"
                      s.Version
                      s.Version
          | _ -> () ]

/// The slot recording `version`, when one exists and its heading can be read.
let internal slotNaming (version: string) (slots: ReleaseSlot list) : ReleaseSlot option =
    slots
    |> List.tryFind (fun s -> s.Version = version && Result.isOk (slotStatus s))

/// The lines of STABILITY.md that open a version section or a change entry — the record that left it in
/// Phase 734. The contract keeps none, so a non-empty answer is an entry written to the wrong file.
let internal stabilityEntryHeadings (stability: string) : string list =
    stability.Replace("\r\n", "\n").Split('\n')
    |> Array.filter (fun l ->
        Regex.IsMatch(l, @"^##\s+\d+\.\d+\.\d+")
        || l.StartsWith("### ", StringComparison.Ordinal))
    |> Array.toList

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
/// from the Fuaran.Core repository and is recorded in that repository's release ledger, which
/// this one's points to rather than restates.
let internal entryHeaderFloor = (0, 33, 0)

/// True when every slot's heading reads DRAFT — the record's own statement that this repository has
/// released nothing yet (Phase 259). It is what lets a clone holding NO release tag be read as "nothing
/// released" rather than as "the tags were not fetched": the two are told apart by the record, not by
/// the absence. Once any heading records a release, an empty tag list is a clone missing its tags, and
/// the tag legs refuse it as the substrate's do. False for a record with no slot at all, which is not a
/// statement of anything.
let internal releasedNothingYet (slots: ReleaseSlot list) : bool =
    not slots.IsEmpty && slots |> List.forall (fun s -> slotStatus s = Ok Draft)

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

/// Release tags at or above `floor` that no slot records, oldest first.
///
/// Ordering is NUMERIC, over the parsed triple, not lexical over the tag string: `0.9.0`
/// sorts BELOW `0.25.0` and a string comparison gets that backwards, which would silently
/// admit every pre-`0.10` tag the floor exists to exclude. It reuses `slotNaming` —
/// the same seam property 3 asserts the standing version through — so the two properties
/// cannot disagree about what counts as a record.
let internal tagsMissingSlot (floor: int * int * int) (tags: Set<string>) (slots: ReleaseSlot list) : string list =
    tags
    |> Set.toList
    |> List.choose (fun t -> releaseTagVersion t |> Option.map (fun v -> v, t))
    |> List.filter (fun (v, _) -> v >= floor)
    |> List.sortBy fst
    |> List.filter (fun (_, t) -> (slotNaming (t.Substring 1) slots).IsNone)
    |> List.map snd

/// Every version directory under `docs/releases/`, by directory name. Empty when the directory is absent.
let internal readSlots (root: string) : ReleaseSlot list =
    let dir = Path.Combine(root, releasesDir)

    if not (Directory.Exists dir) then
        []
    else
        Directory.GetDirectories dir
        |> Array.toList
        |> List.map (fun d ->
            let readme = Path.Combine(d, "README.md")

            { Version = Path.GetFileName d
              Heading =
                if File.Exists readme then
                    File.ReadAllText(readme).Replace("\r\n", "\n").Split('\n') |> Array.tryHead
                else
                    None
              Entries =
                [ for f in Directory.GetFiles d do
                      let n = Path.GetFileName f

                      if n <> "README.md" then
                          yield n
                  for sub in Directory.GetDirectories d do
                      yield Path.GetFileName sub + "/" ]
                |> List.sort })
        |> List.sortBy _.Version

// ---- the decision record (Phase 735, D735.1) -------------------------------

/// The last number `DECISIONS.md` holds. The record is closed at it, and every decision since is a file under
/// `docs/decisions/` (D735.1 item 5). ONE named constant, because it is the line between two records.
let internal closedDecisionsLast = 91

/// The directory a decision is written to from Phase 735 on, relative to the repository root.
let internal decisionsDir = Path.Combine("docs", "decisions")

let private decisionEntryRe =
    Regex(@"^## \d{4}-\d{2}-\d{2} — D(\d{1,9}): \S", RegexOptions.Compiled)

/// Findings against the closed record, each naming its line: a `## ` heading that is not an entry heading
/// (`## <yyyy-mm-dd> — D<n>: <title>`, so an entry written in the new form lands here), an entry numbered
/// above `last`, a number held twice, and a number from 1 to `last` the record no longer holds.
let internal closedRecordFindings (last: int) (record: string) : string list =
    let headings =
        record.Replace("\r\n", "\n").Split('\n')
        |> Array.toList
        |> List.indexed
        |> List.filter (fun (_, l) -> l.StartsWith("## ", StringComparison.Ordinal))
        |> List.map (fun (i, l) -> i + 1, l)

    let numbered =
        headings
        |> List.choose (fun (line, l) ->
            let m = decisionEntryRe.Match l

            if m.Success then
                Some(line, int m.Groups[1].Value)
            else
                None)

    let held = numbered |> List.map snd |> Set.ofList

    [ for line, l in headings do
          if not (decisionEntryRe.IsMatch l) then
              yield
                  sprintf
                      "DECISIONS.md line %d is a `## ` heading and not an entry heading `## <yyyy-mm-dd> — D<n>: <title>`: %s"
                      line
                      (if l.Length > 100 then l.Substring(0, 100) + "…" else l)
      for line, n in numbered do
          if n < 1 || n > last then
              yield sprintf "DECISIONS.md line %d adds D%d, and the record is closed at D%d" line n last
      for n, at in numbered |> List.groupBy snd do
          if at.Length > 1 then
              yield
                  sprintf
                      "DECISIONS.md holds D%d %d times, at lines %s"
                      n
                      at.Length
                      (at |> List.map (fst >> string) |> String.concat ", ")
      for n in 1..last do
          if not (held.Contains n) then
              yield sprintf "DECISIONS.md no longer holds D%d: no entry of the closed record is renumbered or removed" n ]

let private decisionFileRe =
    Regex(@"^D(\d{4,9})\.([1-9]\d{0,8})\.md$", RegexOptions.Compiled)

/// Findings against `docs/decisions/`, given each name in it with its text (`None` for a subdirectory, named
/// with a trailing `/`): a name that is not `D<phase>.<n>.md` (`<phase>` zero-padded to four digits, `<n>`
/// from `1` with no leading zero), a header that does not name the file's own decision and phase
/// (`# D<phase>.<n>: <title>`, a blank line, `**Date:** <yyyy-mm-dd>. **Phase:** <phase>.`), and a phase whose
/// decisions do not run from `1` with no gap.
let internal decisionFileFindings (files: (string * string option) list) : string list =
    let named =
        files
        |> List.choose (fun (name, text) ->
            let m = decisionFileRe.Match name

            if m.Success && (int m.Groups[1].Value).ToString("D4") = m.Groups[1].Value then
                Some(name, int m.Groups[1].Value, int m.Groups[2].Value, text)
            else
                None)

    let namedSet = named |> List.map (fun (n, _, _, _) -> n) |> Set.ofList

    [ for name, _ in files do
          if not (namedSet.Contains name) then
              yield
                  sprintf
                      "docs/decisions/%s is not a decision file: a decision is `D<phase>.<n>.md`, the phase zero-padded to four digits and `<n>` from 1 (`D0735.1.md`)"
                      name
      for name, phase, n, text in named do
          let lines = (defaultArg text "").Replace("\r\n", "\n").Split('\n') |> Array.toList

          let line i =
              List.tryItem i lines |> Option.defaultValue ""

          if
              not (line(0).StartsWith(sprintf "# D%d.%d: " phase n, StringComparison.Ordinal))
              || line(0).Length <= (sprintf "# D%d.%d: " phase n).Length
          then
              yield sprintf "docs/decisions/%s does not open with `# D%d.%d: <title>`" name phase n

          if
              line 1 <> ""
              || not (Regex.IsMatch(line 2, sprintf @"^\*\*Date:\*\* \d{4}-\d{2}-\d{2}\. \*\*Phase:\*\* %d\.$" phase))
          then
              yield
                  sprintf
                      "docs/decisions/%s does not carry `**Date:** <yyyy-mm-dd>. **Phase:** %d.` on its third line, after a blank one"
                      name
                      phase
      for phase, ds in named |> List.groupBy (fun (_, p, _, _) -> p) do
          let ns = ds |> List.map (fun (_, _, n, _) -> n) |> List.sort

          if ns <> [ 1 .. ns.Length ] then
              yield
                  sprintf
                      "Phase %d's decisions are D%d.%s: a phase's decisions run from 1 with no gap"
                      phase
                      phase
                      (ns |> List.map string |> String.concat (sprintf ", D%d." phase)) ]

/// Every name under `docs/decisions/` with its text, a subdirectory with a trailing `/` and no text. Empty
/// when the directory is absent.
let internal readDecisionFiles (root: string) : (string * string option) list =
    let dir = Path.Combine(root, decisionsDir)

    if not (Directory.Exists dir) then
        []
    else
        [ for f in Directory.GetFiles dir do
              yield Path.GetFileName f, Some(File.ReadAllText f)
          for sub in Directory.GetDirectories dir do
              yield Path.GetFileName sub + "/", None ]
        |> List.sortBy fst

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

          test "DECISIONS.md is closed: it holds D1 to D91, each once, and nothing is added to it" {
              // Phase 735 (D735.1): a decision since is a file of its own under docs/decisions/, so two
              // changes in flight never edit one file or take one number. An entry added here is the old
              // habit, and it is refused rather than read.
              withRoot (fun root ->
                  match
                      closedRecordFindings closedDecisionsLast (File.ReadAllText(Path.Combine(root, "DECISIONS.md")))
                  with
                  | [] -> ()
                  | found ->
                      failtestf
                          "%d finding(s) against the closed decision record:\n         %s\n       Remedy: a new decision is its own file, docs/decisions/D<phase>.<n>.md, cited D<phase>.<n>; DECISIONS.md holds D1 to D%d and is edited by no change (D735.1)."
                          found.Length
                          (String.concat "\n         " (List.truncate 10 found))
                          closedDecisionsLast)
          }

          test "every file under docs/decisions/ is a decision named for its phase and headed by its own name" {
              withRoot (fun root ->
                  let files = readDecisionFiles root

                  Expect.isTrue
                      (files |> List.exists (fun (n, _) -> n = "D0735.1.md"))
                      "docs/decisions/ holds D735.1, the decision that fixes its layout"

                  match decisionFileFindings files with
                  | [] -> ()
                  | found ->
                      failtestf
                          "%d finding(s) under docs/decisions/:\n         %s\n       Remedy: a decision is docs/decisions/D<phase>.<n>.md, the phase zero-padded to four digits, headed `# D<phase>.<n>: <title>` with `**Date:** <yyyy-mm-dd>. **Phase:** <phase>.` on its third line (D735.1)."
                          found.Length
                          (String.concat "\n         " (List.truncate 10 found)))
          }

          test "STABILITY.md holds the contract and no release entry" {
              // Phase 734: the record left this file for docs/releases/, a file per entry, so two
              // changes in flight never edit one file. An entry written here again is the old layout
              // coming back by habit, and it is refused rather than read.
              withRoot (fun root ->
                  let stability = File.ReadAllText(Path.Combine(root, "STABILITY.md"))

                  match stabilityEntryHeadings stability with
                  | [] -> ()
                  | found ->
                      failtestf
                          "STABILITY.md holds %d release heading(s), the first: %s\n       Remedy: a change's entry is its own file, docs/releases/<standing version>/phase-<n>.md; the version's heading is that directory's README.md. STABILITY.md is the contract only (DECISIONS.md D91)."
                          found.Length
                          (String.concat " | " (List.truncate 5 found)))
          }

          test "every slot under docs/releases/ is headed by its own version and holds only entry files" {
              withRoot (fun root ->
                  let slots = readSlots root

                  Expect.isNonEmpty
                      slots
                      "docs/releases/ was read and holds at least one version directory — an empty read here would make the checks below vacuous"

                  match standingVersion (File.ReadAllText(Path.Combine(root, "Directory.Build.props"))) with
                  | None -> failtest "Directory.Build.props declares no <Version>"
                  | Some standing ->
                      match slotLayoutFindings standing slots with
                      | [] -> ()
                      | findings ->
                          failtestf
                              "%d finding(s) in the release record:\n       %s\n       Remedy: a slot's README.md opens with `## <v> — DRAFT` (the standing `<Version>` only), `## <v> — released <date> as `v<v>`` or `## <v> — never released; its entries ship in `<w>``, and every other file in it is one entry (DECISIONS.md D91)."
                              findings.Length
                              (String.concat "\n       " findings))
          }

          test "the standing <Version> has a release slot" {
              withRoot (fun root ->
                  let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

                  match standingVersion props with
                  | None -> failtest "Directory.Build.props declares no <Version>"
                  | Some version ->
                      match slotNaming version (readSlots root) with
                      | Some _ -> ()
                      | None ->
                          failtestf
                              "`<Version>` is `%s` and docs/releases/%s/ does not exist or is not headed by it.\n       Remedy: open the slot — docs/releases/%s/README.md headed `## %s — DRAFT` with the draft-slot preamble — and add this change's entry beside it as its own file. The version is what tells a consumer what adopting it costs; a number with no record says nothing."
                              version
                              version
                              version
                              version)
          }

          test "no 'no tag exists' sentence survives the tag it denies" {
              withRoot (fun root ->
                  match gitTags root with
                  | Error why ->
                      // Skipped by NAME with the reason printed, never passed silently.
                      // This is the source-archive / no-git case, not a defect in the tree.
                      printfn "release-record draft-slot tag check SKIPPED: %s" why
                      skiptestf "release-record draft-slot tag check skipped — %s" why
                  | Ok tags ->
                      if Set.isEmpty tags then
                          // No tag at all is legitimate only while the record says nothing was
                          // released; otherwise this clone is missing its tags (fetch them).
                          Expect.isTrue
                              (releasedNothingYet (readSlots root))
                              "`git tag --list` listed no tag, and docs/releases/ records a release — this clone is missing its tags (`git fetch --tags`), so the comparison below would pass vacuously"

                      let documents =
                          Path.Combine(root, "STABILITY.md")
                          :: (Directory.GetFiles(Path.Combine(root, releasesDir), "*.md", SearchOption.AllDirectories)
                              |> Array.sort
                              |> Array.toList)

                      let stale =
                          documents
                          |> List.collect (fun path ->
                              staleDraftSentences tags (fileLines path)
                              |> List.map (fun (n, v) ->
                                  sprintf
                                      "%s:%d says no `v%s` tag exists — it does"
                                      (Path.GetRelativePath(root, path).Replace('\\', '/'))
                                      n
                                      v))

                      match stale with
                      | [] -> ()
                      | stale ->
                          failtestf
                              "%d draft-slot preamble(s) deny a tag this repository holds:\n       %s\n       Remedy: retire the sentence — the slot is released, so say so and say when. Do not delete the entries beside it."
                              stale.Length
                              (String.concat "\n       " stale))
          }

          test "every release tag at or above the floor has a release slot" {
              // Phase 205. Property 3 asserts the STANDING version only, so a released slot
              // that `<Version>` has moved past is outside its frame entirely — which is
              // how `v0.25.0` came to be tagged with no entry while this file read green.
              withRoot (fun root ->
                  match gitTags root with
                  | Error why ->
                      printfn "release-record per-tag check SKIPPED: %s" why
                      skiptestf "release-record per-tag check skipped — %s" why
                  | Ok tags ->
                      let covered =
                          tags
                          |> Set.toList
                          |> List.choose releaseTagVersion
                          |> List.filter (fun v -> v >= entryHeaderFloor)

                      let slots = readSlots root

                      if List.isEmpty covered then
                          // Nothing at or above the floor is legitimate only while the record says
                          // this repository has released nothing (Phase 259); otherwise the empty set
                          // is the shape a mis-parsed or unfetched tag takes.
                          Expect.isTrue
                              (releasedNothingYet slots)
                              "no release tag sits at or above the floor, and docs/releases/ records a release — with none the comparison below would pass vacuously, which is the shape a mis-parsed or unfetched tag would take"

                      match tagsMissingSlot entryHeaderFloor tags slots with
                      | [] -> ()
                      | missing ->
                          failtestf
                              "%d released tag(s) at or above the floor have no release slot: %s\n       Remedy: open docs/releases/<v>/ with a README.md headed '## <v> — released <date> as `v<v>`' and record beside it what shipped in that slot, a file per change, each classed. A released version with no record tells a consumer nothing about what adopting it costs.\n       The floor is `entryHeaderFloor` in this file, and lowering it is a decision about which old slots can still be written from evidence, not a tidy-up."
                              missing.Length
                              (String.concat ", " missing))
          }

          test "each slot's heading agrees with the tags: a draft is untagged, a released version is tagged" {
              // Phase 734. Until this rule, `0.47.0` was tagged with its heading still reading DRAFT,
              // and every property above was green: none of them reads a heading's status.
              withRoot (fun root ->
                  match gitTags root with
                  | Error why ->
                      printfn "release-record heading-status check SKIPPED: %s" why
                      skiptestf "release-record heading-status check skipped — %s" why
                  | Ok tags ->
                      let slots = readSlots root

                      if Set.isEmpty tags then
                          Expect.isTrue
                              (releasedNothingYet slots)
                              "`git tag --list` listed no tag, and docs/releases/ records a release — this clone is missing its tags (`git fetch --tags`), so the comparison below would pass vacuously"

                      match standingVersion (File.ReadAllText(Path.Combine(root, "Directory.Build.props"))) with
                      | None -> failtest "Directory.Build.props declares no <Version>"
                      | Some standing ->
                          match slotTagFindings standing tags slots with
                          | [] -> ()
                          | findings ->
                              failtestf
                                  "%d slot heading(s) disagree with the tags:\n       %s\n       Remedy: the release commit heads its slot `## <v> — released <date> as `v<v>``; a version moved past untagged is headed `never released; its entries ship in `<w>``; the standing `<Version>` is the one DRAFT (DECISIONS.md D91)."
                                  findings.Length
                                  (String.concat "\n       " findings))
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
          // Each live case above reads a file expected to be correct, so every one would
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

          test "the slot heading reader takes the three headings and refuses the rest" {
              let slot heading =
                  { Version = "0.26.0"
                    Heading = heading
                    Entries = [] }

              Expect.equal (slotStatus (slot (Some "## 0.26.0 — DRAFT"))) (Ok Draft) "a draft heading"

              Expect.equal
                  (slotStatus (slot (Some "## 0.26.0 — released 2026-09-17 as `v0.26.0`")))
                  (Ok(Released "v0.26.0"))
                  "a released heading, naming its tag"

              Expect.equal
                  (slotStatus (slot (Some "## 0.26.0 — never released; its entries ship in `0.26.1`")))
                  (Ok NeverReleased)
                  "a never-released heading"

              Expect.isError (slotStatus (slot None)) "a slot with no README is headed by nothing"

              Expect.isError
                  (slotStatus (slot (Some "## 0.27.0 — DRAFT")))
                  "a heading naming another version than its directory is refused"

              Expect.isError
                  (slotStatus (slot (Some "## 0.26.0 (draft)")))
                  "an older spelling of a draft is refused rather than guessed at"

              Expect.isError
                  (slotStatus (slot (Some "## 0.26.0 — released 2026-09-17 as `v0.25.0`")))
                  "a released heading naming another version's tag is refused"
          }

          test "the layout findings report a second draft and a name that is not an entry" {
              let slots =
                  [ { Version = "0.26.0"
                      Heading = Some "## 0.26.0 — DRAFT"
                      Entries = [ "phase-0734.md"; "change-substrate-0.41.0.md"; "phase-10000.md" ] }
                    { Version = "0.25.0"
                      Heading = Some "## 0.25.0 — released 2026-09-15 as `v0.25.0`"
                      Entries = [ "phase-0700.md" ] } ]

              Expect.isEmpty (slotLayoutFindings "0.26.0" slots) "one draft, at the standing version, with entry files"

              Expect.equal
                  (slotLayoutFindings "0.27.0" slots |> List.length)
                  1
                  "a DRAFT that is not the standing <Version> is reported"

              let badNames =
                  [ { Version = "0.26.0"
                      Heading = Some "## 0.26.0 — DRAFT"
                      Entries = [ "phase-734.md"; "notes.txt"; "Phase-0734.md"; "sub/" ] } ]

              Expect.equal
                  (slotLayoutFindings "0.26.0" badNames |> List.length)
                  4
                  "an unpadded phase number, a non-entry file, a capitalised name and a subdirectory are each reported"

              Expect.equal
                  (slotLayoutFindings
                      "0.26.0"
                      [ { Version = "0.25.1"
                          Heading = Some "## 0.25.1 — never released; its entries ship in `0.26.0`"
                          Entries = [ "phase-0735.md" ] } ]
                   |> List.length)
                  1
                  "an entry under a never-released heading is reported"

              Expect.equal
                  (slotLayoutFindings
                      "0.26.0"
                      [ { Version = "0.26.0"
                          Heading = None
                          Entries = [] } ]
                   |> List.length)
                  1
                  "a slot with no heading is reported"
          }

          test "the tag findings report a tagged draft, an untagged release and an undrafted standing version" {
              let slot v heading =
                  { Version = v
                    Heading = Some heading
                    Entries = [] }

              let honest =
                  [ slot "0.26.0" "## 0.26.0 — DRAFT"
                    slot "0.25.0" "## 0.25.0 — released 2026-09-15 as `v0.25.0`"
                    slot "0.24.1" "## 0.24.1 — never released; its entries ship in `0.25.0`" ]

              let tags = Set.ofList [ "v0.24.0"; "v0.25.0" ]

              Expect.isEmpty
                  (slotTagFindings "0.26.0" tags honest)
                  "an untagged draft, a tagged release, an untagged abandoned slot"

              Expect.equal
                  (slotTagFindings "0.26.0" (tags.Add "v0.26.0") honest |> List.length)
                  1
                  "the draft's version tagged with its heading unmoved is reported — the 0.47.0 case"

              Expect.equal
                  (slotTagFindings "0.26.0" (tags.Remove "v0.25.0") honest |> List.length)
                  1
                  "a released heading whose tag does not exist is reported"

              Expect.equal
                  (slotTagFindings "0.26.0" (tags.Add "v0.24.1") honest |> List.length)
                  1
                  "a never-released heading over a tagged version is reported"

              Expect.equal
                  (slotTagFindings
                      "0.26.0"
                      tags
                      [ slot "0.26.0" "## 0.26.0 — never released; its entries ship in `0.27.0`" ]
                   |> List.length)
                  1
                  "the untagged standing version not headed DRAFT is reported"
          }

          test "the closed-record reader refuses an added, a new-form, a doubled and a removed entry" {
              let record =
                  String.concat
                      "\n"
                      [ "# decisions (newest first)"
                        ""
                        "A head note, which is not a heading."
                        ""
                        "## 2026-01-03 — D3: third"
                        "### a sub-heading inside an entry is not an entry"
                        "## 2026-01-02 — D2: second"
                        "## 2026-01-01 — D1: first" ]

              let withTop (heading: string) =
                  record.Replace("## 2026-01-03", heading + "\n## 2026-01-03")

              Expect.isEmpty (closedRecordFindings 3 record) "the closed record reads clean"

              Expect.equal
                  (closedRecordFindings 3 (withTop "## 2026-02-01 — D4: a new entry"))
                  [ "DECISIONS.md line 5 adds D4, and the record is closed at D3" ]
                  "an entry above the last number is refused by line"

              Expect.equal
                  (closedRecordFindings 3 (withTop "## 2026-02-01 — D735.1: the new form in the old file"))
                  [ "DECISIONS.md line 5 is a `## ` heading and not an entry heading `## <yyyy-mm-dd> — D<n>: <title>`: ## 2026-02-01 — D735.1: the new form in the old file" ]
                  "an entry in the new form written into the old file is refused"

              Expect.equal
                  (closedRecordFindings 3 (withTop "## 2026-02-01 — D2: second again"))
                  [ "DECISIONS.md holds D2 2 times, at lines 5, 8" ]
                  "a number taken twice is refused"

              Expect.equal
                  (closedRecordFindings 3 (record.Replace("## 2026-01-02 — D2: second\n", "")))
                  [ "DECISIONS.md no longer holds D2: no entry of the closed record is renumbered or removed" ]
                  "a removed entry is refused"

              Expect.equal (closedRecordFindings 3 (record.Replace("\n", "\r\n"))) [] "CRLF line endings read the same"
          }

          test "the decision-file reader refuses a misnamed file, a wrong header and a gap" {
              let decision (phase: int) (n: int) =
                  Some(
                      sprintf
                          "# D%d.%d: a title\n\n**Date:** 2026-11-28. **Phase:** %d.\n\n**Context.** …\n"
                          phase
                          n
                          phase
                  )

              let good =
                  [ "D0735.1.md", decision 735 1
                    "D0735.2.md", decision 735 2
                    "D1200.1.md", decision 1200 1 ]

              Expect.isEmpty (decisionFileFindings good) "well-named, well-headed decisions read clean"

              let notADecision name =
                  sprintf
                      "docs/decisions/%s is not a decision file: a decision is `D<phase>.<n>.md`, the phase zero-padded to four digits and `<n>` from 1 (`D0735.1.md`)"
                      name

              for name in
                  [ "D735.1.md"
                    "D00735.1.md"
                    "D0735.01.md"
                    "D0735.0.md"
                    "D92.md"
                    "0735.1.md"
                    "d0735.1.md"
                    "D0735.1.txt"
                    "README.md" ] do
                  Expect.equal
                      (decisionFileFindings [ name, decision 735 1 ])
                      [ notADecision name ]
                      (sprintf "%s is refused by name" name)

              Expect.equal
                  (decisionFileFindings [ "notes/", None ])
                  [ notADecision "notes/" ]
                  "a subdirectory is refused"

              Expect.equal
                  (decisionFileFindings [ "D0735.1.md", decision 736 1 ])
                  [ "docs/decisions/D0735.1.md does not open with `# D735.1: <title>`"
                    "docs/decisions/D0735.1.md does not carry `**Date:** <yyyy-mm-dd>. **Phase:** 735.` on its third line, after a blank one" ]
                  "a header naming another decision and phase is refused"

              Expect.equal
                  (decisionFileFindings [ "D0735.1.md", Some "# D735.1: \n\n**Date:** 28 Nov. **Phase:** 735.\n" ])
                  [ "docs/decisions/D0735.1.md does not open with `# D735.1: <title>`"
                    "docs/decisions/D0735.1.md does not carry `**Date:** <yyyy-mm-dd>. **Phase:** 735.` on its third line, after a blank one" ]
                  "an empty title and a date not in the form are refused"

              Expect.equal
                  (decisionFileFindings [ "D0735.1.md", decision 735 1; "D0735.3.md", decision 735 3 ])
                  [ "Phase 735's decisions are D735.1, D735.3: a phase's decisions run from 1 with no gap" ]
                  "a gap in a phase's decisions is refused"

              Expect.equal
                  (decisionFileFindings
                      [ "D0735.1.md", decision 735 1 |> Option.map (fun t -> t.Replace("\n", "\r\n")) ])
                  []
                  "CRLF line endings read the same"
          }

          test "the contract reader finds a release heading written back into STABILITY.md" {
              let contract =
                  "# Stability\n\n## Versioning policy\n\nProse naming 0.27.0.\n\n## The release record\n"

              Expect.isEmpty (stabilityEntryHeadings contract) "the contract's own headings are not release headings"

              Expect.equal
                  (stabilityEntryHeadings (contract + "\n## 0.27.0 — DRAFT\n\n### Something (Phase 1) — additive\n"))
                  [ "## 0.27.0 — DRAFT"; "### Something (Phase 1) — additive" ]
                  "a version section and an entry heading are both found"
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

          test "an empty tag list is admitted only while every slot is a DRAFT (Phase 259)" {
              let slot v heading =
                  { Version = v
                    Heading = Some heading
                    Entries = [] }

              Expect.isTrue
                  (releasedNothingYet [ slot "0.33.0" "## 0.33.0 — DRAFT" ])
                  "a record whose only slot is a draft has released nothing"

              Expect.isFalse
                  (releasedNothingYet
                      [ slot "0.34.0" "## 0.34.0 — DRAFT"
                        slot "0.33.0" "## 0.33.0 — released 2026-10-01 as `v0.33.0`" ])
                  "one released heading means an empty tag list is a clone missing its tags"

              Expect.isFalse
                  (releasedNothingYet [])
                  "a record with no slot states nothing, so it cannot excuse an empty tag list"
          }

          test "the per-tag check reports a release with no slot and respects the floor" {
              let slots =
                  [ { Version = "0.26.0"
                      Heading = Some "## 0.26.0 — released 2026-09-17 as `v0.26.0`"
                      Entries = [] }
                    { Version = "0.24.0"
                      Heading = Some "## 0.24.0 — released 2026-09-15 as `v0.24.0`"
                      Entries = [] }
                    { Version = "0.25.0"
                      Heading = None
                      Entries = [ "phase-0205.md" ] } ]

              let tags =
                  Set.ofList [ "v0.0.1-alpha.8"; "v0.17.0"; "v0.24.0"; "v0.25.0"; "v0.26.0" ]

              Expect.equal
                  (tagsMissingSlot (0, 25, 0) tags slots)
                  [ "v0.25.0" ]
                  "the released tag with no readable slot is reported — a directory holding entries and no heading is not a record of the version"

              Expect.isEmpty
                  (tagsMissingSlot (0, 27, 0) tags slots)
                  "a floor above every tag reports nothing, which is exactly why the live case asserts the covered set is non-empty first"

              Expect.equal
                  (tagsMissingSlot (0, 17, 0) tags slots)
                  [ "v0.17.0"; "v0.25.0" ]
                  "lowering the floor pulls older un-recorded slots in, oldest first — the FLOOR is what excludes them, not a gap in the reader"

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
