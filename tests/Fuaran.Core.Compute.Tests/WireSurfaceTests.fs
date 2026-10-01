module Fuaran.Core.Tests.WireSurfaceTests

// ---------------------------------------------------------------------------
// Phase 214 — a committed baseline for the WIRE surface, and the class of a canonical-encode
// change decided by a gate rather than argued in prose.
//
// `api/<package>.txt` (Phase 183) pins the MANAGED surface: what a .NET consumer links against.
// It names its own boundary — "not semantics" — and the canonical BYTES a package emits are on
// the far side of it. Phase 213 moved them (a `project` step's `cols` became `columns`; a sort
// key's `col` became `column`), which breaks every consumer that pins canonical bytes, and the
// managed gate correctly reported "0 moved". The class was argued in that phase's deviations and
// carried into STABILITY.md by hand. It was argued correctly; nothing here would have noticed if
// it had not been.
//
// So: `api/wire/<package>.txt`, one committed file per wire-bearing package, holding the
// canonical bytes of a DOCUMENT SET that reaches every member name and every discriminator the
// package emits. The gate re-encodes the set and diffs it; a changed byte fails until the
// baseline is regenerated, and the regeneration states the CLASS in the file's own header.
//
// ---- the document set is DERIVED, not authored ------------------------------------------
//
// A hand-written set would be exactly as complete as its author's attention on the day, and the
// next case added to a union would sit outside it with the gate green. So the set is built by
// reflection from each package's ROOT document types: for every union type reachable from a
// root, and every case of it, one document is built that routes through the enclosing unions to
// reach that case, with every record field populated and every option `Some`. A case added
// tomorrow is in tomorrow's set without anyone listing it — and so it MOVES the baseline, which
// is the point. Two roots whose values are not reflectively constructible (they carry functions,
// or `obj` cells) are specimens written by hand, and say so.
//
// ---- what the baseline pins, and what it deliberately does not --------------------------
//
//   * CANONICAL ENCODE ONLY. A consumer pins what a package EMITS; what it will ACCEPT is a
//     superset by design. A decode ALIAS (Phase 213 kept `cols` as one) is therefore not in the
//     baseline, and adding one moves nothing — a test below holds that.
//   * EXACT BYTES, and structure beside them. Each document line carries the sha256 of the
//     bytes the package emitted — which is what a byte-pinning consumer compares — and the
//     document re-rendered canonically, which is what the classifier reads. Two of the emitters
//     (the IDL artifact's indented form, and the hand-assembled stream envelopes) are not
//     `Canon.render`, so the hash is the only thing that sees their layout.
//   * NOT a domain's vocabulary. `Idl.Encode` renders a DOMAIN's values under the domain's own
//     vocabulary, and an op stream embeds the domain's own op encoding verbatim: those member
//     names are the domain's, and so is pinning them. What is pinned here is what THIS
//     repository names.
//
// ---- the classes -----------------------------------------------------------------------
//
// On the pattern the managed baseline uses, and as the phase states them: a new member or a new
// case is ADDITIVE; a renamed or removed member, a changed discriminator, or the same structure
// rendered to different bytes is BREAKING. A reordered member cannot occur in canonical output
// (`Canon.render` sorts keys), so it is not a class of its own — in the two non-canonical
// emitters it surfaces as a byte move, which is breaking.
//
// ---- regenerating ----------------------------------------------------------------------
//
//   CORE_APPROVE_WIRE=1 dotnet run --project tests/Fuaran.Core.Compute.Tests
//
// It rewrites every drifted wire baseline and writes, into each one's header, the class of its
// move since the newest `vX.Y.Z` tag. A separate test holds that stated class to a
// recomputation against the tag it names, so a baseline cannot move without a class, and the
// class it states cannot be wrong.
// ---------------------------------------------------------------------------

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open Microsoft.FSharp.Reflection
open Expecto
open Fuaran.Core

// ---- the exemplar builder -------------------------------------------------

let private isGeneric (def: Type) (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = def

let private isOption = isGeneric typedefof<option<_>>
let private isList = isGeneric typedefof<list<_>>
let private isMap = isGeneric typedefof<Map<_, _>>
let private isSet = isGeneric typedefof<Set<_>>

/// A union type the document set quantifies over — an F# union that is not one of the two
/// collection unions (`option`, `list`), whose cases are structure rather than vocabulary.
let private isDocUnion (t: Type) =
    FSharpType.IsUnion(t, true) && not (isOption t) && not (isList t)

/// The type arguments a value of `t` is built from: record fields, union-case fields, tuple
/// elements and collection element types.
let private componentTypes (t: Type) : Type list =
    if t.IsArray then
        [ t.GetElementType() ]
    elif isOption t || isList t || isMap t || isSet t then
        t.GetGenericArguments() |> Array.toList
    elif FSharpType.IsTuple t then
        FSharpType.GetTupleElements t |> Array.toList
    elif FSharpType.IsRecord(t, true) then
        FSharpType.GetRecordFields(t, true)
        |> Array.map (fun p -> p.PropertyType)
        |> Array.toList
    elif FSharpType.IsUnion(t, true) then
        FSharpType.GetUnionCases(t, true)
        |> Array.collect (fun c -> c.GetFields() |> Array.map (fun p -> p.PropertyType))
        |> Array.toList
    else
        []

/// Every document union reachable from `t` without passing through a type in `avoid`, `t`
/// included when it is one. Cycles are cut by the visited set, so a recursive union is reached
/// once.
let private reachableAvoiding (avoid: Type list) (t: Type) : Type list =
    let seen = Collections.Generic.HashSet<Type>(avoid)
    let found = ResizeArray<Type>()

    let rec walk (x: Type) =
        if seen.Add x then
            if isDocUnion x then
                found.Add x

            for c in componentTypes x do
                walk c

    walk t
    List.ofSeq found

let private reachableUnions (t: Type) : Type list = reachableAvoiding [] t

/// Does building a value of `t` reach `target` without re-entering a type in `avoid`? The route
/// must not re-enter a union already on the path: the inner occurrence is built as a leaf, so a
/// route through it never arrives.
let private reaches (avoid: Type list) (target: Type) (t: Type) =
    t = target || List.contains target (reachableAvoiding avoid t)

/// Does a union case's field list MENTION a type on the stack — directly, or as a collection's
/// element? Shallow on purpose: it picks the case that ends a recursion, and a recursion routed
/// through a record comes back to the union, where it is asked again.
let rec private mentions (stack: Type list) (t: Type) : bool =
    List.contains t stack
    || (t.IsArray && mentions stack (t.GetElementType()))
    || (t.IsGenericType && (t.GetGenericArguments() |> Array.exists (mentions stack)))

/// Build one value of `t`. `target`, when given, is the (union, case) the document exists to
/// exhibit: every union met on the way chooses the first case whose fields reach the target's
/// union, the target union itself chooses the target case, and a union met INSIDE itself chooses
/// the first case that ends the recursion. `log` records every (union, case) actually built — the
/// suite reads it to prove the target was reached rather than trusting the routing.
let rec private build
    (target: (Type * UnionCaseInfo) option)
    (log: ResizeArray<Type * string>)
    (stack: Type list)
    (t: Type)
    : obj =
    if List.length stack > 48 then
        failwithf "exemplar recursion did not terminate at %s (stack: %A)" t.FullName (stack |> List.map _.Name)

    let recur = build target log (t :: stack)

    if t = typeof<string> then
        box "s"
    elif t = typeof<int> then
        box 1
    elif t = typeof<int64> then
        box 1L
    elif t = typeof<uint32> then
        box 1u
    elif t = typeof<float> then
        box 1.5
    elif t = typeof<float32> then
        box 1.5f
    elif t = typeof<decimal> then
        box 1.5M
    elif t = typeof<bool> then
        box true
    elif t = typeof<char> then
        box 'c'
    elif t = typeof<DateTime> then
        box (DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc))
    elif t = typeof<DateTimeOffset> then
        box (DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero))
    elif t = typeof<unit> then
        null
    elif t = typeof<obj> then
        box "s"
    elif t.IsEnum then
        Enum.GetValues(t).GetValue(0)
    elif isOption t then
        let cases = FSharpType.GetUnionCases t
        FSharpValue.MakeUnion(cases.[1], [| recur (t.GetGenericArguments().[0]) |])
    elif isList t then
        let cases = FSharpType.GetUnionCases t
        let empty = FSharpValue.MakeUnion(cases.[0], [||])
        FSharpValue.MakeUnion(cases.[1], [| recur (t.GetGenericArguments().[0]); empty |])
    elif t.IsArray then
        let arr = Array.CreateInstance(t.GetElementType(), 1)
        arr.SetValue(recur (t.GetElementType()), 0)
        box arr
    elif isMap t then
        let args = t.GetGenericArguments()
        let tupleT = FSharpType.MakeTupleType args
        let arr = Array.CreateInstance(tupleT, 1)
        arr.SetValue(FSharpValue.MakeTuple([| recur args.[0]; recur args.[1] |], tupleT), 0)
        Activator.CreateInstance(t, [| box arr |])
    elif isSet t then
        let elemT = t.GetGenericArguments().[0]
        let arr = Array.CreateInstance(elemT, 1)
        arr.SetValue(recur elemT, 0)
        Activator.CreateInstance(t, [| box arr |])
    elif FSharpType.IsTuple t then
        FSharpValue.MakeTuple(FSharpType.GetTupleElements t |> Array.map recur, t)
    elif FSharpType.IsRecord(t, true) then
        let fields =
            FSharpType.GetRecordFields(t, true) |> Array.map (fun p -> recur p.PropertyType)

        FSharpValue.MakeRecord(t, fields, true)
    elif FSharpType.IsUnion(t, true) then
        let cases = FSharpType.GetUnionCases(t, true)

        let fieldTypes (c: UnionCaseInfo) =
            c.GetFields() |> Array.map _.PropertyType

        let case =
            match target with
            | Some(u, c) when u = t && not (List.contains t stack) -> c
            | _ when List.contains t stack ->
                // Inside itself: the first case that ends the recursion.
                cases
                |> Array.tryFind (fun c -> not (fieldTypes c |> Array.exists (mentions (t :: stack))))
                |> Option.defaultValue cases.[0]
            | Some(u, _) ->
                // On the way to the target: the first case whose fields reach it.
                cases
                |> Array.tryFind (fun c -> fieldTypes c |> Array.exists (reaches (t :: stack) u))
                |> Option.defaultValue cases.[0]
            | None -> cases.[0]

        log.Add(t, case.Name)
        FSharpValue.MakeUnion(case, fieldTypes case |> Array.map recur, true)
    else
        failwithf
            "no exemplar for %s — a wire root reaches a type the builder cannot construct; give that root a hand-written specimen"
            t.FullName

/// A readable name for a type: `Slot<String>` rather than ``Slot`1``.
let rec private friendly (t: Type) : string =
    if t.IsGenericType then
        let n = t.Name
        let tick = n.IndexOf '`'
        let bare = if tick < 0 then n else n.Substring(0, tick)

        bare
        + "<"
        + (t.GetGenericArguments() |> Array.map friendly |> String.concat ",")
        + ">"
    else
        t.Name

// ---- the roots ------------------------------------------------------------

/// Where a package's document set comes from.
type internal WireRoot =
    /// Built by reflection from a root type: one base document, and one document per case of
    /// every union the root reaches.
    | Derived of package: string * label: string * rootType: Type * encode: (obj -> string)
    /// Written by hand, for a root whose values reflection cannot construct.
    | Specimens of package: string * label: string * docs: (unit -> (string * string) list)

let internal packageOf (r: WireRoot) =
    match r with
    | Derived(p, _, _, _)
    | Specimens(p, _, _) -> p

let private derived<'T> (package: string) (label: string) (encode: 'T -> string) =
    Derived(package, label, typeof<'T>, (fun o -> encode (unbox<'T> o)))

/// Every root of the wire surface, per package. Adding a wire-bearing package means adding its
/// roots here; the roster test below refuses a packable package that is neither here nor in
/// `notWire`, so the choice cannot be skipped.
let internal roots: WireRoot list =
    [ derived<Transform list> "Fuaran.Core.DataFrame" "pipeline" DataFrameCodec.encodePipeline
      derived<TableDelta> "Fuaran.Core.DataFrame" "tableDelta" DeltaCodec.encode
      derived<ColumnOp> "Fuaran.Core.Column.Ops" "columnOp" ColumnOps.encode ]

/// Packable packages that emit no wire document of their own, each with the reason. A package
/// here that stops being packable, or that gains a root above, fails the roster test.
let internal notWire: (string * string) list =
    [ "Fuaran.Core.DataFrame.Conformance",
      "the law families over the dataframe layer; the transform law corpus is pinned by its own emission test (`--emit-laws`)" ]

/// Build every document of one root: `(name, emitted bytes)`, plus the construction logs for the
/// per-case documents so the suite can prove each reached its case.
let internal documentsOf (r: WireRoot) : (string * string) list * (string * Type * string * (Type * string) list) list =
    match r with
    | Specimens(_, _, docs) -> docs (), []
    | Derived(_, label, rootType, encode) ->
        let emit (name: string) (v: obj) =
            try
                encode v
            with e ->
                failwithf "wire root %s: encoding document `%s` threw — %s" label name e.Message

        let baseLog = ResizeArray()
        let baseDoc = label, emit label (build None baseLog [] rootType)

        let perCase =
            [ for u in reachableUnions rootType |> List.sortBy friendly do
                  for c in FSharpType.GetUnionCases(u, true) do
                      let log = ResizeArray()
                      let v = build (Some(u, c)) log [] rootType
                      let name = sprintf "%s / %s.%s" label (friendly u) c.Name
                      yield (name, emit name v), (name, u, c.Name, List.ofSeq log) ]

        baseDoc :: (perCase |> List.map fst), perCase |> List.map snd

// ---- the baseline ---------------------------------------------------------

/// One document of a baseline: its name, the sha256 of the bytes the package emitted, and the
/// document re-rendered canonically (or `!unparsed` where the emission is not JSON at all).
type internal WireDoc =
    { Name: string
      Hash: string
      Canonical: string }

let private sha256 (s: string) =
    use h = SHA256.Create()

    "sha256:"
    + (h.ComputeHash(Encoding.UTF8.GetBytes s)
       |> Array.map (fun b -> b.ToString "x2")
       |> String.concat "")

let internal toDoc (name: string, emitted: string) : WireDoc =
    { Name = name
      Hash = sha256 emitted
      Canonical =
        match Json.parse emitted with
        | Ok v -> Canon.render v
        | Error _ -> "!unparsed" }

/// Render one package's documents, in the ordinal order of their names.
let internal renderPackage (package: string) : WireDoc list =
    roots
    |> List.filter (fun r -> packageOf r = package)
    |> List.collect (documentsOf >> fst)
    |> List.map toDoc
    |> List.sortWith (fun a b -> String.CompareOrdinal(a.Name, b.Name))

let internal wirePackages () : string list =
    roots |> List.map packageOf |> List.distinct |> List.sort

let private approving () =
    match Environment.GetEnvironmentVariable "CORE_APPROVE_WIRE" with
    | null -> false
    | v -> v.Trim() <> "" && v.Trim() <> "0"

let internal wireDir () : string =
    Snapshots.repoFile (Path.Combine("api", "wire"))

let internal wirePath (package: string) : string =
    Path.Combine(wireDir (), package + ".txt")

[<Literal>]
let private statedPrefix = "# class since "

let internal renderBaseline (package: string) (stated: string) (docs: WireDoc list) : string =
    let header =
        [ sprintf "# Wire-surface baseline for %s (Phase 214) — GENERATED, do not edit." package
          "# The canonical bytes of a document set reaching every member and discriminator this package emits:"
          "# one line per document — name, sha256 of the emitted bytes, the document rendered canonically."
          "# Regenerate with:"
          "#   CORE_APPROVE_WIRE=1 dotnet run --project tests/Fuaran.Core.Compute.Tests"
          statedPrefix + stated ]

    let lines =
        docs |> List.map (fun d -> sprintf "doc\t%s\t%s\t%s" d.Name d.Hash d.Canonical)

    String.concat "\n" (header @ lines) + "\n"

let internal parseDocs (text: string) : WireDoc list =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.toList
    |> List.choose (fun l ->
        match l.Split('\t') with
        | [| "doc"; name; hash; canonical |] ->
            Some
                { Name = name
                  Hash = hash
                  Canonical = canonical }
        | _ -> None)

/// The `(tag, class)` a baseline's header states, or `None` where it states none.
let internal parseStated (text: string) : (string * string) option =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.tryFind (fun l -> l.StartsWith(statedPrefix, StringComparison.Ordinal))
    |> Option.bind (fun l ->
        let rest = l.Substring statedPrefix.Length
        let colon = rest.IndexOf ": "

        if colon <= 0 then
            None
        else
            Some(rest.Substring(0, colon), rest.Substring(colon + 2)))

// ---- the classifier -------------------------------------------------------

type internal WireClass =
    | Additive
    | Breaking

let internal wireClassName (c: WireClass) =
    match c with
    | Additive -> "additive"
    | Breaking -> "breaking"

type internal WireMove =
    { Class: WireClass
      Doc: string
      What: string }

/// The structural tokens of a canonical document. A member is named by its PATH, and a path
/// passes through every `$type` it meets, so `{project}.columns` and `{project}.cols` are
/// different members and a rename reads as one removed and one added. Scalars carry their
/// rendering, so the same structure rendered to different bytes is visible too.
let internal tokensOf (canonical: string) : Set<string> =
    let acc = Collections.Generic.HashSet<string>()

    let rec walk (path: string) (v: JVal) =
        match v with
        | JObj members ->
            let here =
                match members |> List.tryFind (fun (k, _) -> k = "$type") with
                | Some(_, JStr tag) ->
                    acc.Add(sprintf "tag %s %s" path tag) |> ignore
                    path + "{" + tag + "}"
                | _ -> path

            for k, child in members do
                if k <> "$type" then
                    acc.Add(sprintf "member %s.%s" here k) |> ignore
                    walk (here + "." + k) child
        | JArr items ->
            for x in items do
                walk (path + "[]") x
        | scalar -> acc.Add(sprintf "value %s %s" path (Canon.render scalar)) |> ignore

    match Json.parse canonical with
    | Ok v -> walk "$" v
    | Error _ -> acc.Add("unparsed") |> ignore

    Set.ofSeq acc

/// Classify one document that exists on both sides.
let internal classifyDoc (before: WireDoc) (after: WireDoc) : WireMove list =
    if before.Hash = after.Hash && before.Canonical = after.Canonical then
        []
    else
        let b = tokensOf before.Canonical
        let a = tokensOf after.Canonical
        let removed = Set.difference b a
        let added = Set.difference a b

        let structural (t: string) =
            t.StartsWith "member " || t.StartsWith "tag "

        let move cls what =
            { Class = cls
              Doc = after.Name
              What = what }

        let removedStructure = removed |> Set.filter structural |> Set.toList
        let addedStructure = added |> Set.filter structural |> Set.toList

        let valueMoved =
            removed |> Set.exists (structural >> not)
            || added |> Set.exists (structural >> not)

        [ for t in removedStructure do
              yield
                  move
                      Breaking
                      (if t.StartsWith "tag " then
                           "discriminator changed: " + t.Substring 4
                       else
                           "member removed or renamed: " + t.Substring 7)
          for t in addedStructure do
              yield
                  move
                      Additive
                      (if t.StartsWith "tag " then
                           "discriminator added: " + t.Substring 4
                       else
                           "member added: " + t.Substring 7)
          // A scalar re-rendered, where the structure did NOT explain it. A member that merely
          // appeared brings its own value token, which is not a second move.
          if valueMoved && List.isEmpty removedStructure then
              let unexplained = removed |> Set.filter (structural >> not) |> Set.toList

              if not (List.isEmpty unexplained) then
                  yield move Breaking ("a value renders differently: " + String.concat "; " unexplained)
          if List.isEmpty removedStructure && List.isEmpty addedStructure && not valueMoved then
              yield move Breaking "the same structure is emitted as different bytes" ]

/// Classify a package's drift from `before` to `after`.
///
/// A document present on one side only is a case added or removed — unless a document with the
/// SAME bytes sits on the other side under another name, which is an F# case renamed with its
/// wire spelling untouched: the managed gate's business, and not a wire move at all.
let internal classifyPackage (before: WireDoc list) (after: WireDoc list) : WireMove list =
    let byName (ds: WireDoc list) =
        ds |> List.map (fun d -> d.Name, d) |> Map.ofList

    let b = byName before
    let a = byName after

    let bytesOf (ds: WireDoc list) =
        ds |> List.map (fun d -> d.Hash) |> Set.ofList

    let onlyBefore = before |> List.filter (fun d -> not (a.ContainsKey d.Name))
    let onlyAfter = after |> List.filter (fun d -> not (b.ContainsKey d.Name))
    let afterBytes = bytesOf onlyAfter
    let beforeBytes = bytesOf onlyBefore

    [ for d in before do
          match a.TryFind d.Name with
          | Some d' -> yield! classifyDoc d d'
          | None -> ()
      for d in onlyBefore do
          if not (afterBytes.Contains d.Hash) then
              yield
                  { Class = Breaking
                    Doc = d.Name
                    What = "document removed — a case the package no longer emits" }
      for d in onlyAfter do
          if not (beforeBytes.Contains d.Hash) then
              yield
                  { Class = Additive
                    Doc = d.Name
                    What = "document added — a case the package now emits" } ]

let internal headline (moves: WireMove list) : WireClass option =
    if moves |> List.exists (fun m -> m.Class = Breaking) then
        Some Breaking
    elif List.isEmpty moves then
        None
    else
        Some Additive

let internal describe (m: WireMove) =
    sprintf "%-9s %s — %s" (wireClassName m.Class) m.Doc m.What

// ---- git, for the class a baseline states ----------------------------------

let private repoRoot () : string = Snapshots.repoFile ""

let private git (arguments: string) : Result<string, string> =
    try
        let psi = ChildProcess.redirected "git" arguments
        psi.WorkingDirectory <- repoRoot ()
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            Error(sprintf "`git %s` exited %d: %s" arguments p.ExitCode (err.Trim()))
        else
            Ok out
    with e ->
        Error("`git` could not be run: " + e.Message)

let private newestTag () : Result<string, string> =
    git "tag --list"
    |> Result.bind (fun out ->
        match PublicSurfaceTests.newestVersionTag (out.Split('\n') |> Array.toList) with
        | Some t -> Ok t
        | None -> Error "this clone holds no `vX.Y.Z` tag")

/// The class of `docs` against the package's baseline AS OF `tag`, in the words a header states.
let internal statedClassAt (tag: string) (package: string) (docs: WireDoc list) : string =
    match git (sprintf "show %s:api/wire/%s.txt" tag package) with
    | Error _ -> "first snapshot"
    | Ok text ->
        match headline (classifyPackage (parseDocs text) docs) with
        | None -> "unchanged"
        | Some c -> wireClassName c

// ---- the suite ------------------------------------------------------------

let private documentsFor (package: string) =
    roots |> List.filter (fun r -> packageOf r = package)

[<Tests>]
let tests =
    testList
        "Wire surface"
        [ test "every packable package is a wire root or says why it is not" {
              let packable =
                  PackageRosterTests.packableProjects (repoRoot ()) |> List.map _.PackageId

              Expect.isNonEmpty packable "at least one packable project was found under src/"
              let wire = wirePackages ()
              let declared = notWire |> List.map fst

              let unaccounted =
                  packable
                  |> List.filter (fun p -> not (List.contains p wire) && not (List.contains p declared))

              let both = wire |> List.filter (fun p -> List.contains p declared)

              let stale =
                  (wire @ declared) |> List.filter (fun p -> not (List.contains p packable))

              Expect.isEmpty
                  unaccounted
                  "these packable packages neither declare wire roots nor say why they emit no document — add roots to WireSurfaceTests.roots, or a reason to notWire"

              Expect.isEmpty both "a package cannot be a wire root and declare that it emits nothing"
              Expect.isEmpty stale "these entries name no packable package"
          }

          test "every per-case document reaches the case it is named for" {
              // The routing through enclosing unions is what makes a nested case reachable at all
              // (a window function exists only inside a `window` step). A route that silently
              // fell back to a default would name a document after a case it does not contain,
              // and the set would look complete while missing it.
              let misses =
                  [ for r in roots do
                        for name, u, case, log in snd (documentsOf r) do
                            if not (log |> List.exists (fun (t, c) -> t = u && c = case)) then
                                yield name ]

              if not (List.isEmpty misses) then
                  failtestf
                      "these documents never built the case they are named for:\n  %s"
                      (String.concat "\n  " misses)

              let perCase = roots |> List.sumBy (fun r -> List.length (snd (documentsOf r)))
              Expect.isGreaterThan perCase 100 "the derived set is not vacuous"
          }

          test "every wire baseline is what the encoders emit today" {
              let packages = wirePackages ()

              if approving () then
                  Directory.CreateDirectory(wireDir ()) |> ignore

                  let tag =
                      match newestTag () with
                      | Ok t -> t
                      | Error why ->
                          failtestf
                              "CORE_APPROVE_WIRE: a baseline states its class since the newest `vX.Y.Z` tag, and none could be read (%s) — fetch tags and re-run"
                              why

                  printfn ""
                  printfn "==== wire surface: regenerating, class stated since %s" tag

                  for p in packages do
                      let docs = renderPackage p
                      let path = wirePath p
                      let stated = statedClassAt tag p docs
                      let text = renderBaseline p (tag + ": " + stated) docs
                      let old = if File.Exists path then File.ReadAllText path else ""

                      if old <> text then
                          File.WriteAllText(path, text)
                          printfn "  %-28s %-16s (%d documents)  rewritten" p stated docs.Length
                      else
                          printfn "  %-28s %-16s (%d documents)" p stated docs.Length

                  printfn "==== stage the wire baselines you meant to move BY NAME, and state the class in the commit"
              else
                  let failures =
                      [ for p in packages do
                            let path = wirePath p

                            if not (File.Exists path) then
                                yield sprintf "  %s: no baseline at api/wire/%s.txt" p p
                            else
                                let moves = classifyPackage (parseDocs (File.ReadAllText path)) (renderPackage p)

                                match headline moves with
                                | None -> ()
                                | Some c ->
                                    yield sprintf "  %-28s %s, %d move(s)" p (wireClassName c) moves.Length

                                    for m in moves |> List.truncate 12 do
                                        yield "      " + describe m

                                    if moves.Length > 12 then
                                        yield sprintf "      ... and %d more" (moves.Length - 12) ]

                  if not (List.isEmpty failures) then
                      failtestf
                          "the canonical bytes moved and their wire baseline did not:\n%s\n       Remedy: if the move is intended, regenerate with `CORE_APPROVE_WIRE=1 dotnet run --project tests/Fuaran.Core.Compute.Tests`, which writes the class into the baseline's header; stage the baselines BY NAME and state that class in the commit (STABILITY.md, \"The wire surface\")."
                          (String.concat "\n" failures)

                  let orphans =
                      if Directory.Exists(wireDir ()) then
                          Directory.GetFiles(wireDir (), "*.txt")
                          |> Array.map Path.GetFileNameWithoutExtension
                          |> Array.filter (fun f -> not (List.contains f packages))
                          |> Array.toList
                      else
                          []

                  Expect.isEmpty orphans "these wire baselines name no package with wire roots"
          }

          test "every wire baseline states its class, and the class it states is true" {
              // "A baseline cannot move without a stated class", made checkable: the header names
              // a tag and a class, and the class is recomputed against that tag's own copy. The
              // statement is anchored to the tag it NAMES rather than to the newest one, so cutting
              // a release stales nothing — but a baseline that has moved SINCE the newest tag must
              // state its class since that tag, or a move could hide under an older statement.
              match newestTag () with
              | Error why ->
                  printfn "WireSurface stated-class check SKIPPED: %s" why
                  skiptestf "stated-class check skipped — %s" why
              | Ok newest ->
                  let problems =
                      [ for p in wirePackages () do
                            let path = wirePath p

                            if File.Exists path then
                                let text = File.ReadAllText path
                                let docs = parseDocs text

                                match parseStated text with
                                | None -> yield sprintf "%s states no class" p
                                | Some(tag, stated) ->
                                    let actual = statedClassAt tag p docs

                                    if actual <> stated then
                                        yield
                                            sprintf
                                                "%s states `%s` since %s, and the move since %s is `%s`"
                                                p
                                                stated
                                                tag
                                                tag
                                                actual

                                    if tag <> newest && statedClassAt newest p docs <> "unchanged" then
                                        yield
                                            sprintf
                                                "%s has moved since %s but states its class only since %s"
                                                p
                                                newest
                                                tag ]

                  if not (List.isEmpty problems) then
                      failtestf
                          "a wire baseline's stated class is missing, stale or wrong:\n  %s\n       Remedy: regenerate with CORE_APPROVE_WIRE=1, which states the class it computes."
                          (String.concat "\n  " problems)
          }

          // ---- the classifier, pinned on the shapes the phase names ----

          test "go-red: Phase 213's rename, replayed against the pre-213 bytes, is BREAKING and named" {
              // The two moves 213 made, spelled as they were before it and as they are after.
              let pre =
                  [ { Name = "pipeline / Transform.Project"
                      Hash = "sha256:pre"
                      Canonical = """[{"$type":"project","cols":[{"as":"s","column":"s"}]}]""" }
                    { Name = "pipeline / Transform.Sort"
                      Hash = "sha256:pre-sort"
                      Canonical = """[{"$type":"sort","by":[{"col":"s","dir":"asc"}]}]""" } ]

              let post =
                  [ { Name = "pipeline / Transform.Project"
                      Hash = "sha256:post"
                      Canonical = """[{"$type":"project","columns":[{"as":"s","column":"s"}]}]""" }
                    { Name = "pipeline / Transform.Sort"
                      Hash = "sha256:post-sort"
                      Canonical = """[{"$type":"sort","by":[{"column":"s","dir":"asc"}]}]""" } ]

              let moves = classifyPackage pre post
              Expect.equal (headline moves) (Some Breaking) "a renamed member is breaking"

              let said = moves |> List.map describe |> String.concat "\n"
              Expect.stringContains said "$[]{project}.cols" "and the report names the project member that was renamed"
              Expect.stringContains said "$[]{sort}.by[].col" "and the sort key's"
          }

          test "an added optional member reads ADDITIVE" {
              let before =
                  [ { Name = "d"
                      Hash = "sha256:a"
                      Canonical = """{"$type":"limit","n":1}""" } ]

              let after =
                  [ { Name = "d"
                      Hash = "sha256:b"
                      Canonical = """{"$type":"limit","n":1,"offset":1}""" } ]

              let moves = classifyPackage before after
              Expect.equal (headline moves) (Some Additive) "a new member is additive"
              Expect.equal moves.Length 1 "one move — the new member's value is not a second one"
          }

          test "a new case reads ADDITIVE, a changed discriminator BREAKING, a re-rendered value BREAKING" {
              let d name canonical =
                  { Name = name
                    Hash = "sha256:" + canonical
                    Canonical = canonical }

              Expect.equal
                  (headline (
                      classifyPackage
                          [ d "a" """{"$type":"x"}""" ]
                          [ d "a" """{"$type":"x"}"""; d "b" """{"$type":"y"}""" ]
                  ))
                  (Some Additive)
                  "a document added is a case added"

              Expect.equal
                  (headline (classifyPackage [ d "a" """{"$type":"x"}""" ] [ d "a" """{"$type":"z"}""" ]))
                  (Some Breaking)
                  "a discriminator that changed is breaking"

              Expect.equal
                  (headline (classifyPackage [ d "a" """{"n":1.5}""" ] [ d "a" """{"n":1.50}""" ]))
                  (Some Breaking)
                  "the same member rendered to different bytes is breaking"

              Expect.equal
                  (classifyPackage [ d "old name" """{"$type":"x"}""" ] [ d "new name" """{"$type":"x"}""" ])
                  []
                  "an F# case renamed with its wire bytes untouched is not a wire move"

              Expect.equal
                  (headline (
                      classifyPackage
                          [ { Name = "a"
                              Hash = "sha256:1"
                              Canonical = "{}" } ]
                          [ { Name = "a"
                              Hash = "sha256:2"
                              Canonical = "{}" } ]
                  ))
                  (Some Breaking)
                  "identical structure emitted as different bytes is breaking — the hash sees what canonical form hides"
          }

          test "a decode ALIAS moves nothing: the aliased spelling re-encodes to the baseline's bytes" {
              // `cols` is the alias Phase 213 kept. A document written with it decodes, and
              // re-encodes to exactly the bytes the baseline pins for a `project` step — so the
              // alias is outside the pinned surface, and adding one cannot move it.
              let committed = parseDocs (File.ReadAllText(wirePath "Fuaran.Core.DataFrame"))

              let project =
                  committed |> List.find (fun d -> d.Name = "pipeline / Transform.Project")

              let aliased = project.Canonical.Replace("\"columns\":", "\"cols\":")
              Expect.notEqual aliased project.Canonical "the probe really spelled the alias"

              match DataFrameCodec.decodePipeline aliased with
              | Error e -> failtestf "the aliased spelling did not decode: %A" e
              | Ok steps ->
                  let reEncoded = toDoc ("x", DataFrameCodec.encodePipeline steps)
                  Expect.equal reEncoded.Canonical project.Canonical "re-encoded canonically"
                  Expect.equal reEncoded.Hash project.Hash "to the very bytes the baseline pins"
          }

          test "the probe measures real content: the DataFrame baseline names the members 213 moved" {
              let tokens =
                  parseDocs (File.ReadAllText(wirePath "Fuaran.Core.DataFrame"))
                  |> List.map (fun d -> tokensOf d.Canonical)
                  |> Set.unionMany

              Expect.contains tokens "member $[]{project}.columns" "a project step's column list"
              Expect.isFalse (tokens.Contains "member $[]{project}.cols") "and not its decode alias"
          } ]
