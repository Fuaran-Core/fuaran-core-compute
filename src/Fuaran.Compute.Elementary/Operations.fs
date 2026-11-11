namespace Fuaran.Compute.Elementary

/// Every public operation of this package with its exactness class (DECISIONS.md D43, D52), as data a
/// schema walk reads through: a column computed by an operation listed here carries that operation's
/// class. Every operation is `Pinned` — built from `+ − × ÷ √` alone, in one order, with no fused
/// multiply-add and no host mathematics call anywhere (the generator's, `Rng`, from exact integer
/// arithmetic on 16-bit limbs, DECISIONS.md D76; the special functions', `Special`, over the pinned
/// `exp`, `ln` and `pow` and the solvers' stopping rules, DECISIONS.md D78) — so each answers the same bits on every
/// conformant host. The suite holds the table to the package's public surface both ways: an
/// operation published without a row, or a row naming nothing published, is red. The module is named
/// for its package, as `ElementaryLaws` is, because a module named `Operations` here would SHADOW
/// LinearAlgebra's `Operations` member by member in any file that opens both namespaces, the last open
/// winning silently (DECISIONS.md D74 item 2).
[<RequireQualifiedAccess>]
module ElementaryOperations =

    /// How exact an operation's bytes are across hosts — D43's vocabulary, the one `SchemaWalk` reads a
    /// column's lineage in: `Pinned`, every conformant host computes the same bits; `HostRounded`, hosts
    /// agree within a stated ulp bound only. Every operation this package ships is `Pinned`; `exactness`
    /// classifies each one. The type is this package's own, as D49 item 2 makes it every package's, and
    /// it is declared INSIDE this module rather than at namespace level, so `open Fuaran.Compute.Elementary`
    /// brings no type named `Exactness` into a caller's scope to shadow its own (DECISIONS.md D74).
    [<RequireQualifiedAccess>]
    type Exactness =
        | Pinned
        | HostRounded

    /// `(operation, class)`, the operation spelled `<Module>.<function>`, in surface order.
    let exactness: (string * Exactness) list =
        [ "Elementary.expTable", Exactness.Pinned
          "Elementary.lnTable", Exactness.Pinned
          "Elementary.powTable", Exactness.Pinned
          "Elementary.exp", Exactness.Pinned
          "Elementary.ln", Exactness.Pinned
          "Elementary.log10", Exactness.Pinned
          "Elementary.pow", Exactness.Pinned
          "Elementary.erfc", Exactness.Pinned
          "Elementary.normalCdf", Exactness.Pinned
          "Elementary.normalDensity", Exactness.Pinned
          "Elementary.normalQuantile", Exactness.Pinned
          "Special.gammaLn", Exactness.Pinned
          "Special.gamma", Exactness.Pinned
          "Special.digamma", Exactness.Pinned
          "Special.polygamma", Exactness.Pinned
          "Special.trigamma", Exactness.Pinned
          "Special.betaLn", Exactness.Pinned
          "Special.beta", Exactness.Pinned
          "Special.regularizedGammaP", Exactness.Pinned
          "Special.regularizedGammaQ", Exactness.Pinned
          "Special.regularizedBeta", Exactness.Pinned
          "Special.inverseRegularizedBeta", Exactness.Pinned
          "Rng.ofSeed", Exactness.Pinned
          "Rng.next", Exactness.Pinned
          "Rng.uniform53", Exactness.Pinned
          "Rng.below", Exactness.Pinned ]

    /// The class of one operation, or `None` for a name this package does not publish.
    let exactnessOf (operation: string) : Exactness option =
        exactness |> List.tryFind (fun (o, _) -> o = operation) |> Option.map snd
