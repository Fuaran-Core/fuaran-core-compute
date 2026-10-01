/// The compute boundary, held from this side (Phase 259, mirroring the Fuaran.Core repository's own
/// test from Phase 257).
///
/// The substrate's test refuses an UPWARD reference: no spine assembly may reach the compute layer.
/// This one holds the other half of the same line. The three packages this repository produces stand
/// on exactly four substrate packages — `Fuaran.Core.Column` and `Fuaran.Core.Wire` (the table and
/// the canonical JSON the dataframe is built over), `Fuaran.Core.OpStream` (which records columnar
/// edits) and `Fuaran.Core.Conformance` (the kit the dataframe families extend) — and they take each
/// by PACKAGE, never by project. A fifth substrate reference, or a reference to anything that is not a public package, is a design
/// question for a seam, not a line to add to a project file.
///
/// Two readings, because each misses what the other sees:
///
///   * the PROJECT FILES — every project under `src/`: its `PackageReference`s must name only
///     FSharp.Core and the four, and its `ProjectReference`s only the other compute projects;
///   * the BUILT ASSEMBLIES — each compute dll's assembly-reference table, read from its metadata
///     (without loading it). The compiler writes a reference there for every assembly a compiled
///     construct actually uses, so an `open` that resolved against an assembly arriving TRANSITIVELY
///     (the kit brings most of the substrate with it) shows up here even though no project file names
///     it.
module Fuaran.Core.Tests.ComputeBoundaryTests

open System
open System.IO
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Xml.Linq
open Expecto

/// The three assemblies this repository produces.
let compute: Set<string> =
    set
        [ "Fuaran.Core.DataFrame"
          "Fuaran.Core.Column.Ops"
          "Fuaran.Core.DataFrame.Conformance" ]

/// The substrate packages the compute strand stands on — and nothing above them.
let allowedSubstrate: Set<string> =
    set
        [ "Fuaran.Core.Column"
          "Fuaran.Core.Wire"
          "Fuaran.Core.OpStream"
          "Fuaran.Core.Conformance" ]

/// The only non-substrate package a shipped project may take.
let allowedOther: Set<string> = set [ "FSharp.Core" ]

// ---------------------------------------------------------------------------
//  the pure rules, so each has a go-red
// ---------------------------------------------------------------------------

/// A project's references that break the line: a package outside the allowed set, or a project
/// outside this repository's three. `(project, offending reference)` pairs.
let violations (projects: Map<string, string list * string list>) : (string * string) list =
    [ for KeyValue(name, (packages, projectRefs)) in projects do
          for p in packages do
              if not (Set.contains p allowedSubstrate || Set.contains p allowedOther) then
                  yield name, "package " + p

          for r in projectRefs do
              if not (Set.contains r compute) then
                  yield name, "project " + r ]

/// A built assembly's references into the Fuaran family that break the line: any `Fuaran.*`
/// assembly that is neither one of the three nor one of the allowed four.
let assemblyViolations (refs: Map<string, string list>) : (string * string) list =
    [ for KeyValue(name, rs) in refs do
          for r in rs do
              if
                  r.StartsWith("Fuaran.", StringComparison.Ordinal)
                  && not (Set.contains r compute || Set.contains r allowedSubstrate)
              then
                  yield name, r ]

// ---------------------------------------------------------------------------
//  reading the tree
// ---------------------------------------------------------------------------

let private srcDir () = Snapshots.repoFile "src"

/// The project file for an assembly name under `src/` — `.fsproj`, or `.csproj` should a C# project
/// return (the src/ roster check below then names it).
let private projectFileOf (name: string) : string option =
    [ ".fsproj"; ".csproj" ]
    |> List.map (fun ext -> Path.Combine(srcDir (), name, name + ext))
    |> List.tryFind File.Exists

/// The `PackageReference` ids and the `ProjectReference` targets (as assembly names) a project file
/// names — read as XML, so a name in a COMMENT is not read as a reference.
let private referencesOf (projectFile: string) : string list * string list =
    let elements = XDocument.Load(projectFile).Descendants() |> Seq.toList

    let includes (local: string) =
        elements
        |> List.filter (fun e -> e.Name.LocalName = local)
        |> List.choose (fun e ->
            match e.Attribute(XName.Get "Include") with
            | null -> None
            | a -> Some a.Value)

    includes "PackageReference",
    includes "ProjectReference"
    |> List.map (fun p -> Path.GetFileNameWithoutExtension(p.Replace('\\', '/')))

/// Every project under `src/`, with its references.
let private projectReferences () : Map<string, string list * string list> =
    Directory.GetDirectories(srcDir ())
    |> Array.choose (fun d ->
        let name = Path.GetFileName d
        projectFileOf name |> Option.map (fun p -> name, referencesOf p))
    |> Map.ofArray

/// The assembly-reference table of a built dll, read from the metadata so nothing is loaded.
let internal referencedAssemblies (dllPath: string) : string list =
    use stream = File.OpenRead dllPath
    use pe = new PEReader(stream)
    let md = pe.GetMetadataReader()

    [ for h in md.AssemblyReferences -> md.GetString((md.GetAssemblyReference h).Name) ]

/// The built dll for a compute assembly: the copy beside this suite (the suite references all three),
/// otherwise the project's own build output (`PublicSurfaceTests.assemblyFor`, the surface gate's own
/// locator).
let private builtAssembly (name: string) : Result<string, string> =
    let local = Path.Combine(AppContext.BaseDirectory, name + ".dll")

    if File.Exists local then
        Ok local
    else
        match projectFileOf name with
        | None -> Error(sprintf "%s: no project under src/" name)
        | Some p ->
            PublicSurfaceTests.assemblyFor (Snapshots.repoFile "") (Path.GetRelativePath(Snapshots.repoFile "", p)) name

let private render (pairs: (string * string) list) : string =
    pairs |> List.map (fun (s, c) -> s + " -> " + c) |> String.concat "; "

[<Tests>]
let tests =
    testList
        "Compute boundary"
        [

          testCase "the project rule goes red on a sixth substrate package, a private package and a project reference"
          <| fun _ ->
              let clean =
                  Map.ofList
                      [ "Fuaran.Core.DataFrame", ([ "FSharp.Core"; "Fuaran.Core.Column"; "Fuaran.Core.Wire" ], [])
                        "Fuaran.Core.Column.Ops", ([ "Fuaran.Core.OpStream" ], [ "Fuaran.Core.DataFrame" ]) ]

              Expect.isEmpty (violations clean) "a clean graph breaks nothing"

              let above =
                  clean
                  |> Map.add "Fuaran.Core.DataFrame" ([ "Fuaran.Core.Column"; "Fuaran.Core.Propagation" ], [])

              Expect.equal
                  (violations above)
                  [ "Fuaran.Core.DataFrame", "package Fuaran.Core.Propagation" ]
                  "a substrate package above the four is a violation"

              let privatePackage =
                  clean
                  |> Map.add "Fuaran.Core.DataFrame" ([ "Fuaran.Core.Column"; "Acme.Internal" ], [])

              Expect.equal
                  (violations privatePackage)
                  [ "Fuaran.Core.DataFrame", "package Acme.Internal" ]
                  "a package outside the allowed set is a violation, whatever it is"

              let byProject =
                  clean |> Map.add "Fuaran.Core.DataFrame" ([], [ "Fuaran.Core.Column" ])

              Expect.equal
                  (violations byProject)
                  [ "Fuaran.Core.DataFrame", "project Fuaran.Core.Column" ]
                  "the substrate taken by PROJECT is a violation — it is taken by package"

          testCase "the assembly rule goes red on a transitive substrate assembly and stays quiet on the rest"
          <| fun _ ->
              let refs =
                  Map.ofList
                      [ "Fuaran.Core.DataFrame.Conformance",
                        [ "System.Runtime"
                          "FSharp.Core"
                          "Fuaran.Core.Conformance"
                          "Fuaran.Core.DataFrame"
                          "Fuaran.Core.Tree" ] ]

              Expect.equal
                  (assemblyViolations refs)
                  [ "Fuaran.Core.DataFrame.Conformance", "Fuaran.Core.Tree" ]
                  "an assembly the kit brings transitively is a violation once compiled code uses it"

          testCase "every project under src/ takes the substrate by package, and only the four"
          <| fun _ ->
              let projects = projectReferences ()

              Expect.equal
                  (projects |> Map.keys |> Set.ofSeq)
                  compute
                  "src/ holds exactly this repository's three projects"

              let found = violations projects

              Expect.isEmpty
                  found
                  (sprintf
                      "project(s) under src/ reference outside the line: %s. The compute strand stands on Column, Wire, OpStream and Conformance, taken by package; anything more is a seam to design, not a reference to add."
                      (render found))

              // Not vacuous: the reader does find the packages a project names.
              let dataFramePackages = projects["Fuaran.Core.DataFrame"] |> fst
              Expect.contains dataFramePackages "Fuaran.Core.Column" "DataFrame's package references name Column"

          testCase "no built compute assembly references a substrate assembly above the four"
          <| fun _ ->
              let refs =
                  [ for name in compute ->
                        match builtAssembly name with
                        | Ok dll -> name, referencedAssemblies dll
                        | Error e -> failtestf "%s" e ]
                  |> Map.ofList

              let found = assemblyViolations refs

              Expect.isEmpty
                  found
                  (sprintf
                      "built compute assembly(ies) reference a substrate assembly outside the four: %s. The compiler writes a reference for every assembly a compiled construct uses, so a type that arrived transitively through the kit lands here even though no project file names it."
                      (render found))

              // Not vacuous: each reads the substrate it is built over.
              for name, below in
                  [ "Fuaran.Core.DataFrame", "Fuaran.Core.Column"
                    "Fuaran.Core.Column.Ops", "Fuaran.Core.OpStream"
                    "Fuaran.Core.DataFrame.Conformance", "Fuaran.Core.Conformance" ] do
                  Expect.contains refs[name] below (sprintf "%s references %s" name below) ]
