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

    let private floats (xs: float seq) : CorpusCell =
        CorpusCell.List [ for x in xs -> CorpusFile.str (hex x) ]

    let private matrixCell (m: Matrix) : CorpusCell =
        CorpusCell.Row
            [ "rows", CorpusFile.num m.Rows
              "cols", CorpusFile.num m.Cols
              "data", floats (Matrix.toColumnMajor m) ]

    let private argCell (a: Arg) : CorpusCell =
        match a with
        | Scalar x -> CorpusFile.str (hex x)
        | Vec xs -> floats xs
        | Mat(r, c, data) -> CorpusCell.Row [ "rows", CorpusFile.num r; "cols", CorpusFile.num c; "data", floats data ]

    let private errorCell (e: LinAlgError) : CorpusCell =
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

    let private okCell (value: CorpusCell) : CorpusCell =
        CorpusCell.Row [ "verdict", CorpusFile.str "ok"; "value", value ]

    let private answer (toCell: 'T -> CorpusCell) (r: Result<'T, LinAlgError>) : CorpusCell =
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

    let private vec c name =
        match arg c name with
        | Vec xs -> Vector.ofList xs
        | _ -> failwithf "linear-algebra vector %s: %s is not a vector" c.Id name

    let private mat c name =
        match arg c name with
        | Mat(r, k, data) ->
            let a = List.toArray data
            Matrix.init r k (fun i j -> a[j * r + i])
        | _ -> failwithf "linear-algebra vector %s: %s is not a matrix" c.Id name

    let private vecCell (v: Vector<float>) : CorpusCell = floats (Vector.toArray v)

    let private ints (xs: int list) : CorpusCell =
        CorpusCell.List [ for x in xs -> CorpusFile.num x ]

    /// The rows `x`, `y` and weights `w` folded into an empty accumulator by `addRows`.
    let private accumulate (c: Case) : Result<QRAccumulator, LinAlgError> =
        let x = mat c "x"

        QRAccumulator.empty x.Cols |> QRAccumulator.addRows x (vec c "y") (vec c "w")

    let private accumulatorCell (acc: QRAccumulator) : CorpusCell =
        CorpusCell.Row
            [ "r", matrixCell (QRAccumulator.r acc)
              "qty", vecCell (QRAccumulator.qty acc)
              "rss", CorpusFile.str (hex (QRAccumulator.rss acc))
              "count", CorpusFile.num (QRAccumulator.count acc) ]

    /// A fit: each coefficient's bits, or the token `aliased`.
    let private fitCell (fit: LeastSquaresFit) : CorpusCell =
        CorpusCell.Row
            [ "coefficients",
              CorpusCell.List
                  [ for b in fit.Coefficients ->
                        match b with
                        | Some v -> CorpusFile.str (hex v)
                        | None -> CorpusFile.str "aliased" ]
              "rank", CorpusFile.num fit.Rank
              "aliased", ints fit.Aliased
              "rss", CorpusFile.str (hex fit.Rss)
              "count", CorpusFile.num fit.Count
              "qty", vecCell fit.Qty
              "rInverse", matrixCell fit.RInverse ]

    let private pivotedCell (q: PivotedQR) : CorpusCell =
        CorpusCell.Row
            [ "r", matrixCell (UpperTriangular.toMatrix q.R)
              "permutation", ints q.Permutation
              "rank", CorpusFile.num q.Rank
              "aliased", ints q.Aliased ]

    // ---- Phase 593: the elementary functions -----------------------------------------------------

    let private tableName (s: SpecialValue) : string =
        match s with
        | SpecialValue.NaN -> "nan"
        | SpecialValue.Value _ -> "value"
        | SpecialValue.Undefined -> "undefined"
        | SpecialValue.Compute -> "compute"

    /// What D43's table says of the case's input and what the function answers: `(table, answer)`.
    let private elementary (c: Case) : SpecialValue * float =
        match c.Op with
        | "exp" ->
            let x = scalar c "x"
            Elementary.expTable x, Elementary.exp x
        | "ln" ->
            let x = scalar c "x"
            Elementary.lnTable x, Elementary.ln x
        | "log10" ->
            let x = scalar c "x"
            Elementary.lnTable x, Elementary.log10 x
        | "pow" ->
            let x, y = scalar c "x", scalar c "y"
            Elementary.powTable x y, Elementary.pow x y
        | other -> failwithf "linear-algebra vector %s: %s is not an elementary function" c.Id other

    let private elementaryCell (c: Case) : CorpusCell =
        let table, answer = elementary c

        CorpusCell.Row
            [ "verdict", CorpusFile.str "ok"
              "value", CorpusFile.str (hex answer)
              "table", CorpusFile.str (tableName table) ]

    /// The float whose bit pattern is these sixteen hex digits.
    let private ofHex (h: string) : float =
        let mutable b = 0L

        for ch in h do
            let d =
                if ch >= '0' && ch <= '9' then
                    int ch - int '0'
                else
                    int ch - int 'a' + 10

            b <- (b <<< 4) ||| int64 d

        BitConverter.Int64BitsToDouble b

    let private ordered (x: float) : int64 =
        let b = BitConverter.DoubleToInt64Bits x
        if b < 0L then Int64.MinValue - b else b

    /// The distance between two floats in units in the last place: the difference of their ordered
    /// bit patterns (so an infinity and the largest finite double are one apart).
    let ulpsApart (a: float) (b: float) : int64 = abs (ordered a - ordered b)

    /// The reference answer is within this many units in the last place of the package's, on every
    /// vector that carries one (DECISIONS.md D52: the faithful-pinned build is within one).
    let referenceUlps = 1

    /// The correctly rounded answer for every vector the routine computes, as its bit pattern: the
    /// true value to eighty decimal digits (Python's `decimal`: `exp`, `ln`, `log10`, and `pow` as
    /// `exp(y · ln x)`) rounded once to the nearest double. A vector the table answers carries none.
    let references: (string * string) list =
        [ "exp-one", "4005bf0a8b145769"
          "exp-minus-one", "3fd78b56362cef38"
          "exp-at-half-ln2", "3ff6a09e667f3bcc"
          "exp-above-half-ln2", "3ff6a09e667f3bcd"
          "exp-three-halves-ln2", "4006a09e667f3bcc"
          "exp-overflow-edge", "7fefffffffffff2a"
          "exp-underflow-edge", "0000000000000000"
          "exp-subnormal-result", "0000000000000001"
          "exp-just-below-min-normal", "000ff15b469edf89"
          "exp-just-above-min-normal", "00119e98b83de7a3"
          "exp-tiny", "3ff000000006df38"
          "exp-below-two-to-minus-28", "3ff0000000ce288f"
          "exp-at-two-to-minus-28", "3ff0000001000000"
          "exp-negative-tiny", "3feffffffea86712"
          "exp-zero", "3ff0000000000000"
          "exp-negative-zero", "3ff0000000000000"
          "exp-ln-two-to-128", "47efffffdffffff5"
          "exp-minus-700", "00d14f2b0fb9307f"
          "exp-fifty-and-half", "447cf65319c5751a"
          "ln-two", "3fe62e42fefa39ef"
          "ln-half", "bfe62e42fefa39ef"
          "ln-ten", "40026bb1bbb55516"
          "ln-three", "3ff193ea7aad030b"
          "ln-e", "3ff0000000000000"
          "ln-just-above-one", "3cafffffffffffff"
          "ln-just-below-one", "bca0000000000000"
          "ln-one-plus-two-to-minus-20", "3eafffff00000aab"
          "ln-one-plus-below-two-to-minus-20", "3eaffffeffe00aad"
          "ln-one-minus-two-to-minus-20", "beb0000080000555"
          "ln-at-root-two", "3fd62e42fefa39f0"
          "ln-above-root-two", "3fd62e42fefa39f3"
          "ln-max", "40862e42fefa39ef"
          "ln-min-normal", "c086232bdd7abcd2"
          "ln-max-subnormal", "c086232bdd7abcd2"
          "ln-min-subnormal", "c0874385446d71c3"
          "ln-1e300", "4085963447f87fb5"
          "ln-1e-300", "c085963447f87fb5"
          "ln-tenth", "c0026bb1bbb55515"
          "ln-seven", "3fff2272ae325a57"
          "log10-ten", "3ff0000000000000"
          "log10-hundred", "4000000000000000"
          "log10-thousand", "4008000000000000"
          "log10-1e22", "4036000000000000"
          "log10-1e23", "4037000000000000"
          "log10-thousandth", "c008000000000000"
          "log10-two", "3fd34413509f79ff"
          "log10-half", "bfd34413509f79ff"
          "log10-1e-300", "c072c00000000000"
          "log10-min-subnormal", "c07434e6420f4374"
          "log10-max", "40734413509f79ff"
          "log10-just-above-one", "3c9bcb7b1526e50d"
          "log10-seven", "3feb0b0b0b78cc3f"
          "pow-root-shortcut", "3ff6a09e667f3bcd"
          "pow-square-shortcut", "4059000000000000"
          "pow-reciprocal-shortcut", "3fb999999999999a"
          "pow-identity-shortcut", "7e37e43c8800759c"
          "pow-two-ten-and-half", "4096a09e667f3bcd"
          "pow-ten-tiny-exponent", "3ff00000009e3b81"
          "pow-near-one-huge-exponent", "3ff3fa60615291ee"
          "pow-below-one-huge-negative-exponent", "400847c089d2b399"
          "pow-near-overflow", "7fe6a09e667f3bcd"
          "pow-subnormal-result", "0000000000000001"
          "pow-exact-min-subnormal", "0000000000000001"
          "pow-two-cubed", "4020000000000000"
          "pow-two-to-1023", "7fe0000000000000"
          "pow-generic", "4104ac21590dc051"
          "pow-negative-exponent", "3914c4e977ba1f74"
          "pow-three-seven-and-half", "40ad97fd7fdd4adf"
          "pow-subnormal-base-root", "1eb67e93ddbc0e73"
          "pow-overflow", "7ff0000000000000"
          "pow-underflow", "0000000000000000"
          "pow-two-to-1024", "7ff0000000000000"
          "pow-half-to-minus-1024", "7ff0000000000000"
          "pow-seven-tiny-exponent", "3ff0000000000000"
          "pow-small-base-sixty", "01a56e1fc2f8f376"
          "pow-tenth-minus-300", "7e37e43c8800752c"
          "pow-negative-base-odd", "c020000000000000"
          "pow-negative-base-even", "4010000000000000"
          "pow-negative-base-huge-even-exponent", "7ff0000000000000" ]

    /// The reference cells of an elementary case that carries one: the correctly rounded bits and the
    /// package's distance from them in units in the last place.
    let private referenceCells (c: Case) : (string * CorpusCell) list =
        match references |> List.tryFind (fun (id, _) -> id = c.Id) with
        | None -> []
        | Some(_, h) ->
            let _, answer = elementary c

            [ "reference", CorpusFile.str h
              "ulps", CorpusFile.num (int (ulpsApart answer (ofHex h))) ]

    /// The reference answer for one case, by calling the package.
    let expected (c: Case) : CorpusCell =
        match c.Op with
        | "exp"
        | "ln"
        | "log10"
        | "pow" -> elementaryCell c
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
        | "qrAccumulator" -> accumulate c |> answer accumulatorCell
        | "accumulatorWire" -> accumulate c |> answer (LinAlgWire.encodeAccumulator >> CorpusFile.str)
        | "leastSquares" -> accumulate c |> Result.bind LeastSquares.ofAccumulator |> answer fitCell
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

    let private case (id: string) (op: string) (input: (string * Arg) list) : Case = { Id = id; Op = op; Input = input }

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
          // Phase 591 — the streaming QR: the state, the fit, the batch factorizations, the wire form.
          // A design is `x` (column-major), `y` the responses, `w` the prior weights.
          case
              "qr-state-diagonal-non-negative"
              "qrAccumulator"
              [ "x", Mat(3, 2, [ -3.0; 4.0; -1.0; 2.0; -5.0; 0.5 ])
                "y", Vec [ -1.0; 2.0; 3.0 ]
                "w", Vec [ 1.0; 1.0; 1.0 ] ]
          case
              "qr-state-weights-scale-by-root"
              "qrAccumulator"
              [ "x", Mat(3, 2, [ 1.0; 1.0; 1.0; 0.0; 1.0; 2.0 ])
                "y", Vec [ 1.0; 2.0; 5.0 ]
                "w", Vec [ 2.0; 0.0; 0.25 ] ]
          case
              "qr-state-zero-cell-is-identity"
              "qrAccumulator"
              [ "x", Mat(2, 2, [ 0.0; -0.0; 3.0; 4.0 ])
                "y", Vec [ 1.0; 1.0 ]
                "w", Vec [ 1.0; 1.0 ] ]
          case
              "qr-state-tiny-and-huge-rows"
              "qrAccumulator"
              [ "x", Mat(3, 2, [ 1e-300; 1e300; 1.0; tiny; -1e200; 1.0 ])
                "y", Vec [ 1e-300; 1e100; 1.0 ]
                "w", Vec [ 1.0; 1.0; 1.0 ] ]
          case
              "qr-refuses-negative-weight"
              "qrAccumulator"
              [ "x", Mat(2, 1, [ 1.0; 2.0 ]); "y", Vec [ 1.0; 1.0 ]; "w", Vec [ 1.0; -0.5 ] ]
          case
              "qr-refuses-nan-cell"
              "qrAccumulator"
              [ "x", Mat(2, 2, [ 1.0; 2.0; 3.0; nan ])
                "y", Vec [ 1.0; 1.0 ]
                "w", Vec [ 1.0; 1.0 ] ]
          case
              "qr-refuses-infinite-response"
              "qrAccumulator"
              [ "x", Mat(1, 1, [ 1.0 ]); "y", Vec [ -inf ]; "w", Vec [ 1.0 ] ]
          case
              "qr-refuses-infinite-weight"
              "qrAccumulator"
              [ "x", Mat(1, 1, [ 1.0 ]); "y", Vec [ 1.0 ]; "w", Vec [ inf ] ]
          case
              "qr-shape-mismatch"
              "qrAccumulator"
              [ "x", Mat(2, 1, [ 1.0; 2.0 ]); "y", Vec [ 1.0 ]; "w", Vec [ 1.0; 1.0 ] ]
          case
              "lsq-exact-line"
              "leastSquares"
              [ "x", Mat(5, 2, [ 1.0; 1.0; 1.0; 1.0; 1.0; 0.0; 1.0; 2.0; 3.0; 4.0 ])
                "y", Vec [ 2.0; 5.0; 8.0; 11.0; 14.0 ]
                "w", Vec [ 1.0; 1.0; 1.0; 1.0; 1.0 ] ]
          case
              "lsq-noisy-line"
              "leastSquares"
              [ "x", Mat(6, 2, [ 1.0; 1.0; 1.0; 1.0; 1.0; 1.0; 0.1; 0.7; 1.3; 2.2; 2.9; 4.1 ])
                "y", Vec [ 0.3; 1.9; 2.8; 5.3; 6.6; 9.1 ]
                "w", Vec [ 1.0; 1.0; 1.0; 1.0; 1.0; 1.0 ] ]
          case
              "lsq-zero-weight-outlier-ignored"
              "leastSquares"
              [ "x", Mat(4, 2, [ 1.0; 1.0; 1.0; 1.0; 0.0; 1.0; 2.0; 3.0 ])
                "y", Vec [ 1.0; 3.0; 1e6; 7.0 ]
                "w", Vec [ 1.0; 3.0; 0.0; 0.5 ] ]
          case
              "lsq-duplicate-column-aliased"
              "leastSquares"
              [ "x", Mat(4, 3, [ 1.0; 1.0; 1.0; 1.0; 0.5; 1.5; 2.0; 4.0; 0.5; 1.5; 2.0; 4.0 ])
                "y", Vec [ 1.0; 2.0; 2.5; 5.0 ]
                "w", Vec [ 1.0; 1.0; 1.0; 1.0 ] ]
          case
              "lsq-zero-column-aliased"
              "leastSquares"
              [ "x", Mat(3, 2, [ 0.0; 0.0; 0.0; 1.0; 2.0; 3.0 ])
                "y", Vec [ 1.0; 2.0; 4.0 ]
                "w", Vec [ 1.0; 1.0; 1.0 ] ]
          case
              "lsq-collinear-within-tolerance"
              "leastSquares"
              [ "x", Mat(3, 2, [ 1.0; 2.0; 3.0; 1.0; 2.0; 3.0 + 1.0 / 562949953421312.0 ])
                "y", Vec [ 1.0; 2.0; 3.0 ]
                "w", Vec [ 1.0; 1.0; 1.0 ] ]
          case
              "lsq-more-columns-than-rows"
              "leastSquares"
              [ "x", Mat(2, 3, [ 1.0; 2.0; 3.0; -1.0; 0.5; 0.25 ])
                "y", Vec [ 1.0; 2.0 ]
                "w", Vec [ 1.0; 1.0 ] ]
          case "lsq-no-rows" "leastSquares" [ "x", Mat(0, 2, []); "y", Vec []; "w", Vec [] ]
          case
              "lsq-ill-scaled-columns"
              "leastSquares"
              [ "x", Mat(3, 2, [ 1e-150; 2e-150; 4e-150; 1e150; -1e150; 3e150 ])
                "y", Vec [ 1.0; 0.0; 1.0 ]
                "w", Vec [ 1.0; 1.0; 1.0 ] ]
          case
              "lsq-overflowing-state-refused"
              "leastSquares"
              [ "x", Mat(2, 1, [ Double.MaxValue; Double.MaxValue ])
                "y", Vec [ 0.0; 0.0 ]
                "w", Vec [ 1.0; 1.0 ] ]
          case "householder-signs-normalised" "householder" [ "a", Mat(3, 2, [ -3.0; 4.0; 0.0; 1.0; -2.0; 2.0 ]) ]
          case "householder-wide" "householder" [ "a", Mat(1, 2, [ -2.0; 3.0 ]) ]
          case "pivoted-duplicate-column" "pivoted" [ "a", Mat(3, 3, [ 1.0; 0.0; 1.0; 2.0; 1.0; 0.0; 2.0; 1.0; 0.0 ]) ]
          case "pivoted-largest-first" "pivoted" [ "a", Mat(2, 3, [ 1.0; 0.0; 0.0; 5.0; 3.0; 4.0 ]) ]
          case
              "accumulator-wire"
              "accumulatorWire"
              [ "x", Mat(3, 2, [ 1.0; 1.0; 1.0; -0.5; 2.0; 1e-300 ])
                "y", Vec [ 0.1; -0.0; 3.0 ]
                "w", Vec [ 1.0; 0.0; 2.0 ] ]
          // Phase 593 — the elementary functions: the reduction boundaries, both edges of the computed
          // range, subnormal results and inputs, the near-one band of ln, the exact shortcuts of pow,
          // and every row of D43's special-value table.
          case "exp-one" "exp" [ "x", Scalar 1.0 ]
          case "exp-minus-one" "exp" [ "x", Scalar -1.0 ]
          case "exp-at-half-ln2" "exp" [ "x", Scalar 0.34657359027997264 ]
          case "exp-above-half-ln2" "exp" [ "x", Scalar 0.3465735902799727 ]
          case "exp-three-halves-ln2" "exp" [ "x", Scalar 1.0397207708399179 ]
          case "exp-overflow-edge" "exp" [ "x", Scalar 709.782712893384 ]
          case "exp-above-overflow-edge" "exp" [ "x", Scalar 709.7827128933841 ]
          case "exp-underflow-edge" "exp" [ "x", Scalar -745.1332191019412 ]
          case "exp-below-underflow-edge" "exp" [ "x", Scalar -745.14 ]
          case "exp-subnormal-result" "exp" [ "x", Scalar -745.0 ]
          case "exp-just-below-min-normal" "exp" [ "x", Scalar -708.4 ]
          case "exp-just-above-min-normal" "exp" [ "x", Scalar -708.3 ]
          case "exp-tiny" "exp" [ "x", Scalar 1e-10 ]
          case "exp-below-two-to-minus-28" "exp" [ "x", Scalar 3e-09 ]
          case "exp-at-two-to-minus-28" "exp" [ "x", Scalar 3.725290298461914e-09 ]
          case "exp-negative-tiny" "exp" [ "x", Scalar -2.5e-09 ]
          case "exp-zero" "exp" [ "x", Scalar 0.0 ]
          case "exp-negative-zero" "exp" [ "x", Scalar -0.0 ]
          case "exp-ln-two-to-128" "exp" [ "x", Scalar 88.72283905206835 ]
          case "exp-minus-700" "exp" [ "x", Scalar -700.0 ]
          case "exp-fifty-and-half" "exp" [ "x", Scalar 50.5 ]
          case "exp-thousand" "exp" [ "x", Scalar 1000.0 ]
          case "exp-minus-thousand" "exp" [ "x", Scalar -1000.0 ]
          case "exp-infinity" "exp" [ "x", Scalar inf ]
          case "exp-negative-infinity" "exp" [ "x", Scalar -inf ]
          case "exp-nan" "exp" [ "x", Scalar nan ]
          case "ln-two" "ln" [ "x", Scalar 2.0 ]
          case "ln-half" "ln" [ "x", Scalar 0.5 ]
          case "ln-ten" "ln" [ "x", Scalar 10.0 ]
          case "ln-three" "ln" [ "x", Scalar 3.0 ]
          case "ln-e" "ln" [ "x", Scalar 2.718281828459045 ]
          case "ln-just-above-one" "ln" [ "x", Scalar 1.0000000000000002 ]
          case "ln-just-below-one" "ln" [ "x", Scalar 0.9999999999999999 ]
          case "ln-one-plus-two-to-minus-20" "ln" [ "x", Scalar 1.0000009536743164 ]
          case "ln-one-plus-below-two-to-minus-20" "ln" [ "x", Scalar 1.0000009536743162 ]
          case "ln-one-minus-two-to-minus-20" "ln" [ "x", Scalar 0.9999990463256836 ]
          case "ln-at-root-two" "ln" [ "x", Scalar 1.4142135623730951 ]
          case "ln-above-root-two" "ln" [ "x", Scalar 1.4142135623730954 ]
          case "ln-max" "ln" [ "x", Scalar Double.MaxValue ]
          case "ln-min-normal" "ln" [ "x", Scalar 2.2250738585072014e-308 ]
          case "ln-max-subnormal" "ln" [ "x", Scalar 2.225073858507201e-308 ]
          case "ln-min-subnormal" "ln" [ "x", Scalar tiny ]
          case "ln-1e300" "ln" [ "x", Scalar 1e+300 ]
          case "ln-1e-300" "ln" [ "x", Scalar 1e-300 ]
          case "ln-tenth" "ln" [ "x", Scalar 0.1 ]
          case "ln-seven" "ln" [ "x", Scalar 7.0 ]
          case "ln-zero" "ln" [ "x", Scalar 0.0 ]
          case "ln-negative-zero" "ln" [ "x", Scalar -0.0 ]
          case "ln-negative" "ln" [ "x", Scalar -1.0 ]
          case "ln-one" "ln" [ "x", Scalar 1.0 ]
          case "ln-infinity" "ln" [ "x", Scalar inf ]
          case "ln-nan" "ln" [ "x", Scalar nan ]
          case "log10-ten" "log10" [ "x", Scalar 10.0 ]
          case "log10-hundred" "log10" [ "x", Scalar 100.0 ]
          case "log10-thousand" "log10" [ "x", Scalar 1000.0 ]
          case "log10-1e22" "log10" [ "x", Scalar 1e+22 ]
          case "log10-1e23" "log10" [ "x", Scalar 1e+23 ]
          case "log10-thousandth" "log10" [ "x", Scalar 0.001 ]
          case "log10-two" "log10" [ "x", Scalar 2.0 ]
          case "log10-half" "log10" [ "x", Scalar 0.5 ]
          case "log10-1e-300" "log10" [ "x", Scalar 1e-300 ]
          case "log10-min-subnormal" "log10" [ "x", Scalar tiny ]
          case "log10-max" "log10" [ "x", Scalar Double.MaxValue ]
          case "log10-just-above-one" "log10" [ "x", Scalar 1.0000000000000002 ]
          case "log10-seven" "log10" [ "x", Scalar 7.0 ]
          case "log10-zero" "log10" [ "x", Scalar 0.0 ]
          case "log10-one" "log10" [ "x", Scalar 1.0 ]
          case "log10-nan" "log10" [ "x", Scalar nan ]
          case "pow-root-shortcut" "pow" [ "x", Scalar 2.0; "y", Scalar 0.5 ]
          case "pow-square-shortcut" "pow" [ "x", Scalar 10.0; "y", Scalar 2.0 ]
          case "pow-reciprocal-shortcut" "pow" [ "x", Scalar 10.0; "y", Scalar -1.0 ]
          case "pow-identity-shortcut" "pow" [ "x", Scalar 1e+300; "y", Scalar 1.0 ]
          case "pow-two-ten-and-half" "pow" [ "x", Scalar 2.0; "y", Scalar 10.5 ]
          case "pow-ten-tiny-exponent" "pow" [ "x", Scalar 10.0; "y", Scalar 1e-09 ]
          case "pow-near-one-huge-exponent" "pow" [ "x", Scalar 1.0000000000000002; "y", Scalar 1000000000000000.0 ]
          case "pow-below-one-huge-negative-exponent" "pow" [ "x", Scalar 0.9999999999999999; "y", Scalar -1e+16 ]
          case "pow-near-overflow" "pow" [ "x", Scalar 2.0; "y", Scalar 1023.5 ]
          case "pow-subnormal-result" "pow" [ "x", Scalar 2.0; "y", Scalar -1074.5 ]
          case "pow-exact-min-subnormal" "pow" [ "x", Scalar 0.5; "y", Scalar 1074.0 ]
          case "pow-two-cubed" "pow" [ "x", Scalar 2.0; "y", Scalar 3.0 ]
          case "pow-two-to-1023" "pow" [ "x", Scalar 2.0; "y", Scalar 1023.0 ]
          case "pow-generic" "pow" [ "x", Scalar 123.456; "y", Scalar 2.5 ]
          case "pow-negative-exponent" "pow" [ "x", Scalar 10000000000.0; "y", Scalar -3.3 ]
          case "pow-three-seven-and-half" "pow" [ "x", Scalar 3.0; "y", Scalar 7.5 ]
          case "pow-subnormal-base-root" "pow" [ "x", Scalar 1e-320; "y", Scalar 0.5 ]
          case "pow-overflow" "pow" [ "x", Scalar 1e+308; "y", Scalar 1.1 ]
          case "pow-underflow" "pow" [ "x", Scalar 1e-308; "y", Scalar 1.1 ]
          case "pow-two-to-1024" "pow" [ "x", Scalar 2.0; "y", Scalar 1024.0 ]
          case "pow-half-to-minus-1024" "pow" [ "x", Scalar 0.5; "y", Scalar -1024.0 ]
          case "pow-seven-tiny-exponent" "pow" [ "x", Scalar 7.0; "y", Scalar 1e-300 ]
          case "pow-small-base-sixty" "pow" [ "x", Scalar 1e-05; "y", Scalar 60.0 ]
          case "pow-tenth-minus-300" "pow" [ "x", Scalar 0.1; "y", Scalar -300.0 ]
          case "pow-negative-base-odd" "pow" [ "x", Scalar -2.0; "y", Scalar 3.0 ]
          case "pow-negative-base-even" "pow" [ "x", Scalar -2.0; "y", Scalar 2.0 ]
          case "pow-negative-base-non-integer" "pow" [ "x", Scalar -2.0; "y", Scalar 0.5 ]
          case "pow-negative-base-huge-even-exponent" "pow" [ "x", Scalar -1.5; "y", Scalar 2e+18 ]
          case "pow-zero-base-odd" "pow" [ "x", Scalar 0.0; "y", Scalar 3.0 ]
          case "pow-negative-zero-odd" "pow" [ "x", Scalar -0.0; "y", Scalar 3.0 ]
          case "pow-negative-zero-even" "pow" [ "x", Scalar -0.0; "y", Scalar 2.0 ]
          case "pow-zero-negative" "pow" [ "x", Scalar 0.0; "y", Scalar -1.0 ]
          case "pow-infinity-negative-exponent" "pow" [ "x", Scalar inf; "y", Scalar -2.0 ]
          case "pow-negative-infinity-odd" "pow" [ "x", Scalar -inf; "y", Scalar 3.0 ]
          case "pow-negative-infinity-non-integer" "pow" [ "x", Scalar -inf; "y", Scalar 2.5 ]
          case "pow-below-one-to-infinity" "pow" [ "x", Scalar 0.5; "y", Scalar inf ]
          case "pow-two-to-negative-infinity" "pow" [ "x", Scalar 2.0; "y", Scalar -inf ]
          case "pow-one-nan" "pow" [ "x", Scalar 1.0; "y", Scalar nan ]
          case "pow-nan-zero" "pow" [ "x", Scalar nan; "y", Scalar 0.0 ]
          case "pow-minus-one-infinity" "pow" [ "x", Scalar -1.0; "y", Scalar inf ]
          case "pow-anything-zero" "pow" [ "x", Scalar -7.5; "y", Scalar 0.0 ]
          case "pow-zero-zero" "pow" [ "x", Scalar 0.0; "y", Scalar 0.0 ] ]

    let private caseCell (c: Case) : CorpusCell =
        CorpusCell.Row(
            [ "id", CorpusFile.str c.Id
              "op", CorpusFile.str c.Op
              "input", CorpusCell.Row(c.Input |> List.map (fun (n, a) -> n, argCell a))
              "expected", expected c ]
            @ referenceCells c
        )

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
        + "compared exactly. Phase 591 adds the streaming least-squares kernel: `x`, `y` and `w` are a design "
        + "(column-major), its responses and prior weights folded by `QRAccumulator.addRows` into an empty "
        + "accumulator; `qrAccumulator` answers the state (`r`, `qty`, `rss`, `count`), `leastSquares` the fit "
        + "`LeastSquares.ofAccumulator` reads off it (each coefficient its bits or the token `aliased`), and "
        + "`accumulatorWire` the state's canonical wire text, compared byte for byte; `householder` and "
        + "`pivoted` are the batch factorizations, pinned in their own order and not byte-equal to the "
        + "streaming kernel. Phase 593 adds the pinned elementary functions: `exp`, `ln`, `log10` and the "
        + "float-exponent `pow` over `x` (and `y`), each answering `{ verdict: ok, value, table }` where `table` "
        + "is what D43's special-value table said of the input first (`nan`, `value`, `undefined`, `compute`) "
        + "and `value` the function's bits (`nan` where the table said `nan` or `undefined`), compared byte "
        + "for byte; a vector the routine computed also carries `reference`, the correctly rounded bits of "
        + "the true value, and `ulps`, the package's distance from it in units in the last place, which is "
        + "at most one on every such vector (DECISIONS.md D52) and is itself part of the document a host "
        + "must reproduce."

    /// The family's document as the one corpus writer renders it.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "linearAlgebra"
                  "description", CorpusFile.str description ]
              Items = "vectors"
              Rows = cases |> List.map caseCell }
