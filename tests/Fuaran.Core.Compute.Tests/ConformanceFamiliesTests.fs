module Fuaran.Core.Tests.ConformanceFamiliesTests

// The law families this repository ships, held to the assembly that ships them (Phase 259, carried
// from the Fuaran.Core repository's suite with `Fuaran.Core.DataFrame.Conformance`).
//
// Two properties of the assembly that nothing else asserts:
//   * the roster share (`DataFrameFamilies.roster`) names EXACTLY the law entry points the assembly
//     declares, found by reflection over RETURN TYPE (`LawResult list`), so a family added without
//     a roster row, or a row left behind by a removed family, fails here;
//   * every family's pre-split spelling (`Conformance.<family>`, the forwarding module `Forwards.fs`
//     declares) reaches its home in `DataFrameConformance`, member for member, and answers what the
//     home answers. The forwards go when the substrate removes the strand (DECISIONS.md D1, Phase
//     258), and this test goes with them.

open System
open System.Reflection
open Expecto
open Fuaran.Core

/// The law entry points one module declares, by REFLECTION OVER RETURN TYPE.
let lawMethods (t: Type) : MethodInfo list =
    [ for m in t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly) do
          let rt = m.ReturnType

          if
              rt.IsGenericType
              && rt.GetGenericTypeDefinition() = typedefof<list<_>>
              && rt.GenericTypeArguments[0] = typeof<LawResult>
          then
              yield m ]

/// `(declared but not shipped, shipped but not declared)`.
let compareRoster (declared: Set<string>) (shipped: Set<string>) : string list * string list =
    Set.difference shipped declared |> Set.toList, Set.difference declared shipped |> Set.toList

/// The family's home module; the roster keys families by the forwarded spelling a consumer calls
/// today, so the home is read through the forwards rather than rostered twice.
[<Literal>]
let private forwardedHome = "Fuaran.Core.DataFrameConformance"

let private assembly () = KitRoster.assemblies |> List.exactlyOne

/// Every roster-shaped id the assembly ships: `<Module>.<entry point>` over every module but the
/// forwarded home, whose members are counted through their forwards.
let private shippedIds () : Set<string> =
    [ for t in assembly().GetTypes() do
          if t.IsPublic && t.FullName <> forwardedHome then
              let prefix =
                  if t.FullName.StartsWith("Fuaran.Core.", StringComparison.Ordinal) then
                      t.FullName.Substring "Fuaran.Core.".Length
                  else
                      t.FullName

              for m in lawMethods t -> prefix + "." + m.Name ]
    |> Set.ofList

[<Tests>]
let familiesTests =
    testList
        "Conformance.Families"
        [

          testCase "the roster share names exactly the law families the assembly ships"
          <| fun _ ->
              let shipped = shippedIds ()
              Expect.isNonEmpty (Set.toList shipped) "reflection found law entry points at all"

              let unrostered, phantom = compareRoster (Set.ofList KitRoster.ids) shipped

              Expect.isEmpty unrostered "these law entry points ship with no roster row (DataFrameFamilies.roster)"
              Expect.isEmpty phantom "these roster rows name no law entry point the assembly ships"

          testCase "the roster comparison goes red in both directions"
          <| fun _ ->
              Expect.equal
                  (compareRoster (set [ "A"; "B" ]) (set [ "B"; "C" ]))
                  ([ "C" ], [ "A" ])
                  "C ships unrostered, A is rostered and does not ship"

          testCase
              "every dataframe family is forwarded under its pre-split spelling, and every forward reaches its home"
          <| fun _ ->
              // The forwards are what keep `Conformance.<family>` compiling for a consumer written
              // against the pre-split spelling. Held member for member, both directions, by name and
              // parameter types; then each seed-and-iterations forward is RUN beside its home, so a
              // forward that called the wrong family fails too.
              let asm = assembly ()

              let membersOf (fullName: string) =
                  match asm.GetType fullName with
                  | null -> failtestf "%s is not in %s" fullName (asm.GetName().Name)
                  | t ->
                      t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly)
                      |> Array.map (fun m ->
                          m.Name
                          + "("
                          + (m.GetParameters()
                             |> Array.map (fun p -> p.ParameterType.Name)
                             |> String.concat ",")
                          + ")")
                      |> Set.ofArray

              let home = membersOf forwardedHome
              let forwards = membersOf "Fuaran.Core.Conformance"
              let unforwarded, orphaned = compareRoster forwards home

              Expect.isEmpty
                  unforwarded
                  "these DataFrameConformance members have no forward in the Conformance module beside them"

              Expect.isEmpty orphaned "these forwards name no DataFrameConformance member"

              let homeType = asm.GetType forwardedHome
              let fwdType = asm.GetType "Fuaran.Core.Conformance"

              for m in lawMethods homeType do
                  let ps = m.GetParameters() |> Array.map _.ParameterType

                  if ps = [| typeof<int>; typeof<int> |] then
                      let run (t: Type) =
                          t.GetMethod(m.Name, ps).Invoke(null, [| box 20260926; box 3 |]) :?> LawResult list

                      Expect.equal
                          (run fwdType)
                          (run homeType)
                          (sprintf
                              "Conformance.%s must answer exactly what DataFrameConformance.%s answers"
                              m.Name
                              m.Name) ]

[<Tests>]
let conformanceTests =
    testList
        "Conformance"
        [

          // The substrate's Phase 257 split the family: the parity half ships from
          // Fuaran.Core.DataFrame.Conformance under this name, and the null-skip half is
          // `aggregateNullSkipLaws`, in the kit (certified there).
          testCase "aggregateParityLaws certify single-source parity (Phase 36)"
          <| fun _ ->
              let results = Conformance.aggregateParityLaws 4242 200
              Expect.equal (List.length results) 1 "the parity law reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "aggregateParityLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.aggregateParityLaws 4242 200) results "same seed ⇒ identical report" ]
