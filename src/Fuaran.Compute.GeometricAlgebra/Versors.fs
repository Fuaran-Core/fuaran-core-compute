namespace Fuaran.Compute.GeometricAlgebra

// ============================================================================
//  Phase 729 — the versors, as typed wrappers (DECISIONS.md D85 items 4 and 8;
//  the names are D88's).
//
//  A wrapper is a plain immutable record: one named `float` field for each
//  coefficient its type carries, in index order, named for its blade, and
//  `Scalar` for the coefficient of `1`. Each widens to a `Multivector` and is
//  copied back from one, the coefficients it does not carry being `+0.0`.
//
//  The types are declared widest first, so a record expression that names only
//  the fields of a smaller type reads as the smaller one: a rotor's fields are
//  four of a motor's, and a translator's are four of a motor's and of a
//  dilator's.
//
//  FSharp.Core only; Fable-clean.
// ============================================================================

/// A motor: a rotation and then a translation, which is a screw motion. The coefficients of indices
/// 0, 10 to 15 and 30.
type Motor =
    { Scalar: float
      E1E2: float
      E1E3: float
      E1NInf: float
      E2E3: float
      E2NInf: float
      E3NInf: float
      E1E2E3NInf: float }

/// A dilator: a uniform scaling about a point. The coefficients of indices 0, 9, 12, 14 and 15.
type Dilator =
    { Scalar: float
      N0NInf: float
      E1NInf: float
      E2NInf: float
      E3NInf: float }

/// A rotor: a rotation about an axis through the origin. The coefficients of indices 0, 10, 11 and
/// 13.
type Rotor =
    { Scalar: float
      E1E2: float
      E1E3: float
      E2E3: float }

/// A translator. The coefficients of indices 0, 12, 14 and 15.
type Translator =
    { Scalar: float
      E1NInf: float
      E2NInf: float
      E3NInf: float }

/// A reflection in a plane: the plane's vector `n̂ + δ n∞`. The coefficients of indices 2 to 5.
type Reflection =
    { E1: float
      E2: float
      E3: float
      NInf: float }

/// Why a matrix was not read as a motor and a factor (DECISIONS.md D85 item 4; the cases are D88's).
/// A matrix that is not a similarity, or that reverses orientation, is refused by name with the
/// measure that failed.
[<RequireQualifiedAccess>]
type MatrixRefusal =
    /// The threshold `τ` is negative, a NaN or an infinity.
    | InvalidThreshold
    /// The array does not hold sixteen entries.
    | NotSixteenEntries
    /// An entry is a NaN or an infinity.
    | NonFiniteEntry
    /// The last row is not `0 0 0 1`, compared exactly.
    | NotAffine
    /// The squared length of one of the first three columns is zero: a zero column, or one whose
    /// squares underflow.
    | ZeroDivisor
    /// The squared length of one of the first three columns overflows.
    | NonFiniteDivisor
    /// The second or the third column is not of the first column's length. Carries the statistic
    /// that failed: the difference of the two squared lengths over the first column's.
    | NotOneLength of float
    /// Two of the first three columns are not orthogonal. Carries the statistic that failed: their
    /// dot product over the product of their norms, which is the cosine of the angle between them.
    | NotOrthogonal of float
    /// The first three columns reverse orientation. Carries the measure that failed: the triple
    /// product `(a × b) · c`, which is negative.
    | ReversesOrientation of float
    /// The answer would hold a NaN: the arithmetic overflowed into one.
    | NotANumber
