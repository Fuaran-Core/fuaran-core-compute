namespace Fuaran.Compute.GeometricAlgebra

open System

/// `geometricAlgebraLaws` (Phase 727; Phase 728 adds the conformal objects' laws, the last nine):
/// the laws the multivector, its products and the objects built on them are held to, on
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


    // ---- Phase 728: the conformal objects (DECISIONS.md D85 item 7, D87) --------------------------
    //
    // The inputs of these laws are small integers, so that a product of five points' coefficients
    // and every partial sum of it is a double.

    /// An integer in `−4 .. 4`.
    let private small (d: Draw) : float = float (d.Int -4 4)

    let private smallTriple (d: Draw) : float * float * float =
        let a = small d
        let b = small d
        let c = small d
        a, b, c

    let private succeeded (r: Result<'a, GeometryRefusal>) (onOk: 'a -> Result<unit, string>) : Result<unit, string> =
        match r with
        | Ok x -> onOk x
        | Error _ -> Error "a construction on exact inputs was refused"

    let private isPositiveZero (a: Multivector) : bool = sameBits a Multivector.zero

    /// `(a − x)² + (b − y)² + (c − z)²`, exact on small integers.
    let private squaredDistance (a: float, b: float, c: float) (x: float, y: float, z: float) : float =
        ((a - x) * (a - x) + (b - y) * (b - y)) + (c - z) * (c - z)

    /// `−1`, `0` or `+1`.
    let private signOf (x: float) : int =
        if x > 0.0 then 1
        elif x < 0.0 then -1
        else 0

    let private embedding (d: Draw) =
        let x = smallTriple d
        let y = smallTriple d

        succeeded (Point.up x) (fun p ->
            succeeded (Point.up y) (fun q ->
                let pm = Point.toMultivector p
                let qm = Point.toMultivector q
                let half = -0.5 * squaredDistance x y

                all
                    [ check (bits (Multivector.scalarProduct pm pm) = bits 0.0) (fun () -> "up x * up x is not +0.0")
                      check (x = y || bits (Multivector.scalarProduct pm qm) = bits half) (fun () ->
                          "up x * up y is not -1/2 |x - y|^2, bit for bit")
                      check (bits (Multivector.scalarProduct pm (Multivector.basis 5)) = bits -1.0) (fun () ->
                          "up x * n_inf is not -1") ]))

    let private embeddingOfOnePoint (d: Draw) =
        let x = smallTriple d

        succeeded (Point.up x) (fun p ->
            let pm = Point.toMultivector p
            let left = Multivector.scalarProduct pm pm
            let right = -0.5 * squaredDistance x x

            all
                [ check (left = right) (fun () -> "up x * up x and -1/2 |x - x|^2 are not one number")
                  check (bits left <> bits right && bits right = bits -0.0) (fun () ->
                      "the right side is not the negative zero that keeps this law from holding bit for bit") ])

    let private pointOnItsRound (d: Draw) =
        let p = Point.up (smallTriple d)
        let q = Point.up (smallTriple d)
        let r = Point.up (smallTriple d)
        let s = Point.up (smallTriple d)

        succeeded p (fun p ->
            succeeded q (fun q ->
                succeeded r (fun r ->
                    succeeded s (fun s ->
                        let circle = Circle.toMultivector (Circle.through p q r)
                        let sphere = Sphere.toMultivector (Sphere.through p q r s)

                        all
                            [ for name, point in [ "first", p; "second", q; "third", r ] ->
                                  check
                                      (isPositiveZero (Multivector.outerProduct (Point.toMultivector point) circle))
                                      (fun () -> sprintf "the %s point is not on the circle through it" name)
                              for name, point in [ "first", p; "second", q; "third", r; "fourth", s ] ->
                                  check
                                      (isPositiveZero (Multivector.outerProduct (Point.toMultivector point) sphere))
                                      (fun () -> sprintf "the %s point is not on the sphere through it" name) ]))))

    let private threePoints (d: Draw) : Result<Point * Point * Point, GeometryRefusal> =
        Point.up (smallTriple d)
        |> Result.bind (fun p ->
            Point.up (smallTriple d)
            |> Result.bind (fun q -> Point.up (smallTriple d) |> Result.map (fun r -> p, q, r)))

    let private meetOfTwoPlanesIsInBoth (d: Draw) =
        succeeded (threePoints d) (fun (p, q, r) ->
            succeeded (threePoints d) (fun (s, t, u) ->
                let first = Plane.through p q r
                let second = Plane.through s t u
                let line = Line.toMultivector (Plane.meet first second)

                all
                    [ for name, plane in [ "first", first; "second", second ] ->
                          check (isPositiveZero (Blade.meet line (Plane.toMultivector plane))) (fun () ->
                              sprintf "the meet of two planes is not in the %s" name) ]))

    let private meetWithinTheWholeSpace (d: Draw) =
        let a = exact d
        let b = exact d

        match Blade.meetWithin pseudoscalar a b with
        | Ok within ->
            check (sameBits within (Blade.meet a b)) (fun () -> "the meet within the whole space is not the meet")
        | Error _ -> Error "the meet within the whole space was refused"

    let private meetAgainstTheOuterProductOfDuals (d: Draw) =
        let a = exact d
        let b = exact d

        let undualled =
            Multivector.undual (Multivector.outerProduct (Multivector.dual b) (Multivector.dual a))

        check (Blade.meet a b = undualled) (fun () -> "meet A B is not the undual of B* ^ A*")

    /// The sign of each statistic is the sign exact arithmetic gives: integer coordinates, a point and a
    /// sphere of any integer weight, and the same sign from the value, from the test at `0` and from the
    /// test's own reading of the other two.
    let private signsOfTheStatistics (d: Draw) =
        let x = smallTriple d
        let c = smallTriple d
        let radius = float (d.Int 0 6)
        let pointWeight = float (2 * d.Int 0 1 - 1) * float (d.Int 1 3)
        let sphereWeight = float (2 * d.Int 0 1 - 1) * float (d.Int 1 3)

        let weighted (w: float) (p: Point) : Point =
            { N0 = w * p.N0
              E1 = w * p.E1
              E2 = w * p.E2
              E3 = w * p.E3
              NInf = w * p.NInf }

        succeeded (Point.up x) (fun unitPoint ->
            succeeded (Sphere.ofCentreRadius c radius) (fun unitSphere ->
                succeeded (threePoints d) (fun (p, q, r) ->
                    let point = weighted pointWeight unitPoint

                    let sphere: Sphere =
                        { N0E1E2E3 = sphereWeight * unitSphere.N0E1E2E3
                          N0E1E2NInf = sphereWeight * unitSphere.N0E1E2NInf
                          N0E1E3NInf = sphereWeight * unitSphere.N0E1E3NInf
                          N0E2E3NInf = sphereWeight * unitSphere.N0E2E3NInf
                          E1E2E3NInf = sphereWeight * unitSphere.E1E2E3NInf }

                    let truePower = signOf (squaredDistance x c - radius * radius)

                    let ofSide (side: Side) : int =
                        match side with
                        | Side.Inside -> -1
                        | Side.On -> 0
                        | Side.Outside -> 1

                    let plane = Plane.through p q r
                    let (px, py, pz) = (p.E1, p.E2, p.E3)
                    let (qx, qy, qz) = (q.E1 - px, q.E2 - py, q.E3 - pz)
                    let (rx, ry, rz) = (r.E1 - px, r.E2 - py, r.E3 - pz)
                    let (x1, x2, x3) = x
                    let (dx, dy, dz) = (x1 - px, x2 - py, x3 - pz)

                    // The triple product `[q − p, r − p, x − p]`: the side of the plane's normal `x` is on.
                    let trueSide =
                        signOf ((dx * (qy * rz - qz * ry) + dy * (qz * rx - qx * rz)) + dz * (qx * ry - qy * rx))

                    all
                        [ match Sphere.power point sphere, Sphere.side 0.0 point sphere with
                          | Ok power, Ok side ->
                              check (signOf power = truePower && ofSide side = truePower) (fun () ->
                                  "the power against a sphere has another sign than |x - c|^2 - r^2")
                          | _ -> Error "a statistic on exact inputs was refused"
                          match Plane.signedDistance point plane, Plane.side 0.0 point plane with
                          | Ok distance, Ok side ->
                              let ofPlaneSide =
                                  match side with
                                  | PlaneSide.Negative -> -1
                                  | PlaneSide.On -> 0
                                  | PlaneSide.Positive -> 1

                              check (signOf distance = trueSide && ofPlaneSide = trueSide) (fun () ->
                                  "the signed distance from a plane has another sign than the triple product")
                          | Error GeometryRefusal.ZeroDivisor, Error GeometryRefusal.ZeroDivisor ->
                              // Three collinear points: no plane, and both say so.
                              check (Plane.weight plane = 0.0) (fun () ->
                                  "a plane of weight was refused as one of none")
                          | _ -> Error "the value and the test of a signed distance disagree on a refusal" ])))

    /// The in-circle statistic against the determinant predicate, on the integer grid of the plane
    /// `x3 = 0`. The power is negative inside whatever the order of the circle's points, where the
    /// determinant changes sign with their orientation, so the two agree through the orientation.
    let private inCircleAgreesWithTheDeterminant (d: Draw) =
        let grid () : float * float =
            let x = float (d.Int -8 8)
            let y = float (d.Int -8 8)
            x, y

        let (ax, ay) = grid ()
        let (bx, by) = grid ()
        let (cx, cy) = grid ()
        let (px, py) = grid ()
        let orientation = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax)

        let row (x: float) (y: float) =
            x - px, y - py, (x - px) * (x - px) + (y - py) * (y - py)

        let (a1, a2, a3) = row ax ay
        let (b1, b2, b3) = row bx by
        let (c1, c2, c3) = row cx cy

        let determinant =
            (a1 * (b2 * c3 - b3 * c2) - a2 * (b1 * c3 - b3 * c1)) + a3 * (b1 * c2 - b2 * c1)

        let flat (x: float) (y: float) = Point.up (x, y, 0.0)

        succeeded (flat ax ay) (fun a ->
            succeeded (flat bx by) (fun b ->
                succeeded (flat cx cy) (fun c ->
                    succeeded (flat px py) (fun p ->
                        let circle = Circle.through a b c

                        match Circle.power p circle, Circle.side 0.0 p circle with
                        | Ok power, Ok side ->
                            let expected = -(signOf determinant * signOf orientation)

                            let ofSide =
                                match side with
                                | Side.Inside -> -1
                                | Side.On -> 0
                                | Side.Outside -> 1

                            check (orientation <> 0.0 && signOf power = expected && ofSide = expected) (fun () ->
                                "the in-circle statistic disagrees with the determinant predicate")
                        | Error GeometryRefusal.ZeroDivisor, Error GeometryRefusal.ZeroDivisor ->
                            check (orientation = 0.0) (fun () -> "three points that are not collinear were refused")
                        | _ -> Error "the value and the test of the in-circle statistic disagree on a refusal"))))

    // ---- agreement with a closed form, within a stated bound ----------------------------------------

    /// The lattice points of the spheres of radius 3, 7 and 9 about the origin that these laws draw
    /// from: each row a radius and a point at exactly that distance.
    let private latticePoints: (float * (float * float * float)) list =
        [ for radius, (a, b, c) in
              [ 3.0, (1.0, 2.0, 2.0)
                7.0, (2.0, 3.0, 6.0)
                9.0, (1.0, 4.0, 8.0)
                9.0, (4.0, 4.0, 7.0) ] do
              for (x, y, z) in [ a, b, c; b, c, a; c, a, b; a, c, b; c, b, a; b, a, c ] do
                  for sx in [ 1.0; -1.0 ] do
                      for sy in [ 1.0; -1.0 ] do
                          for sz in [ 1.0; -1.0 ] -> radius, (sx * x, sy * y, sz * z) ]

    /// The last place of a non-zero double: the power of two `2^(e − 52)` with `2^e ≤ |x| < 2^(e + 1)`,
    /// found by exact doubling and halving.
    let private lastPlace (x: float) : float =
        let magnitude = if x < 0.0 then -x else x
        let mutable power = 1.0

        while power > magnitude do
            power <- power / 2.0

        while power * 2.0 <= magnitude do
            power <- power * 2.0

        power / 4503599627370496.0

    /// The seeded sample (seed 728, 2,000 configurations) and what was measured over it: a line and a
    /// sphere built from parameters, whose two points of intersection are lattice points known exactly
    /// by construction, against the points the package reads back through `Line.ofPointDirection`,
    /// `Sphere.ofCentreRadius`, `Line.meetSphere`, `PointPair.points` at `tau = 0` and
    /// `Point.coordinates`. Answers the largest error of a coordinate in units in the last place of its
    /// reference, and the largest absolute error of a coordinate whose reference is zero; or the first
    /// configuration that did not answer two real points in the line's order.
    let internal closedFormSample () : Result<float * float, string> =
        let d = Draw 728
        let mutable worstUlps = 0.0
        let mutable worstAbsolute = 0.0
        let mutable failure = None
        let mutable i = 0

        while Option.isNone failure && i < 2000 do
            let radius = List.item (d.Int 0 2) [ 3.0; 7.0; 9.0 ]

            let onSphere =
                latticePoints |> List.filter (fun (r, _) -> r = radius) |> List.map snd

            let (ux, uy, uz) = List.item (d.Int 0 (onSphere.Length - 1)) onSphere
            let (vx, vy, vz) = List.item (d.Int 0 (onSphere.Length - 1)) onSphere
            let cx = float (d.Int -20 20)
            let cy = float (d.Int -20 20)
            let cz = float (d.Int -20 20)
            let k = float (d.Int -3 3)
            let scale = float (d.Int 1 5)

            if not (ux = vx && uy = vy && uz = vz) then
                let direction = (scale * (vx - ux), scale * (vy - uy), scale * (vz - uz))
                let first = (cx + ux, cy + uy, cz + uz)
                let second = (cx + vx, cy + vy, cz + vz)

                let through =
                    (cx + ux + k * (vx - ux), cy + uy + k * (vy - uy), cz + uz + k * (vz - uz))

                let read =
                    Line.ofPointDirection through direction
                    |> Result.bind (fun line ->
                        Sphere.ofCentreRadius (cx, cy, cz) radius
                        |> Result.bind (fun sphere -> PointPair.points 0.0 (Line.meetSphere line sphere)))

                match read with
                | Ok(PairPoints.Real(p, q)) ->
                    match Point.coordinates p, Point.coordinates q with
                    | Ok(p1, p2, p3), Ok(q1, q2, q3) ->
                        let (f1, f2, f3) = first
                        let (s1, s2, s3) = second

                        for got, reference in [ p1, f1; p2, f2; p3, f3; q1, s1; q2, s2; q3, s3 ] do
                            let error = if got < reference then reference - got else got - reference

                            if reference = 0.0 then
                                if error > worstAbsolute then
                                    worstAbsolute <- error
                            else
                                let ulps = error / lastPlace reference

                                if ulps > worstUlps then
                                    worstUlps <- ulps
                    | _ -> failure <- Some(sprintf "configuration %d: a point's coordinates were refused" i)
                | _ ->
                    failure <-
                        Some(sprintf "configuration %d: the line and the sphere did not meet in two real points" i)

            i <- i + 1

        match failure with
        | Some why -> Error why
        | None -> Ok(worstUlps, worstAbsolute)

    /// The bound of the closed-form law: the largest errors measured over `closedFormSample`, with no
    /// margin, because the answers are pinned bits and every host measures the same ones.
    let internal closedFormBoundUlps = 292.0

    let internal closedFormBoundAbsolute = 104.0 / 4503599627370496.0

    let private closedFormVerdict: Lazy<Result<unit, string>> =
        lazy
            (match closedFormSample () with
             | Error why -> Error why
             | Ok(ulps, absolute) ->
                 check (ulps <= closedFormBoundUlps && absolute <= closedFormBoundAbsolute) (fun () ->
                     sprintf
                         "the largest errors are %g units in the last place and %g absolute, past the stated bound"
                         ulps
                         absolute))

    let private lineAndSphereAgainstTheClosedForm (_: Draw) = closedFormVerdict.Value

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
          "equality is the float identity, and equal multivectors hash alike", equalityIsTheFloatIdentity
          "up x * up x is zero and up x * up y is minus half the squared distance, bit for bit", embedding
          "up x * up x is minus half of |x - x|^2, under the float identity", embeddingOfOnePoint
          "a point is on the circle and on the sphere built through it, bit for bit", pointOnItsRound
          "the meet of two planes is in both, bit for bit", meetOfTwoPlanesIsInBoth
          "the meet within the whole space is the meet, bit for bit", meetWithinTheWholeSpace
          "the meet is the undual of the outer product of the duals, under the float identity",
          meetAgainstTheOuterProductOfDuals
          "the sign of each incidence statistic is the sign exact arithmetic gives", signsOfTheStatistics
          "the in-circle statistic agrees with the determinant predicate on the integer grid, in sign",
          inCircleAgreesWithTheDeterminant
          "a line and a sphere meet where the closed form says, within the stated bound",
          lineAndSphereAgainstTheClosedForm ]
        |> List.map (fun (name, body) -> name, law seed count body)
