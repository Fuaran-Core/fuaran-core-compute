namespace Fuaran.Compute.GeometricAlgebra

open System

/// `geometricAlgebraLaws` (Phase 727): the laws the multivector and its products are held to, on
/// whichever host runs them (DECISIONS.md D85 item 7). Each law says which of two comparisons it holds
/// under. BIT FOR BIT compares the sixty-four bits of each coefficient, which tells the two zeros
/// apart. UNDER THE FLOAT IDENTITY is `Multivector`'s own equality, in which the two zeros are one
/// value: it is needed wherever one side of a law ends in a negation and the other in a fold, because
/// a negation turns `+0.0` into `-0.0` and a fold never returns one. No law compares floats with `=`.
///
/// The algebraic identities are held on inputs whose arithmetic is exact: multiples of a quarter no
/// larger than eight, so every product and every partial sum is a double. They are tested there and
/// not proved, and on general inputs they hold only to rounding, where what is pinned is the bits:
/// the committed law vectors. The laws on general inputs here are the ones that are exact by
/// construction: a copy, a negation, one rounded operation, a fold against the same fold.
///
/// Every input is drawn by a pinned generator whose every step is an exact float operation (Park and
/// Miller's minimal standard), so a seed draws the same numbers on every host and the laws run under
/// Fable over the numbers they run over on .NET. A law answers `Ok ()` when it held at every
/// iteration, else the first failure with its iteration. FSharp.Core only.
[<RequireQualifiedAccess>]
module GeometricAlgebraLaws =

    let private bits (x: float) : int64 = BitConverter.DoubleToInt64Bits x

    let private sameBits (a: Multivector) (b: Multivector) : bool =
        Array.forall2 (fun x y -> bits x = bits y) (Multivector.toArray a) (Multivector.toArray b)

    /// How many coefficients are `-0.0`.
    let private negativeZeros (a: Multivector) : int =
        Multivector.toArray a
        |> Array.filter (fun x -> bits x = bits -0.0)
        |> Array.length

    type private Draw(seed: int) =
        let mutable state =
            ((float seed % 2147483646.0) + 2147483646.0) % 2147483646.0 + 1.0

        member _.Next() : float =
            state <- (state * 48271.0) % 2147483647.0
            state

        /// An integer in `lo .. hi`.
        member this.Int (lo: int) (hi: int) : int =
            lo + int (this.Next() % float (hi - lo + 1))

        /// A zero of either sign.
        member this.Zero() : float = if this.Int 0 1 = 0 then 0.0 else -0.0

        /// A multiple of a quarter in `[−8, 8]`, a zero of either sign among them.
        member this.Exact() : float =
            let k = this.Int -32 32
            if k = 0 then this.Zero() else float k / 4.0

        /// A finite double of either sign across eighty binary orders of magnitude, one draw in eight a
        /// zero of either sign.
        member this.General() : float =
            if this.Int 0 7 = 0 then
                this.Zero()
            else
                let mutable x = this.Next() / 2147483648.0 - 0.5
                let exponent = this.Int -40 40
                let steps = if exponent > 0 then exponent else -exponent

                for _ in 1..steps do
                    x <- if exponent > 0 then x * 2.0 else x / 2.0

                x

    let private exact (d: Draw) : Multivector = Multivector.init (fun _ -> d.Exact())

    let private general (d: Draw) : Multivector = Multivector.init (fun _ -> d.General())

    let private check (held: bool) (why: unit -> string) : Result<unit, string> = if held then Ok() else Error(why ())

    let private all (checks: Result<unit, string> list) : Result<unit, string> =
        checks |> List.tryFind Result.isError |> Option.defaultValue (Ok())

    let private law (seed: int) (count: int) (body: Draw -> Result<unit, string>) : Result<unit, string> =
        let draw = Draw seed
        let mutable verdict = Ok()
        let mutable i = 0

        while Result.isOk verdict && i < count do
            match body draw with
            | Ok() -> ()
            | Error why -> verdict <- Error(sprintf "iteration %d: %s" i why)

            i <- i + 1

        verdict

    let private n0 = Multivector.basis 1
    let private e1 = Multivector.basis 2
    let private e2 = Multivector.basis 3
    let private e3 = Multivector.basis 4
    let private nInf = Multivector.basis 5
    let private pseudoscalar = Multivector.basis 31

    /// `I⁻¹ = −I`, written with `+0.0` at every other index.
    let private inversePseudoscalar =
        Multivector.init (fun index -> if index = 31 then -1.0 else 0.0)

    let private gp = Multivector.geometricProduct

    // ---- bit for bit, on exact inputs --------------------------------------------------------------

    let private tableShape (_: Draw) =
        let perOutput (terms: Term[]) (out: int) =
            terms |> Array.filter (fun t -> t.Out = out) |> Array.length

        let pairs (size: int) =
            Basis.geometric
            |> Array.countBy (fun t -> t.Left, t.Right)
            |> Array.filter (fun (_, n) -> n = size)
            |> Array.length

        all
            [ check (Basis.geometric.Length = 1024) (fun () ->
                  sprintf "the table has %d terms, not 1,024" Basis.geometric.Length)
              check (List.forall (fun out -> perOutput Basis.geometric out = 32) [ 0 .. Basis.count - 1 ]) (fun () ->
                  "an output coefficient does not receive exactly thirty-two terms")
              check (pairs 2 = 128 && pairs 1 = 768) (fun () ->
                  sprintf "%d pairs give two terms and %d one, not 128 and 768" (pairs 2) (pairs 1))
              check (Basis.outer.Length = 243) (fun () ->
                  sprintf "the outer product keeps %d terms, not 243" Basis.outer.Length)
              check (Basis.contraction.Length = 243) (fun () ->
                  sprintf "the left contraction keeps %d terms, not 243" Basis.contraction.Length)
              check (Basis.scalar.Length = 32) (fun () ->
                  sprintf "the scalar product keeps %d terms, not 32" Basis.scalar.Length) ]

    let private signature (_: Draw) =
        let ePlus = Multivector.subtract (Multivector.scale 0.5 nInf) n0
        let eMinus = Multivector.add (Multivector.scale 0.5 nInf) n0

        all
            [ check
                  ([ e1; e2; e3 ]
                   |> List.forall (fun e -> sameBits (gp e e) (Multivector.ofScalar 1.0)))
                  (fun () -> "a Euclidean basis vector does not square to +1")
              check (sameBits (gp n0 n0) Multivector.zero) (fun () -> "n0 does not square to zero")
              check (sameBits (gp nInf nInf) Multivector.zero) (fun () -> "n∞ does not square to zero")
              check (bits (Multivector.scalarProduct n0 nInf) = bits -1.0) (fun () -> "n0 ∗ n∞ is not −1")
              check (bits (Multivector.scalarProduct nInf n0) = bits -1.0) (fun () -> "n∞ ∗ n0 is not −1")
              check (sameBits (gp ePlus ePlus) (Multivector.ofScalar 1.0)) (fun () -> "½ n∞ − n0 does not square to +1")
              check (sameBits (gp eMinus eMinus) (Multivector.ofScalar -1.0)) (fun () ->
                  "½ n∞ + n0 does not square to −1")
              check (sameBits (gp pseudoscalar pseudoscalar) (Multivector.ofScalar -1.0)) (fun () ->
                  "the pseudoscalar does not square to −1") ]

    let private associative (d: Draw) =
        let a = exact d
        let b = exact d
        let c = exact d
        check (sameBits (gp (gp a b) c) (gp a (gp b c))) (fun () -> "(A B) C and A (B C) differ in a bit")

    let private distributive (d: Draw) =
        let a = exact d
        let b = exact d
        let c = exact d

        all
            [ check (sameBits (gp a (Multivector.add b c)) (Multivector.add (gp a b) (gp a c))) (fun () ->
                  "A (B + C) and A B + A C differ in a bit")
              check (sameBits (gp (Multivector.add a b) c) (Multivector.add (gp a c) (gp b c))) (fun () ->
                  "(A + B) C and A C + B C differ in a bit") ]

    let private vectorSquaresToNoBivector (d: Draw) =
        let a = Multivector.grade 1 (exact d)

        check (sameBits (Multivector.outerProduct a a) Multivector.zero) (fun () ->
            "a ∧ a is not the zero multivector for a vector")

    let private contractionIsTheAdjoint (d: Draw) =
        let a = exact d
        let b = exact d
        let c = exact d
        let left = Multivector.scalarProduct (Multivector.outerProduct a b) c
        let right = Multivector.scalarProduct a (Multivector.leftContraction b c)
        check (bits left = bits right) (fun () -> sprintf "(A ∧ B) ∗ C = %g and A ∗ (B ⌋ C) = %g" left right)

    // ---- under the float identity, and not bit for bit ---------------------------------------------

    let private reverseIsAnAntiAutomorphism (d: Draw) =
        let a = exact d
        let b = exact d

        all
            [ check (Multivector.reverse (gp a b) = gp (Multivector.reverse b) (Multivector.reverse a)) (fun () ->
                  "rev (A B) and rev B rev A are different multivectors")
              // Why it is the float identity: the left side ends in the reverse's negations.
              check (negativeZeros (Multivector.reverse (gp e1 e2)) = 19) (fun () ->
                  "rev (e1 e2) does not hold nineteen negative zeros")
              check (negativeZeros (gp (Multivector.reverse e2) (Multivector.reverse e1)) = 0) (fun () ->
                  "rev e2 rev e1 holds a negative zero") ]

    let private dualityOfTheProducts (d: Draw) =
        let a = exact d
        let b = exact d

        all
            [ check
                  (Multivector.dual (Multivector.outerProduct a b) = Multivector.leftContraction a (Multivector.dual b))
                  (fun () -> "(A ∧ B)* and A ⌋ B* are different multivectors")
              check (negativeZeros (Multivector.dual (Multivector.outerProduct e1 e2)) = 16) (fun () ->
                  "(e1 ∧ e2)* does not hold sixteen negative zeros")
              check (negativeZeros (Multivector.leftContraction e1 (Multivector.dual e2)) = 0) (fun () ->
                  "e1 ⌋ e2* holds a negative zero") ]

    let private dualIsTheContraction (d: Draw) =
        let a = exact d
        let dual = Multivector.dual a

        all
            [ check (dual = Multivector.leftContraction a inversePseudoscalar) (fun () ->
                  "the dual is not A ⌋ I⁻¹ written as a fold")
              check (dual = gp a inversePseudoscalar) (fun () -> "the dual is not A I⁻¹")
              check (dual = gp inversePseudoscalar a) (fun () -> "the dual is not I⁻¹ A")
              check (Multivector.undual a = Multivector.leftContraction a pseudoscalar) (fun () ->
                  "the undual is not A ⌋ I written as a fold") ]

    // ---- on general inputs: exact by construction ------------------------------------------------

    let private undualUndoesTheDual (d: Draw) =
        let special =
            [| Double.PositiveInfinity; Double.NegativeInfinity; 5e-324; -5e-324 |]

        let a =
            Multivector.init (fun _ ->
                if d.Int 0 5 = 0 then
                    special[d.Int 0 (special.Length - 1)]
                else
                    d.General())

        all
            [ check (sameBits (Multivector.undual (Multivector.dual a)) a) (fun () ->
                  "undual (dual A) differs from A in a bit")
              check (sameBits (Multivector.dual (Multivector.undual a)) a) (fun () ->
                  "dual (undual A) differs from A in a bit") ]

    let private involutionsUndoThemselves (d: Draw) =
        let a = general d

        all
            [ check (sameBits (Multivector.reverse (Multivector.reverse a)) a) (fun () ->
                  "the reverse of a reverse differs from its input in a bit")
              check (sameBits (Multivector.gradeInvolution (Multivector.gradeInvolution a)) a) (fun () ->
                  "the grade involution applied twice differs from its input in a bit")
              check (sameBits (Multivector.negate (Multivector.negate a)) a) (fun () ->
                  "a negation applied twice differs from its input in a bit") ]

    let private gradeProjectionCopiesOneRun (d: Draw) =
        let a = general d
        let xs = Multivector.toArray a

        check
            ([ -1 .. 6 ]
             |> List.forall (fun g ->
                 Multivector.toArray (Multivector.grade g a)
                 |> Array.mapi (fun index x -> bits x = bits (if Basis.grade[index] = g then xs[index] else 0.0))
                 |> Array.forall id))
            (fun () -> "a grade projection is not a copy of its grade's run with +0.0 elsewhere")

    let private oneRoundedOperationEach (d: Draw) =
        let a = general d
        let b = general d
        let s = d.General()
        let xs = Multivector.toArray a
        let ys = Multivector.toArray b

        let coefficientwise (result: Multivector) (f: float -> float -> float) =
            Array.forall2 (fun r (x, y) -> bits r = bits (f x y)) (Multivector.toArray result) (Array.zip xs ys)

        all
            [ check (coefficientwise (Multivector.add a b) (fun x y -> x + y)) (fun () ->
                  "a sum is not one rounded sum for each coefficient")
              check (coefficientwise (Multivector.subtract a b) (fun x y -> x - y)) (fun () ->
                  "a difference is not one rounded difference for each coefficient")
              check (coefficientwise (Multivector.scale s a) (fun x _ -> s * x)) (fun () ->
                  "a scalar multiple is not one rounded product for each coefficient") ]

    let private scalarProductIsTheScalarCoefficient (d: Draw) =
        let a = general d
        let b = general d

        all
            [ check (bits (Multivector.scalarProduct a b) = bits (Multivector.coefficient 0 (gp a b))) (fun () ->
                  "A ∗ B is not the scalar coefficient of A B bit for bit")
              check
                  (bits (Multivector.squaredNorm a) = bits (Multivector.scalarProduct a (Multivector.reverse a)))
                  (fun () -> "the squared norm is not A ∗ Ã bit for bit") ]

    let private productsAreGradeSelections (d: Draw) =
        let r = d.Int 0 5
        let s = d.Int 0 5
        let a = Multivector.grade r (general d)
        let b = Multivector.grade s (general d)
        let product = gp a b

        all
            [ check (sameBits (Multivector.outerProduct a b) (Multivector.grade (r + s) product)) (fun () ->
                  sprintf "A ∧ B is not the grade %d part of A B at grades %d and %d" (r + s) r s)
              check (sameBits (Multivector.leftContraction a b) (Multivector.grade (s - r) product)) (fun () ->
                  sprintf "A ⌋ B is not the grade %d part of A B at grades %d and %d" (s - r) r s) ]

    let private noProductReturnsANegativeZero (d: Draw) =
        // Sparse operands, so that most outputs are sums of zero terms of both signs.
        let sparse () =
            Multivector.init (fun _ -> if d.Int 0 2 = 0 then d.General() else d.Zero())

        let a = sparse ()
        let b = sparse ()

        all
            [ check
                  ([ gp a b; Multivector.outerProduct a b; Multivector.leftContraction a b ]
                   |> List.forall (fun product -> negativeZeros product = 0))
                  (fun () -> "a product returned a negative zero")
              check
                  (bits (Multivector.scalarProduct a b) <> bits -0.0
                   && bits (Multivector.squaredNorm a) <> bits -0.0)
                  (fun () -> "a scalar product returned a negative zero") ]

    let private canonicalNaN (d: Draw) =
        let at = d.Int 0 31

        // A NaN given, a NaN made by a zero against an infinity, and a NaN whose sign a negation moves.
        let withNaN =
            Multivector.init (fun index -> if index = at then Double.NaN else d.General())

        let withInfinity =
            Multivector.init (fun index -> if index = at then Double.PositiveInfinity else d.Zero())

        let b = general d

        let results =
            [ for a in [ withNaN; withInfinity ] do
                  yield Multivector.negate a
                  yield Multivector.reverse (Multivector.negate a)
                  yield Multivector.gradeInvolution (Multivector.negate a)
                  yield Multivector.dual (Multivector.negate a)
                  yield Multivector.undual (Multivector.negate a)
                  yield Multivector.grade (Basis.grade[at]) (Multivector.negate a)
                  yield Multivector.add a b
                  yield Multivector.subtract b a
                  yield Multivector.scale 0.0 a
                  yield gp a b
                  yield gp b a
                  yield Multivector.outerProduct a b
                  yield Multivector.leftContraction a b
                  yield Multivector.ofScalar (Multivector.scalarProduct a b)
                  yield Multivector.ofScalar (Multivector.squaredNorm (Multivector.negate a)) ]

        let coefficients = results |> List.collect (Multivector.toArray >> List.ofArray)

        all
            [ check (coefficients |> List.exists Double.IsNaN) (fun () -> "no operation answered a NaN")
              check
                  (coefficients
                   |> List.forall (fun x -> not (Double.IsNaN x) || bits x = bits Double.NaN))
                  (fun () -> "a NaN left an operation with bits other than Double.NaN's") ]

    // ---- the equality a caller's `=` answers -------------------------------------------------------

    let private equalityIsTheFloatIdentity (d: Draw) =
        let at = d.Int 0 31
        let values = Array.init Basis.count (fun _ -> d.General())
        let a = Multivector.init (fun index -> values[index])

        let withNaN () =
            Multivector.init (fun index -> if index = at then Double.NaN else values[index])

        let positive =
            Multivector.init (fun index -> if index = at then 0.0 else values[index])

        let negative =
            Multivector.init (fun index -> if index = at then -0.0 else values[index])

        let moved =
            Multivector.init (fun index -> if index = at then 1.0 else values[index])

        all
            [ check (withNaN () = withNaN () && hash (withNaN ()) = hash (withNaN ())) (fun () ->
                  "two multivectors holding a NaN at one index are not equal, or hash apart")
              check (let x = withNaN () in x.Equals x) (fun () -> "a multivector holding a NaN is not equal to itself")
              check (positive = negative && hash positive = hash negative) (fun () ->
                  "the two zeros are not one value, or hash apart")
              check (not (sameBits positive negative)) (fun () -> "the two zeros are the same bits")
              check (positive <> moved && withNaN () <> positive) (fun () ->
                  "two multivectors that differ at an index are equal")
              check (a = Multivector.init (fun index -> values[index])) (fun () ->
                  "two multivectors of the same coefficients are not equal")
              check (not (a.Equals(box values))) (fun () -> "a multivector equals something that is not one") ]

    /// The laws, by name, each run at `count` iterations drawn from `seed`.
    let laws (seed: int) (count: int) : (string * Result<unit, string>) list =
        [ "the term table has the recorded shape", tableShape
          "the signature holds, bit for bit", signature
          "the geometric product is associative on exact inputs, bit for bit", associative
          "the geometric product distributes over a sum on exact inputs, bit for bit", distributive
          "a vector's outer product with itself is zero, bit for bit", vectorSquaresToNoBivector
          "the left contraction is the adjoint of the outer product on exact inputs, bit for bit",
          contractionIsTheAdjoint
          "the reverse is an anti-automorphism on exact inputs, under the float identity", reverseIsAnAntiAutomorphism
          "the dual of an outer product is the contraction onto the dual on exact inputs, under the float identity",
          dualityOfTheProducts
          "the dual is the contraction onto the inverse pseudoscalar, under the float identity", dualIsTheContraction
          "the undual of a dual is its input, bit for bit", undualUndoesTheDual
          "the reverse, the grade involution and negation undo themselves, bit for bit", involutionsUndoThemselves
          "a grade projection copies one grade's run, bit for bit", gradeProjectionCopiesOneRun
          "a sum, a difference and a scalar multiple are one rounded operation each, bit for bit",
          oneRoundedOperationEach
          "the scalar product is the geometric product's scalar coefficient, bit for bit",
          scalarProductIsTheScalarCoefficient
          "the outer product and the left contraction are the geometric product's grade parts, bit for bit",
          productsAreGradeSelections
          "no product returns a negative zero", noProductReturnsANegativeZero
          "a NaN leaves as Double.NaN's bits", canonicalNaN
          "equality is the float identity, and equal multivectors hash alike", equalityIsTheFloatIdentity ]
        |> List.map (fun (name, body) -> name, law seed count body)
