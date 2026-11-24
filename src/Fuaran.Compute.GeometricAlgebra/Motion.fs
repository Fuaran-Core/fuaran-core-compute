namespace Fuaran.Compute.GeometricAlgebra

open System

// ============================================================================
//  Phase 729 — the versors, written once, with the arithmetic they are written
//  in as an argument (DECISIONS.md D85 items 4 and 5; D88).
//
//  Every function here takes an `Arithmetic` first, as the conformal objects'
//  do (D87 item 6), and a published function is this function under
//  `Arithmetic.shipped`. The suite calls the same function with ONE member
//  changed and shows a committed law vector fail.
//
//  A typed operation is the general operation on thirty-two coefficients and
//  then a copy of the result type's coefficients (D85 item 4). Applying a
//  versor is the sandwich with its REVERSE, the left product first. Nothing
//  normalises an operand or a result (D85 item 5): a versor that is not a unit
//  scales what it is applied to. `sqrt` is the one host function called: in a
//  dilator, in a versor's normalisation and in a rotor read from a matrix.
//  The sine, the cosine and the arc tangent are `Fuaran.Compute.Elementary`'s
//  pinned ones.
//
//  Fable-clean.
// ============================================================================

module internal Motion =

    // ---- the index sets of D85 item 8's table, and the copies --------------------------------------

    let rotorIndices = [| 0; 10; 11; 13 |]
    let translatorIndices = [| 0; 12; 14; 15 |]
    let motorIndices = [| 0; 10; 11; 12; 13; 14; 15; 30 |]
    let dilatorIndices = [| 0; 9; 12; 14; 15 |]
    let reflectionIndices = [| 2; 3; 4; 5 |]

    let rotorValues (x: Rotor) : float[] = [| x.Scalar; x.E1E2; x.E1E3; x.E2E3 |]

    let rotorOf (v: float[]) : Rotor =
        { Scalar = v[0]
          E1E2 = v[1]
          E1E3 = v[2]
          E2E3 = v[3] }

    let translatorValues (x: Translator) : float[] =
        [| x.Scalar; x.E1NInf; x.E2NInf; x.E3NInf |]

    let translatorOf (v: float[]) : Translator =
        { Scalar = v[0]
          E1NInf = v[1]
          E2NInf = v[2]
          E3NInf = v[3] }

    let motorValues (x: Motor) : float[] =
        [| x.Scalar
           x.E1E2
           x.E1E3
           x.E1NInf
           x.E2E3
           x.E2NInf
           x.E3NInf
           x.E1E2E3NInf |]

    let motorOf (v: float[]) : Motor =
        { Scalar = v[0]
          E1E2 = v[1]
          E1E3 = v[2]
          E1NInf = v[3]
          E2E3 = v[4]
          E2NInf = v[5]
          E3NInf = v[6]
          E1E2E3NInf = v[7] }

    let dilatorValues (x: Dilator) : float[] =
        [| x.Scalar; x.N0NInf; x.E1NInf; x.E2NInf; x.E3NInf |]

    let dilatorOf (v: float[]) : Dilator =
        { Scalar = v[0]
          N0NInf = v[1]
          E1NInf = v[2]
          E2NInf = v[3]
          E3NInf = v[4] }

    let reflectionValues (x: Reflection) : float[] = [| x.E1; x.E2; x.E3; x.NInf |]

    let reflectionOf (v: float[]) : Reflection =
        { E1 = v[0]
          E2 = v[1]
          E3 = v[2]
          NInf = v[3] }

    let rotorMv (x: Rotor) : Multivector =
        Conformal.widen rotorIndices (rotorValues x)

    let translatorMv (x: Translator) : Multivector =
        Conformal.widen translatorIndices (translatorValues x)

    let motorMv (x: Motor) : Multivector =
        Conformal.widen motorIndices (motorValues x)

    let dilatorMv (x: Dilator) : Multivector =
        Conformal.widen dilatorIndices (dilatorValues x)

    let reflectionMv (x: Reflection) : Multivector =
        Conformal.widen reflectionIndices (reflectionValues x)

    let mvRotor (m: Multivector) : Rotor = rotorOf (Conformal.copy rotorIndices m)

    let mvTranslator (m: Multivector) : Translator =
        translatorOf (Conformal.copy translatorIndices m)

    let mvMotor (m: Multivector) : Motor = motorOf (Conformal.copy motorIndices m)

    let mvDilator (m: Multivector) : Dilator =
        dilatorOf (Conformal.copy dilatorIndices m)

    let mvReflection (m: Multivector) : Reflection =
        reflectionOf (Conformal.copy reflectionIndices m)

    // ---- what is refused ----------------------------------------------------------------------------

    let private finite = Conformal.finite

    let private finite3 (x1: float, x2: float, x3: float) : bool = finite x1 && finite x2 && finite x3

    /// The copy of a product into a type's coefficients, refused where it holds a NaN.
    let private typed (indices: int[]) (m: Multivector) : Result<float[], GeometryRefusal> =
        Conformal.numbers (Conformal.copy indices m)

    // ---- applying a versor (D85 item 4) -------------------------------------------------------------

    /// `(V X) Ṽ`: the sandwich with the reverse, the left product first.
    let sandwich (r: Arithmetic) (v: Multivector) (x: Multivector) : Multivector =
        r.Chain r.Geometric [ v; x; r.VersorReverse v ]

    /// `(π X̂) π`: a reflection, `X̂` the grade involution, the left product first.
    let reflect (r: Arithmetic) (pi: Multivector) (x: Multivector) : Multivector =
        r.Chain r.Geometric [ pi; Multivector.gradeInvolution x; pi ]

    /// The composition `B A`, which applies `A` first, copied into a type's coefficients.
    let compose (r: Arithmetic) (indices: int[]) (b: Multivector) (a: Multivector) : float[] =
        Conformal.copy indices (r.Compose r.Geometric b a)

    // ---- construction from parameters (D85 item 4) --------------------------------------------------

    /// The rotor about an axis already of length one: `c` at index 0, `−(s × â3)` at 10, `s × â2` at
    /// 11 and `−(s × â1)` at 13, with `(s, c)` the pinned sine and cosine of `0.5 × θ`, asked once.
    let private rotorOfUnit (r: Arithmetic) (a1: float, a2: float, a3: float) (theta: float) : Multivector =
        let struct (s, c) = r.SinCos(0.5 * theta)
        Conformal.widen rotorIndices [| c; -(s * a3); s * a2; -(s * a1) |]

    let rotorOfAxisAngle (r: Arithmetic) (axis: float * float * float) (theta: float) : Result<Rotor, GeometryRefusal> =
        Conformal.unit r axis
        |> Result.bind (fun a ->
            if finite theta then
                typed rotorIndices (rotorOfUnit r (r.UnitAsGiven a) theta) |> Result.map rotorOf
            else
                Error GeometryRefusal.NonFiniteAngle)

    /// `1 − ½ t ∧ n∞`: `1` at index 0 and `−(0.5 × t_i)` at 12, 14 and 15.
    let private translatorOfFinite (t1: float, t2: float, t3: float) : Multivector =
        let one = 1.0
        Conformal.widen translatorIndices [| one; -(0.5 * t1); -(0.5 * t2); -(0.5 * t3) |]

    let translatorOfVector (t: float * float * float) : Result<Translator, GeometryRefusal> =
        if finite3 t then
            Ok(mvTranslator (translatorOfFinite t))
        else
            Error GeometryRefusal.NonFiniteTranslation

    /// `T R`: the rotation first and then the translation.
    let motorOfRotorTranslator (r: Arithmetic) (rotor: Rotor) (translator: Translator) : Motor =
        motorOf (compose r motorIndices (translatorMv translator) (rotorMv rotor))

    /// The screw `T_(d û) ((T_p R) T̃_p)` about an axis already of length one: `R` the rotor about
    /// that `û` as it is, not divided by its norm again, and `p` as it is given.
    let private screwOfUnit
        (r: Arithmetic)
        (point: float * float * float)
        (unitDirection: float * float * float)
        (theta: float)
        (d: float)
        : Result<Motor, GeometryRefusal> =
        let (u1, u2, u3) = r.UnitAsGiven unitDirection
        let toAxis = translatorOfFinite (r.PointAsGiven point (u1, u2, u3))
        let along = translatorOfFinite (d * u1, d * u2, d * u3)

        let screw =
            r.Screw r.Geometric along toAxis (rotorOfUnit r (u1, u2, u3) theta) (Multivector.reverse toAxis)

        typed motorIndices screw |> Result.map motorOf

    let motorOfScrew
        (r: Arithmetic)
        (p: float * float * float)
        (direction: float * float * float)
        (theta: float)
        (d: float)
        : Result<Motor, GeometryRefusal> =
        if not (finite3 p) then
            Error GeometryRefusal.NonFiniteCoordinate
        else
            Conformal.unit r direction
            |> Result.bind (fun u ->
                if not (finite theta) then
                    Error GeometryRefusal.NonFiniteAngle
                elif not (finite d) then
                    Error GeometryRefusal.NonFiniteDistance
                else
                    screwOfUnit r p u theta d)

    /// The screw about a line: `û` the line's direction divided by its weight, `p` its point nearest
    /// the origin.
    let motorOfLine (r: Arithmetic) (line: Line) (theta: float) (d: float) : Result<Motor, GeometryRefusal> =
        Conformal.divisor r (Conformal.lineWeight r line)
        |> Result.bind (fun w ->
            if not (finite theta) then
                Error GeometryRefusal.NonFiniteAngle
            elif not (finite d) then
                Error GeometryRefusal.NonFiniteDistance
            else
                Conformal.lineNearestPoint r line
                |> Result.bind (fun p ->
                    if finite3 p then
                        screwOfUnit
                            r
                            p
                            (r.Divide line.N0E1NInf w, r.Divide line.N0E2NInf w, r.Divide line.N0E3NInf w)
                            theta
                            d
                    else
                        Error GeometryRefusal.NonFiniteCoordinate))

    /// A factor is finite and positive.
    let private factor (s: float) : Result<float, GeometryRefusal> =
        if not (finite s) then Error GeometryRefusal.NonFiniteFactor
        elif s > 0.0 then Ok s
        else Error GeometryRefusal.NonPositiveFactor

    /// The dilator about the origin: `(s + 1) / q` at index 0 and `(s − 1) / q` at index 9.
    let private dilatorAboutOrigin (r: Arithmetic) (s: float) : Multivector =
        let struct (scalar, origin) = r.DilatorParts r.Divide s
        Conformal.widen dilatorIndices [| scalar; origin; 0.0; 0.0; 0.0 |]

    let dilatorOfFactor (r: Arithmetic) (s: float) : Result<Dilator, GeometryRefusal> =
        factor s
        |> Result.bind (fun s -> typed dilatorIndices (dilatorAboutOrigin r s) |> Result.map dilatorOf)

    /// `(T_c D₀) T̃_c`.
    let dilatorOfCentreFactor (r: Arithmetic) (c: float * float * float) (s: float) : Result<Dilator, GeometryRefusal> =
        if not (finite3 c) then
            Error GeometryRefusal.NonFiniteCoordinate
        else
            factor s
            |> Result.bind (fun s ->
                let toCentre = translatorOfFinite c

                typed
                    dilatorIndices
                    (r.Chain r.Geometric [ toCentre; dilatorAboutOrigin r s; Multivector.reverse toCentre ])
                |> Result.map dilatorOf)

    /// The plane's vector `n̂ + δ n∞`.
    let reflectionOfNormalDistance
        (r: Arithmetic)
        (normal: float * float * float)
        (delta: float)
        : Result<Reflection, GeometryRefusal> =
        Conformal.unit r normal
        |> Result.map r.UnitAsGiven
        |> Result.bind (fun (n1, n2, n3) ->
            if finite delta then
                Ok
                    { E1 = n1
                      E2 = n2
                      E3 = n3
                      NInf = delta }
            else
                Error GeometryRefusal.NonFiniteDistance)

    // ---- normalisation (D85 item 5) -----------------------------------------------------------------

    /// Every coefficient a versor carries divided by `√(V ∗ Ṽ)`, once.
    let normalise (r: Arithmetic) (indices: int[]) (values: float[]) : Result<float[], GeometryRefusal> =
        let v = Conformal.widen indices values

        Conformal.divisor r (r.Scalar v (Multivector.reverse v))
        |> Result.bind (fun q ->
            if q > 0.0 then
                let n = sqrt q
                Conformal.numbers (values |> Array.map (fun x -> r.Divide x n))
            else
                Error GeometryRefusal.NegativeSquaredNorm)

    // ---- the exponential and the logarithm (D85 item 4) ---------------------------------------------

    let private bivectorIndices = [| 10; 11; 13 |]

    /// A Euclidean bivector divided by its norm, one division for each coefficient, with its reverse.
    let private unitBivector (r: Arithmetic) (b: float[]) (n: float) : Multivector * Multivector =
        let u = Conformal.widen bivectorIndices (b |> Array.map (fun x -> r.Divide x n))
        u, Multivector.reverse u

    /// `exp B = cos φ + (sin φ / φ) B` with `φ = |B|`, the quotient formed once; `1 + B` at `φ = 0`.
    /// The scalar and the bivector part lie on different blades, so each coefficient is PLACED and none
    /// is a sum: a zero of `B` keeps its sign (D88 item 3).
    let rotorExp (r: Arithmetic) (b12: float, b13: float, b23: float) : Result<Rotor, GeometryRefusal> =
        if not (finite3 (b12, b13, b23)) then
            Error GeometryRefusal.NonFiniteBivector
        else
            let phi = r.Scaled r.SumOfSquares [| b12; b13; b23 |]

            if r.IsZeroAngle phi then
                let one = 1.0

                Ok
                    { Scalar = r.Placed one
                      E1E2 = r.Placed b12
                      E1E3 = r.Placed b13
                      E2E3 = r.Placed b23 }
            elif not (finite phi) then
                Error GeometryRefusal.NonFiniteBivector
            else
                let struct (s, c) = r.SinCos phi
                let k = r.ByQuotient r.Divide s phi [| b12; b13; b23 |]

                Conformal.numbers ([| c; k[0]; k[1]; k[2] |] |> Array.map r.Placed)
                |> Result.map rotorOf

    /// The logarithm of a rotor: `(φ / |B′|) B′` with `φ = atan2 |B′| c`, the quotient formed once;
    /// `B′` itself where `|B′| = 0` and `c > 0`; refused where `|B′| = 0` and `c` is not positive.
    let rotorLog (r: Arithmetic) (rotor: Rotor) : Result<float * float * float, GeometryRefusal> =
        let c = rotor.Scalar
        let b = [| rotor.E1E2; rotor.E1E3; rotor.E2E3 |]

        if Double.IsNaN c || Array.exists Double.IsNaN b then
            Error GeometryRefusal.NotANumber
        else
            let n = r.Scaled r.SumOfSquares b

            if r.IsZeroBivector n then
                if c > 0.0 then
                    Ok(b[0], b[1], b[2])
                else
                    Error GeometryRefusal.NoRotationPlane
            else
                Conformal.numbers (r.ByQuotient r.Divide (r.Atan2 n c) n b)
                |> Result.map (fun k -> k[0], k[1], k[2])

    /// The exponential of a motor's bivector `B + v ∧ n∞`:
    /// `(1 + v∥ ∧ n∞)(cos φ + (sin φ / φ)(B + v⊥ ∧ n∞))`, and `1 + v ∧ n∞` at `φ = 0`.
    let motorExp
        (r: Arithmetic)
        (b12: float, b13: float, b23: float)
        (v1: float, v2: float, v3: float)
        : Result<Motor, GeometryRefusal> =
        if not (finite3 (b12, b13, b23)) then
            Error GeometryRefusal.NonFiniteBivector
        elif not (finite3 (v1, v2, v3)) then
            Error GeometryRefusal.NonFiniteTranslation
        else
            let b = [| b12; b13; b23 |]
            let phi = r.Scaled r.SumOfSquares b

            if r.IsZeroAngle phi then
                let one = 1.0

                Ok
                    { Scalar = r.Placed one
                      E1E2 = 0.0
                      E1E3 = 0.0
                      E1NInf = r.Placed v1
                      E2E3 = 0.0
                      E2NInf = r.Placed v2
                      E3NInf = r.Placed v3
                      E1E2E3NInf = 0.0 }
            elif not (finite phi) then
                Error GeometryRefusal.NonFiniteBivector
            else
                let u, uReversed = unitBivector r b phi
                let along, across = r.SplitAlong r (Conformal.vector 0.0 v1 v2 v3 0.0) u uReversed
                let struct (s, c) = r.SinCos phi
                let k = r.ByQuotient r.Divide s phi (Array.append b across)
                let one = 1.0
                let left = Conformal.widen translatorIndices [| one; along[0]; along[1]; along[2] |]

                let right =
                    Conformal.widen [| 0; 10; 11; 13; 12; 14; 15 |] [| c; k[0]; k[1]; k[2]; k[3]; k[4]; k[5] |]

                typed motorIndices (r.Geometric left right) |> Result.map motorOf

    /// The logarithm of a motor, `B + v ∧ n∞` as its two triples: `B` from the rotation's part as a
    /// rotor's is; `v∥ ∧ n∞` the motor's coefficient at index 30 times the reverse of the rotation's
    /// unit bivector, over `|B′|`; and `v⊥` the same quotient `φ / |B′|` times what `MotorAcross`
    /// forms. Where `|B′| = 0` and `c > 0` it is `B′` and the three `e_i ∧ n∞` coefficients themselves.
    let motorLog
        (r: Arithmetic)
        (motor: Motor)
        : Result<(float * float * float) * (float * float * float), GeometryRefusal> =
        let c = motor.Scalar
        let b = [| motor.E1E2; motor.E1E3; motor.E2E3 |]
        let m = [| motor.E1NInf; motor.E2NInf; motor.E3NInf |]

        if Array.exists Double.IsNaN (motorValues motor) then
            Error GeometryRefusal.NotANumber
        else
            let n = r.Scaled r.SumOfSquares b

            if r.IsZeroBivector n then
                if c > 0.0 then
                    Ok((b[0], b[1], b[2]), (m[0], m[1], m[2]))
                else
                    Error GeometryRefusal.NoRotationPlane
            else
                let u, uReversed = unitBivector r b n

                let alongRaw =
                    Conformal.copy
                        [| 12; 14; 15 |]
                        (r.Geometric (Conformal.widen [| 30 |] [| motor.E1E2E3NInf |]) uReversed)

                let along = alongRaw |> Array.map (fun x -> r.Divide x n)
                let across = r.MotorAcross r c m along u uReversed
                let k = r.ByQuotient r.Divide (r.Atan2 n c) n (Array.append b across)

                Conformal.numbers [| k[0]; k[1]; k[2]; along[0] + k[3]; along[1] + k[4]; along[2] + k[5] |]
                |> Result.map (fun x -> (x[0], x[1], x[2]), (x[3], x[4], x[5]))

    // ---- interpolation (D85 item 4) -----------------------------------------------------------------

    /// `V₁ exp (t log (Ṽ₁ V₂))`, formed in that order, for two rotors.
    let rotorInterpolate (r: Arithmetic) (t: float) (first: Rotor) (second: Rotor) : Result<Rotor, GeometryRefusal> =
        if not (finite t) then
            Error GeometryRefusal.NonFiniteParameter
        else
            let v1 = rotorMv first

            rotorLog r (mvRotor (r.Between(r.Geometric (r.VersorReverse v1) (rotorMv second))))
            |> Result.bind (fun (b12, b13, b23) -> rotorExp r (t * b12, t * b13, t * b23))
            |> Result.bind (fun e -> typed rotorIndices (r.Geometric v1 (rotorMv e)))
            |> Result.map rotorOf

    /// `V₁ exp (t log (Ṽ₁ V₂))`, formed in that order, for two motors.
    let motorInterpolate (r: Arithmetic) (t: float) (first: Motor) (second: Motor) : Result<Motor, GeometryRefusal> =
        if not (finite t) then
            Error GeometryRefusal.NonFiniteParameter
        else
            let v1 = motorMv first

            motorLog r (mvMotor (r.Between(r.Geometric (r.VersorReverse v1) (motorMv second))))
            |> Result.bind (fun ((b12, b13, b23), (w1, w2, w3)) ->
                motorExp r (t * b12, t * b13, t * b23) (t * w1, t * w2, t * w3))
            |> Result.bind (fun e -> typed motorIndices (r.Geometric v1 (motorMv e)))
            |> Result.map motorOf

    // ---- the matrix (D85 items 4 and 9) -------------------------------------------------------------

    /// The similarity a versor names, as sixteen doubles: the images of `e1`, `e2`, `e3` and of the
    /// origin `n0` under the sandwich, their indices 2 to 4 each divided by the weight of the
    /// origin's image, which is its index 1; and the last row `0 0 0 1`.
    let matrixOf (r: Arithmetic) (v: Multivector) : Result<float[], GeometryRefusal> =
        let origin = sandwich r v Conformal.n0

        Conformal.divisor r origin.Coefficients[1]
        |> Result.bind (fun w ->
            let m = Array.zeroCreate<float> 16

            for column in 0..2 do
                let image = sandwich r v (Multivector.basis (2 + column))

                for row in 0..2 do
                    m[r.MatrixIndex row column] <- r.Divide image.Coefficients[2 + row] w

            for row in 0..2 do
                m[r.MatrixIndex row 3] <- r.Divide origin.Coefficients[2 + row] w

            m[r.MatrixIndex 3 3] <- 1.0
            Conformal.numbers m)

    /// A matrix read as a motor and a factor: the dilation about the origin first, and then the motor.
    /// The factor is the norm of the first column. The statistics of the test, each against `τ`:
    /// for the second and the third column, the difference of its squared length and the first's
    /// over the first's; and for each pair of columns, their dot product over the product of their
    /// norms. At `τ = 0` each is decided from the signs of its numerator and its divisor.
    let motorOfMatrix (r: Arithmetic) (tau: float) (m: float[]) : Result<Motor * float, MatrixRefusal> =
        if not (tau >= 0.0 && finite tau) then
            Error MatrixRefusal.InvalidThreshold
        elif m.Length <> 16 then
            Error MatrixRefusal.NotSixteenEntries
        elif Array.exists (fun x -> not (finite x)) m then
            Error MatrixRefusal.NonFiniteEntry
        else
            let at (row: int) (column: int) : float = m[r.MatrixIndex row column]

            if not (at 3 0 = 0.0 && at 3 1 = 0.0 && at 3 2 = 0.0 && at 3 3 = 1.0) then
                Error MatrixRefusal.NotAffine
            else
                let columns = [| for j in 0..2 -> [| at 0 j; at 1 j; at 2 j |] |]
                let squares = columns |> Array.map (fun c -> r.Dot c c)

                if Array.exists r.IsZeroDivisor squares then
                    Error MatrixRefusal.ZeroDivisor
                elif Array.exists (fun x -> not (finite x)) squares then
                    Error MatrixRefusal.NonFiniteDivisor
                else
                    let norms = columns |> Array.map (r.Scaled r.SumOfSquares)

                    // The first statistic that is not within the threshold, with its value.
                    let lengthFailure =
                        [ 1; 2 ]
                        |> List.tryPick (fun j ->
                            let numerator = squares[j] - squares[0]
                            let quotient () = r.Divide numerator squares[0]

                            if Conformal.against r tau numerator squares[0] quotient = 0 then
                                None
                            else
                                Some(quotient ()))

                    let angleFailure () =
                        [ 0, 1; 0, 2; 1, 2 ]
                        |> List.tryPick (fun (i, j) ->
                            let numerator = r.Dot columns[i] columns[j]

                            let quotient () =
                                r.OverBoth r.Divide numerator norms[i] norms[j]

                            // The divisor is a product of two norms, which are positive.
                            if Conformal.against r tau numerator norms[i] quotient = 0 then
                                None
                            else
                                Some(quotient ()))

                    match lengthFailure with
                    | Some statistic -> Error(MatrixRefusal.NotOneLength statistic)
                    | None ->
                        match angleFailure () with
                        | Some statistic -> Error(MatrixRefusal.NotOrthogonal statistic)
                        | None ->
                            let volume = r.Orientation r.Dot columns[0] columns[1] columns[2]

                            if volume < 0.0 then
                                Error(MatrixRefusal.ReversesOrientation volume)
                            else
                                let s = norms[0]

                                let q =
                                    r.Canonical(
                                        r.RotorOfRotation r.Divide (fun row column -> r.Divide (at row column) s)
                                    )

                                let rotor = Conformal.widen rotorIndices [| q[0]; -q[3]; q[2]; -q[1] |]
                                let translator = translatorOfFinite (at 0 3, at 1 3, at 2 3)
                                let motor = compose r motorIndices translator rotor

                                if Array.exists Double.IsNaN motor then
                                    Error MatrixRefusal.NotANumber
                                else
                                    Ok(motorOf motor, s)
