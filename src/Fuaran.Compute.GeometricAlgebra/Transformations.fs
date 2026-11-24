namespace Fuaran.Compute.GeometricAlgebra

// ============================================================================
//  Phase 729 — the published versors: a module of functions for each versor
//  type, in the type's own name (DECISIONS.md D85 items 4, 5 and 8; the names
//  are D88's).
//
//  Every function here is the function of `Motion` under `Arithmetic.shipped`.
//  What each computes, and in which order, is written there, once.
//
//  Four things hold of every function below.
//    * A versor is applied by the sandwich with its REVERSE, never its inverse,
//      the left product first, and the result is copied into the object's own
//      type. A versor that is not a unit therefore scales what it is applied
//      to, and that is visible, not repaired.
//    * Nothing normalises (D85 item 5). A versor built from parameters is a
//      unit because of what it computes; `normalise` is the one division by
//      `√(V ∗ Ṽ)`, and a caller asks for it by name.
//    * `compose b a` is the product `B A`, which applies `a` first.
//    * Nothing throws on bad data. A function that can refuse returns `Result`
//      and an `Ok` never holds a NaN. Angles are in radians.
//
//  Fable-clean.
// ============================================================================

/// A rotation about an axis through the origin (DECISIONS.md D85 item 4).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Rotor =

    /// The rotor by `angle` about `axis`, counter-clockwise seen from the axis's tip:
    /// `cos ½θ − sin ½θ (â I₃)`, the axis divided by its norm first and the pinned sine and cosine of
    /// `0.5 × θ` asked once. Refused for an axis of norm zero or not finite, and an angle not finite.
    let ofAxisAngle (axis: float * float * float) (angle: float) : Result<Rotor, GeometryRefusal> =
        Motion.rotorOfAxisAngle Arithmetic.shipped axis angle

    /// The exponential of the Euclidean bivector whose coefficients on `e1∧e2`, `e1∧e3` and `e2∧e3`
    /// are `bivector`: `cos φ + (sin φ / φ) B` with `φ = |B|`, and `1 + B` at `φ = 0`. The rotor about
    /// `â` by `θ` is `exp (−½θ â I₃)`.
    let exp (bivector: float * float * float) : Result<Rotor, GeometryRefusal> =
        Motion.rotorExp Arithmetic.shipped bivector

    /// The logarithm, as the bivector's three coefficients: `(φ / |B′|) B′` with
    /// `φ = atan2 |B′| c`, so `φ` lies in `[0, π]`. It never replaces a rotor by its negative to take
    /// the shorter way. Refused where the bivector part is zero and the scalar part is not positive:
    /// no plane is determined.
    let log (v: Rotor) : Result<float * float * float, GeometryRefusal> = Motion.rotorLog Arithmetic.shipped v

    /// `V₁ exp (t log (Ṽ₁ V₂))`, formed in that order. It follows the logarithm's branch, so it can
    /// go the long way round: a caller who wants the shorter way passes the negative of `second`
    /// where the scalar coefficient of `Ṽ₁ V₂` is negative.
    let interpolate (t: float) (first: Rotor) (second: Rotor) : Result<Rotor, GeometryRefusal> =
        Motion.rotorInterpolate Arithmetic.shipped t first second

    /// The product `B A`, which applies `a` first.
    let compose (b: Rotor) (a: Rotor) : Rotor =
        Motion.rotorOf (Motion.compose Arithmetic.shipped Motion.rotorIndices (Motion.rotorMv b) (Motion.rotorMv a))

    /// The rotor applied to a point: `(V X) Ṽ`, copied into a `Point`. Nothing is normalised.
    let applyToPoint (v: Rotor) (x: Point) : Point =
        Conformal.mvPoint (Motion.sandwich Arithmetic.shipped (Motion.rotorMv v) (Conformal.pointMv x))

    /// The rotor applied to a point pair: `(V X) Ṽ`, copied into a `PointPair`. Nothing is normalised.
    let applyToPointPair (v: Rotor) (x: PointPair) : PointPair =
        Conformal.mvPair (Motion.sandwich Arithmetic.shipped (Motion.rotorMv v) (Conformal.pairMv x))

    /// The rotor applied to a line: `(V X) Ṽ`, copied into a `Line`. Nothing is normalised.
    let applyToLine (v: Rotor) (x: Line) : Line =
        Conformal.mvLine (Motion.sandwich Arithmetic.shipped (Motion.rotorMv v) (Conformal.lineMv x))

    /// The rotor applied to a circle: `(V X) Ṽ`, copied into a `Circle`. Nothing is normalised.
    let applyToCircle (v: Rotor) (x: Circle) : Circle =
        Conformal.mvCircle (Motion.sandwich Arithmetic.shipped (Motion.rotorMv v) (Conformal.circleMv x))

    /// The rotor applied to a plane: `(V X) Ṽ`, copied into a `Plane`. Nothing is normalised.
    let applyToPlane (v: Rotor) (x: Plane) : Plane =
        Conformal.mvPlane (Motion.sandwich Arithmetic.shipped (Motion.rotorMv v) (Conformal.planeMv x))

    /// The rotor applied to a sphere: `(V X) Ṽ`, copied into a `Sphere`. Nothing is normalised.
    let applyToSphere (v: Rotor) (x: Sphere) : Sphere =
        Conformal.mvSphere (Motion.sandwich Arithmetic.shipped (Motion.rotorMv v) (Conformal.sphereMv x))

    /// The similarity `x ↦ s R x + t` the rotor names, as sixteen doubles in column-major order acting
    /// on column vectors, a fresh array: the images of `e1`, `e2`, `e3` and of the origin, each
    /// divided by the weight of the origin's image; the last row is `0 0 0 1`. Refused where that
    /// weight is zero or not finite.
    let toMatrix (v: Rotor) : Result<float[], GeometryRefusal> =
        Motion.matrixOf Arithmetic.shipped (Motion.rotorMv v)

    /// Every coefficient divided by `√(V ∗ Ṽ)`, once (D85 item 5). Refused where `V ∗ Ṽ` is zero, not
    /// finite or negative.
    let normalise (v: Rotor) : Result<Rotor, GeometryRefusal> =
        Motion.normalise Arithmetic.shipped Motion.rotorIndices (Motion.rotorValues v)
        |> Result.map Motion.rotorOf

    /// The widening: the coefficients the type does not carry are `+0.0`.
    let toMultivector (v: Rotor) : Multivector = Motion.rotorMv v

    /// The copy back: the coefficients the type carries, the rest discarded without a test.
    let ofMultivector (m: Multivector) : Rotor = Motion.mvRotor m

/// A translation (DECISIONS.md D85 item 4).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Translator =

    /// The translator by `t`: `1 − ½ t ∧ n∞`, `1` at index 0 and `−(0.5 × t_i)` at 12, 14 and 15.
    /// Refused for a component not finite.
    let ofVector (t: float * float * float) : Result<Translator, GeometryRefusal> = Motion.translatorOfVector t

    /// The product `B A`, which applies `a` first.
    let compose (b: Translator) (a: Translator) : Translator =
        Motion.translatorOf (
            Motion.compose Arithmetic.shipped Motion.translatorIndices (Motion.translatorMv b) (Motion.translatorMv a)
        )

    /// The translator applied to a point: `(V X) Ṽ`, copied into a `Point`. Nothing is normalised.
    let applyToPoint (v: Translator) (x: Point) : Point =
        Conformal.mvPoint (Motion.sandwich Arithmetic.shipped (Motion.translatorMv v) (Conformal.pointMv x))

    /// The translator applied to a point pair: `(V X) Ṽ`, copied into a `PointPair`. Nothing is normalised.
    let applyToPointPair (v: Translator) (x: PointPair) : PointPair =
        Conformal.mvPair (Motion.sandwich Arithmetic.shipped (Motion.translatorMv v) (Conformal.pairMv x))

    /// The translator applied to a line: `(V X) Ṽ`, copied into a `Line`. Nothing is normalised.
    let applyToLine (v: Translator) (x: Line) : Line =
        Conformal.mvLine (Motion.sandwich Arithmetic.shipped (Motion.translatorMv v) (Conformal.lineMv x))

    /// The translator applied to a circle: `(V X) Ṽ`, copied into a `Circle`. Nothing is normalised.
    let applyToCircle (v: Translator) (x: Circle) : Circle =
        Conformal.mvCircle (Motion.sandwich Arithmetic.shipped (Motion.translatorMv v) (Conformal.circleMv x))

    /// The translator applied to a plane: `(V X) Ṽ`, copied into a `Plane`. Nothing is normalised.
    let applyToPlane (v: Translator) (x: Plane) : Plane =
        Conformal.mvPlane (Motion.sandwich Arithmetic.shipped (Motion.translatorMv v) (Conformal.planeMv x))

    /// The translator applied to a sphere: `(V X) Ṽ`, copied into a `Sphere`. Nothing is normalised.
    let applyToSphere (v: Translator) (x: Sphere) : Sphere =
        Conformal.mvSphere (Motion.sandwich Arithmetic.shipped (Motion.translatorMv v) (Conformal.sphereMv x))

    /// The similarity `x ↦ s R x + t` the translator names, as sixteen doubles in column-major order acting
    /// on column vectors, a fresh array: the images of `e1`, `e2`, `e3` and of the origin, each
    /// divided by the weight of the origin's image; the last row is `0 0 0 1`. Refused where that
    /// weight is zero or not finite.
    let toMatrix (v: Translator) : Result<float[], GeometryRefusal> =
        Motion.matrixOf Arithmetic.shipped (Motion.translatorMv v)

    /// Every coefficient divided by `√(V ∗ Ṽ)`, once (D85 item 5). Refused where `V ∗ Ṽ` is zero, not
    /// finite or negative.
    let normalise (v: Translator) : Result<Translator, GeometryRefusal> =
        Motion.normalise Arithmetic.shipped Motion.translatorIndices (Motion.translatorValues v)
        |> Result.map Motion.translatorOf

    /// The widening: the coefficients the type does not carry are `+0.0`.
    let toMultivector (v: Translator) : Multivector = Motion.translatorMv v

    /// The copy back: the coefficients the type carries, the rest discarded without a test.
    let ofMultivector (m: Multivector) : Translator = Motion.mvTranslator m

/// A rotation and a translation together, which is a screw motion (DECISIONS.md D85 item 4).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Motor =

    /// `T R`: the rotation first and then the translation.
    let ofRotorTranslator (rotor: Rotor) (translator: Translator) : Motor =
        Motion.motorOfRotorTranslator Arithmetic.shipped rotor translator

    /// A rotor as a motor, by widening.
    let ofRotor (rotor: Rotor) : Motor = Motion.mvMotor (Motion.rotorMv rotor)

    /// A translator as a motor, by widening.
    let ofTranslator (translator: Translator) : Motor =
        Motion.mvMotor (Motion.translatorMv translator)

    /// The screw about the axis through `point` with `direction`, by `angle` and by `distance` along
    /// it: `T_(d û) ((T_p R) T̃_p)`, formed in that order, the direction divided by its norm first and
    /// the point used as it is given.
    let ofScrew
        (point: float * float * float)
        (direction: float * float * float)
        (angle: float)
        (distance: float)
        : Result<Motor, GeometryRefusal> =
        Motion.motorOfScrew Arithmetic.shipped point direction angle distance

    /// The screw about a line: its direction divided by its weight, and its point nearest the origin.
    let ofLine (line: Line) (angle: float) (distance: float) : Result<Motor, GeometryRefusal> =
        Motion.motorOfLine Arithmetic.shipped line angle distance

    /// The exponential of `B + v ∧ n∞`, `B` the Euclidean bivector of `bivector` and `v` the vector:
    /// `(1 + v∥ ∧ n∞)(cos φ + (sin φ / φ)(B + v⊥ ∧ n∞))`, `v` split along the axis of `B` and across
    /// it, and `1 + v ∧ n∞` at `φ = 0`.
    let exp (bivector: float * float * float) (vector: float * float * float) : Result<Motor, GeometryRefusal> =
        Motion.motorExp Arithmetic.shipped bivector vector

    /// The logarithm, as the bivector's three coefficients and the vector's: `φ` is taken from the
    /// rotation's part as a rotor's is, and lies in `[0, π]`. Refused where the rotation's bivector
    /// part is zero and the scalar part is not positive.
    let log (v: Motor) : Result<(float * float * float) * (float * float * float), GeometryRefusal> =
        Motion.motorLog Arithmetic.shipped v

    /// `V₁ exp (t log (Ṽ₁ V₂))`, formed in that order. It follows the logarithm's branch.
    let interpolate (t: float) (first: Motor) (second: Motor) : Result<Motor, GeometryRefusal> =
        Motion.motorInterpolate Arithmetic.shipped t first second

    /// The product `B A`, which applies `a` first.
    let compose (b: Motor) (a: Motor) : Motor =
        Motion.motorOf (Motion.compose Arithmetic.shipped Motion.motorIndices (Motion.motorMv b) (Motion.motorMv a))

    /// The motor applied to a point: `(V X) Ṽ`, copied into a `Point`. Nothing is normalised.
    let applyToPoint (v: Motor) (x: Point) : Point =
        Conformal.mvPoint (Motion.sandwich Arithmetic.shipped (Motion.motorMv v) (Conformal.pointMv x))

    /// The motor applied to a point pair: `(V X) Ṽ`, copied into a `PointPair`. Nothing is normalised.
    let applyToPointPair (v: Motor) (x: PointPair) : PointPair =
        Conformal.mvPair (Motion.sandwich Arithmetic.shipped (Motion.motorMv v) (Conformal.pairMv x))

    /// The motor applied to a line: `(V X) Ṽ`, copied into a `Line`. Nothing is normalised.
    let applyToLine (v: Motor) (x: Line) : Line =
        Conformal.mvLine (Motion.sandwich Arithmetic.shipped (Motion.motorMv v) (Conformal.lineMv x))

    /// The motor applied to a circle: `(V X) Ṽ`, copied into a `Circle`. Nothing is normalised.
    let applyToCircle (v: Motor) (x: Circle) : Circle =
        Conformal.mvCircle (Motion.sandwich Arithmetic.shipped (Motion.motorMv v) (Conformal.circleMv x))

    /// The motor applied to a plane: `(V X) Ṽ`, copied into a `Plane`. Nothing is normalised.
    let applyToPlane (v: Motor) (x: Plane) : Plane =
        Conformal.mvPlane (Motion.sandwich Arithmetic.shipped (Motion.motorMv v) (Conformal.planeMv x))

    /// The motor applied to a sphere: `(V X) Ṽ`, copied into a `Sphere`. Nothing is normalised.
    let applyToSphere (v: Motor) (x: Sphere) : Sphere =
        Conformal.mvSphere (Motion.sandwich Arithmetic.shipped (Motion.motorMv v) (Conformal.sphereMv x))

    /// The similarity `x ↦ s R x + t` the motor names, as sixteen doubles in column-major order acting
    /// on column vectors, a fresh array: the images of `e1`, `e2`, `e3` and of the origin, each
    /// divided by the weight of the origin's image; the last row is `0 0 0 1`. Refused where that
    /// weight is zero or not finite.
    let toMatrix (v: Motor) : Result<float[], GeometryRefusal> =
        Motion.matrixOf Arithmetic.shipped (Motion.motorMv v)

    /// A similarity read as a motor and a factor: the dilation by the factor about the origin first,
    /// and then the motor. The factor is the norm of the first column, and the rotor's scalar part is
    /// not negative. `tau` is the threshold of the test that the three columns are of one length and
    /// orthogonal, in the statistics `MatrixRefusal` names, which have no unit; `0` is the exact
    /// comparison. The last row is compared exactly. A matrix that is not a similarity, or reverses
    /// orientation, is refused by name with the measure that failed.
    let ofMatrix (tau: float) (matrix: float[]) : Result<Motor * float, MatrixRefusal> =
        Motion.motorOfMatrix Arithmetic.shipped tau matrix

    /// Every coefficient divided by `√(V ∗ Ṽ)`, once (D85 item 5). Refused where `V ∗ Ṽ` is zero, not
    /// finite or negative.
    let normalise (v: Motor) : Result<Motor, GeometryRefusal> =
        Motion.normalise Arithmetic.shipped Motion.motorIndices (Motion.motorValues v)
        |> Result.map Motion.motorOf

    /// The widening: the coefficients the type does not carry are `+0.0`.
    let toMultivector (v: Motor) : Multivector = Motion.motorMv v

    /// The copy back: the coefficients the type carries, the rest discarded without a test.
    let ofMultivector (m: Multivector) : Motor = Motion.mvMotor m

/// A uniform scaling about a point (DECISIONS.md D85 item 4). A dilation divides a point's weight by
/// its factor; coordinates are read by dividing by the weight, so they are right.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Dilator =

    /// The dilator by `factor` about the origin: `r = √s`, `q = 2 × r`, then `(s + 1) / q` at index 0
    /// and `(s − 1) / q` at index 9. Refused for a factor not finite, and one not positive.
    let ofFactor (factor: float) : Result<Dilator, GeometryRefusal> =
        Motion.dilatorOfFactor Arithmetic.shipped factor

    /// The dilator by `factor` about `centre`: `(T_c D₀) T̃_c`.
    let ofCentreFactor (centre: float * float * float) (factor: float) : Result<Dilator, GeometryRefusal> =
        Motion.dilatorOfCentreFactor Arithmetic.shipped centre factor

    /// The product `B A`, which applies `a` first.
    let compose (b: Dilator) (a: Dilator) : Dilator =
        Motion.dilatorOf (
            Motion.compose Arithmetic.shipped Motion.dilatorIndices (Motion.dilatorMv b) (Motion.dilatorMv a)
        )

    /// The dilator applied to a point: `(V X) Ṽ`, copied into a `Point`. Nothing is normalised.
    let applyToPoint (v: Dilator) (x: Point) : Point =
        Conformal.mvPoint (Motion.sandwich Arithmetic.shipped (Motion.dilatorMv v) (Conformal.pointMv x))

    /// The dilator applied to a point pair: `(V X) Ṽ`, copied into a `PointPair`. Nothing is normalised.
    let applyToPointPair (v: Dilator) (x: PointPair) : PointPair =
        Conformal.mvPair (Motion.sandwich Arithmetic.shipped (Motion.dilatorMv v) (Conformal.pairMv x))

    /// The dilator applied to a line: `(V X) Ṽ`, copied into a `Line`. Nothing is normalised.
    let applyToLine (v: Dilator) (x: Line) : Line =
        Conformal.mvLine (Motion.sandwich Arithmetic.shipped (Motion.dilatorMv v) (Conformal.lineMv x))

    /// The dilator applied to a circle: `(V X) Ṽ`, copied into a `Circle`. Nothing is normalised.
    let applyToCircle (v: Dilator) (x: Circle) : Circle =
        Conformal.mvCircle (Motion.sandwich Arithmetic.shipped (Motion.dilatorMv v) (Conformal.circleMv x))

    /// The dilator applied to a plane: `(V X) Ṽ`, copied into a `Plane`. Nothing is normalised.
    let applyToPlane (v: Dilator) (x: Plane) : Plane =
        Conformal.mvPlane (Motion.sandwich Arithmetic.shipped (Motion.dilatorMv v) (Conformal.planeMv x))

    /// The dilator applied to a sphere: `(V X) Ṽ`, copied into a `Sphere`. Nothing is normalised.
    let applyToSphere (v: Dilator) (x: Sphere) : Sphere =
        Conformal.mvSphere (Motion.sandwich Arithmetic.shipped (Motion.dilatorMv v) (Conformal.sphereMv x))

    /// The similarity `x ↦ s R x + t` the dilator names, as sixteen doubles in column-major order acting
    /// on column vectors, a fresh array: the images of `e1`, `e2`, `e3` and of the origin, each
    /// divided by the weight of the origin's image; the last row is `0 0 0 1`. Refused where that
    /// weight is zero or not finite.
    let toMatrix (v: Dilator) : Result<float[], GeometryRefusal> =
        Motion.matrixOf Arithmetic.shipped (Motion.dilatorMv v)

    /// Every coefficient divided by `√(V ∗ Ṽ)`, once (D85 item 5). Refused where `V ∗ Ṽ` is zero, not
    /// finite or negative.
    let normalise (v: Dilator) : Result<Dilator, GeometryRefusal> =
        Motion.normalise Arithmetic.shipped Motion.dilatorIndices (Motion.dilatorValues v)
        |> Result.map Motion.dilatorOf

    /// The widening: the coefficients the type does not carry are `+0.0`.
    let toMultivector (v: Dilator) : Multivector = Motion.dilatorMv v

    /// The copy back: the coefficients the type carries, the rest discarded without a test.
    let ofMultivector (m: Multivector) : Dilator = Motion.mvDilator m

/// A reflection in a plane (DECISIONS.md D85 item 4).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Reflection =

    /// The reflection in the plane with `normal` at `distance` from the origin along it: the vector
    /// `n̂ + δ n∞`, the normal divided by its norm first.
    let ofNormalDistance (normal: float * float * float) (distance: float) : Result<Reflection, GeometryRefusal> =
        Motion.reflectionOfNormalDistance Arithmetic.shipped normal distance

    /// The product `B A` of two reflections, which applies `a` first: a motor.
    let compose (b: Reflection) (a: Reflection) : Motor =
        Motion.motorOf (
            Motion.compose Arithmetic.shipped Motion.motorIndices (Motion.reflectionMv b) (Motion.reflectionMv a)
        )

    /// The reflection applied to a point: `(π X̂) π`, copied into a `Point`. Nothing is normalised.
    let applyToPoint (v: Reflection) (x: Point) : Point =
        Conformal.mvPoint (Motion.reflect Arithmetic.shipped (Motion.reflectionMv v) (Conformal.pointMv x))

    /// The reflection applied to a point pair: `(π X̂) π`, copied into a `PointPair`. Nothing is normalised.
    let applyToPointPair (v: Reflection) (x: PointPair) : PointPair =
        Conformal.mvPair (Motion.reflect Arithmetic.shipped (Motion.reflectionMv v) (Conformal.pairMv x))

    /// The reflection applied to a line: `(π X̂) π`, copied into a `Line`. Nothing is normalised.
    let applyToLine (v: Reflection) (x: Line) : Line =
        Conformal.mvLine (Motion.reflect Arithmetic.shipped (Motion.reflectionMv v) (Conformal.lineMv x))

    /// The reflection applied to a circle: `(π X̂) π`, copied into a `Circle`. Nothing is normalised.
    let applyToCircle (v: Reflection) (x: Circle) : Circle =
        Conformal.mvCircle (Motion.reflect Arithmetic.shipped (Motion.reflectionMv v) (Conformal.circleMv x))

    /// The reflection applied to a plane: `(π X̂) π`, copied into a `Plane`. Nothing is normalised.
    let applyToPlane (v: Reflection) (x: Plane) : Plane =
        Conformal.mvPlane (Motion.reflect Arithmetic.shipped (Motion.reflectionMv v) (Conformal.planeMv x))

    /// The reflection applied to a sphere: `(π X̂) π`, copied into a `Sphere`. Nothing is normalised.
    let applyToSphere (v: Reflection) (x: Sphere) : Sphere =
        Conformal.mvSphere (Motion.reflect Arithmetic.shipped (Motion.reflectionMv v) (Conformal.sphereMv x))

    /// Every coefficient divided by `√(V ∗ Ṽ)`, once (D85 item 5). Refused where `V ∗ Ṽ` is zero, not
    /// finite or negative.
    let normalise (v: Reflection) : Result<Reflection, GeometryRefusal> =
        Motion.normalise Arithmetic.shipped Motion.reflectionIndices (Motion.reflectionValues v)
        |> Result.map Motion.reflectionOf

    /// The widening: the coefficients the type does not carry are `+0.0`.
    let toMultivector (v: Reflection) : Multivector = Motion.reflectionMv v

    /// The copy back: the coefficients the type carries, the rest discarded without a test.
    let ofMultivector (m: Multivector) : Reflection = Motion.mvReflection m
