module Fuaran.Compute.Tests.ElementaryTests

// ---------------------------------------------------------------------------
// Phase 593 — the pinned elementary functions, held on both hosts; their own
// package, Fuaran.Compute.Elementary, since Phase 630 (DECISIONS.md D74).
//
//   * `ElementaryLaws.laws` on .NET (the node leg runs them under Fable beside
//     the linear-algebra laws, in LinearAlgebraTests).
//   * The committed hard-input vectors (`conformance/laws/elementary-laws.json`)
//     are the package's answers, rendered through the one corpus writer, each
//     computed vector within one ulp of its correctly rounded reference.
//   * The source reaches no host mathematics call.
//   * `ElementaryOperations.exactness` is the published operation set, both ways.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Reflection
open Expecto
open Fuaran.Core
open Fuaran.Compute.Elementary

let private bits (x: float) = BitConverter.DoubleToInt64Bits x

let private committedPath () =
    Snapshots.repoFile (Path.Combine("conformance", "laws", ElementaryVectors.fileName))

/// The vector with this id, as the document's line for it.
let private lineOf (id: string) : string =
    ElementaryVectors.render().Split('\n')
    |> Array.find (fun l -> l.Contains(sprintf "\"id\": \"%s\"" id))

/// The public functions of the package's operation module, spelled `<Module>.<function>` — the census
/// `ElementaryOperations.exactness` is held to.
let private publishedOperations () : Set<string> =
    typeof<SpecialValue>.Assembly.GetExportedTypes()
    |> Array.filter (fun t -> t.IsAbstract && t.IsSealed && t.Namespace = "Fuaran.Compute.Elementary")
    |> Array.collect (fun t ->
        let name =
            if t.Name.EndsWith "Module" then
                t.Name.Substring(0, t.Name.Length - "Module".Length)
            else
                t.Name

        if name = "Elementary" || name = "Rng" then
            t.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
            |> Array.filter (fun m -> not m.IsSpecialName)
            |> Array.map (fun m -> name + "." + m.Name)
        else
            [||])
    |> Set.ofArray

[<Tests>]
let tests =
    testList
        "Phase 593 - elementary functions"
        [ testCase "elementaryLaws hold on .NET"
          <| fun () ->
              for name, verdict in ElementaryLaws.laws 590 200 do
                  match verdict with
                  | Ok() -> ()
                  | Error why -> failtestf "%s: %s" name why

          testCase "the committed law vectors are the package's answers"
          <| fun () ->
              let committed = File.ReadAllText(committedPath ())

              match CorpusFile.firstDifference committed (ElementaryVectors.render ()) with
              | None -> ()
              | Some d ->
                  failtestf
                      "%s is stale at line %d:\n  committed: %A\n  rendered:  %A\n  Remedy: dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws"
                      ElementaryVectors.fileName
                      d.Line
                      d.Committed
                      d.Rendered

          testCase "every elementary vector with a reference is within one ulp of it, and says so in its ulps cell"
          <| fun () ->
              let referenced =
                  ElementaryVectors.cases
                  |> List.choose (fun c ->
                      ElementaryVectors.references
                      |> List.tryFind (fun (id, _) -> id = c.Id)
                      |> Option.map (fun (_, h) -> c, h))

              Expect.equal referenced.Length ElementaryVectors.references.Length "every reference names a vector"
              Expect.isGreaterThan referenced.Length 60 "the hard inputs are referenced"

              for c, h in referenced do
                  let line = lineOf c.Id
                  Expect.stringContains line (sprintf "\"reference\": \"%s\"" h) (c.Id + " carries its reference")

                  let ulps =
                      line.Substring(line.IndexOf "\"ulps\": " + 8).TrimEnd(' ', '}', ',', '\r')
                      |> int

                  Expect.isLessThanOrEqual
                      ulps
                      ElementaryVectors.referenceUlps
                      (sprintf "%s is %d ulps from the reference" c.Id ulps)

          testCase "the elementary vectors answer what was worked by hand"
          <| fun () ->
              let has (id: string) (fragment: string) =
                  Expect.stringContains (lineOf id) fragment (sprintf "%s answers %s" id fragment)

              let h = ElementaryVectors.hex
              has "exp-zero" (sprintf "\"value\": \"%s\", \"table\": \"compute\"" (h 1.0))

              has
                  "exp-above-overflow-edge"
                  (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h Double.PositiveInfinity))

              has "exp-minus-thousand" (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h 0.0))
              has "exp-nan" "\"value\": \"nan\", \"table\": \"nan\""
              has "ln-zero" "\"value\": \"nan\", \"table\": \"undefined\""
              has "ln-one" (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h 0.0))
              has "ln-two" (sprintf "\"value\": \"%s\"" (h 0.6931471805599453))
              has "log10-thousand" (sprintf "\"value\": \"%s\"" (h 3.0))
              has "pow-square-shortcut" (sprintf "\"value\": \"%s\"" (h 100.0))
              has "pow-root-shortcut" (sprintf "\"value\": \"%s\"" (h (sqrt 2.0)))
              has "pow-negative-zero-odd" (sprintf "\"value\": \"%s\", \"table\": \"compute\"" (h -0.0))
              has "pow-negative-base-non-integer" "\"value\": \"nan\", \"table\": \"undefined\""
              has "pow-one-nan" "\"value\": \"nan\", \"table\": \"nan\""
              has "pow-zero-zero" (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h 1.0))
              has "pow-two-cubed" (sprintf "\"value\": \"%s\"" (h 8.0))

          testCase
              "the host's own libm answers other bits on at least one elementary vector, so the byte comparison sees a port that cheats"
          <| fun () ->
              let differing =
                  ElementaryVectors.cases
                  |> List.choose (fun c ->
                      let scalar name =
                          c.Input |> List.find (fun (n, _) -> n = name) |> snd

                      match c.Op with
                      | "exp" -> Some(c.Id, Elementary.exp (scalar "x"), Math.Exp(scalar "x"))
                      | "ln" -> Some(c.Id, Elementary.ln (scalar "x"), Math.Log(scalar "x"))
                      | "log10" -> Some(c.Id, Elementary.log10 (scalar "x"), Math.Log10(scalar "x"))
                      | "pow" -> Some(c.Id, Elementary.pow (scalar "x") (scalar "y"), Math.Pow(scalar "x", scalar "y"))
                      | _ -> None)
                  |> List.filter (fun (_, pinned, host) ->
                      not (Double.IsNaN pinned) && not (Double.IsNaN host) && bits pinned <> bits host)

              Expect.isNonEmpty differing "the host's libm and the pinned routine differ somewhere on the vectors"

          testCase "Elementary's and Rng's sources reach no host mathematics call"
          <| fun () ->
              // Phase 621: the generator's file is held to the same rule as the functions'.
              let source =
                  [ "Elementary.fs"; "Rng.fs" ]
                  |> List.map (fun file ->
                      File.ReadAllText(Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.Elementary", file))))
                  |> String.concat
                      "
"

              let hostCall =
                  Text.RegularExpressions.Regex(
                      @"Math\.(Exp|Log|Log10|Log2|Pow|Floor|Ceiling|Round|Truncate|FusedMultiplyAdd|ScaleB|ILogB|Sqrt|Cbrt)\b|\*\*"
                  )

              let hits =
                  hostCall.Matches source
                  |> Seq.map (fun m -> m.Value)
                  |> Seq.distinct
                  |> List.ofSeq

              Expect.isEmpty
                  hits
                  "no host mathematics call in Elementary.fs or Rng.fs (the square root is the F# operator over the premise's correctly rounded root)"

          testCase "ElementaryOperations.exactness classifies exactly the published operations, every one Pinned"
          <| fun () ->
              let declared = ElementaryOperations.exactness |> List.map fst
              Expect.equal (List.distinct declared) declared "no operation is classified twice"
              let published = publishedOperations ()
              Expect.isNonEmpty (Set.toList published) "the census found the operation module"
              Expect.equal (Set.ofList declared) published "the table is the published operation set"

              Expect.isTrue
                  (ElementaryOperations.exactness
                   |> List.forall (fun (_, e) -> e = ElementaryOperations.Exactness.Pinned))
                  "every operation here is Pinned (DECISIONS.md D52)"

              Expect.equal
                  (ElementaryOperations.exactnessOf "Elementary.exp")
                  (Some ElementaryOperations.Exactness.Pinned)
                  "a lookup"

              Expect.equal (ElementaryOperations.exactnessOf "Elementary.sin") None "no such operation" ]
