namespace Fuaran.Compute.GeometricAlgebra

open System

// ============================================================================
//  Phase 728 — the conformal objects, written once, with the arithmetic they
//  are written in as an argument (DECISIONS.md D85 items 4, 5 and 6; D87).
//
//  Every function here takes an `Arithmetic` first: the four products, how a
//  quotient is formed and how a statistic's is, the norm, the two halved
//  squares, the `−2` of a power, what a zero divisor and a zero direction are,
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
//  ones: nothing here is specialised. Nothing normalises an operand or a
//  result (D85 item 5). `sqrt` is the one host function called, twice: in the
//  norm, and for a real point pair's `√(X ∗ X)`.
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
        /// The norm of a group of coefficients (D49 item 4).
        Norm: float[] -> float
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

module internal Arithmetic =

    /// The arithmetic DECISIONS.md D85 records. Every comparison of this package's objects with a
    /// float literal is here, in `Conformal.threshold`, `Conformal.radius` and `Conformal.against`.
    let shipped: Arithmetic =
        { Geometric = Multivector.geometricProduct
          Outer = Multivector.outerProduct
          Contraction = Multivector.leftContraction
          Scalar = Multivector.scalarProduct
          Divide = fun x y -> x / y
          OverBoth = fun divide numerator first second -> divide numerator (first * second)
          Norm = Norm.scaled
          HalfSquaredLength = fun x1 x2 x3 -> 0.5 * ((x1 * x1 + x2 * x2) + x3 * x3)
          HalfSquare = fun r -> 0.5 * (r * r)
          MinusTwice = fun s -> -2.0 * s
          IsZeroDivisor = fun d -> d = 0.0
          IsZeroNorm = fun n -> n = 0.0
          Above = fun s tau -> s > tau
          Within = fun s tau -> s <= tau && -s <= tau
          SignsDecide = fun tau -> tau = 0.0 }

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

    /// A type's blades that hold `n0` and not `n∞`; neither; both; `n∞` and not `n0` (item 6).
    let groups (indices: int[]) : int[] * int[] * int[] * int[] =
        let group (withOrigin: bool) (withInfinity: bool) =
            indices
            |> Array.filter (fun index -> holds origin index = withOrigin && holds infinity index = withInfinity)

        group true false, group false false, group true true, group false true

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

    /// The multivector that holds `values` at `indices` and `+0.0` elsewhere.
    let widen (indices: int[]) (values: float[]) : Multivector =
        let xs = Array.zeroCreate<float> Basis.count

        for k in 0 .. indices.Length - 1 do
            xs[indices[k]] <- values[k]

        Kernel.adopt xs

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

    let private n0 = Multivector.basis 1

    let private nInf = Multivector.basis 5

    /// The vector `w n0 + a e1 + b e2 + c e3 + d n∞`.
    let private vector (w: float) (a: float) (b: float) (c: float) (d: float) : Multivector =
        widen Indices.point [| w; a; b; c; d |]

    // ---- what is refused (D85 items 4, 5 and 6) ---------------------------------------------------

    let private finite (x: float) : bool =
        not (Double.IsNaN x || Double.IsInfinity x)

    /// An `Ok` never holds a NaN.
    let private number (x: float) : Result<float, GeometryRefusal> =
        if Double.IsNaN x then
            Error GeometryRefusal.NotANumber
        else
            Ok x

    let private numbers (values: float[]) : Result<float[], GeometryRefusal> =
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
    let private divisor (r: Arithmetic) (d: float) : Result<float, GeometryRefusal> =
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
    let private unit
        (r: Arithmetic)
        (d1: float, d2: float, d3: float)
        : Result<float * float * float, GeometryRefusal> =
        let n = r.Norm [| d1; d2; d3 |]

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
    let private groupNorm (r: Arithmetic) (group: int[]) (m: Multivector) : float = r.Norm(copy group m)

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
        readAsPoint r (r.Geometric (r.Geometric x nInf) x)

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
    let private against (r: Arithmetic) (tau: float) (numerator: float) (d: float) (quotient: unit -> float) : int =
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
                let through = r.Outer (r.Outer (pointMv point) (vector 0.0 d1 d2 d3 0.0)) nInf
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
        | first :: rest -> rest |> List.fold r.Outer first
        | [] -> Multivector.zero

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
        normalise r p.N0 (pointValues p) |> Result.map pointOf

    let pairThrough (r: Arithmetic) (p: Point) (q: Point) : PointPair = mvPair (through r [ p; q ] [])

    let pairWeight (r: Arithmetic) (x: PointPair) : float = roundWeight r Indices.pair (pairMv x)

    let pairNormalise (r: Arithmetic) (x: PointPair) : Result<PointPair, GeometryRefusal> =
        normalise r (pairWeight r x) (pairValues x) |> Result.map pairOf

    let pairCentre (r: Arithmetic) (x: PointPair) : Result<float * float * float, GeometryRefusal> = centre r (pairMv x)

    let pairSquaredRadius (r: Arithmetic) (x: PointPair) : Result<float, GeometryRefusal> = squaredRadius r (pairMv x)

    let lineThrough (r: Arithmetic) (p: Point) (q: Point) : Line =
        mvLine (through r [ p; q ] closedAtInfinity)

    let lineWeight (r: Arithmetic) (x: Line) : float = flatWeight r Indices.line (lineMv x)

    let lineNormalise (r: Arithmetic) (x: Line) : Result<Line, GeometryRefusal> =
        normalise r (lineWeight r x) (lineValues x) |> Result.map lineOf

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
        normalise r (circleWeight r x) (circleValues x) |> Result.map circleOf

    let circleCentre (r: Arithmetic) (x: Circle) : Result<float * float * float, GeometryRefusal> =
        centre r (circleMv x)

    let circleSquaredRadius (r: Arithmetic) (x: Circle) : Result<float, GeometryRefusal> = squaredRadius r (circleMv x)

    let circlePlane (r: Arithmetic) (x: Circle) : Plane = mvPlane (carrier r (circleMv x))

    let planeThrough (r: Arithmetic) (p: Point) (q: Point) (s: Point) : Plane =
        mvPlane (through r [ p; q; s ] closedAtInfinity)

    let planeWeight (r: Arithmetic) (x: Plane) : float = flatWeight r Indices.plane (planeMv x)

    let planeNormalise (r: Arithmetic) (x: Plane) : Result<Plane, GeometryRefusal> =
        normalise r (planeWeight r x) (planeValues x) |> Result.map planeOf

    let planeNearestPoint (r: Arithmetic) (x: Plane) : Result<float * float * float, GeometryRefusal> =
        nearestPoint r (planeMv x)

    let planeKind (r: Arithmetic) (tau: float) (x: Plane) : Result<FlatKind, GeometryRefusal> =
        flatKind r tau Indices.plane (planeMv x)

    let planeMeet (r: Arithmetic) (first: Plane) (second: Plane) : Line =
        mvLine (meet r (planeMv first) (planeMv second))

    let sphereThrough (r: Arithmetic) (p: Point) (q: Point) (s: Point) (t: Point) : Sphere =
        mvSphere (through r [ p; q; s; t ] [])

    let sphereNormalise (r: Arithmetic) (x: Sphere) : Result<Sphere, GeometryRefusal> =
        normalise r x.N0E1E2E3 (sphereValues x) |> Result.map sphereOf

    let sphereCentre (r: Arithmetic) (x: Sphere) : Result<float * float * float, GeometryRefusal> =
        centre r (sphereMv x)

    let sphereSquaredRadius (r: Arithmetic) (x: Sphere) : Result<float, GeometryRefusal> = squaredRadius r (sphereMv x)

    let sphereMeet (r: Arithmetic) (first: Sphere) (second: Sphere) : Circle =
        mvCircle (meet r (sphereMv first) (sphereMv second))

    let sphereMeetPlane (r: Arithmetic) (x: Sphere) (plane: Plane) : Circle =
        mvCircle (meet r (sphereMv x) (planeMv plane))
