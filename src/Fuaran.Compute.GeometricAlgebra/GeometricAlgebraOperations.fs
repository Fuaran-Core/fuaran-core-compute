namespace Fuaran.Compute.GeometricAlgebra

/// Every public operation of this package with its exactness class (DECISIONS.md D43, D85 item 7), as
/// data a schema walk reads through. Every operation is `Pinned`: no host mathematics function is
/// called on any path, and no product is fused, so each answers the same bits on every conformant
/// host. What stands behind the class differs by row:
///
///   * `init`, `ofScalar`, `basis`, `coefficient`, `toArray`, `grade`, `negate`, `reverse`,
///     `gradeInvolution`, `dual` and `undual` are copies and exact negation: exact on every input.
///   * `add`, `subtract` and `scale` are one `+`, `−` or `×` for each coefficient: correctly rounded,
///     coefficient by coefficient.
///   * `geometricProduct`, `outerProduct`, `leftContraction`, `scalarProduct` and `squaredNorm` are
///     D85 item 3's fold: exact whenever every product and every partial sum is representable, and
///     otherwise the fold's rounding, the same bits on every host.
///
/// The suite holds the table to the package's public surface both ways. The module is named for its
/// package, because two opened modules named `Operations` shadow each other member by member
/// (DECISIONS.md D74 item 2).
[<RequireQualifiedAccess>]
module GeometricAlgebraOperations =

    /// How exact an operation's bytes are across hosts — D43's vocabulary. Declared inside this
    /// module, so `open Fuaran.Compute.GeometricAlgebra` brings no type named `Exactness` into a
    /// caller's scope.
    [<RequireQualifiedAccess>]
    type Exactness =
        | Pinned
        | HostRounded

    /// `(operation, class)`, the operation spelled `<Module>.<function>`, in surface order.
    let exactness: (string * Exactness) list =
        [ "Multivector.init", Exactness.Pinned
          "Multivector.ofScalar", Exactness.Pinned
          "Multivector.basis", Exactness.Pinned
          "Multivector.coefficient", Exactness.Pinned
          "Multivector.toArray", Exactness.Pinned
          "Multivector.grade", Exactness.Pinned
          "Multivector.add", Exactness.Pinned
          "Multivector.subtract", Exactness.Pinned
          "Multivector.scale", Exactness.Pinned
          "Multivector.negate", Exactness.Pinned
          "Multivector.reverse", Exactness.Pinned
          "Multivector.gradeInvolution", Exactness.Pinned
          "Multivector.dual", Exactness.Pinned
          "Multivector.undual", Exactness.Pinned
          "Multivector.geometricProduct", Exactness.Pinned
          "Multivector.outerProduct", Exactness.Pinned
          "Multivector.leftContraction", Exactness.Pinned
          "Multivector.scalarProduct", Exactness.Pinned
          "Multivector.squaredNorm", Exactness.Pinned ]

    /// The class of one operation, or `None` for a name this package does not publish.
    let exactnessOf (operation: string) : Exactness option =
        exactness |> List.tryFind (fun (o, _) -> o = operation) |> Option.map snd
