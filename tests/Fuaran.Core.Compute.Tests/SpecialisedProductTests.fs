module Fuaran.Compute.Tests.SpecialisedProductTests

// ---------------------------------------------------------------------------
// Phase 730 — the specialised products of Fuaran.Compute.GeometricAlgebra
// (DECISIONS.md D89).
//
// What is held here:
//   * each specialised product's terms are the general table's, filtered, in
//     the general order, and it drops exactly the terms with a factor on a
//     blade its operand's type does not carry;
//   * each is the general product, bit for bit, the sign of every zero
//     included, on operands of its two types drawn in every regime — drawn
//     doubles, zeros of both signs, products that underflow to a zero,
//     subnormals, the largest finite doubles, and an infinity or a NaN on each
//     blade its types carry — at more seeds than the law family runs;
//   * each go-red witness, the shipped specialised product with ONE thing
//     changed, is red on that law: a fold seeded from a negative zero, where a
//     dropped term would have moved the sign of a zero; the terms in another
//     order; the guard on non-finite operands removed; a fused multiply-add;
//   * the typed functions that reach a specialised product are a stated list,
//     and under a wrong port of it each is red on one of its committed vectors.
//
// The wrong ports of ConformalTests and VersorTests replace the four products
// of the arithmetic with the general fold under a wrong rule, so under any of
// them no function reaches a specialised product, and under any other port the
// specialised product answers the general product's bits: their tables hold as
// they were.
//
// No case reads the clock: the benchmark is run by hand, and no timing is
// asserted.
// ---------------------------------------------------------------------------

open System
open Expecto
open Fuaran.Core
open Fuaran.Compute.GeometricAlgebra

module C = ConformalTests
module W = VersorTests

let private bits (x: float) : int64 = BitConverter.DoubleToInt64Bits x

let private shipped = Arithmetic.shipped

/// The specialised product with one thing changed: its start, its entry, the order of its terms, or
/// whether it reads the guard. The general product it falls back to is the recorded one.
let private witness
    (start: float)
    (enter: bool -> float -> float -> float -> float)
    (order: Term[] -> Term[])
    (guarded: bool)
    : Multivector -> Multivector -> Multivector =
    Kernel.specialisedUnder start enter order guarded Specialised.shapes

/// The four go-red witnesses D89 names.
let private witnesses: (string * (Multivector -> Multivector -> Multivector)) list =
    [ "a specialised fold seeded from a negative zero, where a dropped term would have moved the sign of a zero",
      witness -0.0 Kernel.twoRoundings id true
      "a specialised fold over its terms in another order", witness 0.0 Kernel.twoRoundings Array.rev true
      "a specialised fold without the guard on non-finite operands", witness 0.0 Kernel.twoRoundings id false
      "a specialised fold with a fused multiply-add", witness 0.0 C.fused id true ]

/// The specialised product that answers a NaN on every coefficient: it shows which functions reach
/// the specialised path, and on which committed vectors.
let private poison = witness Double.NaN Kernel.twoRoundings id true

/// A committed vector's answer under an arithmetic, through the conformal or the versor suite's
/// dispatch, as the published function computes it.
let private answerUnder (r: Arithmetic) (c: GeometricAlgebraVectors.Case) : CorpusCell option =
    if GeometricAlgebraVectors.isVersorOp c.Op then
        W.answerUnder r c
    else
        C.answerUnder r c

/// Every committed vector of a typed function.
let private typedVectors: GeometricAlgebraVectors.Case list =
    GeometricAlgebraVectors.cases |> List.filter (fun c -> c.Op.Contains ".")

/// The committed vectors an arithmetic answers otherwise, by function.
let private redUnder (r: Arithmetic) : Map<string, string list> =
    typedVectors
    |> List.choose (fun c ->
        match answerUnder r c with
        | Some cell when C.cellText cell <> C.committed.Value[c.Id] -> Some(c.Op, c.Id)
        | _ -> None)
    |> List.groupBy fst
    |> List.map (fun (op, reds) -> op, reds |> List.map snd)
    |> Map.ofList

/// The typed functions that reach a specialised product on a committed vector, as the poison shows
/// them: each forms a geometric product of a pair of types D89 keeps.
let private served: Set<string> =
    set
        [ "Motor.applyToPoint"
          "Motor.compose"
          "Motor.interpolate"
          "Motor.ofLine"
          "Motor.ofMatrix"
          "Motor.ofRotorTranslator"
          "Motor.ofScrew"
          "Rotor.applyToPoint"
          "Rotor.compose"
          "Rotor.interpolate"
          "Translator.applyToPoint" ]

/// For each witness, the served functions a committed vector of which is red under it. A served
/// function missing from a witness's set is one whose committed vectors that witness cannot reach: the
/// negative zero a seeded fold leaves survives only where every kept term of a coefficient is a zero,
/// and the guard matters only on a non-finite operand, which only one served function's vectors hold.
/// The equality law reaches both on drawn operands, and the other two witnesses are red on a vector of
/// every served function.
let private redByWitness: (string * Set<string>) list =
    [ "a specialised fold seeded from a negative zero, where a dropped term would have moved the sign of a zero",
      set
          [ "Motor.ofMatrix"
            "Motor.ofRotorTranslator"
            "Rotor.applyToPoint"
            "Rotor.interpolate" ]
      "a specialised fold over its terms in another order", served
      "a specialised fold without the guard on non-finite operands", set [ "Rotor.applyToPoint" ]
      "a specialised fold with a fused multiply-add", served ]

[<Tests>]
let tests =
    testList
        "Specialised products"
        [

          testCase "each specialised product's terms are the general table's whose blades its types carry, in its order"
          <| fun () ->
              Expect.equal
                  (Array.length Specialised.shapes)
                  9
                  "nine pairs of types: three products and three sandwiches"

              for shape in Specialised.shapes do
                  let kept (term: Term) =
                      Kernel.carries shape.Left term.Left && Kernel.carries shape.Right term.Right

                  // The same terms, the same records, in the same order: a subsequence of the general
                  // table that holds every term both of whose blades the types carry.
                  Expect.sequenceEqual shape.Terms (Basis.geometric |> Array.filter kept) "the filtered table"

                  let positions =
                      shape.Terms
                      |> Array.map (fun term -> Array.findIndex (fun t -> obj.ReferenceEquals(t, term)) Basis.geometric)

                  Expect.isTrue
                      (positions |> Array.pairwise |> Array.forall (fun (x, y) -> x < y))
                      "ascending in the general table"

                  Expect.isLessThan
                      shape.Terms.Length
                      256
                      "a specialised product drops at least three quarters of the terms"

                  Expect.equal
                      shape.Reached
                      (shape.Terms |> Array.fold (fun mask term -> mask ||| (1 <<< term.Out)) 0)
                      "its answer's type is the blades its terms reach"

          testCase "a typed value carries its type through the reverse, and a value of no type is never specialised"
          <| fun () ->
              let p =
                  Point.toMultivector
                      { N0 = 1.0
                        E1 = 2.0
                        E2 = 3.0
                        E3 = 4.0
                        NInf = 5.0 }

              Expect.equal p.Carried (Kernel.maskOf Indices.point) "a point carries the blades of a point"

              let m =
                  Motor.toMultivector (
                      Motor.ofRotorTranslator
                          { Scalar = 1.0
                            E1E2 = 0.0
                            E1E3 = 0.0
                            E2E3 = 0.0 }
                          { Scalar = 1.0
                            E1NInf = 0.5
                            E2NInf = 0.0
                            E3NInf = 0.0 }
                  )

              Expect.equal (Multivector.reverse m).Carried m.Carried "a motor's reverse is a motor"

              Expect.equal
                  (Multivector.init (fun _ -> 0.0)).Carried
                  Kernel.every
                  "a value built from numbers has no type"

              Expect.equal
                  (Specialised.geometric (Multivector.init (fun index -> Multivector.coefficient index m)) m).Carried
                  Kernel.every
                  "a product with a value of no type is the general product"

          testCase "an output every term of which is dropped is a positive zero, as the general product's is"
          <| fun () ->
              let shape = Specialised.shapes[0]
              let negativeZeros = Kernel.adoptAs shape.Left (Array.create Basis.count -0.0)
              let answer = Multivector.toArray (Specialised.geometric negativeZeros negativeZeros)

              Expect.equal (answer |> Array.map bits) (Array.create Basis.count (bits 0.0)) "every coefficient +0.0"

              Expect.isTrue
                  (answer
                   |> Array.mapi (fun index _ -> not (Kernel.carries shape.Reached index))
                   |> Array.exists id)
                  "some output has every term dropped"

          testCase "each specialised product is the general product, bit for bit, at five more seeds"
          <| fun () ->
              for seed in [ 1; 2; 730; 4242; 2147483646 ] do
                  Expect.equal
                      (GeometricAlgebraLaws.againstTheGeneralProductAt Specialised.geometric seed 300)
                      (Ok())
                      (sprintf "seed %d" seed)

          testCase "each go-red witness is red on the equality law"
          <| fun () ->
              Expect.equal (List.length witnesses) 4 "four witnesses"

              for name, product in witnesses do
                  Expect.isError (GeometricAlgebraLaws.againstTheGeneralProductAt product 730 50) name

          testCase "the witness seeded from a negative zero moves the sign of a zero that a dropped term restores"
          <| fun () ->
              // Two rotors whose coefficients are zeros: every term of every coefficient of their product
              // is a zero. The general fold adds the dropped ones to the accumulator, and a positive zero
              // term turns a negative zero positive; the seeded specialised fold has dropped them.
              let shape = Specialised.shapes[0]
              let zeros = Kernel.adoptAs shape.Left (Array.create Basis.count 0.0)
              let seeded, _ = List.head witnesses
              let product = snd (List.head witnesses)

              let general = Multivector.toArray (Multivector.geometricProduct zeros zeros)
              let answer = Multivector.toArray (product zeros zeros)

              Expect.stringContains seeded "negative zero" "the first witness is the seeded fold"
              Expect.equal (general |> Array.map bits) (Array.create Basis.count (bits 0.0)) "the general: +0.0"

              Expect.isTrue
                  (answer |> Array.exists (fun x -> bits x = bits -0.0))
                  "the seeded specialised fold leaves a -0.0"

          testCase "the typed functions that reach a specialised product are the eleven the record names"
          <| fun () ->
              let reached =
                  redUnder { shipped with Geometric = poison }
                  |> Map.toList
                  |> List.map fst
                  |> Set.ofList

              Expect.equal reached served "the functions a specialised product answering NaN moves"

          testCase "under a wrong port of the specialised product, each function it serves is red on a committed vector"
          <| fun () ->
              for name, product in witnesses do
                  let reds =
                      redUnder { shipped with Geometric = product }
                      |> Map.toList
                      |> List.map fst
                      |> Set.ofList

                  let expected = redByWitness |> List.find (fun (n, _) -> n = name) |> snd
                  Expect.equal reds expected name

              // Each served function is red under at least one wrong port of the specialised product.
              let covered = redByWitness |> List.map snd |> Set.unionMany
              Expect.equal covered served "every served function has a red vector"

          testCase "the claims ladder's row names this family's cases, word for word"
          <| fun () ->
              use ladder =
                  Text.Json.JsonDocument.Parse(IO.File.ReadAllText(Snapshots.repoFile "proofs.json"))

              let row =
                  ladder.RootElement.GetProperty("claims").EnumerateArray()
                  |> Seq.find (fun c ->
                      c.GetProperty("id").GetString() = "specialised-products-are-the-general-product")

              let evidence = row.GetProperty "evidence"
              Expect.equal (row.GetProperty("level").GetString()) "tested" "the row says tested, and no more"

              Expect.equal
                  (evidence.GetProperty("family").GetString())
                  "Specialised products"
                  "the row names this family"

              let cases =
                  [ for c in evidence.GetProperty("cases").EnumerateArray() -> c.GetString() ]

              Expect.isGreaterThan (List.length cases) 5 "the row lists its cases"

              let source =
                  IO.File.ReadAllText(
                      Snapshots.repoFile (
                          IO.Path.Combine("tests", "Fuaran.Core.Compute.Tests", "SpecialisedProductTests.fs")
                      )
                  )

              for case in cases do
                  Expect.stringContains
                      source
                      (sprintf "testCase \"%s\"" case)
                      (sprintf "the case \"%s\" is a test here" case) ]
