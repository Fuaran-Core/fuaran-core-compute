namespace Fuaran.Compute.LinearAlgebra

/// Every public operation of this package with its exactness class (DECISIONS.md D49 item 7, D43's
/// vocabulary), as data a schema walk reads through: a column computed by an operation listed here
/// carries that operation's class. Every operation is `Pinned` — built from `+ − × ÷ √` alone, in one
/// order, with no fused multiply-add and, since Phase 593's `Elementary`, no host mathematics call
/// anywhere — so each answers the same bits on every conformant host. The
/// suite holds the table to the package's public surface both ways: an operation published without a
/// row, or a row naming nothing published, is red.
[<RequireQualifiedAccess>]
module Operations =

    /// `(operation, class)`, the operation spelled `<Module>.<function>`, in surface order.
    let exactness: (string * Exactness) list =
        [ "Matrix.rows", Exactness.Pinned
          "Matrix.cols", Exactness.Pinned
          "Matrix.dims", Exactness.Pinned
          "Matrix.item", Exactness.Pinned
          "Matrix.init", Exactness.Pinned
          "Matrix.identity", Exactness.Pinned
          "Matrix.ofColumns", Exactness.Pinned
          "Matrix.column", Exactness.Pinned
          "Matrix.columns", Exactness.Pinned
          "Matrix.transpose", Exactness.Pinned
          "Matrix.toColumnMajor", Exactness.Pinned
          "Blas.dot", Exactness.Pinned
          "Blas.axpy", Exactness.Pinned
          "Blas.scale", Exactness.Pinned
          "Blas.add", Exactness.Pinned
          "Blas.sub", Exactness.Pinned
          "Blas.norm2", Exactness.Pinned
          "Blas.gemv", Exactness.Pinned
          "Blas.gemm", Exactness.Pinned
          "Blas.gram", Exactness.Pinned
          "UpperTriangular.ofMatrix", Exactness.Pinned
          "UpperTriangular.toMatrix", Exactness.Pinned
          "UpperTriangular.order", Exactness.Pinned
          "UpperTriangular.solveUpper", Exactness.Pinned
          "UpperTriangular.solveUpperT", Exactness.Pinned
          "Givens.make", Exactness.Pinned
          "Givens.apply", Exactness.Pinned
          "QRAccumulator.empty", Exactness.Pinned
          "QRAccumulator.cols", Exactness.Pinned
          "QRAccumulator.count", Exactness.Pinned
          "QRAccumulator.rss", Exactness.Pinned
          "QRAccumulator.r", Exactness.Pinned
          "QRAccumulator.qty", Exactness.Pinned
          "QRAccumulator.addRow", Exactness.Pinned
          "QRAccumulator.addRows", Exactness.Pinned
          "QR.householder", Exactness.Pinned
          "QR.pivoted", Exactness.Pinned
          "LeastSquares.ofAccumulator", Exactness.Pinned
          "LeastSquares.solve", Exactness.Pinned
          "LeastSquares.requireFullRank", Exactness.Pinned
          "LinAlgWire.matrixToJson", Exactness.Pinned
          "LinAlgWire.matrixOfJson", Exactness.Pinned
          "LinAlgWire.encodeMatrix", Exactness.Pinned
          "LinAlgWire.decodeMatrix", Exactness.Pinned
          "LinAlgWire.accumulatorToJson", Exactness.Pinned
          "LinAlgWire.accumulatorOfJson", Exactness.Pinned
          "LinAlgWire.encodeAccumulator", Exactness.Pinned
          "LinAlgWire.decodeAccumulator", Exactness.Pinned
          "Elementary.expTable", Exactness.Pinned
          "Elementary.lnTable", Exactness.Pinned
          "Elementary.powTable", Exactness.Pinned
          "Elementary.exp", Exactness.Pinned
          "Elementary.ln", Exactness.Pinned
          "Elementary.log10", Exactness.Pinned
          "Elementary.pow", Exactness.Pinned ]

    /// The class of one operation, or `None` for a name this package does not publish.
    let exactnessOf (operation: string) : Exactness option =
        exactness |> List.tryFind (fun (o, _) -> o = operation) |> Option.map snd
