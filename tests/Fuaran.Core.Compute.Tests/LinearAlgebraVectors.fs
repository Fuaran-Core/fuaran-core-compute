namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 590 — the linear-algebra law vectors: authored hard inputs, each
//  answer computed by calling Fuaran.Compute.LinearAlgebra, rendered through
//  the substrate's one corpus writer (`CorpusFile`, Phase 543's route) into
//  `conformance/laws/linear-algebra-laws.json`.
//
//  The inputs are the ones a port gets wrong: cancellation (a product and a sum
//  fused into one rounding, a fold taken in another order), subnormals,
//  scaling extremes (a norm that a naive sum of squares overflows or flushes),
//  rank deficiency and the refusals. Every float is written as its IEEE-754
//  binary64 bit pattern, so a host compares its answer byte for byte.
//
//  Fable-clean: the node leg (LinearAlgebraNode/) compiles this file and
//  renders the same document under JavaScript, and the suite holds the two
//  renderings and the committed file equal.
// ============================================================================

open System
open Fuaran.Core
open Fuaran.Compute.LinearAlgebra

module LinearAlgebraVectors =

    /// The file this family is written to under `laws/`.
    let fileName = "linear-algebra-laws.json"

    /// An operand.
    type Arg =
        | Scalar of float
        | Vec of float list
        /// Rows, columns, and the elements in column-major order.
        | Mat of rows: int * cols: int * data: float list

    /// One authored case: an operation over named operands.
    type Case =
        { Id: string
          Op: string
          Input: (string * Arg) list }

    /// A float's cell: its bit pattern as sixteen lowercase hex digits, or `nan` for any NaN — the one
    /// NaN every operation answers (DECISIONS.md D49 item 5) has a host-specific bit pattern (.NET's
    /// `Double.NaN` sets the sign bit, JavaScript's does not), so the pattern of a NaN is not compared.
    let hex (x: float) : string =
        if Double.IsNaN x then
            "nan"
        else
            let b = BitConverter.DoubleToInt64Bits x
            let digits = "0123456789abcdef"

            Array.init 16 (fun k -> string digits[int ((b >>> (4 * (15 - k))) &&& 0xFL)])
            |> String.concat ""

    let private dimsCell (d: Dims) : CorpusCell =
        CorpusCell.Row [ "rows", CorpusFile.num d.Rows; "cols", CorpusFile.num d.Cols ]

    let internal floats (xs: float seq) : CorpusCell =
        CorpusCell.List [ for x in xs -> CorpusFile.str (hex x) ]

    let internal matrixCell (m: Matrix) : CorpusCell =
        CorpusCell.Row
            [ "rows", CorpusFile.num m.Rows
              "cols", CorpusFile.num m.Cols
              "data", floats (Matrix.toColumnMajor m) ]

    let internal argCell (a: Arg) : CorpusCell =
        match a with
        | Scalar x -> CorpusFile.str (hex x)
        | Vec xs -> floats xs
        | Mat(r, c, data) -> CorpusCell.Row [ "rows", CorpusFile.num r; "cols", CorpusFile.num c; "data", floats data ]

    let internal errorCell (e: LinAlgError) : CorpusCell =
        match e with
        | LinAlgError.ShapeMismatch(op, l, r) ->
            CorpusCell.Row
                [ "case", CorpusFile.str "shapeMismatch"
                  "operation", CorpusFile.str op
                  "left", dimsCell l
                  "right", dimsCell r ]
        | LinAlgError.Singular i -> CorpusCell.Row [ "case", CorpusFile.str "singular"; "index", CorpusFile.num i ]
        | LinAlgError.RankDeficient r ->
            CorpusCell.Row [ "case", CorpusFile.str "rankDeficient"; "rank", CorpusFile.num r ]
        | LinAlgError.NotFinite(r, c) ->
            CorpusCell.Row
                [ "case", CorpusFile.str "notFinite"
                  "row", CorpusFile.num r
                  "col", CorpusFile.num c ]
        | LinAlgError.NotTriangular(r, c) ->
            CorpusCell.Row
                [ "case", CorpusFile.str "notTriangular"
                  "row", CorpusFile.num r
                  "col", CorpusFile.num c ]
        | LinAlgError.NotConverged n ->
            CorpusCell.Row [ "case", CorpusFile.str "notConverged"; "iterations", CorpusFile.num n ]
        | LinAlgError.InvalidWeight r ->
            CorpusCell.Row [ "case", CorpusFile.str "invalidWeight"; "row", CorpusFile.num r ]
        | LinAlgError.InvalidResponse r ->
            CorpusCell.Row [ "case", CorpusFile.str "invalidResponse"; "row", CorpusFile.num r ]
        | LinAlgError.Separation r -> CorpusCell.Row [ "case", CorpusFile.str "separation"; "row", CorpusFile.num r ]
        | LinAlgError.UnsupportedModel(family, link) ->
            CorpusCell.Row
                [ "case", CorpusFile.str "unsupportedModel"
                  "family", CorpusFile.str family
                  "link", CorpusFile.str link ]
        | LinAlgError.InvalidPower p ->
            CorpusCell.Row [ "case", CorpusFile.str "invalidPower"; "power", CorpusFile.str (hex p) ]
        | LinAlgError.MeanOutOfRange r ->
            CorpusCell.Row [ "case", CorpusFile.str "meanOutOfRange"; "row", CorpusFile.num r ]
        | LinAlgError.InvalidTheta t ->
            CorpusCell.Row [ "case", CorpusFile.str "invalidTheta"; "theta", CorpusFile.str (hex t) ]
        | LinAlgError.ThetaNotConverged k ->
            CorpusCell.Row [ "case", CorpusFile.str "thetaNotConverged"; "alternation", CorpusFile.num k ]

    let private okCell (value: CorpusCell) : CorpusCell =
        CorpusCell.Row [ "verdict", CorpusFile.str "ok"; "value", value ]

    let internal answer (toCell: 'T -> CorpusCell) (r: Result<'T, LinAlgError>) : CorpusCell =
        match r with
        | Ok v -> okCell (toCell v)
        | Error e -> CorpusCell.Row [ "verdict", CorpusFile.str "error"; "error", errorCell e ]

    let private givensCell (g: Givens) : CorpusCell =
        CorpusCell.Row
            [ "cos", CorpusFile.str (hex g.Cos)
              "sin", CorpusFile.str (hex g.Sin)
              "radius", CorpusFile.str (hex g.Radius)
              "identity", CorpusCell.Value(JBool g.IsIdentity) ]

    let private arg (c: Case) (name: string) : Arg =
        match c.Input |> List.tryFind (fun (n, _) -> n = name) with
        | Some(_, a) -> a
        | None -> failwithf "linear-algebra vector %s has no operand %s" c.Id name

    let private scalar c name =
        match arg c name with
        | Scalar x -> x
        | _ -> failwithf "linear-algebra vector %s: %s is not a scalar" c.Id name

    let internal vec c name =
        match arg c name with
        | Vec xs -> Vector.ofList xs
        | _ -> failwithf "linear-algebra vector %s: %s is not a vector" c.Id name

    let internal mat c name =
        match arg c name with
        | Mat(r, k, data) ->
            let a = List.toArray data
            Matrix.init r k (fun i j -> a[j * r + i])
        | _ -> failwithf "linear-algebra vector %s: %s is not a matrix" c.Id name

    let internal vecCell (v: Vector<float>) : CorpusCell = floats (Vector.toArray v)

    let internal ints (xs: int list) : CorpusCell =
        CorpusCell.List [ for x in xs -> CorpusFile.num x ]

    let private pivotedCell (q: PivotedQR) : CorpusCell =
        CorpusCell.Row
            [ "r", matrixCell (UpperTriangular.toMatrix q.R)
              "permutation", ints q.Permutation
              "rank", CorpusFile.num q.Rank
              "aliased", ints q.Aliased ]

    /// The reference answer for one case, by calling the package.
    let expected (c: Case) : CorpusCell =
        match c.Op with
        | "dot" -> Blas.dot (vec c "x") (vec c "y") |> answer (hex >> CorpusFile.str)
        | "axpy" -> Blas.axpy (scalar c "a") (vec c "x") (vec c "y") |> answer vecCell
        | "scale" -> Blas.scale (scalar c "a") (vec c "x") |> Ok |> answer vecCell
        | "add" -> Blas.add (vec c "x") (vec c "y") |> answer vecCell
        | "sub" -> Blas.sub (vec c "x") (vec c "y") |> answer vecCell
        | "norm2" -> Blas.norm2 (vec c "x") |> Ok |> answer (hex >> CorpusFile.str)
        | "gemv" -> Blas.gemv (mat c "a") (vec c "x") |> answer vecCell
        | "gemm" -> Blas.gemm (mat c "a") (mat c "b") |> answer matrixCell
        | "gram" -> Blas.gram (mat c "a") |> Ok |> answer matrixCell
        | "transpose" -> Matrix.transpose (mat c "a") |> Ok |> answer matrixCell
        | "ofMatrix" ->
            UpperTriangular.ofMatrix (mat c "a")
            |> answer (UpperTriangular.toMatrix >> matrixCell)
        | "solveUpper" ->
            UpperTriangular.ofMatrix (mat c "r")
            |> Result.bind (fun r -> UpperTriangular.solveUpper r (vec c "b"))
            |> answer vecCell
        | "solveUpperT" ->
            UpperTriangular.ofMatrix (mat c "r")
            |> Result.bind (fun r -> UpperTriangular.solveUpperT r (vec c "b"))
            |> answer vecCell
        | "givens" -> Givens.make (scalar c "a") (scalar c "b") |> Ok |> answer givensCell
        | "givensApply" ->
            let x, y =
                Givens.apply (Givens.make (scalar c "a") (scalar c "b")) (scalar c "x") (scalar c "y")

            Ok [ x; y ] |> answer floats
        | "householder" -> QR.householder (mat c "a") |> answer (UpperTriangular.toMatrix >> matrixCell)
        | "pivoted" -> QR.pivoted (mat c "a") |> answer pivotedCell
        | other -> failwithf "linear-algebra vector %s: no operation %s" c.Id other

    // ---- the authored inputs ---------------------------------------------------------------------

    /// `1 + 2⁻³⁰`: squared, `1 + 2⁻²⁹ + 2⁻⁶⁰`, which rounds to `1 + 2⁻²⁹`.
    let private fa = 1.0 + 1.0 / 1073741824.0

    /// `−(1 + 2⁻²⁹)`: added to the rounded square above, exactly zero; to the fused one, `2⁻⁶⁰`.
    let private fc = -(1.0 + 1.0 / 536870912.0)

    /// `2¹⁰⁰⁰`, by exact doublings.
    let private p1000 = List.fold (fun p _ -> p * 2.0) 1.0 [ 1..1000 ]
    let private tiny = Double.Epsilon
    let private inf = Double.PositiveInfinity
    let private nan = Double.NaN

    let internal case (id: string) (op: string) (input: (string * Arg) list) : Case =
        { Id = id; Op = op; Input = input }

    /// Every authored case, in file order.
    let cases: Case list =
        [ // cancellation: a fused multiply-add answers 2⁻⁶⁰ where the pinned two roundings answer +0
          case "dot-fma-witness" "dot" [ "x", Vec [ 1.0; fa ]; "y", Vec [ fc; fa ] ]
          case "axpy-fma-witness" "axpy" [ "a", Scalar fa; "x", Vec [ fa ]; "y", Vec [ fc ] ]
          case "gemv-fma-witness" "gemv" [ "a", Mat(1, 2, [ 1.0; fa ]); "x", Vec [ fc; fa ] ]
          case "gemm-fma-witness" "gemm" [ "a", Mat(1, 2, [ 1.0; fa ]); "b", Mat(2, 1, [ fc; fa ]) ]
          // cancellation: the fold is left to right, so a large term absorbs a small one first
          case "dot-absorbed-then-cancelled" "dot" [ "x", Vec [ 1e16; 1.0; -1e16 ]; "y", Vec [ 1.0; 1.0; 1.0 ] ]
          case "dot-order-pinned" "dot" [ "x", Vec [ 1.0; 1e100; 1.0; -1e100 ]; "y", Vec [ 1.0; 1.0; 1.0; 1.0 ] ]
          case
              "gemv-order-pinned"
              "gemv"
              [ "a", Mat(2, 3, [ 1e16; 1.0; 1.0; 1e16; -1e16; -1e16 ])
                "x", Vec [ 1.0; 1.0; 1.0 ] ]
          case
              "gemm-overflow-cancels-to-nan"
              "gemm"
              [ "a", Mat(1, 2, [ 1e300; 1e300 ]); "b", Mat(2, 1, [ 1e10; -1e10 ]) ]
          case "sub-self-is-positive-zero" "sub" [ "x", Vec [ 1.5; -0.0; 1e300 ]; "y", Vec [ 1.5; -0.0; 1e300 ] ]
          case "add-signed-zeros" "add" [ "x", Vec [ -0.0; -0.0 ]; "y", Vec [ -0.0; 0.0 ] ]
          case "dot-empty-is-positive-zero" "dot" [ "x", Vec []; "y", Vec [] ]
          case "dot-infinities-cancel-to-nan" "dot" [ "x", Vec [ inf; 1.0 ]; "y", Vec [ 1.0; -inf ] ]
          // subnormals
          case "dot-subnormal-products" "dot" [ "x", Vec [ 1e-160; tiny ]; "y", Vec [ 1e-160; 0.5 ] ]
          case "scale-into-subnormal" "scale" [ "a", Scalar 1e-300; "x", Vec [ 1e-10; -3e-15; 1.0 ] ]
          case "norm2-least-subnormals" "norm2" [ "x", Vec [ tiny; tiny ] ]
          case "norm2-tiny" "norm2" [ "x", Vec [ 1e-200; -1e-200 ] ]
          case "gemv-subnormal" "gemv" [ "a", Mat(2, 2, [ tiny; 1e-310; 2.0; -0.5 ]); "x", Vec [ 0.5; tiny ] ]
          case "givens-tiny" "givens" [ "a", Scalar 1e-300; "b", Scalar -1e-300 ]
          // scaling extremes
          case "norm2-huge" "norm2" [ "x", Vec [ 1e300; 1e300 ] ]
          case "norm2-pythagorean-scaled" "norm2" [ "x", Vec [ 3.0 * p1000; -4.0 * p1000 ] ]
          case "norm2-overflows-honestly" "norm2" [ "x", Vec [ Double.MaxValue; Double.MaxValue ] ]
          case "norm2-infinity" "norm2" [ "x", Vec [ 1.0; -inf ] ]
          case "norm2-nan-beats-infinity" "norm2" [ "x", Vec [ inf; nan ] ]
          case "norm2-empty" "norm2" [ "x", Vec [] ]
          case "norm2-signed-zeros" "norm2" [ "x", Vec [ -0.0; 0.0 ] ]
          case "norm2-mixed-scales" "norm2" [ "x", Vec [ 1e-300; 1.0; 1e300 ] ]
          case "givens-huge" "givens" [ "a", Scalar 1e300; "b", Scalar 1e300 ]
          case "givens-negative-a" "givens" [ "a", Scalar -3.0; "b", Scalar 4.0 ]
          case "givens-identity" "givens" [ "a", Scalar -2.0; "b", Scalar 0.0 ]
          case "givens-identity-negative-zero" "givens" [ "a", Scalar 3.0; "b", Scalar -0.0 ]
          case "givens-apply" "givensApply" [ "a", Scalar 3.0; "b", Scalar 4.0; "x", Scalar 3.0; "y", Scalar 4.0 ]
          case
              "givens-apply-identity-keeps-infinity"
              "givensApply"
              [ "a", Scalar 1.0; "b", Scalar 0.0; "x", Scalar -0.0; "y", Scalar inf ]
          case
              "solveUpper-ill-scaled"
              "solveUpper"
              [ "r", Mat(2, 2, [ 1e-300; 0.0; 1.0; 1e300 ]); "b", Vec [ 1.0; 1.0 ] ]
          case
              "solveUpper-order-pinned"
              "solveUpper"
              [ "r", Mat(3, 3, [ 1.0; 0.0; 0.0; 1.0; 1.0; 0.0; 1.0; 0.0; 1.0 ])
                "b", Vec [ 1.0; 1e16; -1e16 ] ]
          case
              "solveUpperT-order-pinned"
              "solveUpperT"
              [ "r", Mat(3, 3, [ 1.0; 0.0; 0.0; 1e16; 1.0; 0.0; 1.0; 0.0; 1.0 ])
                "b", Vec [ 1.0; 1.0; 1.0 ] ]
          // rank deficiency
          case "gram-rank-deficient" "gram" [ "a", Mat(3, 2, [ 1.0; 2.0; 3.0; 1.0; 2.0; 3.0 ]) ]
          case
              "solveUpper-singular-lowest-index"
              "solveUpper"
              [ "r", Mat(3, 3, [ 1.0; 0.0; 0.0; 2.0; 0.0; 0.0; 3.0; 4.0; 0.0 ])
                "b", Vec [ 1.0; 1.0; 1.0 ] ]
          case "solveUpperT-singular" "solveUpperT" [ "r", Mat(2, 2, [ 0.0; 0.0; 1.0; 1.0 ]); "b", Vec [ 1.0; 1.0 ] ]
          // the refusals
          case "ofMatrix-not-square" "ofMatrix" [ "a", Mat(2, 3, [ 1.0; 0.0; 1.0; 1.0; 1.0; 1.0 ]) ]
          case "ofMatrix-not-finite" "ofMatrix" [ "a", Mat(2, 2, [ 1.0; 0.0; nan; 1.0 ]) ]
          case "ofMatrix-not-triangular" "ofMatrix" [ "a", Mat(2, 2, [ 1.0; 1e-300; 0.0; 1.0 ]) ]
          case "ofMatrix-negative-zero-below" "ofMatrix" [ "a", Mat(2, 2, [ 1.0; -0.0; 2.0; 3.0 ]) ]
          case "dot-shape-mismatch" "dot" [ "x", Vec [ 1.0 ]; "y", Vec [ 1.0; 2.0 ] ]
          case "gemv-shape-mismatch" "gemv" [ "a", Mat(2, 2, [ 1.0; 2.0; 3.0; 4.0 ]); "x", Vec [ 1.0 ] ]
          case "gemm-shape-mismatch" "gemm" [ "a", Mat(1, 2, [ 1.0; 2.0 ]); "b", Mat(1, 2, [ 1.0; 2.0 ]) ]
          case "solveUpper-shape-mismatch" "solveUpper" [ "r", Mat(1, 1, [ 1.0 ]); "b", Vec [ 1.0; 2.0 ] ]
          // shapes at the edge, NaN, the transpose
          case "gemv-no-columns" "gemv" [ "a", Mat(2, 0, []); "x", Vec [] ]
          case "gemm-no-inner" "gemm" [ "a", Mat(2, 0, []); "b", Mat(0, 3, []) ]
          case "gemv-nan-reaches-every-row" "gemv" [ "a", Mat(2, 2, [ 1.0; 2.0; 3.0; 4.0 ]); "x", Vec [ nan; 1.0 ] ]
          case "transpose" "transpose" [ "a", Mat(2, 3, [ 1.0; -0.0; tiny; inf; 5.0; 6.0 ]) ]
          // Phase 591 — the batch factorizations. The streaming QR's state, fit and wire form are
          // `RegressionVectors`' since Phase 632.
          case "householder-signs-normalised" "householder" [ "a", Mat(3, 2, [ -3.0; 4.0; 0.0; 1.0; -2.0; 2.0 ]) ]
          case "householder-wide" "householder" [ "a", Mat(1, 2, [ -2.0; 3.0 ]) ]
          case "pivoted-duplicate-column" "pivoted" [ "a", Mat(3, 3, [ 1.0; 0.0; 1.0; 2.0; 1.0; 0.0; 2.0; 1.0; 0.0 ]) ]
          case "pivoted-largest-first" "pivoted" [ "a", Mat(2, 3, [ 1.0; 0.0; 0.0; 5.0; 3.0; 4.0 ]) ] ]

    /// One case's row, its answer computed by `expected` — shared with `RegressionVectors` (Phase 632).
    let internal caseCellOf (expected: Case -> CorpusCell) (c: Case) : CorpusCell =
        CorpusCell.Row
            [ "id", CorpusFile.str c.Id
              "op", CorpusFile.str c.Op
              "input", CorpusCell.Row(c.Input |> List.map (fun (n, a) -> n, argCell a))
              "expected", expected c ]

    let private description =
        "The reference answers for Fuaran.Compute.LinearAlgebra over AUTHORED hard inputs (Phases 590 and 591): "
        + "cancellation that a fused multiply-add or a reordered fold would answer differently, subnormals, "
        + "scaling extremes that a naive sum of squares overflows or flushes, rank deficiency, and every "
        + "refusal. Each answer is computed by calling the package, whose every operation runs in the one "
        + "order DECISIONS.md D49 pins, so a conforming host answers each vector byte for byte. A float is "
        + "its IEEE-754 binary64 bit pattern as sixteen lowercase hex digits, or `nan` for any NaN (whose "
        + "pattern is the host's own canonical NaN and is not compared). A matrix is `{ rows, cols, data }` "
        + "with `data` in column-major order; a vector is a list of floats. `op` names the operation "
        + "(`givensApply` makes the rotation from `a` and `b` and applies it to `x` and `y`; `solveUpper` and "
        + "`solveUpperT` build the factor from `r` through the checked `ofMatrix` first). `expected` is "
        + "`{ verdict: ok, value }` or `{ verdict: error, error: { case, … } }`, the error's case and fields "
        + "compared exactly. Phase 591 adds `householder` and `pivoted`, the batch factorizations, pinned in "
        + "their own order and not byte-equal to the streaming least-squares kernel. The elementary "
        + "functions' vectors (Phase 593) are `elementary-laws.json`'s since Phase 630, which moved them with "
        + "`Fuaran.Compute.Elementary`, and the streaming kernel's (`qrAccumulator`, `leastSquares`, "
        + "`accumulatorWire`) are `regression-laws.json`'s since Phase 632, which moved them with "
        + "`Fuaran.Compute.Regression`."

    /// The family's document as the one corpus writer renders it.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "linearAlgebra"
                  "description", CorpusFile.str description ]
              Items = "vectors"
              Rows = cases |> List.map (caseCellOf expected) }
