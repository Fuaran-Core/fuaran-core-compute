namespace Fuaran.Compute.GeometricAlgebraOps

/// Every public operation of this package with its exactness class (DECISIONS.md D43, D85 item 7,
/// D90), as data a schema walk reads through. Every operation is `Pinned`: no host mathematics
/// function is called on any path, and the only arithmetic of the package's own is the inverse's
/// negation and reciprocal and the compaction's additions, each one rounded operation. What stands
/// behind the class differs by row:
///
///   * `GeometricState.empty`, `toList`, `tryFind` and `count` read or copy the state: exact.
///   * `GeometricOps.apply`, `applyAll` and `streamWitness` build every object and every versor
///     through `Fuaran.Compute.GeometricAlgebra`'s published constructors and apply it by its
///     published `applyTo…`: their answers are the algebra's, the same bits on every host, as exact
///     and as accurate as the algebra's table says of those functions. `canApply` answers whether
///     `apply` does.
///   * `GeometricOps.invert` negates a parameter (exact) or forms `1.0 / factor` (one correctly
///     rounded division); `compact` adds parameters, one correctly rounded addition each.
///   * `GeometricOps.encode` and `decode` copy bit patterns: exact on every input but a NaN, which
///     is written as one pattern and read as the host's NaN.
///
/// The suite holds the table to the package's public surface both ways. The module is named for its
/// package, because two opened modules named `Operations` shadow each other member by member
/// (DECISIONS.md D74 item 2).
[<RequireQualifiedAccess>]
module GeometricAlgebraOpsOperations =

    /// How exact an operation's bytes are across hosts — D43's vocabulary. Declared inside this
    /// module, so `open Fuaran.Compute.GeometricAlgebraOps` brings no type named `Exactness` into a
    /// caller's scope.
    [<RequireQualifiedAccess>]
    type Exactness =
        | Pinned
        | HostRounded

    /// `(operation, class)`, the operation spelled `<Module>.<function>`, in surface order.
    let exactness: (string * Exactness) list =
        [ "GeometricState.empty", Exactness.Pinned
          "GeometricState.toList", Exactness.Pinned
          "GeometricState.tryFind", Exactness.Pinned
          "GeometricState.count", Exactness.Pinned
          "GeometricOps.apply", Exactness.Pinned
          "GeometricOps.canApply", Exactness.Pinned
          "GeometricOps.applyAll", Exactness.Pinned
          "GeometricOps.invert", Exactness.Pinned
          "GeometricOps.encode", Exactness.Pinned
          "GeometricOps.decode", Exactness.Pinned
          "GeometricOps.streamWitness", Exactness.Pinned
          "GeometricOps.compact", Exactness.Pinned ]

    /// The class of one operation, or `None` for a name this package does not publish.
    let exactnessOf (operation: string) : Exactness option =
        exactness |> List.tryFind (fun (o, _) -> o = operation) |> Option.map snd
