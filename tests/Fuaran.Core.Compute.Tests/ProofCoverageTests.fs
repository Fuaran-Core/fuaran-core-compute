/// `Proofs.Coverage` — what this repository proves, and what it declares it does not, held to the
/// tree in two clauses.
///
///   1. THE PACKAGE CLAUSE (Phase 259): coverage is TOTAL OR DECLARED — every packable package is
///      named by a model's `packages` in `proofs/modules.json` or carries an entry in
///      `proofs/coverage-exclusions.json`, never both, and no entry names a package that is not
///      packable or a reason the file does not declare.
///
///   2. THE OPERATION CLAUSE (Phase 432, ported from the Fuaran.Core repository's Phase 335).
///      Clause 1 stops at package granularity: a package with a model is covered, so a verb, the
///      incremental engine or the planner inside it could ship with no theorem and no recorded
///      reason, and nothing said so. The census is the committed API baselines of every modelled
///      package — each `method` line, plus the relational verbs (the `union-case` lines of
///      `Transform`, spelled `Transform.<Verb>`, because the compiler publishes this layer's verbs
///      as union cases) — and every censused operation maps to one of
///
///        (a) a `proofs.json` row (`proved` or `tested`) whose `evidence.operations` names it;
///        (b) an entry in `coverage-exclusions.json`'s `operations` block, with a class from that
///            file's closed `operationClasses` vocabulary and a one-line reason.
///
///      Core's third route, a law family's operation roster, does not exist here: this repository
///      ships no roster, so the route is omitted rather than carried empty. The vocabulary is
///      Core's, verbatim, so the two repositories have one vocabulary (`DECISIONS.md`).
///
///      An unmapped operation reds the suite and names itself; because the census IS the baselines,
///      a newly published operation reds at the commit that publishes it. The check runs both ways:
///      an entry for an operation that is no longer published, or that has since gained a ladder
///      row, fails too. And each class is held to what it says, as far as a file can be: a
///      `forward` names a mapped target, an `obsolete` entry's member carries `System.Obsolete`, and
///      a `measured-elsewhere` entry names a test file that mentions the member or the token it is
///      reached `through`.
///
/// Each rule is a pure function with a go-red over synthetic input beside the live reading, because
/// every live case reads files that are expected to be correct, and a checker that matched nothing
/// would pass them all.
module Fuaran.Compute.Tests.ProofCoverageTests

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

let private root () = Snapshots.repoFile ""

let private readJson (relPath: string) =
    JsonDocument.Parse(File.ReadAllText(Path.Combine(root (), relPath)))

// ---- clause 1: packages ------------------------------------------------------------------------

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

/// The packages a model in `proofs/modules.json` names.
let private liveModelled () : Set<string> =
    use modules = readJson "proofs/modules.json"

    [ for m in modules.RootElement.GetProperty("modules").EnumerateArray() do
          match m.TryGetProperty "packages" with
          | true, ps -> yield! [ for p in ps.EnumerateArray() -> p.GetString() ]
          | _ -> () ]
    |> Set.ofList

// ---- clause 2: operations ----------------------------------------------------------------------

let private finding (subject: string) (clause: string) (detail: string) =
    sprintf "'%s' [%s]: %s" subject clause detail

/// One public operation of the census: the package whose baseline publishes it, its spelling in the
/// coverage records, and the baseline's CLR name.
type Operation =
    { Package: string
      Name: string
      Clr: string }

/// One entry of `coverage-exclusions.json`'s `operations` block.
type OperationExclusion =
    { Members: string list
      Class: string
      Reason: string
      ForwardsTo: string option
      StandIn: string option
      Through: string option }

type OperationInputs =
    {
        Census: Operation list
        /// `proofs.json` rows that carry `evidence.operations`: (row id, level, operations).
        Ladder: (string * string * string list) list
        Exclusions: OperationExclusion list
        /// The class vocabulary, read from the exclusions file's own `operationClasses` block.
        Classes: Set<string>
        /// The text of each stand-in a `measured-elsewhere` entry names, by its repo-relative path. A
        /// path absent from the map does not exist (or is not under `tests/`).
        StandIns: Map<string, string>
        /// The census operations that carry `System.Obsolete`, on itself or its declaring type.
        Obsolete: Set<string>
    }

/// How the census divides, each operation counted once, by the first route that maps it.
type OperationTally =
    {
        Operations: int
        Ladder: int
        ByClass: (string * int) list
        /// (package, owner, operations, on the ladder, by class).
        ByOwner: (string * string * int * int * (string * int) list) list
    }

/// The class order the records and the README table use — Core's.
let operationClassOrder =
    [ "trivial"; "forward"; "obsolete"; "host-seam"; "measured-elsewhere" ]

/// A baseline's CLR member name as the coverage records spell it: `Fuaran.Compute.` dropped,
/// generic arity dropped, nested types joined by `.`, and the compiler's `Module` suffix dropped from
/// every type segment (`Fuaran.Compute.ColExprModule.substitute` is `ColExpr.substitute`).
let operationName (clr: string) : string =
    let rest =
        if clr.StartsWith "Fuaran.Compute." then
            clr.Substring "Fuaran.Compute.".Length
        else
            clr

    let segs = Regex.Replace(rest, "`[0-9]+", "").Replace('+', '.').Split('.')

    segs
    |> Array.mapi (fun i s ->
        if i < segs.Length - 1 && s.Length > 6 && s.EndsWith "Module" then
            s.Substring(0, s.Length - 6)
        else
            s)
    |> String.concat "."

let private verbLine =
    Regex(@"^union-case (Fuaran\.Compute\.Transform\.(?:New)?([A-Za-z]+)) #", RegexOptions.Compiled)

/// The operations one package's baseline publishes: its `method` lines, and the relational verbs —
/// the `union-case` lines of `Transform`, spelled `Transform.<Verb>`.
let censusOf (package: string) (baselineText: string) : Operation list =
    baselineText.Split('\n')
    |> Array.choose (fun raw ->
        let line = raw.TrimEnd '\r'
        let verb = verbLine.Match line

        if line.StartsWith "method " && line.IndexOf '(' > 7 then
            let clr = line.Substring(7, line.IndexOf '(' - 7)

            Some
                { Package = package
                  Name = operationName clr
                  Clr = clr }
        elif verb.Success then
            Some
                { Package = package
                  Name = "Transform." + verb.Groups[2].Value
                  Clr = verb.Groups[1].Value }
        else
            None)
    |> List.ofArray

/// `<Owner>.<member>` — the last two segments, which is how a test file names an operation.
let private twoSegments (op: string) =
    let s = op.Split '.'

    if s.Length >= 2 then
        s[s.Length - 2] + "." + s[s.Length - 1]
    else
        op

/// An operation's owner: every segment but the member.
let private ownerOf (op: string) =
    match op.LastIndexOf '.' with
    | -1 -> op
    | i -> op.Substring(0, i)

/// Whether `text` mentions `token`, bounded as a word on each side where the token has a word
/// character there (a `.Member` token may follow an identifier).
let private mentions (text: string) (token: string) =
    let isWord (c: char) = Char.IsLetterOrDigit c || c = '_'

    token.Length > 0
    && Regex.IsMatch(
        text,
        (if isWord token[0] then @"(?<![\w])" else "")
        + Regex.Escape token
        + (if isWord token[token.Length - 1] then @"(?![\w])" else "")
    )

/// Every finding the operation clause yields, with the tally the README table renders.
let checkOperations (inputs: OperationInputs) : string list * OperationTally =
    let census = inputs.Census |> List.map _.Name |> Set.ofList

    let ladderOps =
        inputs.Ladder
        |> List.filter (fun (_, level, _) -> level = "proved" || level = "tested")
        |> List.collect (fun (_, _, ops) -> ops)
        |> Set.ofList

    let excluded =
        inputs.Exclusions
        |> List.collect (fun e -> e.Members |> List.map (fun m -> m, e))
        |> List.groupBy fst
        |> List.map (fun (m, es) -> m, es |> List.map snd)
        |> Map.ofList

    let directlyMapped (op: string) =
        ladderOps.Contains op
        || (match Map.tryFind op excluded with
            | Some(e :: _) -> e.Class <> "forward" && e.Class <> "obsolete"
            | _ -> false)

    // A forward's coverage is its target's: follow `forwardsTo` until a directly mapped operation,
    // refusing a cycle and a chain that ends nowhere.
    let rec resolves (seen: Set<string>) (op: string) =
        if directlyMapped op then
            true
        elif seen.Contains op then
            false
        else
            match Map.tryFind op excluded with
            | Some(e :: _) when (e.Class = "forward" || e.Class = "obsolete") ->
                match e.ForwardsTo with
                | Some t -> resolves (seen.Add op) t
                | None -> false
            | _ -> false

    let ladderFindings =
        inputs.Ladder
        |> List.collect (fun (id, level, ops) ->
            [ if level <> "proved" && level <> "tested" then
                  yield
                      finding
                          id
                          "operation-ladder"
                          (sprintf
                              "a %s row names operations; only a proved or tested row is evidence an operation is covered"
                              level)
              for op in ops do
                  if not (census.Contains op) then
                      yield
                          finding
                              id
                              "operation-ladder"
                              (sprintf "`evidence.operations` names %s, which no censused baseline publishes" op) ])

    let exclusionFindings =
        [ for KeyValue(m, es) in excluded do
              if List.length es > 1 then
                  yield
                      finding
                          m
                          "operation-exclusion-stale"
                          (sprintf "listed by %d entries; one decision per operation" es.Length)

              if not (census.Contains m) then
                  yield
                      finding
                          m
                          "operation-exclusion-stale"
                          "an exclusion for an operation no censused baseline publishes"
              elif ladderOps.Contains m then
                  yield
                      finding
                          m
                          "operation-exclusion-stale"
                          "an exclusion for an operation that has since gained a ladder row — delete the entry"
          for e in inputs.Exclusions do
              let subject = String.concat ", " e.Members

              if List.isEmpty e.Members then
                  yield finding "(empty entry)" "operation-exclusion-class" "an entry naming no operation"

              if not (inputs.Classes.Contains e.Class) then
                  yield
                      finding
                          subject
                          "operation-exclusion-class"
                          (sprintf "class '%s' is not in the closed vocabulary %A" e.Class (Set.toList inputs.Classes))

              if String.IsNullOrWhiteSpace e.Reason then
                  yield finding subject "operation-exclusion-reason" "an entry with no reason"

              let forwarding = e.Class = "forward" || e.Class = "obsolete"

              match forwarding, e.ForwardsTo with
              | true, None ->
                  yield finding subject "operation-forward" (sprintf "a %s entry names no `forwardsTo`" e.Class)
              | false, Some _ ->
                  yield finding subject "operation-forward" "`forwardsTo` belongs to a forward or obsolete entry"
              | true, Some t when not (census.Contains t) ->
                  yield
                      finding
                          subject
                          "operation-forward"
                          (sprintf "forwards to %s, which no censused baseline publishes" t)
              | true, Some t ->
                  for m in e.Members do
                      if census.Contains m && not (resolves Set.empty m) then
                          yield
                              finding
                                  m
                                  "operation-forward"
                                  (sprintf "forwards to %s, which is not itself mapped (or the chain is a cycle)" t)
              | false, None -> ()

              if e.Class = "obsolete" then
                  for m in e.Members do
                      if census.Contains m && not (inputs.Obsolete.Contains m) then
                          yield
                              finding
                                  m
                                  "operation-obsolete"
                                  "an obsolete entry for an operation that carries no System.Obsolete"

              match e.Class = "measured-elsewhere", e.StandIn with
              | true, None -> yield finding subject "operation-stand-in" "a measured-elsewhere entry names no `standIn`"
              | false, Some _ ->
                  yield finding subject "operation-stand-in" "`standIn` belongs to a measured-elsewhere entry"
              | true, Some path ->
                  match Map.tryFind path inputs.StandIns with
                  | None ->
                      yield
                          finding
                              subject
                              "operation-stand-in"
                              (sprintf "stand-in %s is not a test file under tests/ in this repository" path)
                  | Some text ->
                      for m in e.Members do
                          let token = e.Through |> Option.defaultValue (twoSegments m)

                          if not (mentions text token) then
                              yield
                                  finding m "operation-stand-in" (sprintf "stand-in %s never mentions `%s`" path token)
              | false, None -> ()

              if e.Class <> "measured-elsewhere" && e.Through.IsSome then
                  yield finding subject "operation-stand-in" "`through` belongs to a measured-elsewhere entry" ]

    let unmapped =
        inputs.Census
        |> List.filter (fun o -> not (resolves Set.empty o.Name) && not (Map.containsKey o.Name excluded))
        |> List.map (fun o ->
            finding
                o.Name
                "operation-mapped"
                (sprintf "published by %s and mapped to no ladder row or recorded exclusion" o.Package))

    let routeOf (op: string) =
        if ladderOps.Contains op then
            "ladder"
        else
            match Map.tryFind op excluded with
            | Some(e :: _) -> e.Class
            | _ -> "unmapped"

    let countIn (ops: Operation list) (route: string) =
        ops |> List.filter (fun o -> routeOf o.Name = route) |> List.length

    let tally =
        { Operations = List.length inputs.Census
          Ladder = countIn inputs.Census "ladder"
          ByClass = operationClassOrder |> List.map (fun c -> c, countIn inputs.Census c)
          ByOwner =
            inputs.Census
            |> List.groupBy (fun o -> o.Package, ownerOf o.Name)
            |> List.sortBy fst
            |> List.map (fun ((p, owner), ops) ->
                p,
                owner,
                List.length ops,
                countIn ops "ladder",
                operationClassOrder |> List.map (fun c -> c, countIn ops c)) }

    ladderFindings @ exclusionFindings @ unmapped, tally

/// The operation clause's line, emitted whatever the verdict.
let renderOperationPredicate (mapped: bool) (t: OperationTally) : string =
    sprintf
        "proofs: operations %s (%d censused operations: %d on the ladder, %d excluded — %s)"
        (if mapped then "mapped" else "NOT mapped")
        t.Operations
        t.Ladder
        (t.ByClass |> List.sumBy snd)
        (t.ByClass |> List.map (fun (c, n) -> sprintf "%d %s" n c) |> String.concat ", ")

let operationTableBegin =
    "<!-- operation-coverage:begin — generated from ../api/*.txt, ../proofs.json and coverage-exclusions.json by the Proofs.Coverage family; CORE_APPROVE_LADDER=1 rewrites it -->"

let operationTableEnd = "<!-- operation-coverage:end -->"

/// The README's table: each owner's operations by the route that maps them.
let renderOperationTable (t: OperationTally) : string =
    let header =
        [ "| Package | Owner | Operations | Ladder | "
          + (operationClassOrder |> String.concat " | ")
          + " |"
          "|---|---|---:|---:|"
          + (operationClassOrder |> List.map (fun _ -> "---:|") |> String.concat "") ]

    let row (pkg: string) (owner: string) (ops: int) (lad: int) (classes: (string * int) list) =
        sprintf
            "| %s | %s | %d | %d | %s |"
            pkg
            owner
            ops
            lad
            (classes |> List.map (snd >> string) |> String.concat " | ")

    let rows =
        t.ByOwner
        |> List.map (fun (p, owner, ops, lad, cls) -> row ("`" + p + "`") ("`" + owner + "`") ops lad cls)

    header @ rows @ [ row "**Total**" "" t.Operations t.Ladder t.ByClass ]
    |> String.concat "\n"

/// The README with its operation table replaced by `table`, or why the markers were not found.
let regenerateOperationTable (table: string) (readmeText: string) : Result<string, string> =
    let text = readmeText.Replace("\r\n", "\n")

    match text.IndexOf operationTableBegin, text.IndexOf operationTableEnd with
    | -1, _
    | _, -1 -> Error "the operation-coverage markers are missing from proofs/README.md"
    | b, e when e < b -> Error "the operation-coverage end marker precedes its begin marker"
    | b, e ->
        Ok(
            text.Substring(0, b + operationTableBegin.Length)
            + "\n"
            + table
            + "\n"
            + text.Substring e
        )

// ---- the live inputs ----

let private strMember (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
    | _ -> None

/// The census: the baselines of the packages a model names.
let private liveCensus () : Operation list =
    liveModelled ()
    |> Set.toList
    |> List.collect (fun p -> censusOf p (File.ReadAllText(Path.Combine(root (), "api", p + ".txt"))))

let private liveLadderOperations () =
    use doc = readJson "proofs.json"

    doc.RootElement.GetProperty("claims").EnumerateArray()
    |> Seq.choose (fun row ->
        match row.TryGetProperty "evidence" with
        | true, ev when ev.ValueKind = JsonValueKind.Object ->
            match ev.TryGetProperty "operations" with
            | true, ops when ops.ValueKind = JsonValueKind.Array ->
                Some(
                    strMember row "id" |> Option.defaultValue "",
                    strMember row "level" |> Option.defaultValue "",
                    ops.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
                )
            | _ -> None
        | _ -> None)
    |> List.ofSeq

let private operationExclusionsOf (root: JsonElement) : OperationExclusion list =
    match root.TryGetProperty "operations" with
    | true, ops when ops.ValueKind = JsonValueKind.Array ->
        ops.EnumerateArray()
        |> Seq.map (fun el ->
            { Members =
                match el.TryGetProperty "members" with
                | true, v when v.ValueKind = JsonValueKind.Array ->
                    v.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
                | _ -> []
              Class = strMember el "class" |> Option.defaultValue ""
              Reason = strMember el "reason" |> Option.defaultValue ""
              ForwardsTo = strMember el "forwardsTo"
              StandIn = strMember el "standIn"
              Through = strMember el "through" })
        |> List.ofSeq
    | _ -> []

let private operationClassesOf (root: JsonElement) : Set<string> =
    match root.TryGetProperty "operationClasses" with
    | true, v when v.ValueKind = JsonValueKind.Object -> v.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
    | _ -> Set.empty

/// Whether the baseline member carries `System.Obsolete`, on itself or on a declaring type — read
/// from the loaded assembly, so an `obsolete` entry cannot claim an attribute the code lacks.
let private carriesObsolete (o: Operation) : bool =
    try
        let asm = Reflection.Assembly.Load(Reflection.AssemblyName o.Package)
        let cut = o.Clr.LastIndexOf '.'
        let ty = asm.GetType(o.Clr.Substring(0, cut))
        let name = Regex.Replace(o.Clr.Substring(cut + 1), "`[0-9]+$", "")

        let rec typeObsolete (t: Type) =
            not (isNull t)
            && (t.IsDefined(typeof<ObsoleteAttribute>, false) || typeObsolete t.DeclaringType)

        not (isNull ty)
        && (typeObsolete ty
            || ty.GetMethods(
                Reflection.BindingFlags.Public
                ||| Reflection.BindingFlags.Static
                ||| Reflection.BindingFlags.Instance
               )
               |> Array.exists (fun m -> m.Name = name && m.IsDefined(typeof<ObsoleteAttribute>, false)))
    with _ ->
        false

let private liveOperationInputs () : OperationInputs =
    use exclusionsDoc = readJson "proofs/coverage-exclusions.json"
    let exclusions = operationExclusionsOf exclusionsDoc.RootElement
    let census = liveCensus ()

    let claimedObsolete =
        exclusions
        |> List.filter (fun e -> e.Class = "obsolete")
        |> List.collect _.Members
        |> Set.ofList

    let standIns =
        exclusions
        |> List.choose _.StandIn
        |> List.distinct
        |> List.choose (fun p ->
            let full = Path.Combine(root (), p)

            if p.StartsWith "tests/" && not (p.Contains "..") && File.Exists full then
                Some(p, File.ReadAllText full)
            else
                None)
        |> Map.ofList

    { Census = census
      Ladder = liveLadderOperations ()
      Exclusions = exclusions
      Classes = operationClassesOf exclusionsDoc.RootElement
      StandIns = standIns
      Obsolete =
        census
        |> List.filter (fun o -> claimedObsolete.Contains o.Name && carriesObsolete o)
        |> List.map _.Name
        |> Set.ofList }

// ---- the synthetic baseline the go-reds perturb ----

let private opFixture: OperationInputs =
    let op pkg name =
        { Package = pkg
          Name = name
          Clr = "Fuaran.Compute." + name }

    { Census =
        [ op "Pkg.A" "Thing.proved"
          op "Pkg.A" "Thing.Verb"
          op "Pkg.B" "Thing.tiny"
          op "Pkg.B" "Thing.alias"
          op "Pkg.B" "Thing.old"
          op "Pkg.B" "Thing.tested"
          op "Pkg.B" "Thing.reached"
          op "Pkg.B" "Thing.Hosted" ]
      Ladder = [ "t1", "proved", [ "Thing.proved" ]; "t2", "tested", [ "Thing.Verb" ] ]
      Exclusions =
        [ { Members = [ "Thing.tiny" ]
            Class = "trivial"
            Reason = "builds a record"
            ForwardsTo = None
            StandIn = None
            Through = None }
          { Members = [ "Thing.alias" ]
            Class = "forward"
            Reason = "the older name"
            ForwardsTo = Some "Thing.proved"
            StandIn = None
            Through = None }
          { Members = [ "Thing.old" ]
            Class = "obsolete"
            Reason = "renamed"
            ForwardsTo = Some "Thing.Verb"
            StandIn = None
            Through = None }
          { Members = [ "Thing.tested" ]
            Class = "measured-elsewhere"
            Reason = "example-tested"
            ForwardsTo = None
            StandIn = Some "tests/ThingTests.fs"
            Through = None }
          { Members = [ "Thing.reached" ]
            Class = "measured-elsewhere"
            Reason = "reached through tested"
            ForwardsTo = None
            StandIn = Some "tests/ThingTests.fs"
            Through = Some "Thing.via" }
          { Members = [ "Thing.Hosted" ]
            Class = "host-seam"
            Reason = "a host implements it"
            ForwardsTo = None
            StandIn = None
            Through = None } ]
      Classes = Set.ofList operationClassOrder
      StandIns = Map.ofList [ "tests/ThingTests.fs", "let t = Thing.tested 1 |> Thing.via" ]
      Obsolete = Set.ofList [ "Thing.old" ] }

/// An operation go-red: this perturbation yields EXACTLY one finding, naming the clause and subject.
let private expectOneOperationFinding (name: string) (inputs: OperationInputs) (clause: string) (subject: string) =
    let findings, _ = checkOperations inputs

    Expect.equal
        (List.length findings)
        1
        (sprintf
            "%s carries exactly one defect, so exactly one finding is expected — got:\n%s"
            name
            (String.concat "\n" findings))

    let only = List.head findings
    Expect.stringContains only (sprintf "[%s]" clause) (sprintf "%s: the finding names the clause" name)
    Expect.stringContains only subject (sprintf "%s: the finding names what it is about" name)

/// A go-red whose subject a forward in the baseline leans on: the operation names itself under
/// `operation-mapped`, and every other finding is the forward that pointed at it, naming it.
let private expectNamedUnmapped (name: string) (inputs: OperationInputs) (subject: string) =
    let findings, _ = checkOperations inputs

    Expect.exists
        findings
        (fun f -> f.StartsWith(sprintf "'%s' [operation-mapped]" subject))
        (sprintf "%s: the unmapped operation names itself — got:\n%s" name (String.concat "\n" findings))

    for f in findings do
        Expect.stringContains f subject (sprintf "%s: every finding is about %s" name subject)

let private withExclusion
    (members: string list)
    (f: OperationExclusion -> OperationExclusion)
    (inputs: OperationInputs)
    =
    { inputs with
        Exclusions = inputs.Exclusions |> List.map (fun e -> if e.Members = members then f e else e) }

// -----------------------------------------------------------------------------------------------

[<Tests>]
let coverageTests =
    testList
        "Proofs.Coverage"
        [

          // ---- clause 1: packages ----

          testCase "every packable package is modelled or excluded, never both, and every exclusion is live"
          <| fun _ ->
              let packable =
                  PackageRosterTests.packableProjects (root ())
                  |> List.map _.PackageId
                  |> Set.ofList

              Expect.isNonEmpty (Set.toList packable) "the roster found packable projects"

              use excl = readJson "proofs/coverage-exclusions.json"

              let excluded =
                  [ for e in excl.RootElement.GetProperty("exclusions").EnumerateArray() ->
                        e.GetProperty("package").GetString(), e.GetProperty("reason").GetString() ]

              let reasons =
                  [ for p in excl.RootElement.GetProperty("reasons").EnumerateObject() -> p.Name ]
                  |> Set.ofList

              Expect.isEmpty
                  (coverageFindings packable (liveModelled ()) excluded reasons)
                  "coverage is total or declared"

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
                  "an uncovered package is named"

          // ---- clause 2: operations ----

          testCase "the synthetic operation baseline is mapped, by every route"
          <| fun _ ->
              let findings, tally = checkOperations opFixture

              Expect.isEmpty findings (sprintf "the baseline must be clean — got:\n%s" (String.concat "\n" findings))
              Expect.equal tally.Ladder 2 "two on the ladder"

              Expect.equal
                  tally.ByClass
                  [ "trivial", 1
                    "forward", 1
                    "obsolete", 1
                    "host-seam", 1
                    "measured-elsewhere", 2 ]
                  "and each exclusion counted under its class"

          testCase "every censused operation is mapped on this tree, and the clause says so in one line"
          <| fun _ ->
              let findings, tally = checkOperations (liveOperationInputs ())

              printfn "%s" (renderOperationPredicate (List.isEmpty findings) tally)

              Expect.isEmpty
                  findings
                  (sprintf
                      "every censused operation maps to a ladder row or a recorded exclusion — each finding names the clause and the subject:\n%s"
                      (String.concat "\n" findings))

          testCase "the operation inputs are real, so the clause is not quantifying over nothing"
          <| fun _ ->
              let inputs = liveOperationInputs ()
              let names = inputs.Census |> List.map _.Name

              Expect.isGreaterThan (List.length inputs.Census) 150 "the census reads the modelled baselines"

              Expect.equal
                  (inputs.Census |> List.map _.Package |> Set.ofList)
                  (liveModelled ())
                  "every modelled package is censused, and nothing else"

              Expect.isGreaterThan (List.length inputs.Ladder) 5 "ladder rows name the operations they cover"
              Expect.isNonEmpty inputs.Exclusions "the operation exclusions are read"

              Expect.equal
                  inputs.Classes
                  (Set.ofList operationClassOrder)
                  "the closed class vocabulary is read from the file, and it is Core's"

              Expect.equal
                  (List.distinct names |> List.length)
                  (List.length names)
                  "no two censused operations share a spelling, so a name maps one operation"

              Expect.equal
                  (names
                   |> List.filter (fun n -> n.StartsWith "Transform." && Char.IsUpper n[10])
                   |> List.length)
                  14
                  "the census names the fourteen relational verbs"

              Expect.isEmpty
                  (Set.difference inputs.Obsolete (Set.ofList names))
                  "every operation the obsolete reading names is a censused one"

          testCase "every operation class in the closed vocabulary is carried by at least one entry"
          <| fun _ ->
              let inputs = liveOperationInputs ()
              let used = inputs.Exclusions |> List.map _.Class |> Set.ofList

              // `obsolete` is carried exactly when a censused operation carries `System.Obsolete`:
              // none does today, and the class stays in the vocabulary because it is Core's.
              let exempt =
                  if inputs.Census |> List.exists carriesObsolete then
                      Set.empty
                  else
                      Set.singleton "obsolete"

              Expect.equal
                  (Set.difference (Set.difference inputs.Classes used) exempt)
                  Set.empty
                  "a class no entry carries is a vocabulary term nothing tests — carry it or drop it"

          testCase
              "proofs/README.md's operation table is the census's projection (CORE_APPROVE_LADDER=1 regenerates it)"
          <| fun _ ->
              let _, tally = checkOperations (liveOperationInputs ())
              let path = Path.Combine(root (), "proofs", "README.md")
              let committed = File.ReadAllText path

              match regenerateOperationTable (renderOperationTable tally) committed with
              | Error why -> failtest why
              | Ok expected when expected = committed.Replace("\r\n", "\n") -> ()
              | Ok expected when Environment.GetEnvironmentVariable "CORE_APPROVE_LADDER" = "1" ->
                  File.WriteAllText(path, expected)
              | Ok _ ->
                  failtest
                      "proofs/README.md's operation-coverage table is not the census's projection — re-run with CORE_APPROVE_LADDER=1 and commit the README"

          testCase "operation names drop the prefix, the arity and the Module suffix; the verbs are union cases"
          <| fun _ ->
              Expect.equal
                  (operationName "Fuaran.Compute.ColExprModule.substitute")
                  "ColExpr.substitute"
                  "module suffix"

              Expect.equal (operationName "Fuaran.Compute.Delta.diff`1") "Delta.diff" "generic arity"

              Expect.equal
                  (censusOf
                      "P"
                      "# header\nmethod Fuaran.Compute.Ops.apply`2(A, B) : C\nproperty Fuaran.Compute.X.y : Z { get }\nunion-case Fuaran.Compute.Transform.NewJoin #4(Item: A)\nunion-case Fuaran.Compute.Transform.Distinct #9()\nunion-case Fuaran.Compute.Other.NewJoin #1(Item: A)\n")
                  [ { Package = "P"
                      Name = "Ops.apply"
                      Clr = "Fuaran.Compute.Ops.apply`2" }
                    { Package = "P"
                      Name = "Transform.Join"
                      Clr = "Fuaran.Compute.Transform.NewJoin" }
                    { Package = "P"
                      Name = "Transform.Distinct"
                      Clr = "Fuaran.Compute.Transform.Distinct" } ]
                  "method lines and Transform's cases are operations; nothing else is"

          testCase "go-red: operation-mapped — a newly published operation that maps to nothing names itself"
          <| fun _ ->
              expectOneOperationFinding
                  "a new census line"
                  { opFixture with
                      Census =
                          opFixture.Census
                          @ [ { Package = "Pkg.B"
                                Name = "Thing.brandNew"
                                Clr = "Fuaran.Compute.Thing.brandNew" } ] }
                  "operation-mapped"
                  "Thing.brandNew"

          testCase "go-red: operation-mapped — a ladder row's operations removed, the operation names itself"
          <| fun _ ->
              expectNamedUnmapped
                  "a ladder mention removed"
                  { opFixture with
                      Ladder = [ "t1", "proved", [ "Thing.proved" ]; "t2", "tested", [] ] }
                  "Thing.Verb"

          testCase "go-red: operation-ladder — an assumed row naming operations"
          <| fun _ ->
              expectOneOperationFinding
                  "an assumed row"
                  { opFixture with
                      Ladder = opFixture.Ladder @ [ "t3", "assumed", [] ] }
                  "operation-ladder"
                  "t3"

          testCase "go-red: operation-ladder — a row naming an operation no baseline publishes"
          <| fun _ ->
              expectOneOperationFinding
                  "a stale ladder name"
                  { opFixture with
                      Ladder =
                          [ "t1", "proved", [ "Thing.proved"; "Thing.gone" ]
                            "t2", "tested", [ "Thing.Verb" ] ] }
                  "operation-ladder"
                  "Thing.gone"

          testCase "go-red: operation-exclusion-stale — an entry for an operation a model now covers"
          <| fun _ ->
              expectOneOperationFinding
                  "an excluded operation now on the ladder"
                  { opFixture with
                      Ladder =
                          [ "t1", "proved", [ "Thing.proved"; "Thing.tiny" ]
                            "t2", "tested", [ "Thing.Verb" ] ] }
                  "operation-exclusion-stale"
                  "Thing.tiny"

          testCase "go-red: operation-exclusion-stale — an entry for an operation no longer published"
          <| fun _ ->
              expectOneOperationFinding
                  "a retired operation still excluded"
                  { opFixture with
                      Census = opFixture.Census |> List.filter (fun o -> o.Name <> "Thing.tiny") }
                  "operation-exclusion-stale"
                  "Thing.tiny"

          testCase "go-red: operation-exclusion-stale — one operation listed by two entries"
          <| fun _ ->
              let findings, _ =
                  checkOperations (
                      withExclusion
                          [ "Thing.tiny" ]
                          (fun e ->
                              { e with
                                  Members = [ "Thing.tiny"; "Thing.tested" ] })
                          opFixture
                  )

              Expect.exists
                  findings
                  (fun f -> f.Contains "[operation-exclusion-stale]" && f.Contains "Thing.tested")
                  "a member listed twice is named"

          testCase "go-red: operation-exclusion-class — a class outside the closed vocabulary"
          <| fun _ ->
              expectOneOperationFinding
                  "an invented class"
                  (withExclusion [ "Thing.tiny" ] (fun e -> { e with Class = "harmless" }) opFixture)
                  "operation-exclusion-class"
                  "harmless"

          testCase "go-red: operation-exclusion-reason — an entry with no reason"
          <| fun _ ->
              expectOneOperationFinding
                  "a blank reason"
                  (withExclusion [ "Thing.tiny" ] (fun e -> { e with Reason = " " }) opFixture)
                  "operation-exclusion-reason"
                  "Thing.tiny"

          testCase "go-red: operation-forward — a forward whose target maps to nothing, and one naming no target"
          <| fun _ ->
              let toBare =
                  { (withExclusion
                        [ "Thing.alias" ]
                        (fun e ->
                            { e with
                                ForwardsTo = Some "Thing.bare" })
                        opFixture) with
                      Census =
                          opFixture.Census
                          @ [ { Package = "Pkg.B"
                                Name = "Thing.bare"
                                Clr = "Fuaran.Compute.Thing.bare" } ] }

              let findings, _ = checkOperations toBare

              Expect.exists
                  findings
                  (fun f -> f.Contains "[operation-forward]" && f.Contains "Thing.alias")
                  "a forward's coverage is its target's, so a forward to an unmapped operation is refused"

              let untargeted, _ =
                  checkOperations (withExclusion [ "Thing.alias" ] (fun e -> { e with ForwardsTo = None }) opFixture)

              Expect.exists
                  untargeted
                  (fun f -> f.Contains "[operation-forward]" && f.Contains "Thing.alias")
                  "a forward naming no target is refused"

          testCase "go-red: operation-forward — two forwards that name each other are a cycle, not coverage"
          <| fun _ ->
              let cyc =
                  opFixture
                  |> withExclusion [ "Thing.alias" ] (fun e -> { e with ForwardsTo = Some "Thing.old" })
                  |> withExclusion [ "Thing.old" ] (fun e ->
                      { e with
                          ForwardsTo = Some "Thing.alias" })

              let findings, _ = checkOperations cyc

              Expect.exists
                  findings
                  (fun f -> f.Contains "[operation-forward]" && f.Contains "Thing.alias")
                  "the cycle is named"

          testCase "go-red: operation-obsolete — an obsolete entry for an operation the attribute is not on"
          <| fun _ ->
              expectOneOperationFinding
                  "an unmarked obsolete claim"
                  { opFixture with Obsolete = Set.empty }
                  "operation-obsolete"
                  "Thing.old"

          testCase "go-red: operation-stand-in — a stand-in that does not exist, and one that never mentions the member"
          <| fun _ ->
              expectOneOperationFinding
                  "a missing stand-in"
                  (withExclusion
                      [ "Thing.tested" ]
                      (fun e ->
                          { e with
                              StandIn = Some "tests/Nowhere.fs" })
                      opFixture)
                  "operation-stand-in"
                  "tests/Nowhere.fs"

              expectOneOperationFinding
                  "a stand-in that never touches the member"
                  { opFixture with
                      StandIns = Map.ofList [ "tests/ThingTests.fs", "let t = Thing.testedLater 1 |> Thing.via" ] }
                  "operation-stand-in"
                  "Thing.tested"

              expectOneOperationFinding
                  "a measured-elsewhere entry with no stand-in"
                  (withExclusion [ "Thing.reached" ] (fun e -> { e with StandIn = None }) opFixture)
                  "operation-stand-in"
                  "Thing.reached" ]
