namespace Fuaran.Compute.Solvers

/// Every public operation of this package with its exactness class (DECISIONS.md D43, D77), as data a
/// schema walk reads through. Every operation is `Pinned`: a solver's own arithmetic is `+ − × ÷` and
/// comparisons in one order, with no fused multiply-add and no host mathematics call, so given
/// functions that answer the same bits on every host it makes the same decisions and answers the same
/// bits on every host. The class says nothing of the caller's functions, which carry their own. The
/// suite holds the table to the package's public surface both ways. The module is named for its
/// package, as `ElementaryOperations` is, because two opened modules named `Operations` shadow each
/// other member by member (DECISIONS.md D74 item 2).
[<RequireQualifiedAccess>]
module SolversOperations =

    /// How exact an operation's bytes are across hosts — D43's vocabulary. Declared inside this
    /// module, so `open Fuaran.Compute.Solvers` brings no type named `Exactness` into a caller's scope.
    [<RequireQualifiedAccess>]
    type Exactness =
        | Pinned
        | HostRounded

    /// `(operation, class)`, the operation spelled `<Module>.<function>`, in surface order.
    let exactness: (string * Exactness) list =
        [ "StoppingRule.create", Exactness.Pinned
          "StoppingRule.valid", Exactness.Pinned
          "StoppingRule.armOf", Exactness.Pinned
          "StoppingRule.bracketWithin", Exactness.Pinned
          "StoppingRule.stops", Exactness.Pinned
          "StoppingRule.rises", Exactness.Pinned
          "Root.bisect", Exactness.Pinned
          "Root.bracketedNewton", Exactness.Pinned
          "Root.integerSearch", Exactness.Pinned
          "Minimise.goldenSection", Exactness.Pinned
          "Damped.iterate", Exactness.Pinned ]

    /// The class of one operation, or `None` for a name this package does not publish.
    let exactnessOf (operation: string) : Exactness option =
        exactness |> List.tryFind (fun (o, _) -> o = operation) |> Option.map snd
