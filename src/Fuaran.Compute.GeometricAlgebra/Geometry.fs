namespace Fuaran.Compute.GeometricAlgebra

// ============================================================================
//  Phase 728 — the published geometry: the blade operations and a module of
//  functions for each object type, in the type's own name (DECISIONS.md D85
//  items 4, 5, 6 and 8; the names are D87's).
//
//  Every function here is one line: the function of `Conformal` under
//  `Arithmetic.shipped`. What each computes, and in which order, is written
//  there, once.
//
//  Three things hold of every function below.
//    * Nothing normalises (D85 item 5). An object built from parameters has
//      weight one because of what it computes; one built through points, or by
//      a meet, has the weight its product gives. The two `normalise` routines
//      of each kind are the only division by a weight a caller does not ask
//      for by name, and a read-back divides only where D85 item 4 says so.
//    * A zero test takes its threshold `tau` on every call (D85 item 6): a
//      finite `τ ≥ 0`, and `0` is the exact comparison. There is no default.
//    * Nothing throws on bad data. A function that can refuse returns `Result`
//      and an `Ok` never holds a NaN; a function that only multiplies or
//      copies is total, and what it returned is told by `kind`.
//
//  FSharp.Core only; Fable-clean.
// ============================================================================

/// The operations on blades held as multivectors (DECISIONS.md D85 item 4). Both the join and the
/// meet are taken in the whole space, and a zero blade is an answer: the join of two operands that
/// share a factor, the meet of two that do not span the space.
[<RequireQualifiedAccess>]
module Blade =

    /// The join `A ∧ B`: one outer product.
    let join (a: Multivector) (b: Multivector) : Multivector = Conformal.join Arithmetic.shipped a b

    /// The meet `B* ⌋ A`: the dual of the second operand, by the permutation, contracted onto the
    /// first. With the operands exchanged it is the same blade up to a sign.
    let meet (a: Multivector) (b: Multivector) : Multivector = Conformal.meet Arithmetic.shipped a b

    /// A blade's inverse `X̃ / (X ∗ X̃)`, each coefficient divided once. Refused when the divisor is
    /// zero or not finite. It is not the inverse of a general multivector.
    let inverse (x: Multivector) : Result<Multivector, GeometryRefusal> = Conformal.inverse Arithmetic.shipped x

    /// The meet within the span `j`, a blade that contains both operands: `(B ⌋ J⁻¹) ⌋ A`. With the
    /// pseudoscalar for `j` it is `meet`. Refused as `inverse j` is.
    let meetWithin (j: Multivector) (a: Multivector) (b: Multivector) : Result<Multivector, GeometryRefusal> =
        Conformal.meetWithin Arithmetic.shipped j a b

/// A point's functions (DECISIONS.md D85 items 1, 4 and 5).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Point =

    /// The conformal point of the Euclidean point `x`: `n0 + x + ½ x² n∞`, of weight one, its index 5
    /// formed as `0.5 × ((x1·x1 + x2·x2) + x3·x3)`. Refuses a coordinate that is not finite.
    let up (x: float * float * float) : Result<Point, GeometryRefusal> = Conformal.up Arithmetic.shipped x

    /// The Euclidean point: the coefficients of indices 2 to 4, each divided by the weight. Refuses a
    /// weight that is zero or not finite.
    let coordinates (p: Point) : Result<float * float * float, GeometryRefusal> =
        Conformal.coordinates Arithmetic.shipped p

    /// The weight: the coefficient of `n0`, a signed number and not a norm.
    let weight (p: Point) : float = p.N0

    /// Every coefficient divided by the weight, once. Refuses a weight that is zero or not finite.
    let normalise (p: Point) : Result<Point, GeometryRefusal> =
        Conformal.pointNormalise Arithmetic.shipped p

    /// The multivector that holds the point's coefficients at indices 1 to 5 and `+0.0` elsewhere.
    let toMultivector (p: Point) : Multivector = Conformal.pointMv p

    /// A copy of the coefficients of indices 1 to 5; the rest is discarded without a test.
    let ofMultivector (m: Multivector) : Point = Conformal.mvPoint m

/// A point pair's functions (DECISIONS.md D85 items 4, 5 and 6).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PointPair =

    /// The pair `P ∧ Q`, in that order.
    let through (p: Point) (q: Point) : PointPair =
        Conformal.pairThrough Arithmetic.shipped p q

    /// The weight: the norm of the coefficients of `n0∧e1`, `n0∧e2` and `n0∧e3`; never negative.
    let weight (x: PointPair) : float =
        Conformal.pairWeight Arithmetic.shipped x

    /// Every coefficient divided by the weight, once. Refuses a weight that is zero or not finite.
    let normalise (x: PointPair) : Result<PointPair, GeometryRefusal> =
        Conformal.pairNormalise Arithmetic.shipped x

    /// The centre: the point `⟨(X n∞) X⟩₁`, read as a point is.
    let centre (x: PointPair) : Result<float * float * float, GeometryRefusal> =
        Conformal.pairCentre Arithmetic.shipped x

    /// The squared radius `−(X ∗ X̃) / (W ∗ W̃)`, `W = n∞ ⌋ X`: a quarter of the squared distance
    /// between the two points, and negative for an imaginary pair.
    let squaredRadius (x: PointPair) : Result<float, GeometryRefusal> =
        Conformal.pairSquaredRadius Arithmetic.shipped x

    /// The two points, decided by the squared radius `ρ` against `tau` (a squared length) before any
    /// root is taken. `ρ > τ`: `⟨(X − s) W⁻¹⟩₁` and `⟨(X + s) W⁻¹⟩₁` with `s = +√(X ∗ X)`, in that
    /// order. `|ρ| ≤ τ`: the centre. `ρ < −τ`: the centre and `ρ`.
    let points (tau: float) (x: PointPair) : Result<PairPoints, GeometryRefusal> =
        Conformal.pairPoints Arithmetic.shipped tau x

    /// What kind of blade this is, by the norms of its groups of coefficients against `tau` (in
    /// coefficient units) and, for a round, its squared radius against `tau`.
    let kind (tau: float) (x: PointPair) : Result<PointPairKind, GeometryRefusal> =
        Conformal.pairKind Arithmetic.shipped tau x

    let toMultivector (x: PointPair) : Multivector = Conformal.pairMv x

    /// A copy of the coefficients of indices 6 to 15.
    let ofMultivector (m: Multivector) : PointPair = Conformal.mvPair m

/// A line's functions (DECISIONS.md D85 items 4, 5 and 6).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Line =

    /// The line `P ∧ Q ∧ n∞`: its direction is `q − p`, times the two weights.
    let through (p: Point) (q: Point) : Line =
        Conformal.lineThrough Arithmetic.shipped p q

    /// The line `up p ∧ d̂ ∧ n∞` through the point `p` with the direction `d`, of weight one. `d` is
    /// divided by its norm, one division for each component. Refuses a coordinate that is not finite
    /// and a direction that is zero or not finite.
    let ofPointDirection (p: float * float * float) (d: float * float * float) : Result<Line, GeometryRefusal> =
        Conformal.lineOfPointDirection Arithmetic.shipped p d

    /// The weight: the norm of the direction; never negative.
    let weight (x: Line) : float =
        Conformal.lineWeight Arithmetic.shipped x

    /// Every coefficient divided by the weight, once. Refuses a weight that is zero or not finite.
    let normalise (x: Line) : Result<Line, GeometryRefusal> =
        Conformal.lineNormalise Arithmetic.shipped x

    /// The direction: the coefficients of indices 18, 20 and 21, read and not computed, so not
    /// divided by the weight.
    let direction (x: Line) : float * float * float = x.N0E1NInf, x.N0E2NInf, x.N0E3NInf

    /// The moment `p ∧ d`: the coefficients of `e1∧e2∧n∞`, `e1∧e3∧n∞` and `e2∧e3∧n∞`, read.
    let moment (x: Line) : float * float * float = x.E1E2NInf, x.E1E3NInf, x.E2E3NInf

    /// The point of the line nearest the origin: the coordinates of the vector `⟨(n0 ⌋ X) X⁻¹⟩₁`.
    let nearestPoint (x: Line) : Result<float * float * float, GeometryRefusal> =
        Conformal.lineNearestPoint Arithmetic.shipped x

    /// What kind of blade this is, by the norms of its groups of coefficients against `tau`.
    let kind (tau: float) (x: Line) : Result<FlatKind, GeometryRefusal> =
        Conformal.lineKind Arithmetic.shipped tau x

    /// What two lines meet in. Their meet in the whole space is the zero blade unless they are skew,
    /// so two lines of one plane are met within it: the span is the join of the first line with a
    /// point of the second. Each "is the zero blade" is the test of a kind, with `tau`.
    let meet (tau: float) (first: Line) (second: Line) : Result<LineMeet, GeometryRefusal> =
        Conformal.lineMeet Arithmetic.shipped tau first second

    /// The meet of the line with a plane: the flat point `P ∧ n∞` where it crosses, a blade at
    /// infinity where it is parallel, the zero blade where it lies in the plane.
    let meetPlane (x: Line) (plane: Plane) : PointPair =
        Conformal.lineMeetPlane Arithmetic.shipped x plane

    /// The meet of the line with a sphere: a point pair, whose two points run along the line's
    /// direction; imaginary where the line misses the sphere.
    let meetSphere (x: Line) (sphere: Sphere) : PointPair =
        Conformal.lineMeetSphere Arithmetic.shipped x sphere

    let toMultivector (x: Line) : Multivector = Conformal.lineMv x

    /// A copy of the coefficients of indices 18, 20, 21, 23, 24 and 25.
    let ofMultivector (m: Multivector) : Line = Conformal.mvLine m

/// A circle's functions (DECISIONS.md D85 items 4, 5 and 6).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Circle =

    /// The circle `P ∧ Q ∧ R`, oriented by the order of its points. Three collinear points give a
    /// flat, which `kind` answers carrying the line.
    let through (p: Point) (q: Point) (r: Point) : Circle =
        Conformal.circleThrough Arithmetic.shipped p q r

    /// The circle with this centre and radius in the plane with this normal:
    /// `meet (sphere c r) (plane n̂ (n̂ · c))`, counter-clockwise about the normal. Refuses a coordinate
    /// that is not finite, a radius that is negative or not finite, and a normal that is zero or not
    /// finite.
    let ofCentreRadiusNormal
        (centre: float * float * float)
        (radius: float)
        (normal: float * float * float)
        : Result<Circle, GeometryRefusal> =
        Conformal.circleOfCentreRadiusNormal Arithmetic.shipped centre radius normal

    /// The weight: the norm of the coefficients of `n0∧e1∧e2`, `n0∧e1∧e3` and `n0∧e2∧e3`.
    let weight (x: Circle) : float =
        Conformal.circleWeight Arithmetic.shipped x

    /// Every coefficient divided by the weight, once. Refuses a weight that is zero or not finite.
    let normalise (x: Circle) : Result<Circle, GeometryRefusal> =
        Conformal.circleNormalise Arithmetic.shipped x

    /// The centre: the point `⟨(X n∞) X⟩₁`, read as a point is.
    let centre (x: Circle) : Result<float * float * float, GeometryRefusal> =
        Conformal.circleCentre Arithmetic.shipped x

    /// The squared radius `−(X ∗ X̃) / (W ∗ W̃)`, `W = n∞ ⌋ X`: negative for the meet of two spheres
    /// that do not touch.
    let squaredRadius (x: Circle) : Result<float, GeometryRefusal> =
        Conformal.circleSquaredRadius Arithmetic.shipped x

    /// What kind of blade this is, by the norms of its groups of coefficients against `tau` and, for a
    /// round, its squared radius against `tau`.
    let kind (tau: float) (x: Circle) : Result<CircleKind, GeometryRefusal> =
        Conformal.circleKind Arithmetic.shipped tau x

    /// The circle's plane `X ∧ n∞`. Whether a point is in it is `Plane.side`, and the caller's to ask.
    let plane (x: Circle) : Plane =
        Conformal.circlePlane Arithmetic.shipped x

    /// A point's power against the circle, for a point in the circle's plane `Y = X ∧ n∞`:
    /// `(−2.0 × ((P ∧ X) ∗ Ỹ)) / (w_P × (Y ∗ Ỹ))`, a squared length. Negative inside, whatever the
    /// order of the circle's points.
    let power (p: Point) (x: Circle) : Result<float, GeometryRefusal> =
        Conformal.circlePower Arithmetic.shipped p x

    /// Where a point of the circle's plane is: `Inside` when the power is below `−tau`, `On` within
    /// `tau`, `Outside` above it. At `tau = 0` the signs decide and no quotient is formed.
    let side (tau: float) (p: Point) (x: Circle) : Result<Side, GeometryRefusal> =
        Conformal.circleSide Arithmetic.shipped tau p x

    let toMultivector (x: Circle) : Multivector = Conformal.circleMv x

    /// A copy of the coefficients of indices 16 to 25.
    let ofMultivector (m: Multivector) : Circle = Conformal.mvCircle m

/// A plane's functions (DECISIONS.md D85 items 4, 5 and 6).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Plane =

    /// The plane `P ∧ Q ∧ R ∧ n∞`: its normal is `(q − p) × (r − p)`, times the three weights.
    let through (p: Point) (q: Point) (r: Point) : Plane =
        Conformal.planeThrough Arithmetic.shipped p q r

    /// The plane `(n̂ + δ n∞)*` with this normal at the distance `δ` from the origin along it, of
    /// weight one. The normal is divided by its norm. Refuses a normal that is zero or not finite and
    /// a distance that is not finite.
    let ofNormalDistance (normal: float * float * float) (distance: float) : Result<Plane, GeometryRefusal> =
        Conformal.planeOfNormalDistance Arithmetic.shipped normal distance

    /// The weight: the norm of the normal; never negative.
    let weight (x: Plane) : float =
        Conformal.planeWeight Arithmetic.shipped x

    /// Every coefficient divided by the weight, once. Refuses a weight that is zero or not finite.
    let normalise (x: Plane) : Result<Plane, GeometryRefusal> =
        Conformal.planeNormalise Arithmetic.shipped x

    /// The normal: the Euclidean part of the plane's vector, its undual, an exact read. It is `n̂`
    /// times the weight.
    let normal (x: Plane) : float * float * float = Conformal.planeNormal x

    /// The distance from the origin along the normal, times the weight: the `n∞` coefficient of the
    /// plane's vector, an exact read.
    let distance (x: Plane) : float = Conformal.planeDistance x

    /// The point of the plane nearest the origin: the coordinates of the vector `⟨(n0 ⌋ X) X⁻¹⟩₁`.
    let nearestPoint (x: Plane) : Result<float * float * float, GeometryRefusal> =
        Conformal.planeNearestPoint Arithmetic.shipped x

    /// What kind of blade this is, by the norms of its groups of coefficients against `tau`.
    let kind (tau: float) (x: Plane) : Result<FlatKind, GeometryRefusal> =
        Conformal.planeKind Arithmetic.shipped tau x

    /// The meet of two planes: their line, a blade at infinity where they are parallel, the zero
    /// blade where they are one plane.
    let meet (first: Plane) (second: Plane) : Line =
        Conformal.planeMeet Arithmetic.shipped first second

    /// A point's signed distance from the plane: `(P ∗ π) / (w_P × w_π)`, `π` the plane's vector, a
    /// length, positive on the side the normal points to.
    let signedDistance (p: Point) (x: Plane) : Result<float, GeometryRefusal> =
        Conformal.planeSignedDistance Arithmetic.shipped p x

    /// Where a point is: `Negative` when the signed distance is below `−tau`, `On` within `tau`,
    /// `Positive` above it. At `tau = 0` the signs decide and no quotient is formed.
    let side (tau: float) (p: Point) (x: Plane) : Result<PlaneSide, GeometryRefusal> =
        Conformal.planeSide Arithmetic.shipped tau p x

    let toMultivector (x: Plane) : Multivector = Conformal.planeMv x

    /// A copy of the coefficients of indices 27 to 30.
    let ofMultivector (m: Multivector) : Plane = Conformal.mvPlane m

/// A sphere's functions (DECISIONS.md D85 items 4, 5 and 6).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Sphere =

    /// The sphere `P ∧ Q ∧ R ∧ S`: its weight is `det [q − p, r − p, s − p]`, times the four weights.
    /// Four coplanar points give a flat, which `kind` answers carrying the plane.
    let through (p: Point) (q: Point) (r: Point) (s: Point) : Sphere =
        Conformal.sphereThrough Arithmetic.shipped p q r s

    /// The sphere `(up c − ½ r² n∞)*` with this centre and radius, of weight one; `½ r²` is
    /// `0.5 × (r × r)`. Refuses a coordinate that is not finite and a radius that is negative or not
    /// finite.
    let ofCentreRadius (centre: float * float * float) (radius: float) : Result<Sphere, GeometryRefusal> =
        Conformal.sphereOfCentreRadius Arithmetic.shipped centre radius

    /// The weight: the coefficient of `n0∧e1∧e2∧e3`, a signed number and not a norm.
    let weight (x: Sphere) : float = x.N0E1E2E3

    /// Every coefficient divided by the weight, once: a sphere of negative weight changes
    /// orientation. Refuses a weight that is zero or not finite.
    let normalise (x: Sphere) : Result<Sphere, GeometryRefusal> =
        Conformal.sphereNormalise Arithmetic.shipped x

    /// The centre: the point `⟨(X n∞) X⟩₁`, read as a point is.
    let centre (x: Sphere) : Result<float * float * float, GeometryRefusal> =
        Conformal.sphereCentre Arithmetic.shipped x

    /// The squared radius `−(X ∗ X̃) / (W ∗ W̃)`, `W = n∞ ⌋ X`.
    let squaredRadius (x: Sphere) : Result<float, GeometryRefusal> =
        Conformal.sphereSquaredRadius Arithmetic.shipped x

    /// What kind of blade this is, by the norms of its groups of coefficients against `tau` and, for a
    /// round, its squared radius against `tau`.
    let kind (tau: float) (x: Sphere) : Result<SphereKind, GeometryRefusal> =
        Conformal.sphereKind Arithmetic.shipped tau x

    /// The meet of two spheres: their circle, of negative squared radius where they do not touch.
    let meet (first: Sphere) (second: Sphere) : Circle =
        Conformal.sphereMeet Arithmetic.shipped first second

    /// The meet of the sphere with a plane: their circle.
    let meetPlane (x: Sphere) (plane: Plane) : Circle =
        Conformal.sphereMeetPlane Arithmetic.shipped x plane

    /// A point's power against the sphere: `(−2.0 × (P ∗ σ)) / (w_P × w_σ)`, `σ` the sphere's vector,
    /// which is `|x − c|² − r²`, a squared length. A power that is zero is `-0.0` over a positive
    /// divisor.
    let power (p: Point) (x: Sphere) : Result<float, GeometryRefusal> =
        Conformal.spherePower Arithmetic.shipped p x

    /// Where a point is: `Inside` when the power is below `−tau`, `On` within `tau`, `Outside` above
    /// it. At `tau = 0` the signs decide and no quotient is formed.
    let side (tau: float) (p: Point) (x: Sphere) : Result<Side, GeometryRefusal> =
        Conformal.sphereSide Arithmetic.shipped tau p x

    let toMultivector (x: Sphere) : Multivector = Conformal.sphereMv x

    /// A copy of the coefficients of indices 26 to 30.
    let ofMultivector (m: Multivector) : Sphere = Conformal.mvSphere m
