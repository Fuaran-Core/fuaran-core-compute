namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 632 — the regression law vectors: the streaming least-squares
//  kernel's authored hard inputs, each answer computed by calling
//  Fuaran.Compute.Regression, rendered through the substrate's one corpus
//  writer into `conformance/laws/regression-laws.json`.
//
//  The cases left `LinearAlgebraVectors` with the code they exercise (Phase
//  591's accumulator, fit and wire form), each row unchanged, and render
//  through that module's cells, so a row reads exactly as it did there.
//
//  Fable-clean: the node leg (LinearAlgebraNode/) compiles this file and
//  renders the same document under JavaScript, and the suite holds the two
//  renderings and the committed file equal.
// ============================================================================

open System
open Fuaran.Core
open Fuaran.Compute.LinearAlgebra
open Fuaran.Compute.Regression

module RegressionVectors =

    module LA = LinearAlgebraVectors

    /// The file this family is written to under `laws/`.
    let fileName = "regression-laws.json"

    /// The rows `x`, `y` and weights `w` folded into an empty accumulator by `addRows`.
    let private accumulate (c: LA.Case) : Result<QRAccumulator, LinAlgError> =
        let x = LA.mat c "x"

        QRAccumulator.empty x.Cols
        |> QRAccumulator.addRows x (LA.vec c "y") (LA.vec c "w")

    let private accumulatorCell (acc: QRAccumulator) : CorpusCell =
        CorpusCell.Row
            [ "r", LA.matrixCell (QRAccumulator.r acc)
              "qty", LA.vecCell (QRAccumulator.qty acc)
              "rss", CorpusFile.str (LA.hex (QRAccumulator.rss acc))
              "count", CorpusFile.num (QRAccumulator.count acc) ]

    /// A fit: each coefficient's bits, or the token `aliased`.
    let private fitCell (fit: LeastSquaresFit) : CorpusCell =
        CorpusCell.Row
            [ "coefficients",
              CorpusCell.List
                  [ for b in fit.Coefficients ->
                        match b with
                        | Some v -> CorpusFile.str (LA.hex v)
                        | None -> CorpusFile.str "aliased" ]
              "rank", CorpusFile.num fit.Rank
              "aliased", LA.ints fit.Aliased
              "rss", CorpusFile.str (LA.hex fit.Rss)
              "count", CorpusFile.num fit.Count
              "qty", LA.vecCell fit.Qty
              "rInverse", LA.matrixCell fit.RInverse ]

    /// The reference answer for one case, by calling the package.
    let expected (c: LA.Case) : CorpusCell =
        match c.Op with
        | "qrAccumulator" -> accumulate c |> LA.answer accumulatorCell
        | "accumulatorWire" -> accumulate c |> LA.answer (RegressionWire.encodeAccumulator >> CorpusFile.str)
        | "leastSquares" -> accumulate c |> Result.bind LeastSquares.ofAccumulator |> LA.answer fitCell
        | other -> failwithf "regression vector %s: no operation %s" c.Id other

    // ---- the authored inputs ---------------------------------------------------------------------

    let private tiny = Double.Epsilon
    let private inf = Double.PositiveInfinity
    let private nan = Double.NaN

    /// Every authored case, in file order.
    let cases: LA.Case list =
        [ // Phase 591 — the streaming QR: the state, the fit and the wire form.
          // A design is `x` (column-major), `y` the responses, `w` the prior weights.
          LA.case
              "qr-state-diagonal-non-negative"
              "qrAccumulator"
              [ "x", LA.Mat(3, 2, [ -3.0; 4.0; -1.0; 2.0; -5.0; 0.5 ])
                "y", LA.Vec [ -1.0; 2.0; 3.0 ]
                "w", LA.Vec [ 1.0; 1.0; 1.0 ] ]
          LA.case
              "qr-state-weights-scale-by-root"
              "qrAccumulator"
              [ "x", LA.Mat(3, 2, [ 1.0; 1.0; 1.0; 0.0; 1.0; 2.0 ])
                "y", LA.Vec [ 1.0; 2.0; 5.0 ]
                "w", LA.Vec [ 2.0; 0.0; 0.25 ] ]
          LA.case
              "qr-state-zero-cell-is-identity"
              "qrAccumulator"
              [ "x", LA.Mat(2, 2, [ 0.0; -0.0; 3.0; 4.0 ])
                "y", LA.Vec [ 1.0; 1.0 ]
                "w", LA.Vec [ 1.0; 1.0 ] ]
          LA.case
              "qr-state-tiny-and-huge-rows"
              "qrAccumulator"
              [ "x", LA.Mat(3, 2, [ 1e-300; 1e300; 1.0; tiny; -1e200; 1.0 ])
                "y", LA.Vec [ 1e-300; 1e100; 1.0 ]
                "w", LA.Vec [ 1.0; 1.0; 1.0 ] ]
          LA.case
              "qr-refuses-negative-weight"
              "qrAccumulator"
              [ "x", LA.Mat(2, 1, [ 1.0; 2.0 ])
                "y", LA.Vec [ 1.0; 1.0 ]
                "w", LA.Vec [ 1.0; -0.5 ] ]
          LA.case
              "qr-refuses-nan-cell"
              "qrAccumulator"
              [ "x", LA.Mat(2, 2, [ 1.0; 2.0; 3.0; nan ])
                "y", LA.Vec [ 1.0; 1.0 ]
                "w", LA.Vec [ 1.0; 1.0 ] ]
          LA.case
              "qr-refuses-infinite-response"
              "qrAccumulator"
              [ "x", LA.Mat(1, 1, [ 1.0 ]); "y", LA.Vec [ -inf ]; "w", LA.Vec [ 1.0 ] ]
          LA.case
              "qr-refuses-infinite-weight"
              "qrAccumulator"
              [ "x", LA.Mat(1, 1, [ 1.0 ]); "y", LA.Vec [ 1.0 ]; "w", LA.Vec [ inf ] ]
          LA.case
              "qr-shape-mismatch"
              "qrAccumulator"
              [ "x", LA.Mat(2, 1, [ 1.0; 2.0 ])
                "y", LA.Vec [ 1.0 ]
                "w", LA.Vec [ 1.0; 1.0 ] ]
          LA.case
              "lsq-exact-line"
              "leastSquares"
              [ "x", LA.Mat(5, 2, [ 1.0; 1.0; 1.0; 1.0; 1.0; 0.0; 1.0; 2.0; 3.0; 4.0 ])
                "y", LA.Vec [ 2.0; 5.0; 8.0; 11.0; 14.0 ]
                "w", LA.Vec [ 1.0; 1.0; 1.0; 1.0; 1.0 ] ]
          LA.case
              "lsq-noisy-line"
              "leastSquares"
              [ "x", LA.Mat(6, 2, [ 1.0; 1.0; 1.0; 1.0; 1.0; 1.0; 0.1; 0.7; 1.3; 2.2; 2.9; 4.1 ])
                "y", LA.Vec [ 0.3; 1.9; 2.8; 5.3; 6.6; 9.1 ]
                "w", LA.Vec [ 1.0; 1.0; 1.0; 1.0; 1.0; 1.0 ] ]
          LA.case
              "lsq-zero-weight-outlier-ignored"
              "leastSquares"
              [ "x", LA.Mat(4, 2, [ 1.0; 1.0; 1.0; 1.0; 0.0; 1.0; 2.0; 3.0 ])
                "y", LA.Vec [ 1.0; 3.0; 1e6; 7.0 ]
                "w", LA.Vec [ 1.0; 3.0; 0.0; 0.5 ] ]
          LA.case
              "lsq-duplicate-column-aliased"
              "leastSquares"
              [ "x", LA.Mat(4, 3, [ 1.0; 1.0; 1.0; 1.0; 0.5; 1.5; 2.0; 4.0; 0.5; 1.5; 2.0; 4.0 ])
                "y", LA.Vec [ 1.0; 2.0; 2.5; 5.0 ]
                "w", LA.Vec [ 1.0; 1.0; 1.0; 1.0 ] ]
          LA.case
              "lsq-zero-column-aliased"
              "leastSquares"
              [ "x", LA.Mat(3, 2, [ 0.0; 0.0; 0.0; 1.0; 2.0; 3.0 ])
                "y", LA.Vec [ 1.0; 2.0; 4.0 ]
                "w", LA.Vec [ 1.0; 1.0; 1.0 ] ]
          LA.case
              "lsq-collinear-within-tolerance"
              "leastSquares"
              [ "x", LA.Mat(3, 2, [ 1.0; 2.0; 3.0; 1.0; 2.0; 3.0 + 1.0 / 562949953421312.0 ])
                "y", LA.Vec [ 1.0; 2.0; 3.0 ]
                "w", LA.Vec [ 1.0; 1.0; 1.0 ] ]
          LA.case
              "lsq-more-columns-than-rows"
              "leastSquares"
              [ "x", LA.Mat(2, 3, [ 1.0; 2.0; 3.0; -1.0; 0.5; 0.25 ])
                "y", LA.Vec [ 1.0; 2.0 ]
                "w", LA.Vec [ 1.0; 1.0 ] ]
          LA.case "lsq-no-rows" "leastSquares" [ "x", LA.Mat(0, 2, []); "y", LA.Vec []; "w", LA.Vec [] ]
          LA.case
              "lsq-ill-scaled-columns"
              "leastSquares"
              [ "x", LA.Mat(3, 2, [ 1e-150; 2e-150; 4e-150; 1e150; -1e150; 3e150 ])
                "y", LA.Vec [ 1.0; 0.0; 1.0 ]
                "w", LA.Vec [ 1.0; 1.0; 1.0 ] ]
          LA.case
              "lsq-overflowing-state-refused"
              "leastSquares"
              [ "x", LA.Mat(2, 1, [ Double.MaxValue; Double.MaxValue ])
                "y", LA.Vec [ 0.0; 0.0 ]
                "w", LA.Vec [ 1.0; 1.0 ] ]
          LA.case
              "accumulator-wire"
              "accumulatorWire"
              [ "x", LA.Mat(3, 2, [ 1.0; 1.0; 1.0; -0.5; 2.0; 1e-300 ])
                "y", LA.Vec [ 0.1; -0.0; 3.0 ]
                "w", LA.Vec [ 1.0; 0.0; 2.0 ] ] ]

    let private description =
        "The reference answers for Fuaran.Compute.Regression over AUTHORED hard inputs (Phase 591; their own "
        + "file since Phase 632, every vector unchanged): the streaming least-squares kernel. `x`, `y` and `w` "
        + "are a design (column-major), its responses and prior weights folded by `QRAccumulator.addRows` into "
        + "an empty accumulator; `qrAccumulator` answers the state (`r`, `qty`, `rss`, `count`), `leastSquares` "
        + "the fit `LeastSquares.ofAccumulator` reads off it (each coefficient its bits or the token "
        + "`aliased`), and `accumulatorWire` the state's canonical wire text, compared byte for byte. Each "
        + "answer is computed by calling the package, whose every operation runs in the one order "
        + "DECISIONS.md D49 pins, so a conforming host answers each vector byte for byte. A float is its "
        + "IEEE-754 binary64 bit pattern as sixteen lowercase hex digits, or `nan` for any NaN (whose pattern "
        + "is the host's own canonical NaN and is not compared). A matrix is `{ rows, cols, data }` with "
        + "`data` in column-major order; a vector is a list of floats. `expected` is `{ verdict: ok, value }` "
        + "or `{ verdict: error, error: { case, … } }`, the error's case and fields compared exactly, as in "
        + "`linear-algebra-laws.json`, whose matrix operations these fold with."

    /// The family's document as the one corpus writer renders it.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "regression"
                  "description", CorpusFile.str description ]
              Items = "vectors"
              Rows = cases |> List.map (LA.caseCellOf expected) }
