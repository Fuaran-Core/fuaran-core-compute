namespace Fuaran.Compute.GeometricAlgebra

// ============================================================================
//  Phase 728 — the objects of three-dimensional Euclidean space as blades of
//  `Cl(4,1)`: the six typed wrappers of DECISIONS.md D85 item 8, the one union
//  of refusals, and the unions of kinds an answer has (D85 item 6). The names
//  of the cases are D87's; everything they mean is D85's.
//
//  A wrapper is a plain immutable record: one named `float` field for each
//  coefficient its type carries, in index order, the field named for its
//  blade. `Circle` is declared before `Line`, and `Sphere` before `Plane`,
//  because a line's blades are six of a circle's and a plane's are four of a
//  sphere's: a record expression that names only the shared fields then reads
//  as the smaller type.
//
//  The records keep F#'s own equality, which is not what a law compares with
//  (D85 item 8): a law compares bits.
//
//  FSharp.Core only; Fable-clean.
// ============================================================================

/// Why a function of this package refused (DECISIONS.md D85 items 4, 5 and 6; the names are D87's).
/// Nothing throws on bad data: a refusal is a case, and an `Ok` never holds a NaN.
[<RequireQualifiedAccess>]
type GeometryRefusal =
    /// A coordinate of a point parameter is a NaN or an infinity.
    | NonFiniteCoordinate
    /// A direction parameter (a line's direction, a normal) has norm zero.
    | ZeroDirection
    /// A direction parameter holds a NaN or an infinity, or its norm overflows.
    | NonFiniteDirection
    /// A radius is negative.
    | NegativeRadius
    /// A radius is a NaN or an infinity.
    | NonFiniteRadius
    /// A plane's distance from the origin is a NaN or an infinity.
    | NonFiniteDistance
    /// A divisor is zero: a weight, `X ∗ X̃`, `W ∗ W̃`, `J ∗ J̃`, or the product of two weights.
    | ZeroDivisor
    /// A divisor is a NaN or an infinity.
    | NonFiniteDivisor
    /// A threshold `τ` is negative, a NaN or an infinity.
    | InvalidThreshold
    /// An answer would hold a NaN: the operands held one, or their arithmetic overflowed into one.
    | NotANumber

/// A point: a vector, the coefficients of indices 1 to 5. `Point.up` builds
/// `n0 + x + ½ x² n∞`, of weight one.
type Point =
    { N0: float
      E1: float
      E2: float
      E3: float
      NInf: float }

/// A point pair `P ∧ Q`: a bivector, the coefficients of indices 6 to 15.
type PointPair =
    { N0E1: float
      N0E2: float
      N0E3: float
      N0NInf: float
      E1E2: float
      E1E3: float
      E1NInf: float
      E2E3: float
      E2NInf: float
      E3NInf: float }

/// A circle `P ∧ Q ∧ R`: a trivector, the coefficients of indices 16 to 25.
type Circle =
    { N0E1E2: float
      N0E1E3: float
      N0E1NInf: float
      N0E2E3: float
      N0E2NInf: float
      N0E3NInf: float
      E1E2E3: float
      E1E2NInf: float
      E1E3NInf: float
      E2E3NInf: float }

/// A line `P ∧ Q ∧ n∞`: the coefficients of indices 18, 20 and 21, its direction, and 23, 24 and 25,
/// its moment.
type Line =
    { N0E1NInf: float
      N0E2NInf: float
      N0E3NInf: float
      E1E2NInf: float
      E1E3NInf: float
      E2E3NInf: float }

/// A sphere `P ∧ Q ∧ R ∧ S`: the coefficients of indices 26 to 30.
type Sphere =
    { N0E1E2E3: float
      N0E1E2NInf: float
      N0E1E3NInf: float
      N0E2E3NInf: float
      E1E2E3NInf: float }

/// A plane `P ∧ Q ∧ R ∧ n∞`: the coefficients of indices 27 to 30.
type Plane =
    { N0E1E2NInf: float
      N0E1E3NInf: float
      N0E2E3NInf: float
      E1E2E3NInf: float }

/// What a round is by its squared radius `ρ` against the caller's `τ`: `Real` when `ρ > τ`, `Tangent`
/// when `|ρ| ≤ τ`, `Imaginary` when `ρ < −τ` (DECISIONS.md D85 item 6).
[<RequireQualifiedAccess>]
type Reality =
    | Real
    | Tangent
    | Imaginary

/// What kind of blade a `PointPair` is (DECISIONS.md D85 item 6).
[<RequireQualifiedAccess>]
type PointPairKind =
    /// A point pair.
    | Round of Reality
    /// A flat of grade two, `P ∧ n∞`: what a line and a plane meet in, answered as the point's
    /// coordinates.
    | FlatPoint of float * float * float
    /// Coefficients only on the blades that hold `n∞` and not `n0`: what two parallel lines meet in
    /// within their plane.
    | AtInfinity
    /// The zero blade.
    | Zero
    /// Anything else.
    | NoObject

/// What kind of blade a `Circle` is (DECISIONS.md D85 item 6).
[<RequireQualifiedAccess>]
type CircleKind =
    /// A circle.
    | Round of Reality
    /// A flat: three collinear points define no circle, and the answer carries the line.
    | Flat of Line
    | AtInfinity
    | Zero
    | NoObject

/// What kind of blade a `Sphere` is (DECISIONS.md D85 item 6).
[<RequireQualifiedAccess>]
type SphereKind =
    /// A sphere.
    | Round of Reality
    /// A flat: four coplanar points define no sphere, and the answer carries the plane.
    | Flat of Plane
    | AtInfinity
    | Zero
    | NoObject

/// What kind of blade a `Line` or a `Plane` is (DECISIONS.md D85 item 6). Neither type carries a
/// blade that holds `n0` and not `n∞`, so neither is ever a round.
[<RequireQualifiedAccess>]
type FlatKind =
    /// A line, or a plane.
    | Flat
    /// The meet of two parallel planes; a plane through no finite point.
    | AtInfinity
    | Zero
    /// A coefficient is a NaN.
    | NoObject

/// A point pair's two points (DECISIONS.md D85 item 4), decided by the squared radius before any root
/// is taken.
[<RequireQualifiedAccess>]
type PairPoints =
    /// The two points, in order: for `P ∧ Q`, `P` and then `Q`.
    | Real of Point * Point
    /// The one point, the centre, as coordinates.
    | Tangent of float * float * float
    /// The centre, as coordinates, and the squared radius, which is negative.
    | Imaginary of (float * float * float) * float

/// What two lines meet in (DECISIONS.md D85 item 4).
[<RequireQualifiedAccess>]
type LineMeet =
    /// The lines lie in one plane and are not one line: their meet within that plane, a flat point
    /// where they cross and a blade at infinity where they are parallel. `PointPair.kind` says which.
    | Within of PointPair
    /// The lines are one line.
    | SameLine
    /// The lines span the space: their meet in it, a multiple of `n∞`.
    | Skew of Point

/// Where a point is against a sphere, or against a circle in the circle's plane: the sign of the
/// power (DECISIONS.md D85 item 6).
[<RequireQualifiedAccess>]
type Side =
    | Inside
    | On
    | Outside

/// Where a point is against a plane: the sign of the signed distance, positive on the side the
/// normal points to (DECISIONS.md D85 item 6).
[<RequireQualifiedAccess>]
type PlaneSide =
    | Negative
    | On
    | Positive
