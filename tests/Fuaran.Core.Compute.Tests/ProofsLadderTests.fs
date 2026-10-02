/// The claims ladder (`proofs.json`) and the coverage declaration (`proofs/modules.json` +
/// `proofs/coverage-exclusions.json`) held to the tree (Phase 259).
///
/// The proof-leg kit ships the leg that RUNS these families and deliberately not the families
/// themselves, because what a row must be held to is a property of the adopting repository's tree
/// (`proofs/kit/README.md`, "Adopting it", step 5). These are this repository's, written against the
/// schema in `proofs/kit/LADDER.md`: a ladder is data a tool reads, so a row that names a theorem the
/// model does not declare, a model the leg does not check, or a test case that does not exist is a
/// false statement the gate refuses rather than prose a reader has to catch.
///
/// `Proofs.Ladder`:
///   * every row's level, class and `closes` are the closed forms, and every phase citation is
///     `<repository>#<number>`;
///   * a PROVED row names a model `proofs/check.ps1` checks and a theorem that is a top-level
///     declaration of that model;
///   * every checked model carries at least one proved row;
///   * a TESTED row on the `Proofs.Oracle` family names a model with a proved row, and every case it
///     lists is a test of that family, word for word.
///
/// `Proofs.Coverage`: coverage is TOTAL OR DECLARED — every packable package is named by a model's
/// `packages` in `modules.json` or carries an entry in `coverage-exclusions.json`, never both, and no
/// entry names a package that is not packable or a reason the file does not declare.
///
/// Each rule is a pure function with a go-red over synthetic input beside the live reading, because
/// every live case reads files that are expected to be correct, and a checker that matched nothing
/// would pass them all.
module Fuaran.Compute.Tests.ProofsLadderTests

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

/// One ladder row, as much of it as the rules read.
type internal Row =
    { Id: string
      Level: string
      Phase: string
      Class: string option
      Closes: string option
      Theorem: string option
      Model: string option
      Family: string option
      Cases: string list }

let private levels = set [ "proved"; "tested"; "assumed"; "policy" ]
let private classes = set [ "domain-obligation"; "model-bridge"; "premise" ]
let private phaseRe = Regex(@"^[a-z][a-z0-9-]*#\d+$", RegexOptions.Compiled)

let private modelRe =
    Regex(@"^proofs/(?<m>[A-Za-z0-9_]+)\.fst$", RegexOptions.Compiled)

let private str (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
    | _ -> None

/// The rows of a ladder document.
let internal parseRows (json: string) : Row list =
    use doc = JsonDocument.Parse json

    [ for c in doc.RootElement.GetProperty("claims").EnumerateArray() ->
          let ev =
              match c.TryGetProperty "evidence" with
              | true, e when e.ValueKind = JsonValueKind.Object -> Some e
              | _ -> None

          let evStr name = ev |> Option.bind (fun e -> str e name)

          { Id = str c "id" |> Option.defaultValue ""
            Level = str c "level" |> Option.defaultValue ""
            Phase = str c "phase" |> Option.defaultValue ""
            Class = str c "class"
            Closes = str c "closes"
            Theorem = evStr "theorem"
            Model = evStr "model"
            Family = evStr "family"
            Cases =
              match ev with
              | Some e ->
                  match e.TryGetProperty "cases" with
                  | true, cs when cs.ValueKind = JsonValueKind.Array ->
                      [ for x in cs.EnumerateArray() -> x.GetString() ]
                  | _ -> []
              | None -> [] } ]

/// The module a `proofs/<Module>.fst` citation names.
let internal modelModule (model: string) : string option =
    let m = modelRe.Match model
    if m.Success then Some m.Groups["m"].Value else None

/// The findings of the FORM rules — levels, classes, `closes`, phases — one line per finding.
let internal formFindings (rows: Row list) : string list =
    [ for r in rows do
          if not (levels.Contains r.Level) then
              yield sprintf "%s: level `%s` is not one of %A" r.Id r.Level (Set.toList levels)

          if not (phaseRe.IsMatch r.Phase) then
              yield sprintf "%s: phase `%s` is not `<repository>#<number>`" r.Id r.Phase

          match r.Level, r.Class with
          | "assumed", None -> yield sprintf "%s: an assumed row carries no class" r.Id
          | "assumed", Some c when not (classes.Contains c) ->
              yield sprintf "%s: class `%s` is not a known class" r.Id c
          | "assumed", Some "model-bridge" ->
              match r.Closes with
              | Some "permanent"
              | Some "unscheduled" -> ()
              | Some c when phaseRe.IsMatch c -> ()
              | other -> yield sprintf "%s: a model-bridge row's `closes` is %A" r.Id other
          | lvl, Some _ when lvl <> "assumed" -> yield sprintf "%s: only an assumed row carries a class" r.Id
          | _ -> () ]

/// Whether a model's source declares `theorem` at top level.
let internal declares (fstSource: string) (theorem: string) : bool =
    Regex.IsMatch(
        fstSource.Replace("\r\n", "\n"),
        @"^(val|let|let rec|and)\s+" + Regex.Escape theorem + @"\b",
        RegexOptions.Multiline
    )

/// The findings of the TREE rules: proved rows against the checked models and their sources, every
/// checked model with a proved row, tested oracle rows against the oracle family's source.
let internal treeFindings
    (checkedModules: string list)
    (sourceOf: string -> string option)
    (oracleSource: string)
    (rows: Row list)
    : string list =
    let provedModels =
        rows
        |> List.filter (fun r -> r.Level = "proved")
        |> List.choose (fun r -> r.Model |> Option.bind modelModule)
        |> Set.ofList

    [ for r in rows do
          if r.Level = "proved" then
              match r.Model |> Option.bind modelModule, r.Theorem with
              | None, _ -> yield sprintf "%s: a proved row names no `proofs/<Module>.fst` model" r.Id
              | _, None -> yield sprintf "%s: a proved row names no theorem" r.Id
              | Some m, Some t ->
                  if not (List.contains m checkedModules) then
                      yield sprintf "%s: model %s is not one proofs/check.ps1 checks" r.Id m

                  match sourceOf m with
                  | None -> yield sprintf "%s: proofs/%s.fst does not exist" r.Id m
                  | Some src when not (declares src t) ->
                      yield sprintf "%s: `%s` is not a top-level declaration of proofs/%s.fst" r.Id t m
                  | Some _ -> ()

          if r.Level = "tested" && r.Family = Some "Proofs.Oracle" then
              match r.Model |> Option.bind modelModule with
              | None -> yield sprintf "%s: an oracle row names no model it runs beside" r.Id
              | Some m when not (provedModels.Contains m) ->
                  yield
                      sprintf
                          "%s: its model %s carries no proved row, so the differential is paired to no theorem"
                          r.Id
                          m
              | Some _ -> ()

              if List.isEmpty r.Cases then
                  yield sprintf "%s: an oracle row names no case" r.Id

              for c in r.Cases do
                  if not (oracleSource.Contains("\"" + c + "\"")) then
                      yield sprintf "%s: case \"%s\" is not a test of the Proofs.Oracle family" r.Id c

      for m in checkedModules do
          if not (provedModels.Contains m) then
              yield sprintf "checked model %s carries no proved row" m ]

/// The coverage findings: packable packages against the modelled and the excluded sets.
let internal coverageFindings
    (packable: Set<string>)
    (modelled: Set<string>)
    (excluded: (string * string) list)
    (reasons: Set<string>)
    : string list =
    let excludedSet = excluded |> List.map fst |> Set.ofList

    [ for p in packable do
          match modelled.Contains p, excludedSet.Contains p with
          | false, false -> yield sprintf "%s: packable, named by no model and carrying no exclusion" p
          | true, true -> yield sprintf "%s: named by a model AND excluded — the exclusion outlived its reason" p
          | _ -> ()
      for p, reason in excluded do
          if not (packable.Contains p) then
              yield sprintf "%s: excluded but not a packable package" p

          if not (reasons.Contains reason) then
              yield sprintf "%s: reason `%s` is not one the file declares" p reason ]

// ---- the live readings ------------------------------------------------------------------------

let private root () = Snapshots.repoFile ""

/// The `$modules` literal line of proofs/check.ps1, parsed.
let internal checkedModulesOf (checkPs1: string) : string list =
    let line =
        checkPs1.Replace("\r\n", "\n").Split('\n')
        |> Array.tryFind (fun l -> l.TrimStart().StartsWith("$modules = @(", StringComparison.Ordinal))

    match line with
    | None -> []
    | Some l -> [ for m in Regex.Matches(l, @"'([A-Za-z0-9_]+)'") -> m.Groups[1].Value ]

let private liveRows () =
    parseRows (File.ReadAllText(Path.Combine(root (), "proofs.json")))

let private liveChecked () =
    checkedModulesOf (File.ReadAllText(Path.Combine(root (), "proofs", "check.ps1")))

let private sourceOf (m: string) : string option =
    let p = Path.Combine(root (), "proofs", m + ".fst")
    if File.Exists p then Some(File.ReadAllText p) else None

let private oracleSource () =
    File.ReadAllText(Path.Combine(root (), "tests", "Fuaran.Core.Compute.Tests", "ProofOracleTests.fs"))

let private row id level =
    { Id = id
      Level = level
      Phase = "fuaran-core#1"
      Class = None
      Closes = None
      Theorem = None
      Model = None
      Family = None
      Cases = [] }

[<Tests>]
let ladderTests =
    testList
        "Proofs.Ladder"
        [

          testCase "every row is in the closed forms"
          <| fun _ ->
              let rows = liveRows ()
              Expect.isNonEmpty rows "proofs.json parsed to at least one row"
              Expect.isEmpty (formFindings rows) "the ladder's levels, classes, closes and phases"

          testCase "every proved row names a checked model and a theorem it declares; every checked model has one"
          <| fun _ ->
              let checkedModules = liveChecked ()
              Expect.isNonEmpty checkedModules "proofs/check.ps1's $modules line parsed to at least one model"
              let rows = liveRows ()

              Expect.isNonEmpty
                  (rows |> List.filter (fun r -> r.Level = "proved"))
                  "the ladder carries proved rows at all"

              Expect.isEmpty (treeFindings checkedModules sourceOf (oracleSource ()) rows) "the ladder against the tree"

          testCase "the form rules go red on a bad level, a missing class, a bad closes and a bad phase"
          <| fun _ ->
              let bad =
                  [ row "a" "believed"
                    { row "b" "assumed" with Class = None }
                    { row "c" "assumed" with
                        Class = Some "model-bridge"
                        Closes = Some "soon" }
                    { row "d" "proved" with Phase = "176" } ]

              Expect.equal (formFindings bad |> List.length) 4 "one finding per planted defect"

          testCase
              "the tree rules go red on a missing theorem, an unchecked model, a phantom case and an uncovered model"
          <| fun _ ->
              let src = "module M\n\nval real_theorem : unit -> Lemma True\nlet other = 1\n"

              let rows =
                  [ { row "p1" "proved" with
                        Model = Some "proofs/M.fst"
                        Theorem = Some "real_theorem" }
                    { row "p2" "proved" with
                        Model = Some "proofs/M.fst"
                        Theorem = Some "imagined_theorem" }
                    { row "p3" "proved" with
                        Model = Some "proofs/Unchecked.fst"
                        Theorem = Some "real_theorem" }
                    { row "t1" "tested" with
                        Model = Some "proofs/M.fst"
                        Family = Some "Proofs.Oracle"
                        Cases = [ "a case that exists"; "a case nobody wrote" ] } ]

              let findings =
                  treeFindings
                      [ "M"; "Uncovered" ]
                      (fun m -> if m = "M" || m = "Unchecked" then Some src else None)
                      "testCase \"a case that exists\""
                      rows

              let has (needle: string) =
                  findings |> List.exists (fun f -> f.Contains needle)

              Expect.isTrue (has "imagined_theorem") "an undeclared theorem is named"
              Expect.isTrue (has "Unchecked is not one proofs/check.ps1 checks") "an unchecked model is named"
              Expect.isTrue (has "a case nobody wrote") "a phantom case is named"
              Expect.isTrue (has "checked model Uncovered carries no proved row") "an uncovered model is named"
              Expect.isFalse (has "real_theorem`") "a declared theorem is not"
              Expect.isFalse (has "a case that exists") "an existing case is not"

          testCase "the $modules reader takes the literal line and nothing else"
          <| fun _ ->
              Expect.equal
                  (checkedModulesOf "# $modules = @('Commented')\n$modules = @('Limits', 'ColumnOps')\n")
                  [ "Limits"; "ColumnOps" ]
                  "the literal line, not a comment naming it"

              Expect.isEmpty (checkedModulesOf "no declaration here") "no line reads as no models" ]

[<Tests>]
let coverageTests =
    testList
        "Proofs.Coverage"
        [

          testCase "every packable package is modelled or excluded, never both, and every exclusion is live"
          <| fun _ ->
              let packable =
                  PackageRosterTests.packableProjects (root ())
                  |> List.map _.PackageId
                  |> Set.ofList

              Expect.isNonEmpty (Set.toList packable) "the roster found packable projects"

              use modules =
                  JsonDocument.Parse(File.ReadAllText(Path.Combine(root (), "proofs", "modules.json")))

              let modelled =
                  [ for m in modules.RootElement.GetProperty("modules").EnumerateArray() do
                        match m.TryGetProperty "packages" with
                        | true, ps -> yield! [ for p in ps.EnumerateArray() -> p.GetString() ]
                        | _ -> () ]
                  |> Set.ofList

              use excl =
                  JsonDocument.Parse(File.ReadAllText(Path.Combine(root (), "proofs", "coverage-exclusions.json")))

              let excluded =
                  [ for e in excl.RootElement.GetProperty("exclusions").EnumerateArray() ->
                        e.GetProperty("package").GetString(), e.GetProperty("reason").GetString() ]

              let reasons =
                  [ for p in excl.RootElement.GetProperty("reasons").EnumerateObject() -> p.Name ]
                  |> Set.ofList

              Expect.isEmpty (coverageFindings packable modelled excluded reasons) "coverage is total or declared"

          testCase "the coverage rule goes red in both directions"
          <| fun _ ->
              let packable = set [ "A"; "B"; "C" ]
              let reasons = set [ "facade" ]

              let findings =
                  coverageFindings packable (set [ "A"; "B" ]) [ "B", "facade"; "Gone", "facade"; "C", "whim" ] reasons

              Expect.equal
                  (List.length findings)
                  3
                  "B modelled and excluded, Gone not packable, C an undeclared reason — and nothing for A"

              Expect.equal
                  (coverageFindings packable (set [ "A" ]) [ "B", "facade" ] reasons)
                  [ "C: packable, named by no model and carrying no exclusion" ]
                  "an uncovered package is named" ]
