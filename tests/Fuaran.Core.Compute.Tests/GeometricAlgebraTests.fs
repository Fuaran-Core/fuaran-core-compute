module Fuaran.Compute.Tests.GeometricAlgebraTests

// ---------------------------------------------------------------------------
// Phase 727 — Fuaran.Compute.GeometricAlgebra: the laws on .NET, the committed
// law vectors, the term tables held to the digests DECISIONS.md D85 pins, the
// blade table held to the record as written, what was worked by hand, the
// operation table held to the public surface, the sources held to no host
// mathematics call, and each go-red witness (DECISIONS.md D86).
//
// A witness is the shipped kernel with exactly one rule changed: the order of
// the terms, how a term enters its accumulator, where a fold starts, one sign
// of the table, how a coefficient is negated. It is never a second
// implementation: the kernels take their rule as an argument for this, and a
// perturbed run goes through the same canonicalisation and the same rendering
// as the shipped one. Each witness is shown red on a named vector of the
// committed file, beside the shipped rule shown green on the same vector.
//
// The node leg (LinearAlgebraTests) runs the same laws and renders the same
// document under JavaScript.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Text
open Expecto
open Fuaran.Core
open Fuaran.Compute.GeometricAlgebra

let private committedPath () =
    Snapshots.repoFile (Path.Combine("conformance", "laws", GeometricAlgebraVectors.fileName))

/// The committed document's line for the vector with this id.
let private committedLine (id: string) : string =
    File.ReadAllText(committedPath ()).Split('\n')
    |> Array.find (fun l -> l.Contains(sprintf "\"id\": \"%s\"" id))

/// A multivector answer as the document writes it.
let private expectedList (hexes: string list) : string =
    hexes
    |> List.map (sprintf "\"%s\"")
    |> String.concat ", "
    |> sprintf "\"expected\": [%s]"

/// A float answer as the document writes it.
let private expectedFloat (x: float) : string =
    sprintf "\"expected\": \"%s\"" (ElementaryVectors.hex x)

let private h = ElementaryVectors.hex

let private operands (id: string) : Multivector * Multivector =
    let c = GeometricAlgebraVectors.find id

    GeometricAlgebraVectors.multivector (GeometricAlgebraVectors.operand c "a"),
    GeometricAlgebraVectors.multivector (GeometricAlgebraVectors.operand c "b")

let private operand (id: string) : Multivector =
    let c = GeometricAlgebraVectors.find id
    GeometricAlgebraVectors.multivector (GeometricAlgebraVectors.operand c "a")

/// The three products, by the name their vectors carry and the terms each folds over.
let private products: (string * Term[]) list =
    [ "geometric", Basis.geometric
      "outer", Basis.outer
      "contraction", Basis.contraction ]

/// The terms a product vector's function folds over, or `None` for any other vector.
let private termsOf (c: GeometricAlgebraVectors.Case) : Term[] option =
    match c.Op with
    | "geometricProduct" -> Some Basis.geometric
    | "outerProduct" -> Some Basis.outer
    | "leftContraction" -> Some Basis.contraction
    | _ -> None

/// `expected` holds on the committed line of `id` for the shipped answer and not for the perturbed
/// one: the witness is red on that vector, and the vector is not vacuous.
let private redOn (id: string) (what: string) (shipped: string) (perturbed: string) =
    let line = committedLine id
    Expect.stringContains line shipped (sprintf "%s: the shipped rule answers the committed vector" id)
    Expect.isFalse (line.Contains perturbed) (sprintf "%s: %s answers the committed vector too" id what)

/// A product witness on a named vector: the shipped fold against the fold with one rule changed.
let private productRedOn
    (id: string)
    (what: string)
    (terms: Term[])
    (perturbed: Multivector -> Multivector -> Multivector)
    =
    let a, b = operands id
    let shipped = Kernel.productUnder 0.0 Kernel.twoRoundings terms a b

    redOn
        id
        what
        (expectedList (GeometricAlgebraVectors.hexes shipped))
        (expectedList (GeometricAlgebraVectors.hexes (perturbed a b)))

/// The same for the scalar product.
let private scalarRedOn (id: string) (what: string) (perturbed: float[] -> float[] -> float) =
    let a, b = operands id
    let xs = Multivector.toArray a
    let ys = Multivector.toArray b
    let shipped = Kernel.scalarUnder 0.0 Kernel.twoRoundings Basis.scalar xs ys
    redOn id what (expectedFloat shipped) (expectedFloat (perturbed xs ys))

/// A fused multiply-add where the record asks for two roundings.
let private fused (negative: bool) (r: float) (x: float) (y: float) : float =
    if negative then
        Math.FusedMultiplyAdd(-x, y, r)
    else
        Math.FusedMultiplyAdd(x, y, r)

/// A term left out because one of its coefficients is a zero.
let private skippingZeros (negative: bool) (r: float) (x: float) (y: float) : float =
    if x = 0.0 || y = 0.0 then
        r
    else
        Kernel.twoRoundings negative r x y

/// The table as item 3 says it is written: one line `i j k s` for each term, `s` written `+` or `-`,
/// single spaces, a line feed after each line whatever the platform, ASCII.
let private renderTable (terms: Term[]) : byte[] =
    terms
    |> Array.map (fun t -> sprintf "%d %d %d %s\n" t.Left t.Right t.Out (if t.Negative then "-" else "+"))
    |> String.concat ""
    |> Encoding.ASCII.GetBytes

let private sha256 (bytes: byte[]) : string =
    Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

/// DECISIONS.md D85 item 2's table as written: index, blade, grade, `rev`, `inv`, and the dual's sign
/// and target. A blade is its basis vectors in order, `0` for `n0`, `1` to `3` for `e1` to `e3` and `4`
/// for `n∞`; a sign is `+` or `-`.
let private recordedBlades: (int * int list * int * char * char * char * int) list =
    [ 0, [], 0, '+', '+', '-', 31
      1, [ 0 ], 1, '+', '-', '+', 26
      2, [ 1 ], 1, '+', '-', '+', 29
      3, [ 2 ], 1, '+', '-', '-', 28
      4, [ 3 ], 1, '+', '-', '+', 27
      5, [ 4 ], 1, '+', '-', '+', 30
      6, [ 0; 1 ], 2, '-', '+', '+', 19
      7, [ 0; 2 ], 2, '-', '+', '-', 17
      8, [ 0; 3 ], 2, '-', '+', '+', 16
      9, [ 0; 4 ], 2, '-', '+', '+', 22
      10, [ 1; 2 ], 2, '-', '+', '+', 21
      11, [ 1; 3 ], 2, '-', '+', '-', 20
      12, [ 1; 4 ], 2, '-', '+', '+', 25
      13, [ 2; 3 ], 2, '-', '+', '+', 18
      14, [ 2; 4 ], 2, '-', '+', '-', 24
      15, [ 3; 4 ], 2, '-', '+', '+', 23
      16, [ 0; 1; 2 ], 3, '-', '-', '-', 8
      17, [ 0; 1; 3 ], 3, '-', '-', '+', 7
      18, [ 0; 1; 4 ], 3, '-', '-', '-', 13
      19, [ 0; 2; 3 ], 3, '-', '-', '-', 6
      20, [ 0; 2; 4 ], 3, '-', '-', '+', 11
      21, [ 0; 3; 4 ], 3, '-', '-', '-', 10
      22, [ 1; 2; 3 ], 3, '-', '-', '-', 9
      23, [ 1; 2; 4 ], 3, '-', '-', '-', 15
      24, [ 1; 3; 4 ], 3, '-', '-', '+', 14
      25, [ 2; 3; 4 ], 3, '-', '-', '-', 12
      26, [ 0; 1; 2; 3 ], 4, '+', '+', '-', 1
      27, [ 0; 1; 2; 4 ], 4, '+', '+', '-', 4
      28, [ 0; 1; 3; 4 ], 4, '+', '+', '+', 3
      29, [ 0; 2; 3; 4 ], 4, '+', '+', '-', 2
      30, [ 1; 2; 3; 4 ], 4, '+', '+', '-', 5
      31, [ 0; 1; 2; 3; 4 ], 5, '+', '-', '+', 0 ]

/// The scalar coefficient of `A B` as D85 item 3 writes it out: the left index, the right index and
/// the sign of each of its thirty-two terms, in order.
let private recordedScalarTerms: (int * int * char) list =
    [ 0, 0, '+'
      1, 5, '-'
      2, 2, '+'
      3, 3, '+'
      4, 4, '+'
      5, 1, '-'
      6, 12, '-'
      7, 14, '-'
      8, 15, '-'
      9, 9, '+'
      10, 10, '-'
      11, 11, '-'
      12, 6, '-'
      13, 13, '-'
      14, 7, '-'
      15, 8, '-'
      16, 23, '+'
      17, 24, '+'
      18, 18, '+'
      19, 25, '+'
      20, 20, '+'
      21, 21, '+'
      22, 22, '-'
      23, 16, '+'
      24, 17, '+'
      25, 19, '+'
      26, 30, '+'
      27, 27, '-'
      28, 28, '-'
      29, 29, '-'
      30, 26, '+'
      31, 31, '-' ]

let private sign (negative: bool) : char = if negative then '-' else '+'

let private algebraAssembly = typeof<Multivector>.Assembly

/// A module's source name: the compiler suffixes a module that shares its name with a type.
let private moduleName (t: Type) : string =
    let n = t.Name.Split('`').[0]

    if n.EndsWith "Module" then
        n.Substring(0, n.Length - "Module".Length)
    else
        n

/// The public functions of the package's operation modules, spelled `<Module>.<function>` — the census
/// `GeometricAlgebraOperations.exactness` is held to.
let private publishedOperations () : Set<string> =
    algebraAssembly.GetExportedTypes()
    |> Array.filter (fun t -> t.IsAbstract && t.IsSealed && not t.IsNested)
    |> Array.collect (fun t ->
        if List.contains (moduleName t) [ "Multivector" ] then
            t.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
            |> Array.filter (fun m -> not m.IsSpecialName)
            |> Array.map (fun m -> moduleName t + "." + m.Name)
        else
            [||])
    |> Set.ofArray

[<Tests>]
let tests =
    testList
        "Geometric algebra"
        [

          testCase "the laws hold on .NET"
          <| fun () ->
              let failures =
                  GeometricAlgebraLaws.laws 590 200
                  |> List.choose (fun (name, verdict) ->
                      match verdict with
                      | Ok() -> None
                      | Error why -> Some(name + ": " + why))

              Expect.isEmpty failures "every law of the geometric algebra holds"
              Expect.equal (GeometricAlgebraLaws.laws 590 1 |> List.length) 18 "eighteen laws are run"

          testCase "the committed law vectors are the package's answers"
          <| fun () ->
              let committed = File.ReadAllText(committedPath ())

              match CorpusFile.firstDifference committed (GeometricAlgebraVectors.render ()) with
              | None -> ()
              | Some d ->
                  failtestf
                      "%s is stale at line %d:\n  committed: %A\n  rendered:  %A\n  Remedy: dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws"
                      GeometricAlgebraVectors.fileName
                      d.Line
                      d.Committed
                      d.Rendered

          testCase "the three term tables render to the digests the record pins"
          <| fun () ->
              // DECISIONS.md D85 item 3: the term counts, the byte counts and the SHA-256 of each
              // rendering. The table is built by the item's rule, in code; this holds the build to the
              // record.
              let pinned =
                  [ "geometric", 1024, 10304, "29b9805b10126723149f83a56a8d75f1e0755ea4992a690c828fb987383d43d8"
                    "outer", 243, 2358, "794d5f82a81b29af115be4ef5ddb3f97c2444d61ac11ac7de9fc363896432af2"
                    "contraction", 243, 2358, "c564f7bc6b5a0bae494c2ceaada6d2ba1a87b474eb4eb615c2ebea2e75276453" ]

              for name, terms, bytes, digest in pinned do
                  let table = products |> List.find (fun (n, _) -> n = name) |> snd
                  let rendered = renderTable table
                  Expect.equal table.Length terms (sprintf "the %s table's term count" name)
                  Expect.equal rendered.Length bytes (sprintf "the %s table's rendering, in bytes" name)
                  Expect.equal (sha256 rendered) digest (sprintf "the %s table's SHA-256" name)

              Expect.isFalse
                  (renderTable Basis.geometric |> Array.contains (byte '\r'))
                  "the rendering ends a line with a line feed alone"

              let sorted (terms: Term[]) =
                  terms |> Array.sortBy (fun t -> t.Left, t.Right, t.Out)

              Expect.equal Basis.geometric (sorted Basis.geometric) "the terms are ascending in (i, j, k)"

              let flipped = Array.copy Basis.geometric

              flipped[517] <-
                  { flipped[517] with
                      Negative = not flipped[517].Negative }

              Expect.notEqual
                  (sha256 (renderTable flipped))
                  (sha256 (renderTable Basis.geometric))
                  "one sign of one term moves the digest"

          testCase "the blades, their signs and the dual are item 2's table as written"
          <| fun () ->
              Expect.equal Basis.blades.Length 32 "thirty-two blades"
              Expect.equal (List.length recordedBlades) 32 "the record's table is transcribed whole"

              for index, blade, grade, rev, inv, dualSign, dualTarget in recordedBlades do
                  Expect.equal Basis.blades[index] blade (sprintf "blade %d is the record's" index)
                  Expect.equal Basis.grade[index] grade (sprintf "blade %d: grade" index)
                  Expect.equal (sign Basis.reverseNegative[index]) rev (sprintf "blade %d: rev" index)
                  Expect.equal (sign Basis.involutionNegative[index]) inv (sprintf "blade %d: inv" index)
                  Expect.equal Basis.dualTarget[index] dualTarget (sprintf "blade %d: where the dual sends it" index)
                  Expect.equal (sign Basis.dualNegative[index]) dualSign (sprintf "blade %d: the dual's sign" index)

              Expect.equal
                  (Basis.grade |> Array.countBy id |> List.ofArray)
                  [ 0, 1; 1, 5; 2, 10; 3, 10; 4, 5; 5, 1 ]
                  "each grade is one contiguous run: 0, 1-5, 6-15, 16-25, 26-30, 31"

              // The undual is the inverse permutation with the same signs.
              for index in 0..31 do
                  Expect.equal Basis.undualTarget[Basis.dualTarget[index]] index "the undual sends a dual back"

                  Expect.equal
                      Basis.undualNegative[Basis.dualTarget[index]]
                      Basis.dualNegative[index]
                      "with the sign the dual gave it"

          testCase "the scalar coefficient's terms are the ones the record writes out"
          <| fun () ->
              Expect.equal
                  (Basis.scalar
                   |> Array.map (fun t -> t.Left, t.Right, sign t.Negative)
                   |> List.ofArray)
                  recordedScalarTerms
                  "the scalar coefficient of A B, term by term in order"

              // "The coefficient of n0 begins +a0·b1 +a1·b0 +a1·b9 −a2·b6": a left blade can meet two
              // right blades for one output, the right index ascending.
              Expect.equal
                  (Basis.geometric
                   |> Array.filter (fun t -> t.Out = 1)
                   |> Array.truncate 4
                   |> Array.map (fun t -> t.Left, t.Right, sign t.Negative)
                   |> List.ofArray)
                  [ 0, 1, '+'; 1, 0, '+'; 1, 9, '+'; 2, 6, '-' ]
                  "the coefficient of n0, its first four terms"

          testCase "the hard inputs answer what was worked by hand"
          <| fun () ->
              let has (id: string) (fragment: string) =
                  Expect.stringContains (committedLine id) fragment (sprintf "%s answers %s" id fragment)

              let zeros = List.replicate 32 (h 0.0)
              let withAt (index: int) (x: float) (xs: string list) = List.updateAt index (h x) xs
              let p60 = List.fold (fun p _ -> p * 2.0) 1.0 [ 1..60 ]

              // 1 times B is B: the powers of two themselves.
              has
                  "table-geometric-00"
                  (expectedList [ for j in 0..31 -> h (List.fold (fun p _ -> p * 2.0) 1.0 [ 1..j ]) ])
              // n0 n0 = 0 and n0 n∞ = −1 + E: against the powers of two, the scalar is −2^5 and the
              // coefficient of n0 is 2^0 + 2^9.
              has "table-geometric-01" (sprintf "\"expected\": [\"%s\", \"%s\", " (h -32.0) (h 513.0))
              // ((2^60 + 1) − 2^60) + 1 = 1: the first 1 is absorbed.
              has "scalar-absorbed-then-cancelled" (expectedFloat 1.0)
              has "geometric-absorbed-then-cancelled" (sprintf "\"expected\": [\"%s\", " (h 1.0))
              // Two roundings, not one: the fused answer would be −2^-60.
              has "scalar-fma-witness" (expectedFloat 0.0)
              has "geometric-fma-witness" (sprintf "\"expected\": [\"%s\", " (h 0.0))
              // A product whose every term is a negative zero answers +0.0.
              has "scalar-every-term-a-negative-zero" (expectedFloat 0.0)
              has "geometric-every-scalar-term-a-negative-zero" (expectedList zeros)
              // A zero against an infinity is a NaN, and the product says so.
              has
                  "geometric-zero-against-infinity"
                  (expectedList (List.replicate 32 "nan" |> withAt 2 Double.PositiveInfinity))

              has "scalar-zero-against-infinity" "\"expected\": \"nan\""
              has "squaredNorm-zero-against-infinity" "\"expected\": \"nan\""
              // The squared norm is signed: ½ n∞ + n0 squares to −1, and a null vector to zero.
              has "squaredNorm-negative-signature" (expectedFloat -1.0)
              has "squaredNorm-null-vector" (expectedFloat 0.0)
              // Exact negation gives a positive zero its sign: the reverse negates grades 2 and 3, and
              // the dual sixteen blades.
              has
                  "reverse-positive-zeros"
                  (expectedList [ for index in 0..31 -> if index >= 6 && index <= 25 then h -0.0 else h 0.0 ])

              has
                  "dual-positive-zeros"
                  (expectedList
                      [ for target in 0..31 ->
                            let _, _, _, _, _, dualSign, _ =
                                recordedBlades |> List.find (fun (_, _, _, _, _, _, t) -> t = target)

                            if dualSign = '-' then h -0.0 else h 0.0 ])

              has "negate-zeros" (expectedList [ for index in 0..31 -> if index % 2 = 0 then h -0.0 else h 0.0 ])
              // A grade projection copies its run, a negative zero included, and writes +0.0 elsewhere.
              has
                  "grade-2-of-negative-zeros"
                  (expectedList [ for index in 0..31 -> if index >= 6 && index <= 15 then h -0.0 else h 0.0 ])

              has "grade-6-general" (expectedList zeros)
              has "grade-below-zero-general" (expectedList zeros)
              // One rounded operation each: x + (−x) and x − x are +0.0, and −0.0 + −0.0 is −0.0.
              has "add-opposites" (expectedList zeros)
              has "subtract-itself" (expectedList zeros)

              has
                  "add-zeros-of-both-signs"
                  (expectedList [ for index in 0..31 -> if index % 2 = 0 then h 0.0 else h -0.0 ])

              has "scale-zeros-by-infinity" (expectedList (List.replicate 32 "nan"))
              has "ofScalar-nan" (expectedList (zeros |> List.updateAt 0 "nan"))
              has "basis-09" (expectedList (zeros |> withAt 9 1.0))
              Expect.equal (h p60) "43b0000000000000" "2^60, the absorbing term"

          // ---- the go-red witnesses: the shipped kernel with one rule changed ----------------------

          testCase "a fold in another order of summation fails a vector"
          <| fun () ->
              let inOrder (terms: Term[]) (a: Multivector) (b: Multivector) =
                  Kernel.productUnder 0.0 Kernel.twoRoundings terms a b

              let rightIndexOutermost =
                  Basis.geometric |> Array.sortBy (fun t -> t.Right, t.Left, t.Out)

              let descending = Array.rev Basis.geometric

              for what, order in
                  [ "the right operand's index outermost", rightIndexOutermost
                    "the terms descending", descending ] do
                  Expect.notEqual order Basis.geometric (sprintf "%s is another order" what)

                  for id in
                      [ "geometric-absorbed-then-cancelled"
                        "geometric-general-3"
                        "geometric-general-4" ] do
                      productRedOn id what Basis.geometric (inOrder order)

              // The scalar product, by the same reordering of its own terms.
              scalarRedOn "scalar-absorbed-then-cancelled" "the terms descending" (fun xs ys ->
                  Kernel.scalarUnder 0.0 Kernel.twoRoundings (Array.rev Basis.scalar) xs ys)

              scalarRedOn "scalar-general-2" "the terms descending" (fun xs ys ->
                  Kernel.scalarUnder 0.0 Kernel.twoRoundings (Array.rev Basis.scalar) xs ys)

              // What is NOT observable (D85 item 3): gathering one output at a time. For one output the
              // terms arrive in the same order either way, so every vector answers the same bits.
              let gathered (terms: Term[]) =
                  terms |> Array.sortBy (fun t -> t.Out, t.Left, t.Right)

              Expect.notEqual (gathered Basis.geometric) Basis.geometric "gathering is another loop"

              for c in GeometricAlgebraVectors.cases do
                  match termsOf c with
                  | Some terms ->
                      let a, b = operands c.Id

                      Expect.equal
                          (GeometricAlgebraVectors.hexes (inOrder (gathered terms) a b))
                          (GeometricAlgebraVectors.hexes (inOrder terms a b))
                          (sprintf "%s: one output at a time answers the same bits" c.Id)
                  | None -> ()

          testCase "a fused multiply-add fails a vector"
          <| fun () ->
              productRedOn "geometric-fma-witness" "a fused multiply-add" Basis.geometric (fun a b ->
                  Kernel.productUnder 0.0 fused Basis.geometric a b)

              productRedOn "geometric-general-1" "a fused multiply-add" Basis.geometric (fun a b ->
                  Kernel.productUnder 0.0 fused Basis.geometric a b)

              productRedOn "outer-general-1" "a fused multiply-add" Basis.outer (fun a b ->
                  Kernel.productUnder 0.0 fused Basis.outer a b)

              productRedOn "contraction-general-1" "a fused multiply-add" Basis.contraction (fun a b ->
                  Kernel.productUnder 0.0 fused Basis.contraction a b)

              scalarRedOn "scalar-fma-witness" "a fused multiply-add" (fun xs ys ->
                  Kernel.scalarUnder 0.0 fused Basis.scalar xs ys)

              // The fused answer is the one worked by hand: −2^-60 where two roundings answer +0.0.
              let a, b = operands "scalar-fma-witness"

              Expect.equal
                  (h (Kernel.scalarUnder 0.0 fused Basis.scalar (Multivector.toArray a) (Multivector.toArray b)))
                  (h -(List.fold (fun p _ -> p / 2.0) 1.0 [ 1..60 ]))
                  "the fused scalar is −2^-60"

          testCase "a fold seeded from its first term fails a vector"
          <| fun () ->
              // An accumulator that starts at −0.0 is a fold seeded from its first term, exactly:
              // −0.0 + p is p and −0.0 − p is −p for every p, a zero of either sign included.
              productRedOn
                  "geometric-every-scalar-term-a-negative-zero"
                  "a fold seeded from its first term"
                  Basis.geometric
                  (fun a b -> Kernel.productUnder -0.0 Kernel.twoRoundings Basis.geometric a b)

              scalarRedOn "scalar-every-term-a-negative-zero" "a fold seeded from its first term" (fun xs ys ->
                  Kernel.scalarUnder -0.0 Kernel.twoRoundings Basis.scalar xs ys)

              let a, b = operands "scalar-every-term-a-negative-zero"

              Expect.equal
                  (h (
                      Kernel.scalarUnder
                          -0.0
                          Kernel.twoRoundings
                          Basis.scalar
                          (Multivector.toArray a)
                          (Multivector.toArray b)
                  ))
                  (h -0.0)
                  "the seeded fold answers the negative zero the record rules out"

          testCase "a term skipped on a zero coefficient fails a vector, and over finite operands moves no bit"
          <| fun () ->
              for short, terms in products do
                  productRedOn (short + "-zero-against-infinity") "a fold that skips a zero's term" terms (fun a b ->
                      Kernel.productUnder 0.0 skippingZeros terms a b)

              scalarRedOn "scalar-zero-against-infinity" "a fold that skips a zero's term" (fun xs ys ->
                  Kernel.scalarUnder 0.0 skippingZeros Basis.scalar xs ys)

              // D85 item 3's second statement, which a specialised product stands on: over FINITE
              // operands, leaving out a term one of whose factors is a zero cannot move a bit.
              let finite (a: Multivector) =
                  Multivector.toArray a |> Array.forall Double.IsFinite

              let mutable held = 0

              for c in GeometricAlgebraVectors.cases do
                  match termsOf c with
                  | Some terms ->
                      let a, b = operands c.Id

                      if finite a && finite b then
                          held <- held + 1

                          Expect.equal
                              (GeometricAlgebraVectors.hexes (Kernel.productUnder 0.0 skippingZeros terms a b))
                              (GeometricAlgebraVectors.hexes (Kernel.productUnder 0.0 Kernel.twoRoundings terms a b))
                              (sprintf "%s: skipping a zero's term over finite operands answers the same bits" c.Id)
                  | None -> ()

              Expect.isGreaterThan held 100 "the statement was held over the finite product vectors"

          testCase "one wrong sign, one missing term or one term too many in a table fails its row's vector"
          <| fun () ->
              let powers = GeometricAlgebraVectors.multivector GeometricAlgebraVectors.PowersOfTwo

              let row (terms: Term[]) (left: int) =
                  GeometricAlgebraVectors.hexes (
                      Kernel.productUnder 0.0 Kernel.twoRoundings terms (Multivector.basis left) powers
                  )

              let mutable runs = 0

              for short, terms in products do
                  let shipped = Array.init 32 (row terms)

                  for left in 0..31 do
                      Expect.stringContains
                          (committedLine (sprintf "table-%s-%02d" short left))
                          (expectedList shipped[left])
                          "the shipped table answers the committed row"

                  let isRed (perturbed: Term[]) (left: int) = row perturbed left <> shipped[left]

                  // Every sign, flipped alone.
                  for at in 0 .. terms.Length - 1 do
                      let perturbed = Array.copy terms

                      perturbed[at] <-
                          { terms[at] with
                              Negative = not terms[at].Negative }

                      runs <- runs + 1

                      if not (isRed perturbed terms[at].Left) then
                          failtestf "%s: term %d with its sign flipped still answers its row" short at

                  // Every term, left out alone.
                  for at in 0 .. terms.Length - 1 do
                      runs <- runs + 1

                      if not (isRed (Array.removeAt at terms) terms[at].Left) then
                          failtestf "%s: the table without term %d still answers its row" short at

                  // Every term of the geometric product a selection does not keep, kept.
                  for extra in Basis.geometric do
                      if not (Array.contains extra terms) then
                          runs <- runs + 1

                          if not (isRed (Array.append terms [| extra |]) extra.Left) then
                              failtestf "%s: the table with a term it does not keep still answers its row" short

              Expect.equal runs (2 * (1024 + 243 + 243) + 2 * (1024 - 243)) "every perturbation was run"

          testCase "a negation that loses a zero's sign fails a vector"
          <| fun () ->
              // `0.0 − x` is `−x` everywhere but at `+0.0`, where it answers `+0.0`.
              let lossy (x: float) : float = 0.0 - x

              let witness (id: string) (target: int[]) (negative: bool[]) =
                  let a = operand id

                  redOn
                      id
                      "a negation by subtraction from zero"
                      (expectedList (
                          GeometricAlgebraVectors.hexes (Kernel.permutedUnder Kernel.exactNegation target negative a)
                      ))
                      (expectedList (GeometricAlgebraVectors.hexes (Kernel.permutedUnder lossy target negative a)))

              witness "negate-zeros" Basis.identity Kernel.everyBlade
              witness "reverse-zeros" Basis.identity Basis.reverseNegative
              witness "reverse-positive-zeros" Basis.identity Basis.reverseNegative
              witness "gradeInvolution-zeros" Basis.identity Basis.involutionNegative
              witness "dual-zeros" Basis.dualTarget Basis.dualNegative
              witness "dual-positive-zeros" Basis.dualTarget Basis.dualNegative
              witness "undual-zeros" Basis.undualTarget Basis.undualNegative

          // ---- the type ---------------------------------------------------------------------------

          testCase "Equals and GetHashCode are the float identity, as written"
          <| fun () ->
              let values = [| for index in 0..31 -> float (index - 7) / 4.0 |]

              let of' (xs: float[]) =
                  Multivector.init (fun index -> xs[index])

              let a = of' values

              let nan () =
                  of' (Array.mapi (fun i x -> if i = 5 then Double.NaN else x) values)

              let negativeZero = of' (Array.mapi (fun i x -> if i = 7 then -0.0 else x) values)

              Expect.isTrue (a.Equals(of' values)) "equal coefficients are equal"
              Expect.isTrue (nan().Equals(nan ())) "a NaN equals a NaN at the same index"
              Expect.isTrue (a.Equals negativeZero) "the two zeros are one value"
              Expect.isFalse (a.Equals(nan ())) "a NaN equals no number"
              Expect.isFalse (a.Equals(box values)) "a multivector equals nothing that is not one"
              Expect.isTrue (a = negativeZero) "a caller's = answers the same identity"
              Expect.equal (hash a) (hash negativeZero) "the two zeros hash alike"
              Expect.equal (hash (nan ())) (hash (nan ())) "a NaN hashes as one value"

              // D85 item 8's hash, as written: from 32, each coefficient in index order as h × 31 + k.
              let written (xs: float[]) =
                  xs
                  |> Array.fold
                      (fun acc x ->
                          let k =
                              if Double.IsNaN x then 0x7FC00000
                              elif x = 0.0 then 0
                              else hash x

                          acc * 31 + k)
                      32

              Expect.equal (a.GetHashCode()) (written values) "the hash is item 8's"
              Expect.equal (nan().GetHashCode()) (written (Multivector.toArray (nan ()))) "with a NaN"

              let keyed =
                  System.Collections.Generic.HashSet<Multivector>([ a; negativeZero; nan (); nan () ])

              Expect.equal keyed.Count 2 "a hashed collection holds each value once"

          testCase "construction and read-back are total but for a blade index, which is an argument"
          <| fun () ->
              let mutable order = []

              let a =
                  Multivector.init (fun index ->
                      order <- index :: order
                      float index)

              Expect.equal (List.rev order) [ 0..31 ] "init calls its function once for each index, ascending"
              let read = Multivector.toArray a
              read[3] <- 99.0
              Expect.equal (Multivector.coefficient 3 a) 3.0 "toArray hands out a fresh array, never the storage"

              Expect.equal
                  (Multivector.toArray Multivector.zero |> Array.map h |> Array.distinct)
                  [| h 0.0 |]
                  "zero is +0.0s"

              for index in [ -1; 32 ] do
                  Expect.throwsT<IndexOutOfRangeException>
                      (fun () -> Multivector.basis index |> ignore)
                      "a blade index outside 0..31"

                  Expect.throwsT<IndexOutOfRangeException>
                      (fun () -> Multivector.coefficient index a |> ignore)
                      "a coefficient index outside 0..31"

              let canonicalBits = BitConverter.DoubleToInt64Bits Double.NaN
              let other = BitConverter.Int64BitsToDouble 0x7FF8000000000001L
              Expect.notEqual (BitConverter.DoubleToInt64Bits other) canonicalBits "another NaN's bits"

              Expect.equal
                  (BitConverter.DoubleToInt64Bits(Multivector.coefficient 0 (Multivector.ofScalar other)))
                  canonicalBits
                  "a NaN enters as Double.NaN's bits"

              Expect.equal
                  (BitConverter.DoubleToInt64Bits(
                      Multivector.coefficient 0 (Multivector.negate (Multivector.ofScalar Double.NaN))
                  ))
                  canonicalBits
                  "a negated NaN leaves as Double.NaN's bits"

          // ---- the package ------------------------------------------------------------------------

          testCase "the package's sources reach no host mathematics call"
          <| fun () ->
              let source =
                  [ "Basis.fs"
                    "Multivector.fs"
                    "GeometricAlgebraOperations.fs"
                    "GeometricAlgebraLaws.fs" ]
                  |> List.map (fun file ->
                      File.ReadAllText(
                          Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.GeometricAlgebra", file))
                      ))
                  |> String.concat "\n"

              let hostCall =
                  Text.RegularExpressions.Regex(
                      @"Math\.\w+|(?<![\w.])(sqrt|exp|log|log10|log2|pown|floor|ceil|round|truncate|abs|sin|cos|tan|sinh|cosh|tanh|atan|atan2|asin|acos)\s+[\w(]|\*\*"
                  )

              let code =
                  source.Split('\n')
                  |> Array.filter (fun l -> not (l.TrimStart().StartsWith "//"))

              Expect.isGreaterThan code.Length 300 "the sources were read"

              let hits =
                  code
                  |> Array.collect (fun l -> [| for m in hostCall.Matches l -> m.Value |])
                  |> Array.distinct
                  |> List.ofArray

              Expect.isEmpty hits "the algebra is + − × and exact negation (DECISIONS.md D85 item 7)"

              // The rule is not vacuous: it reads a fused multiply-add, a root and a power where there is one.
              Expect.equal
                  ([ "Math.FusedMultiplyAdd(x, y, r)"; "sqrt x"; "x ** 2.0"; "abs (x - y)" ]
                   |> List.filter hostCall.IsMatch
                   |> List.length)
                  4
                  "the rule reads each kind of host call"

          testCase "GeometricAlgebraOperations.exactness classifies exactly the published operations, every one Pinned"
          <| fun () ->
              let declared = GeometricAlgebraOperations.exactness |> List.map fst
              Expect.equal (List.distinct declared) declared "no operation is classified twice"
              let published = publishedOperations ()
              Expect.isNonEmpty (Set.toList published) "the census found the operation module"
              Expect.equal (Set.ofList declared) published "the table is the published operation set"

              Expect.isTrue
                  (GeometricAlgebraOperations.exactness
                   |> List.forall (fun (_, e) -> e = GeometricAlgebraOperations.Exactness.Pinned))
                  "every operation here is Pinned (DECISIONS.md D85 item 7)"

              Expect.equal
                  (GeometricAlgebraOperations.exactnessOf "Multivector.geometricProduct")
                  (Some GeometricAlgebraOperations.Exactness.Pinned)
                  "a lookup"

              Expect.equal (GeometricAlgebraOperations.exactnessOf "Multivector.inverse") None "no such operation"

              // Every operation that computes has a vector in the committed file.
              let vectored =
                  GeometricAlgebraVectors.cases
                  |> List.map (fun c -> "Multivector." + c.Op)
                  |> Set.ofList

              Expect.equal
                  (Set.difference published vectored)
                  (set [ "Multivector.init"; "Multivector.toArray" ])
                  "every published operation has a vector but the two every vector is read through"

          testCase "the claims ladder's row names this family's cases, word for word"
          <| fun () ->
              // The row that says the algebra is tested (proofs.json) lists the cases that test it. A case
              // renamed here and not there would leave the ladder citing a test that does not exist.
              use ladder =
                  Text.Json.JsonDocument.Parse(File.ReadAllText(Snapshots.repoFile "proofs.json"))

              let row =
                  ladder.RootElement.GetProperty("claims").EnumerateArray()
                  |> Seq.find (fun c -> c.GetProperty("id").GetString() = "geometric-algebra-pinned-by-recorded-rules")

              let evidence = row.GetProperty "evidence"
              Expect.equal (row.GetProperty("level").GetString()) "tested" "the row says tested, and no more"
              Expect.equal (evidence.GetProperty("family").GetString()) "Geometric algebra" "the row names this family"

              let cases =
                  [ for c in evidence.GetProperty("cases").EnumerateArray() -> c.GetString() ]

              Expect.isGreaterThan (List.length cases) 5 "the row lists its cases"

              let source =
                  File.ReadAllText(
                      Snapshots.repoFile (
                          Path.Combine("tests", "Fuaran.Core.Compute.Tests", "GeometricAlgebraTests.fs")
                      )
                  )

              for case in cases do
                  Expect.stringContains
                      source
                      (sprintf "testCase \"%s\"" case)
                      (sprintf "the case \"%s\" is a test here" case)

          testCase "the package publishes no operator, and no name another Compute package declares"
          <| fun () ->
              let exported = algebraAssembly.GetExportedTypes()

              let operators =
                  [ for t in exported do
                        for m in t.GetMethods(BindingFlags.Public ||| BindingFlags.Static) do
                            if m.Name.StartsWith "op_" then
                                yield t.Name + "." + m.Name ]

              Expect.isEmpty operators "a product is a named function (DECISIONS.md D85 item 8)"

              let mine =
                  exported
                  |> Array.filter (fun t -> not t.IsNested)
                  |> Array.map moduleName
                  |> Set.ofArray

              Expect.equal
                  mine
                  (set [ "Multivector"; "GeometricAlgebraOperations"; "GeometricAlgebraLaws" ])
                  "the package's top-level names"

              // `open Fuaran.Compute.GeometricAlgebra` beside another package's namespace must not shadow
              // either side's names (DECISIONS.md D74 item 2).
              let others =
                  [ typeof<Fuaran.Compute.Solvers.StoppingRule>.Assembly
                    typeof<Fuaran.Compute.Elementary.SpecialValue>.Assembly
                    typeof<Fuaran.Compute.LinearAlgebra.Matrix>.Assembly
                    typeof<Fuaran.Compute.Regression.GlmFit>.Assembly
                    typeof<Fuaran.Compute.Transform>.Assembly
                    typeof<Fuaran.Compute.ColumnOp>.Assembly ]

              Expect.equal
                  (others |> List.map (fun a -> a.GetName().Name) |> List.distinct |> List.length)
                  6
                  "six other packages are read"

              let clashes =
                  [ for a in others do
                        for t in a.GetExportedTypes() do
                            if not t.IsNested && Set.contains (moduleName t) mine then
                                yield a.GetName().Name + ": " + moduleName t ]

              Expect.isEmpty clashes "no top-level name of the algebra is declared by another Compute package" ]
