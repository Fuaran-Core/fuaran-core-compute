namespace Fuaran.Compute.Regression

/// Every public operation of this package with its exactness class (DECISIONS.md D49 item 7, D43's
/// vocabulary), as data a schema walk reads through: a column computed by an operation listed here
/// carries that operation's class. Every operation is `Pinned` — built from `+ − × ÷ √` alone, in one
/// order, with no fused multiply-add and no host mathematics call anywhere — so each answers the same
/// bits on every conformant host. The matrix operations it folds with are Fuaran.Compute.LinearAlgebra's
/// and the scalar functions `Glm` reads are Fuaran.Compute.Elementary's, each classified by that
/// package's own table. The suite holds the table to the package's public surface both ways: an
/// operation published without a row, or a row naming nothing published, is red. The module is named
/// for its package, as `RegressionLaws` is, because a module named `Operations` here would SHADOW
/// LinearAlgebra's `Operations` member by member in any file that opens both namespaces, the last open
/// winning silently (DECISIONS.md D74 item 2, D75).
[<RequireQualifiedAccess>]
module RegressionOperations =

    /// How exact an operation's bytes are across hosts — D43's vocabulary, the one `SchemaWalk` reads a
    /// column's lineage in: `Pinned`, every conformant host computes the same bits; `HostRounded`, hosts
    /// agree within a stated ulp bound only. Every operation this package ships is `Pinned`; `exactness`
    /// classifies each one. The type is this package's own, as D49 item 2 makes it every package's, and
    /// it is declared INSIDE this module rather than at namespace level, so `open Fuaran.Compute.Regression`
    /// brings no type named `Exactness` into a caller's scope to shadow its own (DECISIONS.md D74, D75).
    [<RequireQualifiedAccess>]
    type Exactness =
        | Pinned
        | HostRounded

    /// `(operation, class)`, the operation spelled `<Module>.<function>`, in surface order.
    let exactness: (string * Exactness) list =
        [ "QRAccumulator.empty", Exactness.Pinned
          "QRAccumulator.cols", Exactness.Pinned
          "QRAccumulator.count", Exactness.Pinned
          "QRAccumulator.rss", Exactness.Pinned
          "QRAccumulator.r", Exactness.Pinned
          "QRAccumulator.qty", Exactness.Pinned
          "QRAccumulator.addRow", Exactness.Pinned
          "QRAccumulator.addRows", Exactness.Pinned
          "LeastSquares.ofAccumulator", Exactness.Pinned
          "LeastSquares.solve", Exactness.Pinned
          "LeastSquares.requireFullRank", Exactness.Pinned
          "LeastSquares.standardErrors", Exactness.Pinned
          "LeastSquares.robustStandardErrors", Exactness.Pinned
          "RegressionWire.accumulatorToJson", Exactness.Pinned
          "RegressionWire.accumulatorOfJson", Exactness.Pinned
          "RegressionWire.encodeAccumulator", Exactness.Pinned
          "RegressionWire.decodeAccumulator", Exactness.Pinned
          "GlmControl.create", Exactness.Pinned
          "Glm.admitted", Exactness.Pinned
          "Glm.defaultLink", Exactness.Pinned
          "Glm.linkFunction", Exactness.Pinned
          "Glm.inverseLink", Exactness.Pinned
          "Glm.meanDerivative", Exactness.Pinned
          "Glm.validPredictor", Exactness.Pinned
          "Glm.responseInterval", Exactness.Pinned
          "Glm.fit", Exactness.Pinned
          "Glm.nullDeviance", Exactness.Pinned
          "Glm.robustStandardErrors", Exactness.Pinned ]

    /// The class of one operation, or `None` for a name this package does not publish.
    let exactnessOf (operation: string) : Exactness option =
        exactness |> List.tryFind (fun (o, _) -> o = operation) |> Option.map snd
