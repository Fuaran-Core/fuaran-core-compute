namespace Fuaran.Compute.Elementary

/// How exact an operation's bytes are across hosts — D43's vocabulary, the one `SchemaWalk` reads a
/// column's lineage in: `Pinned`, every conformant host computes the same bits; `HostRounded`, hosts
/// agree within a stated ulp bound only. Every operation this package ships is `Pinned`;
/// `Operations.exactness` classifies each one. Declared here rather than read from a package above,
/// because this package depends on nothing in Compute (DECISIONS.md D74; the per-package type is
/// D49 item 2's precedent).
[<RequireQualifiedAccess>]
type Exactness =
    | Pinned
    | HostRounded

/// Every public operation of this package with its exactness class (DECISIONS.md D43, D52), as data a
/// schema walk reads through: a column computed by an operation listed here carries that operation's
/// class. Every operation is `Pinned` — built from `+ − × ÷ √` alone, in one order, with no fused
/// multiply-add and no host mathematics call anywhere — so each answers the same bits on every
/// conformant host. The suite holds the table to the package's public surface both ways: an
/// operation published without a row, or a row naming nothing published, is red.
[<RequireQualifiedAccess>]
module Operations =

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
          "Elementary.normalQuantile", Exactness.Pinned ]

    /// The class of one operation, or `None` for a name this package does not publish.
    let exactnessOf (operation: string) : Exactness option =
        exactness |> List.tryFind (fun (o, _) -> o = operation) |> Option.map snd
