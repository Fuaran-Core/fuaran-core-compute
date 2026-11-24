namespace Fuaran.Compute.GeometricAlgebra

open System

// ============================================================================
//  Phase 728 — the conformal objects, written once, with the arithmetic they
//  are written in as an argument (DECISIONS.md D85 items 4, 5 and 6; D87).
//
//  Every function here takes an `Arithmetic` first: the four products, how a
//  quotient is formed and how a statistic's is, the norm's scaling and its
//  sum, how a product of three factors is grouped, the two halved squares, the `−2` of a power, what a zero divisor and a zero direction are,
//  what "above `τ`" and "within `τ`" are, and when the signs decide. Each
//  member is one rule and no member is two. A public function passes
//  `Arithmetic.shipped`, which is the record as D85 writes it. The suite calls
//  the same function with ONE member changed and shows a committed law vector
//  fail, so a go-red witness is this code and never a copy of it (D86 item 4
//  ruled the same for the fold). That is the only reason the arithmetic is an
//  argument.
//
//  A typed operation is the general operation on thirty-two coefficients, the
//  ones a type does not carry being `+0.0`, followed by a copy of the result
//  type's coefficients (D85 item 4). The products are Phase 727's general
//  ones, but for the geometric product of a pair of types D89 keeps, which is
//  specialised to those types and answers the general product's bits (Phase
//  730). Nothing normalises an operand or a result (D85 item 5). `sqrt` is
//  the one host function called, twice: in the norm, and for a real point
//  pair's `√(X ∗ X)`.
//
//  FSharp.Core only; Fable-clean.
// ============================================================================

/// The arithmetic a conformal operation is written in. `Arithmetic.shipped` is DECISIONS.md D85's.
type internal Arithmetic =
    {
        /// The geometric product, the outer product and the left contraction: D85 item 3's folds.
        Geometric: Multivector -> Multivector -> Multivector
        Outer: Multivector -> Multivector -> Multivector
        Contraction: Multivector -> Multivector -> Multivector
        /// The scalar product: the scalar coefficient of the geometric product.
        Scalar: Multivector -> Multivector -> float
        /// A quotient: one rounded division, never a product with a reciprocal.
        Divide: float -> float -> float
        /// An incidence statistic's quotient (D85 item 6): the numerator over the product of the
        /// divisor's two factors, the product first and then one division, by the division it is
        /// given.
        OverBoth: (float -> float -> float) -> float -> float -> float -> float
        /// The norm of a group of coefficients (D49 item 4), scaled: the largest magnitude, the
        /// quotients by it, and that magnitude times the root of the sum it is given of their squares.
        Scaled: (float[] -> float) -> float[] -> float
        /// The sum of the norm's squared quotients (D49 item 4): folded in index order from `+0.0`, a
        /// rounded square and then a rounded sum.
        SumOfSquares: float[] -> float
        /// A product of more than two factors (D85 item 3): two at a time, left to right.
        Chain: (Multivector -> Multivector -> Multivector) -> Multivector list -> Multivector
        /// `½ x²` for a point's coordinates: `0.5 × ((x1·x1 + x2·x2) + x3·x3)` (D85 item 4).
        HalfSquaredLength: float -> float -> float -> float
        /// `½ r²` for a radius: `0.5 × (r × r)`, the square first (D85 item 4).
        HalfSquare: float -> float
        /// `−2.0` times a scalar product, which is exact: a power's numerator (D85 item 6).
        MinusTwice: float -> float
        /// Whether a divisor is zero: a weight, `X ∗ X̃`, `W ∗ W̃`, `J ∗ J̃`, the product of two weights.
        IsZeroDivisor: float -> bool
        /// Whether the norm of a direction parameter is zero.
        IsZeroNorm: float -> bool
        /// `s > τ`: above the threshold (D85 item 6).
        Above: float -> float -> bool
        /// `|s| ≤ τ`: the zero test (D85 item 6).
        Within: float -> float -> bool
        /// Whether a threshold is the exact comparison, `τ = 0`. There a statistic that is a quotient
        /// is decided from the signs of its numerator and its divisor, and no quotient is formed
        /// (D85 item 6).
        SignsDecide: float -> bool
        // ---- the versors (Phase 729, DECISIONS.md D85 items 4 and 5; D88) ----------------------------
        /// `(sin x, cos x)`: the pinned pair of `Fuaran.Compute.Elementary`, asked once.
        SinCos: float -> struct (float * float)
        /// The pinned arc tangent of `y / x` by quadrant, `y` first.
        Atan2: float -> float -> float
        /// What a versor is applied by: its reverse, never its inverse (D85 item 4).
        VersorReverse: Multivector -> Multivector
        /// The composition `B A`, which applies `A` first: by the product it is given, `B` then `A`.
        Compose: (Multivector -> Multivector -> Multivector) -> Multivector -> Multivector -> Multivector
        /// A screw, `T_(d û) ((T_p R) T̃_p)`, formed in that order by the product it is given: the
        /// translator along the axis, the translator to the axis, the rotor, and that translator's
        /// reverse.
        Screw:
            (Multivector -> Multivector -> Multivector)
                -> Multivector
                -> Multivector
                -> Multivector
                -> Multivector
                -> Multivector
        /// Coefficients times a quotient, the quotient formed once by the division it is given:
        /// `sin φ / φ` in an exponential and `φ / |B′|` in a logarithm.
        ByQuotient: (float -> float -> float) -> float -> float -> float[] -> float[]
        /// Whether an exponential's `φ` is zero, where `exp B` is `1 + B`.
        IsZeroAngle: float -> bool
        /// Whether a logarithm's `|B′|` is zero, where no quotient is formed.
        IsZeroBivector: float -> bool
        /// A dilator's two coefficients from its factor `s`: `r = √s`, `q = 2 × r`, then `(s + 1) / q`
        /// and `(s − 1) / q`, by the division it is given.
        DilatorParts: (float -> float -> float) -> float -> struct (float * float)
        /// The versor whose logarithm an interpolation takes: `Ṽ₁ V₂` as it was formed, never its
        /// negative.
        Between: Multivector -> Multivector
        /// How a motor's exponential forms the two parts of `v`, along the axis of `B` and across it,
        /// from `v`, the unit bivector of `B` and that bivector's reverse (D88).
        SplitAlong: Arithmetic -> Multivector -> Multivector -> Multivector -> float[] * float[]
        /// How a motor's logarithm forms `(sin φ / φ) v⊥` from the motor's scalar, its three
        /// coefficients on the blades `e_i ∧ n∞`, the part of `v` along the axis, and the unit
        /// bivector of the rotation with its reverse (D88).
        MotorAcross: Arithmetic -> float -> float[] -> float[] -> Multivector -> Multivector -> float[]
        /// Where the entry of a row and a column is among a matrix's sixteen doubles: column-major.
        MatrixIndex: int -> int -> int
        /// The dot product of two columns: folded in index order from `+0.0`, a rounded product and
        /// then a rounded sum.
        Dot: float[] -> float[] -> float
        /// The triple product `(a × b) · c`, whose sign says whether three columns reverse
        /// orientation: each component of the cross product two rounded products and their rounded
        /// difference, then the dot product it is given.
        Orientation: (float[] -> float[] -> float) -> float[] -> float[] -> float[] -> float
        /// The rotor of a rotation, as `(cos ½θ, sin ½θ â)`, from its nine entries by row and column
        /// and the division it is given (D88).
        RotorOfRotation: (float -> float -> float) -> (int -> int -> float) -> float[]
        /// The sign of a rotor read from a matrix (D85 item 4): its scalar part is not negative, and
        /// at a half turn its axis component of largest magnitude is positive, the first in a tie.
        Canonical: float[] -> float[]
        /// A direction already divided by its norm (an axis, a normal, a line's direction over its
        /// weight): used as it is, and never divided by its norm a second time (D85 item 4).
        UnitAsGiven: float * float * float -> float * float * float
        /// The point a screw's axis passes through, beside the axis's unit direction: used as it is
        /// given, never moved along the axis (D85 item 4).
        PointAsGiven: float * float * float -> float * float * float -> float * float * float
        /// A coefficient of an exponential: placed on its blade and never added to a zero, so that it
        /// keeps the sign of a zero (D88 item 3).
        Placed: float -> float
        /// A normalisation (D85 item 5): the division of every coefficient by one scalar that it is
        /// given, applied to the coefficients once.
        NormaliseOnce: (float[] -> Result<float[], GeometryRefusal>) -> float[] -> Result<float[], GeometryRefusal>
    }

/// The one 2-norm (DECISIONS.md D49 item 4), transcribed: this package cannot reference
/// LinearAlgebra, and the suite, which references both, holds the two routines to one answer bit for
/// bit. `m = max |x_i|`; a NaN when any element is one; `+∞` when `m` is; `0` when `m` is; otherwise
/// `m × √(Σ (x_i / m)²)`, the sum folded in index order. The largest magnitude is found with
/// `v > m` and `−v > m`, which is the same `m` and calls nothing.
module internal Norm =

    /// The norm with the sum of the squared quotients as an argument, so that a witness is this
    /// routine with that one sum changed.
    let scaledWith (sumOfSquares: float[] -> float) (x: float[]) : float =
        let mutable m = 0.0
        let mutable nan = false

        for v in x do
            if Double.IsNaN v then
                nan <- true
            elif v > m then
                m <- v
            elif -v > m then
                m <- -v

        if nan then
            Double.NaN
        elif Double.IsPositiveInfinity m then
            Double.PositiveInfinity
        elif m = 0.0 then
            0.0
        else
            m * sqrt (sumOfSquares (x |> Array.map (fun v -> v / m)))

    /// The squared quotients folded in index order from `+0.0`: a rounded square, then a rounded sum.
    let foldedSquares (quotients: float[]) : float =
        let mutable acc = 0.0

        for q in quotients do
            let sq = q * q
            acc <- acc + sq

        acc

    let scaled (x: float[]) : float = scaledWith foldedSquares x

/// The expressions DECISIONS.md D85 leaves to the phase that ships them, each written once (D88). A
/// member of `Arithmetic.shipped` is one of these; the forms that were measured and not chosen stay
/// beside the chosen one, because the suite shows each of them red on a committed vector.
module internal Forms =

    let dilatorParts (divide: float -> float -> float) (s: float) : struct (float * float) =
        let root = sqrt s
        let q = 2.0 * root
        struct (divide (s + 1.0) q, divide (s - 1.0) q)

    let byQuotient (divide: float -> float -> float) (numerator: float) (denominator: float) (xs: float[]) : float[] =
        let k = divide numerator denominator
        xs |> Array.map (fun x -> k * x)

    let screw
        (product: Multivector -> Multivector -> Multivector)
        (along: Multivector)
        (toAxis: Multivector)
        (rotor: Multivector)
        (back: Multivector)
        : Multivector =
        product along (product (product toAxis rotor) back)

    let dot (a: float[]) (b: float[]) : float =
        let mutable acc = 0.0

        for i in 0 .. a.Length - 1 do
            let p = a[i] * b[i]
            acc <- acc + p

        acc

    let orientation (dot: float[] -> float[] -> float) (a: float[]) (b: float[]) (c: float[]) : float =
        let minor (x: float) (y: float) (z: float) (w: float) : float =
            let first = x * y
            let second = z * w
            first - second

        dot
            [| minor a[1] b[2] a[2] b[1]
               minor a[2] b[0] a[0] b[2]
               minor a[0] b[1] a[1] b[0] |]
            c

    /// The four sums `4w²`, `4x²`, `4y²`, `4z²` of a rotation's diagonal, each from `1.0` left to right.
    let private diagonalSums (e: int -> int -> float) : float[] =
        [| ((1.0 + e 0 0) + e 1 1) + e 2 2
           ((1.0 + e 0 0) - e 1 1) - e 2 2
           ((1.0 - e 0 0) + e 1 1) - e 2 2
           ((1.0 - e 0 0) - e 1 1) + e 2 2 |]

    /// `(w, x, y, z)` from the component `pivot`, whose sum is `u`: half its root, and each other
    /// component a sum or a difference of two entries over twice that root.
    let private rotorFrom (divide: float -> float -> float) (e: int -> int -> float) (pivot: int) (u: float) : float[] =
        let h = sqrt u
        let half = 0.5 * h
        let d = h + h

        match pivot with
        | 0 ->
            [| half
               divide (e 2 1 - e 1 2) d
               divide (e 0 2 - e 2 0) d
               divide (e 1 0 - e 0 1) d |]
        | 1 ->
            [| divide (e 2 1 - e 1 2) d
               half
               divide (e 1 0 + e 0 1) d
               divide (e 0 2 + e 2 0) d |]
        | 2 ->
            [| divide (e 0 2 - e 2 0) d
               divide (e 1 0 + e 0 1) d
               half
               divide (e 2 1 + e 1 2) d |]
        | _ ->
            [| divide (e 1 0 - e 0 1) d
               divide (e 0 2 + e 2 0) d
               divide (e 2 1 + e 1 2) d
               half |]

    /// The rotor of a rotation from the largest of the four sums, the first in a tie.
    let rotorByLargestSum (divide: float -> float -> float) (e: int -> int -> float) : float[] =
        let u = diagonalSums e
        let mutable pivot = 0

        for k in 1..3 do
            if u[k] > u[pivot] then
                pivot <- k

        rotorFrom divide e pivot u[pivot]

    /// The rotor of a rotation from the scalar's sum always: measured, and not shipped (D88).
    let rotorByScalarSum (divide: float -> float -> float) (e: int -> int -> float) : float[] =
        let u = diagonalSums e
        rotorFrom divide e 0 u[0]

    let canonical (q: float[]) : float[] =
        let magnitude (x: float) : float = if x > -x then x else -x
        let mutable largest = 1

        for k in 2..3 do
            if magnitude q[k] > magnitude q[largest] then
                largest <- k

        if q[0] < 0.0 || (q[0] = 0.0 && q[largest] < 0.0) then
            q |> Array.map (fun x -> -x)
        else
            q

    let private euclidean (m: Multivector) : float[] =
        [| m.Coefficients[2]; m.Coefficients[3]; m.Coefficients[4] |]

    let private less (a: float[]) (b: float[]) : float[] = Array.map2 (fun x y -> x - y) a b

    /// `v∥ = (v ∧ Û) Ũ`, and `v⊥ = v − v∥`.
    let splitByOuter (r: Arithmetic) (v: Multivector) (u: Multivector) (uReversed: Multivector) : float[] * float[] =
        let along = euclidean (r.Geometric (r.Outer v u) uReversed)
        along, less (euclidean v) along

    /// `v⊥ = (v ⌋ Û) Ũ`, and `v∥ = v − v⊥`.
    let splitByContraction
        (r: Arithmetic)
        (v: Multivector)
        (u: Multivector)
        (uReversed: Multivector)
        : float[] * float[] =
        let across = euclidean (r.Geometric (r.Contraction v u) uReversed)
        less (euclidean v) across, across

    /// `v∥ = (v ∧ Û) Ũ` and `v⊥ = (v ⌋ Û) Ũ`, each by its own projection.
    let splitByBoth (r: Arithmetic) (v: Multivector) (u: Multivector) (uReversed: Multivector) : float[] * float[] =
        euclidean (r.Geometric (r.Outer v u) uReversed), euclidean (r.Geometric (r.Contraction v u) uReversed)

    /// `(sin φ / φ) v⊥` as what is left of the motor's `e_i ∧ n∞` coefficients: `m_i − c × v∥_i`.
    let acrossBySubtraction
        (_: Arithmetic)
        (c: float)
        (m: float[])
        (along: float[])
        (_: Multivector)
        (_: Multivector)
        : float[] =
        Array.map2
            (fun x a ->
                let p = c * a
                x - p)
            m
            along

    /// `(sin φ / φ) v⊥` as the projection of those coefficients onto the rotation's plane,
    /// `(m ⌋ Û) Ũ`.
    let acrossByProjection
        (r: Arithmetic)
        (_: float)
        (m: float[])
        (_: float[])
        (u: Multivector)
        (uReversed: Multivector)
        : float[] =
        let vector =
            Kernel.adopt (Array.init Basis.count (fun index -> if index >= 2 && index <= 4 then m[index - 2] else 0.0))

        euclidean (r.Geometric (r.Contraction vector u) uReversed)

/// The index sets of DECISIONS.md D85 item 8's table and the groups of item 6, read off the blades.
module internal Indices =

    let private holds (vector: int) (index: int) : bool =
        List.contains vector Basis.blades[index]

    let private origin = 0

    let private infinity = 4

    let private ofGrade (g: int) : int[] =
        [| for index in 0 .. Basis.count - 1 do
               if Basis.grade[index] = g then
                   yield index |]

    let point = ofGrade 1
    let pair = ofGrade 2
    let circle = ofGrade 3
    let sphere = ofGrade 4
    let line = circle |> Array.filter (holds infinity)
    let plane = sphere |> Array.filter (holds infinity)

    /// The versors' (D88), here since Phase 730 so that the specialised products can read them.
    let rotor = [| 0; 10; 11; 13 |]
    let translator = [| 0; 12; 14; 15 |]
    let motor = [| 0; 10; 11; 12; 13; 14; 15; 30 |]
    let dilator = [| 0; 9; 12; 14; 15 |]
    let reflection = [| 2; 3; 4; 5 |]

    /// A type's blades that hold `n0` and not `n∞`; neither; both; `n∞` and not `n0` (item 6).
    let groups (indices: int[]) : int[] * int[] * int[] * int[] =
        let group (withOrigin: bool) (withInfinity: bool) =
            indices
            |> Array.filter (fun index -> holds origin index = withOrigin && holds infinity index = withInfinity)

        group true false, group false false, group true true, group false true

/// The specialised products this package keeps (Phase 730, DECISIONS.md D89): pairs of operand types
/// whose geometric product the typed functions form, each kept because the committed benchmark shows
/// it faster than the general product by D89's margin on .NET and under node. A product of any other
/// pair of types, or of a value of no type, is the general product.
module internal Specialised =

    let private point = Kernel.maskOf Indices.point

    let private rotor = Kernel.maskOf Indices.rotor

    let private translator = Kernel.maskOf Indices.translator

    let private motor = Kernel.maskOf Indices.motor

    /// The two products of the sandwich `(V X) Ṽ`: the versor's type with the object's, and the type
    /// of that answer with the versor's, which is its reverse's.
    let private sandwich (v: int) (x: int) : Kernel.Specialisation list =
        let first = Kernel.specialise v x
        [ first; Kernel.specialise first.Reached v ]

    /// The pairs, each with its terms.
    let shapes: Kernel.Specialisation[] =
        [| yield Kernel.specialise rotor rotor
           yield Kernel.specialise motor motor
           yield Kernel.specialise translator rotor
           yield! sandwich rotor point
           yield! sandwich translator point
           yield! sandwich motor point |]

    /// The geometric product as the typed functions form it: specialised where `shapes` holds the
    /// operands' types and both are admitted, and the general product otherwise. Every answer is the
    /// general product's, bit for bit.
    let geometric (a: Multivector) (b: Multivector) : Multivector =
        Kernel.specialisedWith 0.0 Kernel.twoRoundings id true shapes a b

module internal Arithmetic =

    /// The arithmetic DECISIONS.md D85 records. Every comparison of this package's objects with a
    /// float literal is here, in `Conformal.threshold`, `Conformal.radius` and `Conformal.against`.
    let shipped: Arithmetic =
        { Geometric = Specialised.geometric
          Outer = Multivector.outerProduct
          Contraction = Multivector.leftContraction
          Scalar = Multivector.scalarProduct
          Divide = fun x y -> x / y
          OverBoth = fun divide numerator first second -> divide numerator (first * second)
          Scaled = Norm.scaledWith
          SumOfSquares = Norm.foldedSquares
          Chain = fun product factors -> List.reduce product factors
          HalfSquaredLength = fun x1 x2 x3 -> 0.5 * ((x1 * x1 + x2 * x2) + x3 * x3)
          HalfSquare = fun r -> 0.5 * (r * r)
          MinusTwice = fun s -> -2.0 * s
          IsZeroDivisor = fun d -> d = 0.0
          IsZeroNorm = fun n -> n = 0.0
          Above = fun s tau -> s > tau
          Within = fun s tau -> s <= tau && -s <= tau
          SignsDecide = fun tau -> tau = 0.0
          SinCos = Fuaran.Compute.Elementary.Elementary.sinCos
          Atan2 = Fuaran.Compute.Elementary.Elementary.atan2
          VersorReverse = Multivector.reverse
          Compose = fun product b a -> product b a
          Screw = Forms.screw
          ByQuotient = Forms.byQuotient
          IsZeroAngle = fun phi -> phi = 0.0
          IsZeroBivector = fun n -> n = 0.0
          DilatorParts = Forms.dilatorParts
          Between = id
          SplitAlong = Forms.splitByOuter
          MotorAcross = Forms.acrossBySubtraction
          MatrixIndex = fun row column -> 4 * column + row
          Dot = Forms.dot
          Orientation = Forms.orientation
          RotorOfRotation = Forms.rotorByLargestSum
          Canonical = Forms.canonical
          UnitAsGiven = id
          PointAsGiven = fun p _ -> p
          Placed = id
          NormaliseOnce = fun once values -> once values }

/// What a typed blade is by the norms of its groups, before a round's squared radius is read.
[<RequireQualifiedAccess>]
type internal Shape =
    | Round
    | Flat
    | AtInfinity
    | Zero
    | NoObject

module internal Conformal =

    // ---- widening and the copy back (D85 item 4) -------------------------------------------------

    /// The multivector that holds `values` at `indices` and `+0.0` elsewhere, a value of the type
    /// whose blades are `indices`.
    let widen (indices: int[]) (values: float[]) : Multivector =
        let xs = Array.zeroCreate<float> Basis.count

        for k in 0 .. indices.Length - 1 do
            xs[indices[k]] <- values[k]

        Kernel.adoptAs (Kernel.maskOf indices) xs

    /// The coefficients of `m` at `indices`: the copy that discards the rest without a test.
    let copy (indices: int[]) (m: Multivector) : float[] =
        indices |> Array.map (fun index -> m.Coefficients[index])

    let pointValues (p: Point) : float[] = [| p.N0; p.E1; p.E2; p.E3; p.NInf |]

    let pointOf (v: float[]) : Point =
        { N0 = v[0]
          E1 = v[1]
          E2 = v[2]
          E3 = v[3]
          NInf = v[4] }

    let pairValues (x: PointPair) : float[] =
        [| x.N0E1
           x.N0E2
           x.N0E3
           x.N0NInf
           x.E1E2
           x.E1E3
           x.E1NInf
           x.E2E3
           x.E2NInf
           x.E3NInf |]

    let pairOf (v: float[]) : PointPair =
        { N0E1 = v[0]
          N0E2 = v[1]
          N0E3 = v[2]
          N0NInf = v[3]
          E1E2 = v[4]
          E1E3 = v[5]
          E1NInf = v[6]
          E2E3 = v[7]
          E2NInf = v[8]
          E3NInf = v[9] }

    let circleValues (x: Circle) : float[] =
        [| x.N0E1E2
           x.N0E1E3
           x.N0E1NInf
           x.N0E2E3
           x.N0E2NInf
           x.N0E3NInf
           x.E1E2E3
           x.E1E2NInf
           x.E1E3NInf
           x.E2E3NInf |]

    let circleOf (v: float[]) : Circle =
        { N0E1E2 = v[0]
          N0E1E3 = v[1]
          N0E1NInf = v[2]
          N0E2E3 = v[3]
          N0E2NInf = v[4]
          N0E3NInf = v[5]
          E1E2E3 = v[6]
          E1E2NInf = v[7]
          E1E3NInf = v[8]
          E2E3NInf = v[9] }

    let lineValues (x: Line) : float[] =
        [| x.N0E1NInf; x.N0E2NInf; x.N0E3NInf; x.E1E2NInf; x.E1E3NInf; x.E2E3NInf |]

    let lineOf (v: float[]) : Line =
        { N0E1NInf = v[0]
          N0E2NInf = v[1]
          N0E3NInf = v[2]
          E1E2NInf = v[3]
          E1E3NInf = v[4]
          E2E3NInf = v[5] }

    let sphereValues (x: Sphere) : float[] =
        [| x.N0E1E2E3; x.N0E1E2NInf; x.N0E1E3NInf; x.N0E2E3NInf; x.E1E2E3NInf |]

    let sphereOf (v: float[]) : Sphere =
        { N0E1E2E3 = v[0]
          N0E1E2NInf = v[1]
          N0E1E3NInf = v[2]
          N0E2E3NInf = v[3]
          E1E2E3NInf = v[4] }

    let planeValues (x: Plane) : float[] =
        [| x.N0E1E2NInf; x.N0E1E3NInf; x.N0E2E3NInf; x.E1E2E3NInf |]

    let planeOf (v: float[]) : Plane =
        { N0E1E2NInf = v[0]
          N0E1E3NInf = v[1]
          N0E2E3NInf = v[2]
          E1E2E3NInf = v[3] }

    let pointMv (p: Point) : Multivector = widen Indices.point (pointValues p)
    let pairMv (x: PointPair) : Multivector = widen Indices.pair (pairValues x)
    let circleMv (x: Circle) : Multivector = widen Indices.circle (circleValues x)
    let lineMv (x: Line) : Multivector = widen Indices.line (lineValues x)
    let sphereMv (x: Sphere) : Multivector = widen Indices.sphere (sphereValues x)
    let planeMv (x: Plane) : Multivector = widen Indices.plane (planeValues x)

    let mvPoint (m: Multivector) : Point = pointOf (copy Indices.point m)
    let mvPair (m: Multivector) : PointPair = pairOf (copy Indices.pair m)
    let mvCircle (m: Multivector) : Circle = circleOf (copy Indices.circle m)
    let mvLine (m: Multivector) : Line = lineOf (copy Indices.line m)
    let mvSphere (m: Multivector) : Sphere = sphereOf (copy Indices.sphere m)
    let mvPlane (m: Multivector) : Plane = planeOf (copy Indices.plane m)

    let n0 = Multivector.basis 1

    let private nInf = Multivector.basis 5

    /// The vector `w n0 + a e1 + b e2 + c e3 + d n∞`.
    let vector (w: float) (a: float) (b: float) (c: float) (d: float) : Multivector =
        widen Indices.point [| w; a; b; c; d |]

    // ---- what is refused (D85 items 4, 5 and 6) ---------------------------------------------------

    let finite (x: float) : bool =
        not (Double.IsNaN x || Double.IsInfinity x)

    /// An `Ok` never holds a NaN.
    let private number (x: float) : Result<float, GeometryRefusal> =
        if Double.IsNaN x then
            Error GeometryRefusal.NotANumber
        else
            Ok x

    let numbers (values: float[]) : Result<float[], GeometryRefusal> =
        if Array.exists Double.IsNaN values then
            Error GeometryRefusal.NotANumber
        else
            Ok values

    let private triple (x1: float, x2: float, x3: float) : Result<float * float * float, GeometryRefusal> =
        if Double.IsNaN x1 || Double.IsNaN x2 || Double.IsNaN x3 then
            Error GeometryRefusal.NotANumber
        else
            Ok(x1, x2, x3)

    /// A divisor is refused when it is zero or not finite: exact comparisons, no threshold.
    let divisor (r: Arithmetic) (d: float) : Result<float, GeometryRefusal> =
        if r.IsZeroDivisor d then
            Error GeometryRefusal.ZeroDivisor
        elif not (finite d) then
            Error GeometryRefusal.NonFiniteDivisor
        else
            Ok d

    /// A threshold is a finite `τ ≥ 0`.
    let private threshold (tau: float) : Result<float, GeometryRefusal> =
        if tau >= 0.0 && finite tau then
            Ok tau
        else
            Error GeometryRefusal.InvalidThreshold

    /// A radius is finite and not negative.
    let private radius (rho: float) : Result<float, GeometryRefusal> =
        if not (finite rho) then
            Error GeometryRefusal.NonFiniteRadius
        elif rho < 0.0 then
            Error GeometryRefusal.NegativeRadius
        else
            Ok rho

    /// A direction parameter divided by its norm, one division for each component.
    let unit (r: Arithmetic) (d1: float, d2: float, d3: float) : Result<float * float * float, GeometryRefusal> =
        let n = r.Scaled r.SumOfSquares [| d1; d2; d3 |]

        if r.IsZeroNorm n then
            Error GeometryRefusal.ZeroDirection
        elif not (finite n) then
            Error GeometryRefusal.NonFiniteDirection
        else
            Ok(r.Divide d1 n, r.Divide d2 n, r.Divide d3 n)

    // ---- the point ----------------------------------------------------------------------------------

    /// `up x = n0 + x + ½ x² n∞`.
    let up (r: Arithmetic) (x1: float, x2: float, x3: float) : Result<Point, GeometryRefusal> =
        if finite x1 && finite x2 && finite x3 then
            let weightOne = 1.0

            Ok
                { N0 = weightOne
                  E1 = x1
                  E2 = x2
                  E3 = x3
                  NInf = r.HalfSquaredLength x1 x2 x3 }
        else
            Error GeometryRefusal.NonFiniteCoordinate

    /// A vector read as a point is: its indices 2 to 4, each divided by its index 1.
    let private readAsPoint (r: Arithmetic) (v: Multivector) : Result<float * float * float, GeometryRefusal> =
        let c = v.Coefficients

        divisor r c[1]
        |> Result.bind (fun w -> triple (r.Divide c[2] w, r.Divide c[3] w, r.Divide c[4] w))

    let coordinates (r: Arithmetic) (p: Point) : Result<float * float * float, GeometryRefusal> =
        readAsPoint r (pointMv p)

    /// Every coefficient a type carries divided by one scalar, once (D85 item 5).
    let normalise (r: Arithmetic) (weight: float) (values: float[]) : Result<float[], GeometryRefusal> =
        divisor r weight
        |> Result.bind (fun w -> numbers (values |> Array.map (fun v -> r.Divide v w)))

    // ---- the blade operations (D85 item 4) ----------------------------------------------------------

    /// The meet `B* ⌋ A`: the dual of the second operand, by the permutation, contracted onto the first.
    let meet (r: Arithmetic) (a: Multivector) (b: Multivector) : Multivector = r.Contraction (Multivector.dual b) a

    /// A blade's inverse `X̃ / (X ∗ X̃)`, each coefficient divided once.
    let inverse (r: Arithmetic) (x: Multivector) : Result<Multivector, GeometryRefusal> =
        let reversed = Multivector.reverse x

        divisor r (r.Scalar x reversed)
        |> Result.bind (fun d -> numbers (reversed.Coefficients |> Array.map (fun c -> r.Divide c d)))
        |> Result.map Kernel.adopt

    /// The meet within the span `J`: `(B ⌋ J⁻¹) ⌋ A`.
    let meetWithin
        (r: Arithmetic)
        (j: Multivector)
        (a: Multivector)
        (b: Multivector)
        : Result<Multivector, GeometryRefusal> =
        inverse r j
        |> Result.bind (fun inverted ->
            let within = r.Contraction (r.Contraction b inverted) a
            numbers within.Coefficients |> Result.map (fun _ -> within))

    // ---- read-back (D85 item 4) ---------------------------------------------------------------------

    /// The weight of a blade with these blades: the norm of one group of its coefficients.
    let private groupNorm (r: Arithmetic) (group: int[]) (m: Multivector) : float =
        r.Scaled r.SumOfSquares (copy group m)

    /// The weight of a point pair or a circle: the norm on the blades that hold `n0` and not `n∞`.
    let roundWeight (r: Arithmetic) (indices: int[]) (m: Multivector) : float =
        let o, _, _, _ = Indices.groups indices
        groupNorm r o m

    /// The weight of a flat: the norm on the blades that hold both.
    let flatWeight (r: Arithmetic) (indices: int[]) (m: Multivector) : float =
        let _, _, b, _ = Indices.groups indices
        groupNorm r b m

    /// The centre of a round: the point `⟨(X n∞) X⟩₁`, read as a point is.
    let centre (r: Arithmetic) (x: Multivector) : Result<float * float * float, GeometryRefusal> =
        readAsPoint r (r.Chain r.Geometric [ x; nInf; x ])

    /// The numerator `−(X ∗ X̃)` and the divisor `W ∗ W̃`, `W = n∞ ⌋ X`, of a round's squared radius.
    /// Refused where the divisor is zero or not finite, or the numerator a NaN.
    let private squaredRadiusParts (r: Arithmetic) (x: Multivector) : Result<float * float, GeometryRefusal> =
        let numerator = -(r.Scalar x (Multivector.reverse x))
        let w = r.Contraction nInf x

        divisor r (r.Scalar w (Multivector.reverse w))
        |> Result.bind (fun d ->
            if Double.IsNaN numerator then
                Error GeometryRefusal.NotANumber
            else
                Ok(numerator, d))

    /// A statistic that is a quotient, against `τ` (D85 item 6): `+1` above it, `0` within it, `−1`
    /// below `−τ`. At `τ = 0` the signs of the numerator and the divisor decide, and the quotient is
    /// not formed: a quotient that underflows to a zero is not a zero statistic.
    let against (r: Arithmetic) (tau: float) (numerator: float) (d: float) (quotient: unit -> float) : int =
        if r.SignsDecide tau then
            if numerator = 0.0 then 0
            elif (numerator < 0.0) = (d < 0.0) then 1
            else -1
        else
            let q = quotient ()

            if r.Above q tau then 1
            elif r.Within q tau then 0
            else -1

    /// The squared radius of a round: `−(X ∗ X̃) / (W ∗ W̃)` with `W = n∞ ⌋ X`.
    let squaredRadius (r: Arithmetic) (x: Multivector) : Result<float, GeometryRefusal> =
        squaredRadiusParts r x
        |> Result.bind (fun (numerator, d) -> number (r.Divide numerator d))

    /// The point of a flat nearest the origin: the coordinates of the vector `⟨(n0 ⌋ X) X⁻¹⟩₁`.
    let nearestPoint (r: Arithmetic) (x: Multivector) : Result<float * float * float, GeometryRefusal> =
        inverse r x
        |> Result.bind (fun inverted -> readAsPoint r (r.Geometric (r.Contraction n0 x) inverted))

    /// A point pair's two points, decided by the squared radius before any root is taken.
    let pairPoints (r: Arithmetic) (tau: float) (pair: PointPair) : Result<PairPoints, GeometryRefusal> =
        let x = pairMv pair

        threshold tau
        |> Result.bind (fun tau ->
            squaredRadiusParts r x
            |> Result.bind (fun (numerator, d) ->
                let verdict = against r tau numerator d (fun () -> r.Divide numerator d)

                if verdict > 0 then
                    inverse r (r.Contraction nInf x)
                    |> Result.bind (fun inverted ->
                        let s = Multivector.ofScalar (sqrt (r.Scalar x x))
                        let first = copy Indices.point (r.Geometric (Multivector.subtract x s) inverted)
                        let second = copy Indices.point (r.Geometric (Multivector.add x s) inverted)

                        numbers (Array.append first second)
                        |> Result.map (fun _ -> PairPoints.Real(pointOf first, pointOf second)))
                elif verdict = 0 then
                    centre r x |> Result.map PairPoints.Tangent
                else
                    // What an imaginary pair carries is its squared radius, the quotient (D85 item 4),
                    // whatever decided that it is imaginary.
                    centre r x
                    |> Result.bind (fun c ->
                        number (r.Divide numerator d)
                        |> Result.map (fun rho -> PairPoints.Imaginary(c, rho)))))

    // ---- what kind of blade an answer is (D85 item 6) ----------------------------------------------

    /// The kind of a blade of one type, by the norms of its four groups against `τ`. A blade that holds
    /// a NaN is no object.
    let shape (r: Arithmetic) (tau: float) (indices: int[]) (m: Multivector) : Shape =
        let o, n, b, i = Indices.groups indices
        let onOrigin = groupNorm r o m
        let onNeither = groupNorm r n m
        let onBoth = groupNorm r b m
        let onInfinity = groupNorm r i m
        let none (norm: float) = r.Within norm tau

        if Array.exists Double.IsNaN (copy indices m) then
            Shape.NoObject
        elif r.Above onOrigin tau then
            Shape.Round
        elif none onOrigin && none onNeither && r.Above onBoth tau then
            Shape.Flat
        elif none onOrigin && none onNeither && none onBoth && r.Above onInfinity tau then
            Shape.AtInfinity
        elif none onOrigin && none onNeither && none onBoth && none onInfinity then
            Shape.Zero
        else
            Shape.NoObject

    /// A round by its squared radius against `τ`.
    let private reality (r: Arithmetic) (tau: float) (x: Multivector) : Result<Reality, GeometryRefusal> =
        squaredRadiusParts r x
        |> Result.map (fun (numerator, d) ->
            let verdict = against r tau numerator d (fun () -> r.Divide numerator d)

            if verdict > 0 then Reality.Real
            elif verdict = 0 then Reality.Tangent
            else Reality.Imaginary)

    let pairKind (r: Arithmetic) (tau: float) (pair: PointPair) : Result<PointPairKind, GeometryRefusal> =
        let x = pairMv pair

        threshold tau
        |> Result.bind (fun tau ->
            match shape r tau Indices.pair x with
            | Shape.Round -> reality r tau x |> Result.map PointPairKind.Round
            | Shape.Flat -> nearestPoint r x |> Result.map PointPairKind.FlatPoint
            | Shape.AtInfinity -> Ok PointPairKind.AtInfinity
            | Shape.Zero -> Ok PointPairKind.Zero
            | Shape.NoObject -> Ok PointPairKind.NoObject)

    let circleKind (r: Arithmetic) (tau: float) (circle: Circle) : Result<CircleKind, GeometryRefusal> =
        let x = circleMv circle

        threshold tau
        |> Result.bind (fun tau ->
            match shape r tau Indices.circle x with
            | Shape.Round -> reality r tau x |> Result.map CircleKind.Round
            | Shape.Flat -> Ok(CircleKind.Flat(mvLine x))
            | Shape.AtInfinity -> Ok CircleKind.AtInfinity
            | Shape.Zero -> Ok CircleKind.Zero
            | Shape.NoObject -> Ok CircleKind.NoObject)

    let sphereKind (r: Arithmetic) (tau: float) (sphere: Sphere) : Result<SphereKind, GeometryRefusal> =
        let x = sphereMv sphere

        threshold tau
        |> Result.bind (fun tau ->
            match shape r tau Indices.sphere x with
            | Shape.Round -> reality r tau x |> Result.map SphereKind.Round
            | Shape.Flat -> Ok(SphereKind.Flat(mvPlane x))
            | Shape.AtInfinity -> Ok SphereKind.AtInfinity
            | Shape.Zero -> Ok SphereKind.Zero
            | Shape.NoObject -> Ok SphereKind.NoObject)

    let flatKind (r: Arithmetic) (tau: float) (indices: int[]) (x: Multivector) : Result<FlatKind, GeometryRefusal> =
        threshold tau
        |> Result.map (fun tau ->
            match shape r tau indices x with
            | Shape.Flat -> FlatKind.Flat
            | Shape.AtInfinity -> FlatKind.AtInfinity
            | Shape.Zero -> FlatKind.Zero
            | Shape.Round
            | Shape.NoObject -> FlatKind.NoObject)

    // ---- construction from parameters (D85 item 4) --------------------------------------------------

    /// `(n̂ + δ n∞)*` for a normal already of length one.
    let private planeOfUnit (n1: float, n2: float, n3: float) (delta: float) : Multivector =
        Multivector.dual (vector 0.0 n1 n2 n3 delta)

    /// `(up c − ½ r² n∞)*`: `½ r²` subtracted from the coefficient of `up c` at index 5.
    let private sphereMvOf
        (r: Arithmetic)
        (c: float * float * float)
        (rho: float)
        : Result<Multivector, GeometryRefusal> =
        up r c
        |> Result.bind (fun p ->
            radius rho
            |> Result.bind (fun rho ->
                let dualised =
                    Multivector.dual (vector p.N0 p.E1 p.E2 p.E3 (p.NInf - r.HalfSquare rho))

                numbers dualised.Coefficients |> Result.map (fun _ -> dualised)))

    let sphereOfCentreRadius (r: Arithmetic) (c: float * float * float) (rho: float) : Result<Sphere, GeometryRefusal> =
        sphereMvOf r c rho |> Result.map mvSphere

    let planeOfNormalDistance
        (r: Arithmetic)
        (normal: float * float * float)
        (delta: float)
        : Result<Plane, GeometryRefusal> =
        unit r normal
        |> Result.bind (fun n ->
            if finite delta then
                Ok(mvPlane (planeOfUnit n delta))
            else
                Error GeometryRefusal.NonFiniteDistance)

    /// `up p ∧ d̂ ∧ n∞`, left to right.
    let lineOfPointDirection
        (r: Arithmetic)
        (p: float * float * float)
        (direction: float * float * float)
        : Result<Line, GeometryRefusal> =
        up r p
        |> Result.bind (fun point ->
            unit r direction
            |> Result.bind (fun (d1, d2, d3) ->
                let through = r.Chain r.Outer [ pointMv point; vector 0.0 d1 d2 d3 0.0; nInf ]
                numbers (copy Indices.line through) |> Result.map lineOf))

    /// `meet (sphere c r) (plane n̂ (n̂ · c))`: the circle runs counter-clockwise about its normal.
    let circleOfCentreRadiusNormal
        (r: Arithmetic)
        (c: float * float * float)
        (rho: float)
        (normal: float * float * float)
        : Result<Circle, GeometryRefusal> =
        up r c
        |> Result.bind (fun _ -> radius rho)
        |> Result.bind (fun _ -> unit r normal)
        |> Result.bind (fun (n1, n2, n3) ->
            sphereMvOf r c rho
            |> Result.bind (fun sphere ->
                let c1, c2, c3 = c
                let delta = r.Scalar (vector 0.0 n1 n2 n3 0.0) (vector 0.0 c1 c2 c3 0.0)
                let through = meet r sphere (planeOfUnit (n1, n2, n3) delta)
                numbers (copy Indices.circle through) |> Result.map circleOf))

    // ---- through points: outer products, left to right ---------------------------------------------

    let through (r: Arithmetic) (points: Point list) (closing: Multivector list) : Multivector =
        match (points |> List.map pointMv) @ closing with
        | [] -> Multivector.zero
        | factors -> r.Chain r.Outer factors

    let closedAtInfinity: Multivector list = [ nInf ]

    /// A plane's normal: the Euclidean part of its vector, the undual, an exact read.
    let planeNormal (plane: Plane) : float * float * float =
        let v = (Multivector.undual (planeMv plane)).Coefficients
        v[2], v[3], v[4]

    /// A plane's distance from the origin along its normal, times its weight: its vector's `n∞`
    /// coefficient, an exact read.
    let planeDistance (plane: Plane) : float =
        (Multivector.undual (planeMv plane)).Coefficients[5]

    /// A circle's plane `X ∧ n∞`.
    let carrier (r: Arithmetic) (x: Multivector) : Multivector = r.Outer x nInf

    // ---- two lines (D85 item 4) ---------------------------------------------------------------------

    /// "Is the zero blade": the test of a kind, on a blade of one type.
    let private isZeroBlade (r: Arithmetic) (tau: float) (indices: int[]) (m: Multivector) : bool =
        shape r tau indices m = Shape.Zero

    /// What two lines meet in. Their meet in the whole space is the zero blade unless they span it.
    /// Otherwise the span is the join of the first with a point of the second: `up` of the coordinates
    /// of its point nearest the origin, or, when that join is the zero blade, `up` of those coordinates
    /// plus the second line's direction as it is stored, one addition for each; when that join is the
    /// zero blade too, the lines are one line.
    let lineMeet (r: Arithmetic) (tau: float) (first: Line) (second: Line) : Result<LineMeet, GeometryRefusal> =
        let a = lineMv first
        let b = lineMv second

        threshold tau
        |> Result.bind (fun tau ->
            let inSpace = widen Indices.point (copy Indices.point (meet r a b))

            if not (isZeroBlade r tau Indices.point inSpace) then
                numbers (copy Indices.point inSpace)
                |> Result.map (fun values -> LineMeet.Skew(pointOf values))
            else
                let spanThrough (q: float * float * float) : Result<Multivector, GeometryRefusal> =
                    up r q
                    |> Result.map (fun point -> widen Indices.plane (copy Indices.plane (r.Outer a (pointMv point))))

                let within (span: Multivector) : Result<LineMeet, GeometryRefusal> =
                    meetWithin r span a b
                    |> Result.map (fun m -> LineMeet.Within(pairOf (copy Indices.pair m)))

                nearestPoint r b
                |> Result.bind (fun (q1, q2, q3) ->
                    spanThrough (q1, q2, q3)
                    |> Result.bind (fun span ->
                        if not (isZeroBlade r tau Indices.plane span) then
                            within span
                        else
                            spanThrough (q1 + second.N0E1NInf, q2 + second.N0E2NInf, q3 + second.N0E3NInf)
                            |> Result.bind (fun other ->
                                if isZeroBlade r tau Indices.plane other then
                                    Ok LineMeet.SameLine
                                else
                                    within other))))

    // ---- incidence (D85 item 6) ---------------------------------------------------------------------

    /// A statistic's value: the numerator over the product of the divisor's two factors, one division.
    let private statistic
        (r: Arithmetic)
        (numerator: float)
        (first: float)
        (second: float)
        : Result<float, GeometryRefusal> =
        divisor r (first * second)
        |> Result.bind (fun _ -> number (r.OverBoth r.Divide numerator first second))

    /// A statistic's kind: `−1` below `−τ`, `0` within `τ`, `+1` above it. At `τ = 0` the signs of the
    /// numerator and the denominator decide and no quotient is formed.
    let private decide
        (r: Arithmetic)
        (tau: float)
        (numerator: float)
        (first: float)
        (second: float)
        : Result<int, GeometryRefusal> =
        threshold tau
        |> Result.bind (fun tau ->
            divisor r (first * second)
            |> Result.bind (fun d ->
                if Double.IsNaN numerator then
                    Error GeometryRefusal.NotANumber
                else
                    Ok(against r tau numerator d (fun () -> r.OverBoth r.Divide numerator first second))))

    /// A point against a sphere: the numerator `−2.0 × (P ∗ σ)` and the two weights.
    let private spherePowerParts (r: Arithmetic) (p: Point) (sphere: Sphere) : float * float * float =
        let sigma = Multivector.undual (sphereMv sphere)
        r.MinusTwice(r.Scalar (pointMv p) sigma), p.N0, sphere.N0E1E2E3

    /// A point against a plane: the numerator `P ∗ π` and the two weights.
    let private planeDistanceParts (r: Arithmetic) (p: Point) (plane: Plane) : float * float * float =
        let m = planeMv plane
        r.Scalar (pointMv p) (Multivector.undual m), p.N0, flatWeight r Indices.plane m

    /// A point against a circle, in the circle's plane `Y = X ∧ n∞`: the numerator
    /// `−2.0 × ((P ∧ X) ∗ Ỹ)`, the point's weight and `Y ∗ Ỹ`.
    let private circlePowerParts (r: Arithmetic) (p: Point) (circle: Circle) : float * float * float =
        let x = circleMv circle
        let y = Multivector.reverse (carrier r x)
        r.MinusTwice(r.Scalar (r.Outer (pointMv p) x) y), p.N0, r.Scalar (carrier r x) y

    let private side (verdict: int) : Side =
        if verdict < 0 then Side.Inside
        elif verdict > 0 then Side.Outside
        else Side.On

    let spherePower (r: Arithmetic) (p: Point) (sphere: Sphere) : Result<float, GeometryRefusal> =
        let numerator, first, second = spherePowerParts r p sphere
        statistic r numerator first second

    let sphereSide (r: Arithmetic) (tau: float) (p: Point) (sphere: Sphere) : Result<Side, GeometryRefusal> =
        let numerator, first, second = spherePowerParts r p sphere
        decide r tau numerator first second |> Result.map side

    let circlePower (r: Arithmetic) (p: Point) (circle: Circle) : Result<float, GeometryRefusal> =
        let numerator, first, second = circlePowerParts r p circle
        statistic r numerator first second

    let circleSide (r: Arithmetic) (tau: float) (p: Point) (circle: Circle) : Result<Side, GeometryRefusal> =
        let numerator, first, second = circlePowerParts r p circle
        decide r tau numerator first second |> Result.map side

    let planeSignedDistance (r: Arithmetic) (p: Point) (plane: Plane) : Result<float, GeometryRefusal> =
        let numerator, first, second = planeDistanceParts r p plane
        statistic r numerator first second

    let planeSide (r: Arithmetic) (tau: float) (p: Point) (plane: Plane) : Result<PlaneSide, GeometryRefusal> =
        let numerator, first, second = planeDistanceParts r p plane

        decide r tau numerator first second
        |> Result.map (fun verdict ->
            if verdict < 0 then PlaneSide.Negative
            elif verdict > 0 then PlaneSide.Positive
            else PlaneSide.On)

    // ---- the typed operations: the general operation, then a copy of the result type's coefficients --
    //
    // One for each published function that computes. The published function is this one under
    // `Arithmetic.shipped`, and the suite's witness is this one under another arithmetic.

    let join (r: Arithmetic) (a: Multivector) (b: Multivector) : Multivector = r.Outer a b

    let pointNormalise (r: Arithmetic) (p: Point) : Result<Point, GeometryRefusal> =
        r.NormaliseOnce (fun v -> normalise r v[0] v) (pointValues p)
        |> Result.map pointOf

    let pairThrough (r: Arithmetic) (p: Point) (q: Point) : PointPair = mvPair (through r [ p; q ] [])

    let pairWeight (r: Arithmetic) (x: PointPair) : float = roundWeight r Indices.pair (pairMv x)

    let pairNormalise (r: Arithmetic) (x: PointPair) : Result<PointPair, GeometryRefusal> =
        r.NormaliseOnce (fun v -> normalise r (pairWeight r (pairOf v)) v) (pairValues x)
        |> Result.map pairOf

    let pairCentre (r: Arithmetic) (x: PointPair) : Result<float * float * float, GeometryRefusal> = centre r (pairMv x)

    let pairSquaredRadius (r: Arithmetic) (x: PointPair) : Result<float, GeometryRefusal> = squaredRadius r (pairMv x)

    let lineThrough (r: Arithmetic) (p: Point) (q: Point) : Line =
        mvLine (through r [ p; q ] closedAtInfinity)

    let lineWeight (r: Arithmetic) (x: Line) : float = flatWeight r Indices.line (lineMv x)

    let lineNormalise (r: Arithmetic) (x: Line) : Result<Line, GeometryRefusal> =
        r.NormaliseOnce (fun v -> normalise r (lineWeight r (lineOf v)) v) (lineValues x)
        |> Result.map lineOf

    let lineNearestPoint (r: Arithmetic) (x: Line) : Result<float * float * float, GeometryRefusal> =
        nearestPoint r (lineMv x)

    let lineKind (r: Arithmetic) (tau: float) (x: Line) : Result<FlatKind, GeometryRefusal> =
        flatKind r tau Indices.line (lineMv x)

    let lineMeetPlane (r: Arithmetic) (x: Line) (plane: Plane) : PointPair =
        mvPair (meet r (lineMv x) (planeMv plane))

    let lineMeetSphere (r: Arithmetic) (x: Line) (sphere: Sphere) : PointPair =
        mvPair (meet r (lineMv x) (sphereMv sphere))

    let circleThrough (r: Arithmetic) (p: Point) (q: Point) (s: Point) : Circle = mvCircle (through r [ p; q; s ] [])

    let circleWeight (r: Arithmetic) (x: Circle) : float =
        roundWeight r Indices.circle (circleMv x)

    let circleNormalise (r: Arithmetic) (x: Circle) : Result<Circle, GeometryRefusal> =
        r.NormaliseOnce (fun v -> normalise r (circleWeight r (circleOf v)) v) (circleValues x)
        |> Result.map circleOf

    let circleCentre (r: Arithmetic) (x: Circle) : Result<float * float * float, GeometryRefusal> =
        centre r (circleMv x)

    let circleSquaredRadius (r: Arithmetic) (x: Circle) : Result<float, GeometryRefusal> = squaredRadius r (circleMv x)

    let circlePlane (r: Arithmetic) (x: Circle) : Plane = mvPlane (carrier r (circleMv x))

    let planeThrough (r: Arithmetic) (p: Point) (q: Point) (s: Point) : Plane =
        mvPlane (through r [ p; q; s ] closedAtInfinity)

    let planeWeight (r: Arithmetic) (x: Plane) : float = flatWeight r Indices.plane (planeMv x)

    let planeNormalise (r: Arithmetic) (x: Plane) : Result<Plane, GeometryRefusal> =
        r.NormaliseOnce (fun v -> normalise r (planeWeight r (planeOf v)) v) (planeValues x)
        |> Result.map planeOf

    let planeNearestPoint (r: Arithmetic) (x: Plane) : Result<float * float * float, GeometryRefusal> =
        nearestPoint r (planeMv x)

    let planeKind (r: Arithmetic) (tau: float) (x: Plane) : Result<FlatKind, GeometryRefusal> =
        flatKind r tau Indices.plane (planeMv x)

    let planeMeet (r: Arithmetic) (first: Plane) (second: Plane) : Line =
        mvLine (meet r (planeMv first) (planeMv second))

    let sphereThrough (r: Arithmetic) (p: Point) (q: Point) (s: Point) (t: Point) : Sphere =
        mvSphere (through r [ p; q; s; t ] [])

    let sphereNormalise (r: Arithmetic) (x: Sphere) : Result<Sphere, GeometryRefusal> =
        r.NormaliseOnce (fun v -> normalise r v[0] v) (sphereValues x)
        |> Result.map sphereOf

    let sphereCentre (r: Arithmetic) (x: Sphere) : Result<float * float * float, GeometryRefusal> =
        centre r (sphereMv x)

    let sphereSquaredRadius (r: Arithmetic) (x: Sphere) : Result<float, GeometryRefusal> = squaredRadius r (sphereMv x)

    let sphereMeet (r: Arithmetic) (first: Sphere) (second: Sphere) : Circle =
        mvCircle (meet r (sphereMv first) (sphereMv second))

    let sphereMeetPlane (r: Arithmetic) (x: Sphere) (plane: Plane) : Circle =
        mvCircle (meet r (sphereMv x) (planeMv plane))
