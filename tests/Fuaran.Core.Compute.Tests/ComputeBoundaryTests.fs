/// Phase 257 — the compute boundary, held.
///
/// DECISIONS.md D66 rules that `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` leave this
/// repository for one of their own, and D68 records how the line was prepared inside it first: the
/// families and the facade half that read them moved into `Fuaran.Core.DataFrame.Conformance` and
/// `Fuaran.Core.DataFrame.CSharp`, so the later move is a copy of whole assemblies. That only stays
/// true while nothing on this side of the line reaches across it again, and a helper that does is
/// the easiest change in the world to make. So this test refuses it, on two readings, because each
/// misses what the other sees:
///
///   * the PROJECT FILES — every spine project's `ProjectReference` closure, walked through the
///     projects it names, so a reference that arrives through an intermediate project is caught;
///   * the BUILT ASSEMBLIES — each spine dll's own assembly-reference table, read from its
///     metadata (the list `Assembly.GetReferencedAssemblies` returns, read without loading the
///     assembly into this process). The compiler writes a reference there for every assembly a
///     compiled construct actually uses, so an `open` that resolved against a transitively
///     available assembly shows up here even where no project file names it.
///
/// The compute side is the two ids that leave plus the two assemblies this phase cut beside them;
/// a spine assembly referencing either new one reaches `DataFrame` through it.
module Fuaran.Core.Tests.ComputeBoundaryTests

open System
open System.IO
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Xml.Linq
open Expecto

/// The spine: every assembly that stays in this repository when the compute layer leaves (D66).
let spine: string list =
    [ "Fuaran.Core.Tree"
      "Fuaran.Core.Ops"
      "Fuaran.Core.OpStream"
      "Fuaran.Core.OpStream.Dag"
      "Fuaran.Core.Wire"
      "Fuaran.Core.Column"
      "Fuaran.Core.Validator"
      "Fuaran.Core.Function"
      "Fuaran.Core.Query"
      "Fuaran.Core.Projection"
      "Fuaran.Core.Propagation"
      "Fuaran.Core.AiSurface"
      "Fuaran.Core.Idl"
      "Fuaran.Core.Idl.Codegen"
      "Fuaran.Core.Idl.Cli"
      "Fuaran.Core.Conformance"
      "Fuaran.Core.CSharp" ]

/// The compute side of the line.
let compute: Set<string> =
    set
        [ "Fuaran.Core.DataFrame"
          "Fuaran.Core.Column.Ops"
          "Fuaran.Core.DataFrame.Conformance"
          "Fuaran.Core.DataFrame.CSharp" ]

// ---------------------------------------------------------------------------
//  the pure rule, so it has a go-red
// ---------------------------------------------------------------------------

/// Every (spine assembly, compute assembly) pair such that the compute assembly is reachable from
/// the spine one through `edges` — a map from an assembly to the assemblies it references
/// directly. Reachability rather than adjacency, so an intermediate hop cannot launder a crossing.
let crossings (edges: Map<string, string list>) (spineNames: string list) : (string * string) list =
    let rec reach (seen: Set<string>) (frontier: string list) =
        match frontier with
        | [] -> seen
        | x :: rest ->
            let next =
                edges
                |> Map.tryFind x
                |> Option.defaultValue []
                |> List.filter (fun n -> not (Set.contains n seen))

            reach (Set.union seen (Set.ofList next)) (next @ rest)

    [ for s in spineNames do
          for c in reach Set.empty [ s ] |> Set.intersect compute |> Set.toList -> s, c ]

// ---------------------------------------------------------------------------
//  reading the tree
// ---------------------------------------------------------------------------

let private srcDir () = Snapshots.repoFile "src"

/// The project file for an assembly name under `src/` — `.fsproj`, or `.csproj` for the facade.
let private projectFileOf (name: string) : string option =
    [ ".fsproj"; ".csproj" ]
    |> List.map (fun ext -> Path.Combine(srcDir (), name, name + ext))
    |> List.tryFind File.Exists

/// The `ProjectReference` targets a project file names, as assembly names — read as XML, so a
/// project name that appears in a COMMENT (the kit's own project file explains in one why it no
/// longer references the dataframe layer) is not read as a reference.
let private projectReferencesOf (projectFile: string) : string list =
    XDocument.Load(projectFile).Descendants()
    |> Seq.filter (fun e -> e.Name.LocalName = "ProjectReference")
    |> Seq.choose (fun e ->
        match e.Attribute(XName.Get "Include") with
        | null -> None
        | a -> Some(Path.GetFileNameWithoutExtension(a.Value.Replace('\\', '/'))))
    |> Seq.toList

/// The project-reference graph over every project under `src/`.
let private projectEdges () : Map<string, string list> =
    Directory.GetDirectories(srcDir ())
    |> Array.choose (fun d ->
        let name = Path.GetFileName d

        projectFileOf name |> Option.map (fun p -> name, projectReferencesOf p))
    |> Map.ofArray

/// The assembly-reference table of a built dll: the names `Assembly.GetReferencedAssemblies`
/// would return, read from the metadata so nothing is loaded.
let internal referencedAssemblies (dllPath: string) : string list =
    use stream = File.OpenRead dllPath
    use pe = new PEReader(stream)
    let md = pe.GetMetadataReader()

    [ for h in md.AssemblyReferences -> md.GetString((md.GetAssemblyReference h).Name) ]

/// The built dll for a spine assembly: the copy in this test's own output when the suite
/// references it, otherwise the project's own build output, preferring this binary's
/// configuration (`PublicSurfaceTests.assemblyFor`, the surface gate's own locator).
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

          testCase "the rule goes red on a direct crossing, an indirect one, and neither"
          <| fun _ ->
              // The go-red, over a synthetic graph: a spine project gaining a DataFrame reference,
              // one gaining it through an intermediate hop, and the clean graph beside them.
              let clean =
                  Map.ofList
                      [ "Fuaran.Core.Query", [ "Fuaran.Core.Column"; "Fuaran.Core.Function" ]
                        "Fuaran.Core.Column", [ "Fuaran.Core.Wire" ]
                        "Fuaran.Core.DataFrame", [ "Fuaran.Core.Column" ] ]

              Expect.isEmpty (crossings clean [ "Fuaran.Core.Query" ]) "a clean graph crosses nothing"

              let direct = clean |> Map.add "Fuaran.Core.Query" [ "Fuaran.Core.DataFrame" ]

              Expect.equal
                  (crossings direct [ "Fuaran.Core.Query" ])
                  [ "Fuaran.Core.Query", "Fuaran.Core.DataFrame" ]
                  "a direct reference to DataFrame is a crossing"

              let indirect =
                  clean
                  |> Map.add "Fuaran.Core.Query" [ "Fuaran.Core.Hop" ]
                  |> Map.add "Fuaran.Core.Hop" [ "Fuaran.Core.Column.Ops" ]

              Expect.equal
                  (crossings indirect [ "Fuaran.Core.Query" ])
                  [ "Fuaran.Core.Query", "Fuaran.Core.Column.Ops" ]
                  "a reference through an intermediate project is a crossing too"

          testCase "no spine project references the compute side, directly or through another project"
          <| fun _ ->
              let edges = projectEdges ()

              for name in spine do
                  Expect.isTrue
                      (Map.containsKey name edges)
                      (sprintf "%s has no project under src/ — the spine list names a project that is not there" name)

              let found = crossings edges spine

              Expect.isEmpty
                  found
                  (sprintf
                      "spine project(s) reach the compute side through their ProjectReferences: %s. D66/D68: the compute layer leaves this repository; a spine project that needs it is a design question for the compute repository's seam, not a reference to add here."
                      (render found))

          testCase "no built spine assembly references the compute side"
          <| fun _ ->
              let refs =
                  [ for name in spine ->
                        match builtAssembly name with
                        | Ok dll -> name, referencedAssemblies dll
                        | Error e -> failtestf "%s" e ]

              let found =
                  [ for name, rs in refs do
                        for r in rs do
                            if Set.contains r compute then
                                yield name, r ]

              Expect.isEmpty
                  found
                  (sprintf
                      "built spine assembly(ies) carry a reference to the compute side in their metadata: %s. The compiler writes a reference for every assembly a compiled construct uses, so an `open` that resolved against a transitively available assembly lands here even where no project file names it."
                      (render found))

              // The reading is not vacuous: the kit's own dll references the spine it runs over,
              // so a table that read as empty would be a broken reader rather than a clean line.
              let kit = refs |> List.find (fun (n, _) -> n = "Fuaran.Core.Conformance") |> snd
              Expect.contains kit "Fuaran.Core.Column" "the kit's reference table names Column, which it reads"

          testCase "the compute side does reference the spine it is built over"
          <| fun _ ->
              // The other direction is allowed, and is what makes the line a line: the two new
              // assemblies read the kit and the facade below them. Checked so the reader above is
              // seen to find a reference where one exists.
              for name, below in
                  [ "Fuaran.Core.DataFrame.Conformance", "Fuaran.Core.Conformance"
                    "Fuaran.Core.DataFrame.CSharp", "Fuaran.Core.CSharp" ] do
                  match builtAssembly name with
                  | Error e -> failtestf "%s" e
                  | Ok dll -> Expect.contains (referencedAssemblies dll) below (sprintf "%s references %s" name below) ]
