namespace Fuaran.Compute.Distributions

/// Every public operation of this package with its exactness class (DECISIONS.md D43, D79), as data a
/// schema walk reads through. Every operation is `Pinned`: each is `+ − × ÷ √`, comparisons and the
/// pinned functions of `Fuaran.Compute.Elementary` in one order, with no fused multiply-add and no
/// host mathematics call, so it answers the same bits on every conformant host. The suite holds the
/// table to the package's public surface both ways. The module is named for its package, as
/// `ElementaryOperations` is, because two opened modules named `Operations` shadow each other member
/// by member (DECISIONS.md D74 item 2).
[<RequireQualifiedAccess>]
module DistributionsOperations =

    /// How exact an operation's bytes are across hosts — D43's vocabulary. Declared inside this
    /// module, so `open Fuaran.Compute.Distributions` brings no type named `Exactness` into a caller's
    /// scope.
    [<RequireQualifiedAccess>]
    type Exactness =
        | Pinned
        | HostRounded

    /// `(operation, class)`, the operation spelled `<Module>.<function>`, in surface order.
    let exactness: (string * Exactness) list =
        [ "Normal.cdf", Exactness.Pinned
          "Normal.quantile", Exactness.Pinned
          "StudentT.cdf", Exactness.Pinned
          "StudentT.quantile", Exactness.Pinned
          "ChiSquare.cdf", Exactness.Pinned
          "ChiSquare.quantile", Exactness.Pinned
          "FisherF.cdf", Exactness.Pinned
          "FisherF.quantile", Exactness.Pinned ]

    /// The class of one operation, or `None` for a name this package does not publish.
    let exactnessOf (operation: string) : Exactness option =
        exactness |> List.tryFind (fun (o, _) -> o = operation) |> Option.map snd
