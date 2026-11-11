namespace Fuaran.Compute.Regression

open System
open Fuaran.Core
open Fuaran.Compute.LinearAlgebra

/// `regressionLaws`: the laws this package's operations are held to, on whichever host runs them — the
/// streaming least-squares accumulator, its fold and its codec (Phase 591), the solve and the batch
/// factorizations held to it, and the generalised linear models (Phases 595, 598 and 600). They left
/// `LinearAlgebraLaws` with the code they hold in Phase 632 (DECISIONS.md D75), each law's body
/// unchanged. Two kinds, as there: BYTE laws hold a pinned operation to a reference spelling of the same
/// roundings in the same order, and ALGEBRAIC laws hold what rounding can only approximate within a
/// stated bound.
///
/// Every input is drawn by the same pinned generator `LinearAlgebraLaws` draws with — Park and Miller's
/// minimal standard in exact float steps, copied here rather than shared so neither package publishes
/// it — and each law draws from its own generator over `seed`, so a law answers over exactly the numbers
/// it answered over before the move, on .NET and under Fable. A law answers `Ok ()` when it held at
/// every iteration, else the first failure with its iteration.
[<RequireQualifiedAccess>]
module RegressionLaws =

    /// The unit roundoff of binary64, `2⁻⁵³`.
    let private u: float = 1.0 / 9007199254740992.0

    let private bits (x: float) : int64 = BitConverter.DoubleToInt64Bits x

    let private sameBits (xs: float[]) (ys: float[]) : bool =
        xs.Length = ys.Length && Array.forall2 (fun x y -> bits x = bits y) xs ys

    let private vecBits (v: Vector<float>) : float[] = Vector.toArray v

    /// The pinned generator: Park and Miller's minimal standard, run in float arithmetic. Every
    /// intermediate is an integer below `2⁵³`, so each step is exact on every host — no host's integer
    /// width or overflow rule is read.
    type private Draw(seed: int) =
        let mutable state =
            ((float seed % 2147483646.0) + 2147483646.0) % 2147483646.0 + 1.0

        member _.Next() : float =
            state <- (state * 48271.0) % 2147483647.0
            state

        /// An integer in `lo .. hi`.
        member this.Int (lo: int) (hi: int) : int =
            lo + int (this.Next() % float (hi - lo + 1))

        /// A float in `(−1, 1)`.
        member this.Unit() : float =
            2.0 * (this.Next() / 2147483647.0) - 1.0

    let private matrix (d: Draw) (rows: int) (cols: int) : Matrix =
        Matrix.init rows cols (fun _ _ -> d.Unit())

    let private vector (d: Draw) (n: int) : Vector<float> =
        Vector.adopt (Array.init n (fun _ -> d.Unit()))

    let private row (a: Matrix) (i: int) : Vector<float> = Vector.init a.Cols (fun j -> a[i, j])

    let private absM (a: Matrix) : Matrix =
        Matrix.init a.Rows a.Cols (fun i j -> abs a[i, j])

    let private ok (r: Result<'T, LinAlgError>) : 'T =
        match r with
        | Ok v -> v
        | Error e -> failwithf "an operation refused drawn input of a fitting shape: %A" e

    /// A well-conditioned upper triangle of order `n`: a diagonal of magnitude in `[1, 2)` with a drawn
    /// sign, and entries above it scaled by `1 / n`.
    let private triangle (d: Draw) (n: int) : UpperTriangular =
        Matrix.init n n (fun i j ->
            if i > j then
                0.0
            elif i = j then
                (if d.Unit() < 0.0 then -1.0 else 1.0) * (1.5 + 0.5 * d.Unit())
            else
                d.Unit() / float n)
        |> UpperTriangular.ofMatrix
        |> ok

    /// Run `body` at `count` iterations over one draw from `seed`; the first failure is the verdict.
    let private law (seed: int) (count: int) (body: Draw -> string option) : Result<unit, string> =
        let d = Draw seed

        let rec go i =
            if i >= count then
                Ok()
            else
                match body d with
                | None -> go (i + 1)
                | Some why -> Error(sprintf "iteration %d (seed %d): %s" i seed why)

        go 0

    let private check (holds: bool) (why: unit -> string) : string option = if holds then None else Some(why ())


    // ---- Phase 591: the streaming QR, the factorizations, least squares and the codec ----------

    /// Rows for an accumulator: a design of `n` by `p` drawn in `(−1, 1)`, a response, and weights —
    /// a quarter of them zero, the rest in `(0, 2)` — so a zero weight and a non-unit `√w` both occur.
    let private weightedRows (d: Draw) (n: int) (p: int) : Matrix * Vector<float> * Vector<float> =
        let x = matrix d n p
        let y = vector d n

        let w =
            Vector.adopt (
                Array.init n (fun _ ->
                    let pick = d.Unit()
                    if pick < -0.5 then 0.0 else 1.0 + d.Unit())
            )

        x, y, w

    let private unitWeights (n: int) : Vector<float> = Vector.adopt (Array.create n 1.0)

    /// The rows `lo .. hi − 1` of `x`, `y` and `w`.
    let private slice (x: Matrix, y: Vector<float>, w: Vector<float>) (lo: int) (hi: int) =
        Matrix.init (hi - lo) x.Cols (fun i j -> x[lo + i, j]), Vector.slice lo (hi - lo) y, Vector.slice lo (hi - lo) w

    /// The state's bits: `R` in column-major order, then `Qᵀy`, the residual sum of squares, the count.
    let private stateBits (acc: QRAccumulator) : float[] =
        Array.concat
            [ Matrix.toColumnMajor (QRAccumulator.r acc)
              vecBits (QRAccumulator.qty acc)
              [| QRAccumulator.rss acc; float (QRAccumulator.count acc) |] ]

    /// The accumulator spelled with the public rotation alone: `Givens.make` on `(R[j,j], x_j)`, the
    /// radius into the diagonal, `Givens.apply` on every pair to its right and on `((Qᵀy)_j, y)`.
    let private spelledAccumulator (x: Matrix) (y: Vector<float>) (w: Vector<float>) : float[] =
        let p = x.Cols
        // R column-major, `R[i,j]` at `j * p + i`.
        let r = Array.zeroCreate<float> (p * p)
        let qty = Array.zeroCreate<float> p
        let mutable rss = 0.0
        let mutable count = 0

        for i in 0 .. x.Rows - 1 do
            if w[i] > 0.0 then
                let sw = sqrt w[i]
                let row = Array.init p (fun k -> x[i, k] * sw)
                let mutable yi = y[i] * sw

                for j in 0 .. p - 1 do
                    let g = Givens.make r[j * p + j] row[j]

                    if not g.IsIdentity then
                        r[j * p + j] <- g.Radius
                        row[j] <- 0.0

                        for k in j + 1 .. p - 1 do
                            let a, b = Givens.apply g r[k * p + j] row[k]
                            r[k * p + j] <- a
                            row[k] <- b

                        let a, b = Givens.apply g qty[j] yi
                        qty[j] <- a
                        yi <- b

                let e2 = yi * yi
                rss <- rss + e2
                count <- count + 1

        Array.concat
            [ Array.init (p * p) (fun k -> let i, j = k % p, k / p in if i > j then 0.0 else r[k])
              qty
              [| rss; float count |] ]

    let private orNone (r: Result<'T, LinAlgError>) : 'T option =
        match r with
        | Ok v -> Some v
        | Error _ -> None

    let private accumulatorIsSpelled (d: Draw) =
        let p = d.Int 0 7
        let x, y, w = weightedRows d (d.Int 0 14) p
        let acc = QRAccumulator.empty p |> QRAccumulator.addRows x y w |> ok

        check (sameBits (stateBits acc) (spelledAccumulator x y w)) (fun () ->
            sprintf "the accumulator differs from its spelling through Givens.make and Givens.apply at %dx%d" x.Rows p)

    let private nativeAccumulatorIsPortable (d: Draw) =
        // Up to 21 columns: a row of R is rotated over up to 21 pairs, crossing the SIMD widths.
        let p = d.Int 0 21
        let x, y, w = weightedRows d (d.Int 0 30) p

        let fold rotate =
            QRAccumulator.foldRowsWith
                rotate
                x.Rows
                (fun i k -> x[i, k])
                (fun i -> y[i])
                (fun i -> w[i])
                (QRAccumulator.empty p)
            |> ok
            |> stateBits

        check (sameBits (fold Kernels.rotateNative) (fold Kernels.rotatePortable)) (fun () ->
            sprintf "the accumulator over the native rotation differs from the portable one at %dx%d" x.Rows p)

    let private addRowsIsRepeatedAddRow (d: Draw) =
        let p = d.Int 0 6
        let n = d.Int 0 12
        let x0, y0, w0 = weightedRows d n p
        // Half the time one row is made unfit: a negative weight, a NaN cell or an infinite response.
        let bad = if n > 0 && d.Unit() < 0.0 then d.Int 0 (n - 1) else -1
        let kind = d.Int 0 2
        let badCol = if p > 0 then d.Int 0 (p - 1) else 0

        let x =
            Matrix.init n p (fun i j ->
                if i = bad && kind = 1 && j = badCol then
                    Double.NaN
                else
                    x0[i, j])

        let y =
            Vector.adopt (
                Array.init n (fun i ->
                    if i = bad && kind = 2 then
                        Double.NegativeInfinity
                    else
                        y0[i])
            )

        let w =
            Vector.adopt (
                Array.init n (fun i ->
                    if i = bad && (kind = 0 || (kind = 1 && p = 0)) then
                        -1.0
                    else
                        w0[i])
            )

        let batch = QRAccumulator.empty p |> QRAccumulator.addRows x y w

        let rec oneByOne i acc =
            if i >= n then
                Ok acc
            else
                match QRAccumulator.addRow (row x i) y[i] w[i] acc with
                | Ok next -> oneByOne (i + 1) next
                | Error e -> Error(i, e)

        let single = oneByOne 0 (QRAccumulator.empty p)

        let rowOf (e: LinAlgError) =
            match e with
            | LinAlgError.InvalidWeight _ -> LinAlgError.InvalidWeight 0
            | LinAlgError.NotFinite(_, c) -> LinAlgError.NotFinite(0, c)
            | other -> other

        let agrees =
            match batch, single with
            | Ok a, Ok b -> bad < 0 && sameBits (stateBits a) (stateBits b)
            | Error(LinAlgError.InvalidWeight k), Error(i, e)
            | Error(LinAlgError.NotFinite(k, _)), Error(i, e) ->
                k = bad
                && i = bad
                && (match batch with
                    | Error be -> rowOf be = e
                    | Ok _ -> false)
            | _ -> false

        check agrees (fun () -> sprintf "addRows is not addRow row by row at %dx%d (unfit row %d)" n p bad)

    let private appendIsFresh (d: Draw) =
        let p = d.Int 0 6
        let n = d.Int 0 16
        let rows = weightedRows d n p
        let cut = d.Int 0 n
        let x, y, w = rows
        let xa, ya, wa = slice rows 0 cut
        let xb, yb, wb = slice rows cut n
        let fresh = QRAccumulator.empty p |> QRAccumulator.addRows x y w |> ok

        let appended =
            QRAccumulator.empty p
            |> QRAccumulator.addRows xa ya wa
            |> Result.bind (QRAccumulator.addRows xb yb wb)
            |> ok

        check (sameBits (stateBits fresh) (stateBits appended)) (fun () ->
            sprintf "appending rows %d.. differs from a fresh fold of all %d at width %d" cut n p)

    let private codecContinues (d: Draw) =
        let p = d.Int 0 6
        let n = d.Int 0 16
        let rows = weightedRows d n p
        let cut = d.Int 0 n
        let xa, ya, wa = slice rows 0 cut
        let xb, yb, wb = slice rows cut n
        let primed = QRAccumulator.empty p |> QRAccumulator.addRows xa ya wa |> ok
        let text = RegressionWire.encodeAccumulator primed
        let restored = RegressionWire.decodeAccumulator text

        let continued (acc: QRAccumulator) =
            acc |> QRAccumulator.addRows xb yb wb |> ok

        let held =
            match restored with
            | Error _ -> false
            | Ok back ->
                RegressionWire.encodeAccumulator back = text
                && sameBits (stateBits (continued back)) (stateBits (continued primed))
                && (match
                        LeastSquares.ofAccumulator (continued back), LeastSquares.ofAccumulator (continued primed)
                    with
                    | Ok f, Ok g ->
                        sameBits
                            (f.Coefficients |> List.map (Option.defaultValue nan) |> Array.ofList)
                            (g.Coefficients |> List.map (Option.defaultValue nan) |> Array.ofList)
                    | Error e, Error g -> e = g
                    | _ -> false)

        check held (fun () ->
            sprintf
                "an accumulator through the codec does not continue identically at %d+%d rows, width %d"
                cut
                (n - cut)
                p)

    let private codecRefusals (d: Draw) =
        let p = d.Int 1 4
        let x, y, w = weightedRows d (p + 3) p
        let acc = QRAccumulator.empty p |> QRAccumulator.addRows x y w |> ok
        let text = RegressionWire.encodeAccumulator acc

        let refused (t: string) =
            Result.isError (RegressionWire.decodeAccumulator t)
        // The first diagonal entry's sign bit set: the first hex digit of `r` gains 8.
        let at = text.IndexOf("\"r\":\"") + 5
        let digit = text[at]

        let flipped =
            if digit < '8' then
                "89abcdef"[int digit - int '0']
            else
                digit

        let negative = text.Substring(0, at) + string flipped + text.Substring(at + 1)

        check
            (refused (text.Replace("\"count\":", "\"extra\":0,\"count\":"))
             && refused (text.Replace("qrAccumulator", "matrix"))
             && refused (text.Replace("\"r\":\"", "\"r\":\"0"))
             && refused (text.Replace("\"qty\":\"", "\"qty\":\"G"))
             && (digit >= '8' || refused negative)
             && refused "{\"$type\":\"matrix\",\"bits\":\"\",\"cols\":-1,\"rows\":0}"
             && Result.isOk (RegressionWire.decodeAccumulator text))
            (fun () -> sprintf "a malformed accumulator document was read at width %d" p)

    /// `|a| ≤ bound` for every pair of a matrix and its reference, with `bound i j`.
    let private within (a: Matrix) (b: Matrix) (bound: int -> int -> float) : bool =
        [ for i in 0 .. a.Rows - 1 do
              for j in 0 .. a.Cols - 1 -> abs (a[i, j] - b[i, j]) <= bound i j ]
        |> List.forall id

    let private columnNorms (a: Matrix) : float[] =
        Array.init a.Cols (fun j -> Blas.norm2 (Matrix.column j a))

    /// The weighted design `√W X`, as the accumulator sees it.
    let private weighted (x: Matrix) (w: Vector<float>) : Matrix =
        Matrix.init x.Rows x.Cols (fun i j -> x[i, j] * sqrt w[i])

    let private rIsTriangularAndGram (d: Draw) =
        let p = d.Int 1 6
        let x, y, w = weightedRows d (d.Int 0 20) p
        let acc = QRAccumulator.empty p |> QRAccumulator.addRows x y w |> ok
        let r = QRAccumulator.r acc
        let xw = weighted x w
        let norms = columnNorms xw
        let rtr = Blas.gemm (Matrix.transpose r) r |> ok
        let gamma = 16.0 * float (x.Rows + p + 2) * u

        check
            (Result.isOk (UpperTriangular.ofMatrix r)
             && List.forall (fun j -> r[j, j] >= 0.0) [ 0 .. p - 1 ]
             && within rtr (Blas.gram xw) (fun i j -> gamma * norms[i] * norms[j]))
            (fun () -> sprintf "R is not a triangle whose RᵀR is XᵀWX within its bound at %dx%d" x.Rows p)

    /// A tall design whose columns are well-conditioned: `n ≥ 3p + 2` rows drawn in `(−1, 1)`, with the
    /// identity added to its top rows.
    let private tall (d: Draw) : Matrix * Vector<float> =
        let p = d.Int 1 6
        let n = d.Int (3 * p + 2) (3 * p + 12)
        let a = matrix d n p
        Matrix.init n p (fun i j -> if i = j then a[i, j] + 2.0 else a[i, j]), vector d n

    let private normalEquations (d: Draw) =
        let x, y = tall d
        let fit = LeastSquares.solve x y |> ok |> LeastSquares.requireFullRank |> ok
        let beta = Vector.adopt (fit.Coefficients |> List.map Option.get |> Array.ofList)
        let residual = Blas.sub y (Blas.gemv x beta |> ok) |> ok
        let xtr = Blas.gemv (Matrix.transpose x) residual |> ok
        let norms = columnNorms x

        let scale =
            Blas.norm2 y + (Array.mapi (fun j nj -> abs beta[j] * nj) norms |> Array.sum)

        let gamma = 64.0 * float (x.Rows + x.Cols) * u
        let rss = Blas.dot residual residual |> ok

        check
            (Array.forall id (Array.init x.Cols (fun j -> abs xtr[j] <= gamma * norms[j] * scale))
             && abs (fit.Rss - rss) <= gamma * (rss + Blas.norm2 y * scale))
            (fun () -> sprintf "the fit does not satisfy the normal equations within its bound at %dx%d" x.Rows x.Cols)

    let private householderAgrees (d: Draw) =
        let x, y = tall d

        let streaming =
            QRAccumulator.empty x.Cols
            |> QRAccumulator.addRows x y (unitWeights x.Rows)
            |> ok

        let batch = QR.householder x |> ok |> UpperTriangular.toMatrix
        let norms = columnNorms x
        let gamma = 256.0 * float (x.Rows + x.Cols) * u

        check
            (within (QRAccumulator.r streaming) batch (fun _ j -> gamma * norms[j])
             && List.forall (fun j -> batch[j, j] >= 0.0) [ 0 .. x.Cols - 1 ])
            (fun () -> sprintf "the Householder R is not the streaming R within its bound at %dx%d" x.Rows x.Cols)

    /// A tall design with column `dup` a copy of an earlier column `src`.
    let private aliasedDesign (d: Draw) : Matrix * Vector<float> * int * int =
        let x, y = tall d
        let p = x.Cols + 1
        let src = d.Int 0 (x.Cols - 1)
        let dup = d.Int (src + 1) x.Cols
        let column j = if j < dup then j else j - 1
        Matrix.init x.Rows p (fun i j -> if j = dup then x[i, src] else x[i, column j]), y, src, dup

    let private aliasing (d: Draw) =
        let x, y, src, dup = aliasedDesign d
        let fit = LeastSquares.solve x y |> ok
        let pivoted = QR.pivoted x |> ok

        let reduced =
            Matrix.init x.Rows (x.Cols - 1) (fun i j -> x[i, if j < dup then j else j + 1])

        let reference = LeastSquares.solve reduced y |> ok
        let gamma = 1024.0 * float (x.Rows + x.Cols) * u

        let close =
            List.zip (fit.Coefficients |> List.removeAt dup) reference.Coefficients
            |> List.forall (fun (a, b) ->
                match a, b with
                | Some a, Some b -> abs (a - b) <= gamma * (1.0 + abs b)
                | _ -> false)

        check
            (fit.Aliased = [ dup ]
             && fit.Rank = x.Cols - 1
             && fit.Coefficients[dup].IsNone
             && fit.ResidualDf = x.Rows - fit.Rank
             && LeastSquares.requireFullRank fit = Error(LinAlgError.RankDeficient(x.Cols - 1))
             && close
             && pivoted.Rank = x.Cols - 1
             && (pivoted.Aliased = [ src ] || pivoted.Aliased = [ dup ])
             && List.sort pivoted.Permutation = [ 0 .. x.Cols - 1 ])
            (fun () ->
                sprintf
                    "column %d copying column %d was not aliased as stated at %dx%d (aliased %A, pivoted %A)"
                    dup
                    src
                    x.Rows
                    x.Cols
                    fit.Aliased
                    pivoted.Aliased)

    let private inverseAndEffects (d: Draw) =
        let x, y = tall d
        let fit = LeastSquares.solve x y |> ok
        let p = fit.Rank
        let r = UpperTriangular.toMatrix fit.R
        let product = Blas.gemm r fit.RInverse |> ok
        let gamma = 64.0 * float (p + 2) * u
        let absProduct = Blas.gemm (absM r) (absM fit.RInverse) |> ok
        let back = UpperTriangular.solveUpper fit.R fit.Qty |> ok |> vecBits

        check
            (within product (Matrix.identity p) (fun i j -> gamma * absProduct[i, j])
             && sameBits back (fit.Coefficients |> List.map Option.get |> Array.ofList))
            (fun () -> sprintf "R⁻¹ is not R's inverse, or Qᵀy does not solve to the coefficients, at order %d" p)

    let private qrRefusals (d: Draw) =
        let p = d.Int 1 5
        let acc = QRAccumulator.empty p
        let x = vector d p
        let row = Matrix.init 1 p (fun _ j -> x[j])
        let nanAt = d.Int 0 (p - 1)
        let withNaN = Vector.adopt (Array.init p (fun j -> if j = nanAt then nan else x[j]))
        let vd (v: Vector<float>) = { Rows = v.Length; Cols = 1 }
        let huge = Matrix.init 2 1 (fun _ _ -> Double.MaxValue)

        let expected: (string * bool) list =
            [ "addRow shape",
              QRAccumulator.addRow (vector d (p + 1)) 1.0 1.0 acc |> Result.map ignore =
                  Error(
                      LinAlgError.ShapeMismatch(
                          "QRAccumulator.addRow",
                          { Rows = p; Cols = 1 },
                          { Rows = p + 1; Cols = 1 }
                      )
                  )
              "addRow negative weight",
              (QRAccumulator.addRow x 1.0 -0.5 acc |> Result.map ignore) = Error(LinAlgError.InvalidWeight 0)
              "addRow NaN weight",
              (QRAccumulator.addRow x 1.0 nan acc |> Result.map ignore) = Error(LinAlgError.InvalidWeight 0)
              "addRow infinite weight",
              (QRAccumulator.addRow x 1.0 Double.PositiveInfinity acc |> Result.map ignore) =
                  Error(LinAlgError.InvalidWeight 0)
              "addRow NaN cell",
              (QRAccumulator.addRow withNaN 1.0 1.0 acc |> Result.map ignore) = Error(LinAlgError.NotFinite(0, nanAt))
              "addRow infinite response",
              (QRAccumulator.addRow x Double.NegativeInfinity 1.0 acc |> Result.map ignore) =
                  Error(LinAlgError.NotFinite(0, p))
              "addRow zero weight counts nothing",
              (QRAccumulator.addRow x 1.0 -0.0 acc
               |> ok
               |> stateBits
               |> sameBits (stateBits acc))
              "addRows weights shape",
              (QRAccumulator.addRows row (vector d 1) (vector d 2) acc |> Result.map ignore) =
                  Error(LinAlgError.ShapeMismatch("QRAccumulator.addRows", row.Dims, vd (vector d 2)))
              "solve shape",
              (LeastSquares.solve row (vector d 2) |> Result.map ignore) =
                  Error(LinAlgError.ShapeMismatch("LeastSquares.solve", row.Dims, { Rows = 2; Cols = 1 }))
              "an overflowing state",
              (QRAccumulator.empty 1
               |> QRAccumulator.addRows huge (Vector.ofList [ 0.0; 0.0 ]) (unitWeights 2)
               |> Result.bind LeastSquares.ofAccumulator
               |> Result.map ignore) =
                  Error(LinAlgError.NotFinite(0, 0))
              "householder NaN",
              (QR.householder (Matrix.init 2 2 (fun i j -> if i = 1 && j = 0 then nan else 1.0))
               |> Result.map ignore) =
                  Error(LinAlgError.NotFinite(1, 0))
              "no rows",
              (LeastSquares.ofAccumulator acc
               |> Result.map (fun f -> f.Rank, f.Aliased, bits f.Rss)) =
                  Ok(0, [ 0 .. p - 1 ], bits 0.0)
              "empty accumulator",
              orNone (QR.pivoted (Matrix.init 0 p (fun _ _ -> 0.0)))
              |> Option.map (fun q -> q.Rank) =
                  Some 0 ]

        match expected |> List.filter (snd >> not) with
        | [] -> None
        | failed ->
            Some(sprintf "QR refusals not answered as stated: %s" (failed |> List.map fst |> String.concat ", "))

    // ---- generalised linear models (Phase 595) ---------------------------------------------------

    /// A drawn GLM design: `n` rows, an intercept column then `k` drawn columns; prior weights with
    /// zeros among the later rows (the first `k + 3` positive); an offset in `(−½, ½)`.
    let private glmDesign (d: Draw) : Matrix * Vector<float> * Vector<float> * int =
        let k = d.Int 1 3
        let n = d.Int (k + 6) 24
        let x = Matrix.init n (k + 1) (fun _ j -> if j = 0 then 1.0 else d.Unit())

        let w =
            Vector.adopt (
                Array.init n (fun i ->
                    if i >= k + 3 && d.Int 0 4 = 0 then
                        0.0
                    else
                        0.5 + abs (d.Unit()))
            )

        let o = Vector.adopt (Array.init n (fun _ -> 0.5 * d.Unit()))
        x, w, o, k

    /// Counts in `0 .. 6`, every one of the first `k + 3` at least one, so the maximum-likelihood
    /// estimate exists.
    let private counts (d: Draw) (n: int) (k: int) : Vector<float> =
        Vector.adopt (Array.init n (fun i -> float (d.Int (if i < k + 3 then 1 else 0) 6)))

    /// Positive responses in `(0.25, 4.25)`.
    let private positives (d: Draw) (n: int) : Vector<float> =
        Vector.adopt (Array.init n (fun _ -> 2.25 + 2.0 * d.Unit()))

    let private optBits (xs: float option list) : int64 option list = xs |> List.map (Option.map bits)

    /// `Gaussian` under `Identity` through `Glm.fit` is the least-squares fold of `y − offset` under the
    /// prior weights, byte for byte: its coefficients, its fitted values (`Σⱼ xᵢⱼβⱼ` then the offset), its
    /// deviance, its standard errors, and one iteration.
    let private gaussianIsLeastSquares (d: Draw) =
        let x, w, o, _ = glmDesign d
        let n = x.Rows
        let y = vector d n
        let ya, oa, wa = Vector.toArray y, Vector.toArray o, Vector.toArray w

        let ls =
            QRAccumulator.empty x.Cols
            |> QRAccumulator.addRows x (Vector.adopt (Array.init n (fun i -> ya[i] - oa[i]))) w
            |> Result.bind LeastSquares.ofAccumulator
            |> ok

        let beta = ls.Coefficients |> List.toArray

        let fitted =
            Array.init n (fun i ->
                let mutable acc = 0.0

                for j in 0 .. x.Cols - 1 do
                    match beta[j] with
                    | Some b -> acc <- acc + x[i, j] * b
                    | None -> ()

                acc + oa[i])

        let mutable dev = 0.0

        for i in 0 .. n - 1 do
            let r = ya[i] - fitted[i]
            dev <- dev + wa[i] * (r * r)

        let df = ls.Count - ls.Rank
        let se = LeastSquares.standardErrors (dev / float df) ls

        match Glm.fit GlmFamily.Gaussian GlmLink.Identity GlmControl.defaults x y w o with
        | Error e -> Some(sprintf "refused: %A" e)
        | Ok g ->
            check
                (optBits g.Coefficients = optBits ls.Coefficients
                 && sameBits (vecBits g.Fitted) fitted
                 && bits g.Deviance = bits dev
                 && optBits g.StdErrors = optBits se
                 && g.Iterations = 1)
                (fun () ->
                    sprintf "the IRLS fit is not the least-squares fit: %A against %A" g.Coefficients ls.Coefficients)

    /// The maximum-likelihood equations at convergence: `Xᵀ W (y − μ) = 0` for the canonical Poisson
    /// log link, within a bound of the tolerance's order times the terms' scale — with the intercept,
    /// `Σ w μ̂ = Σ w y`.
    let private poissonScore (d: Draw) =
        let x, w, o, k = glmDesign d
        let n = x.Rows
        let y = counts d n k
        let ya, wa = Vector.toArray y, Vector.toArray w

        match Glm.fit GlmFamily.Poisson GlmLink.Log GlmControl.defaults x y w o with
        | Error e -> Some(sprintf "refused: %A" e)
        | Ok g ->
            let mu = vecBits g.Fitted

            [ 0 .. x.Cols - 1 ]
            |> List.tryPick (fun j ->
                let mutable score = 0.0
                let mutable scale = 0.0

                for i in 0 .. n - 1 do
                    score <- score + wa[i] * x[i, j] * (ya[i] - mu[i])
                    scale <- scale + wa[i] * abs x[i, j] * (ya[i] + mu[i])

                check (abs score <= 1e-5 * scale) (fun () ->
                    sprintf "the score of column %d is %.17g over a scale of %.17g" j score scale))

    /// An offset of `c` on every row is the intercept shifted by `c`: the other coefficients and the
    /// means agree, within the tolerance's order, with the fit without it.
    let private offsetShiftsIntercept (d: Draw) =
        let x, w, _, k = glmDesign d
        let n = x.Rows
        let y = counts d n k
        let c = d.Unit()
        let zero = Vector.adopt (Array.zeroCreate n)
        let shifted = Vector.adopt (Array.create n c)

        match
            Glm.fit GlmFamily.Poisson GlmLink.Log GlmControl.defaults x y w zero,
            Glm.fit GlmFamily.Poisson GlmLink.Log GlmControl.defaults x y w shifted
        with
        | Ok a, Ok b ->
            let ca = a.Coefficients |> List.map (Option.defaultValue nan) |> List.toArray
            let cb = b.Coefficients |> List.map (Option.defaultValue nan) |> List.toArray
            let near (p: float) (q: float) = abs (p - q) <= 1e-4 * (1.0 + abs p)

            let coefficientsAgree =
                near ca[0] (cb[0] + c) && Array.forall2 near (Array.skip 1 ca) (Array.skip 1 cb)

            let meansAgree = Array.forall2 near (vecBits a.Fitted) (vecBits b.Fitted)

            check (coefficientsAgree && meansAgree) (fun () ->
                sprintf "an offset of %.17g: %A against %A" c a.Coefficients b.Coefficients)
        | a, b -> Some(sprintf "refused: %A, %A" a b)

    /// Under the Gamma family and the log link every iteration's working weight is the prior weight,
    /// exactly, and observing the iterations changes nothing.
    let private gammaWeightsArePrior (d: Draw) =
        let x, w, o, _ = glmDesign d
        let y = positives d x.Rows
        let seen = ResizeArray<float[]>()

        let observed =
            Glm.fitObserved
                (fun _ ww -> seen.Add(Array.copy ww))
                GlmFamily.Gamma
                GlmLink.Log
                GlmControl.defaults
                x
                y
                w
                o

        let plain = Glm.fit GlmFamily.Gamma GlmLink.Log GlmControl.defaults x y w o
        let prior = vecBits w

        match observed, plain with
        | Ok a, Ok b ->
            check
                (seen.Count = a.Iterations
                 && seen |> Seq.forall (fun ww -> sameBits ww prior)
                 && optBits a.Coefficients = optBits b.Coefficients)
                (fun () ->
                    sprintf
                        "a working weight is not the prior weight, or %d iterations observed against %d reported"
                        seen.Count
                        a.Iterations)
        | a, _ -> Some(sprintf "refused: %A" a)

    /// Every Glm refusal is answered as stated, naming the first offending row.
    let private glmRefusals (d: Draw) =
        let x, w, o, k = glmDesign d
        let n = x.Rows
        let y = counts d n k
        let yPos = positives d n
        let at = d.Int 0 (n - 1)
        let c = GlmControl.defaults

        let withAt (v: Vector<float>) (value: float) =
            Vector.adopt (Vector.toArray v |> Array.mapi (fun i e -> if i = at then value else e))

        let fit family link x y w o = Glm.fit family link c x y w o
        let vd (len: int) : Dims = { Rows = len; Cols = 1 }
        let short = Vector.adopt (Array.zeroCreate (n - 1))

        let xNaN =
            Matrix.init n x.Cols (fun i j -> if i = at && j = x.Cols - 1 then nan else x[i, j])

        // A zero-weight row far out along the last column: its mean overflows unless the slope is
        // exactly zero, which leaves the row in range.
        let farW = withAt w 0.0

        let xFar =
            Matrix.init n x.Cols (fun i j -> if i = at && j = x.Cols - 1 then 1e300 else x[i, j])

        let separated =
            match fit GlmFamily.Poisson GlmLink.Log xFar y farW o with
            | Error(LinAlgError.Separation r) -> r = at
            | Ok g -> (List.last g.Coefficients |> Option.defaultValue 0.0) = 0.0
            | Error _ -> false

        let poisson = fit GlmFamily.Poisson GlmLink.Log

        let expected: (string * bool) list =
            [ "shape", poisson x short w o = Error(LinAlgError.ShapeMismatch("Glm.fit", x.Dims, vd (n - 1)))
              "pairing",
              fit GlmFamily.Poisson GlmLink.Logit x y w o = Error(LinAlgError.UnsupportedModel("poisson", "logit"))
              "gaussian probit",
              fit GlmFamily.Gaussian GlmLink.Probit x y w o = Error(LinAlgError.UnsupportedModel("gaussian", "probit"))
              "weight", poisson x y (withAt w -1.0) o = Error(LinAlgError.InvalidWeight at)
              "weight nan", poisson x y (withAt w nan) o = Error(LinAlgError.InvalidWeight at)
              "poisson negative", poisson x (withAt y -1.0) w o = Error(LinAlgError.InvalidResponse at)
              "poisson infinite",
              poisson x (withAt y Double.PositiveInfinity) w o = Error(LinAlgError.InvalidResponse at)
              "gamma zero",
              fit GlmFamily.Gamma GlmLink.Log x (withAt yPos 0.0) w o = Error(LinAlgError.InvalidResponse at)
              "design", poisson xNaN y w o = Error(LinAlgError.NotFinite(at, x.Cols - 1))
              "offset", poisson x y w (withAt o nan) = Error(LinAlgError.NotFinite(at, x.Cols))
              "separation", separated
              "not converged",
              Glm.fit GlmFamily.Poisson GlmLink.Log (GlmControl.create 1 1e-8 |> Option.get) x y w o =
                  Error(LinAlgError.NotConverged 1)
              "control",
              GlmControl.create 0 1e-8 = None
              && GlmControl.create 1 0.0 = None
              && GlmControl.create 1 nan = None
              "null shape",
              Glm.nullDeviance GlmFamily.Poisson GlmLink.Log c true y short o =
                  Error(LinAlgError.ShapeMismatch("Glm.fit", { Rows = n; Cols = 1 }, vd (n - 1))) ]

        match expected |> List.filter (snd >> not) with
        | [] -> None
        | failed ->
            Some(sprintf "Glm refusals not answered as stated: %s" (failed |> List.map fst |> String.concat ", "))

    /// Quasi-Poisson iterates exactly as Poisson (Phase 598, D57): its coefficients, means, deviance and
    /// iteration count are Poisson's, byte for byte; its dispersion is Pearson's `Σ w·(y − μ)²/μ` over the
    /// residual degrees of freedom, and its standard errors are the classical ones under it.
    let private quasiPoissonIsPoisson (d: Draw) =
        let x, w, o, k = glmDesign d
        let n = x.Rows
        let y = counts d n k
        let ya, wa = Vector.toArray y, Vector.toArray w

        match
            Glm.fit GlmFamily.Poisson GlmLink.Log GlmControl.defaults x y w o,
            Glm.fit GlmFamily.QuasiPoisson GlmLink.Log GlmControl.defaults x y w o
        with
        | Ok p, Ok q ->
            let mu = vecBits q.Fitted
            let df = q.ResidualDf
            let mutable pearson = 0.0

            for i in 0 .. n - 1 do
                if wa[i] > 0.0 then
                    let r = ya[i] - mu[i]
                    pearson <- pearson + wa[i] * (r * r / mu[i])

            let phi = if df > 0 then Some(pearson / float df) else None

            let se =
                match phi with
                | Some f -> LeastSquares.standardErrors f q.LeastSquares
                | None -> List.replicate x.Cols None

            check
                (optBits p.Coefficients = optBits q.Coefficients
                 && sameBits (vecBits p.Fitted) mu
                 && bits p.Deviance = bits q.Deviance
                 && p.Iterations = q.Iterations
                 && (q.Dispersion |> Option.map bits) = (phi |> Option.map bits)
                 && optBits q.StdErrors = optBits se)
                (fun () -> sprintf "quasi-Poisson is not Poisson: %A against %A" q.Coefficients p.Coefficients)
        | a, b -> Some(sprintf "refused: %A, %A" a b)

    /// Tweedie at `p = 1` agrees with quasi-Poisson (Phase 598, D57): the same variance function and
    /// working weights (`Elementary.pow μ 1` is `μ`), from Tweedie's own start, so the coefficients, the
    /// means and the dispersion agree within the tolerance's order; and Tweedie at `p = 2` is Gamma's
    /// iteration, its coefficients and deviance byte for byte.
    let private tweedieEndpoints (d: Draw) =
        let x, w, o, k = glmDesign d
        let n = x.Rows
        let y = counts d n k
        let yPos = positives d n

        let fit family y =
            Glm.fit family GlmLink.Log GlmControl.defaults x y w o

        let near (p: float) (q: float) = abs (p - q) <= 1e-4 * (1.0 + abs p)

        let nearAll (a: float option list) (b: float option list) =
            List.forall2 (fun p q -> Option.map2 near p q |> Option.defaultValue (p = q)) a b

        let pOne =
            match fit (GlmFamily.Tweedie 1.0) y, fit GlmFamily.QuasiPoisson y with
            | Ok t1, Ok q ->
                check
                    (nearAll t1.Coefficients q.Coefficients
                     && Array.forall2 near (vecBits t1.Fitted) (vecBits q.Fitted)
                     && nearAll [ t1.Dispersion ] [ q.Dispersion ])
                    (fun () -> sprintf "Tweedie(1) %A against quasi-Poisson %A" t1.Coefficients q.Coefficients)
            | a, b -> Some(sprintf "refused: %A, %A" a b)

        // Gamma's iteration may refuse (no convergence) on a draw; Tweedie at p = 2 then refuses alike.
        let pTwo =
            match fit (GlmFamily.Tweedie 2.0) yPos, fit GlmFamily.Gamma yPos with
            | Ok t2, Ok g ->
                check
                    (optBits t2.Coefficients = optBits g.Coefficients
                     && bits t2.Deviance = bits g.Deviance
                     && t2.Iterations = g.Iterations)
                    (fun () -> sprintf "Tweedie(2) %A against Gamma %A" t2.Coefficients g.Coefficients)
            | Error a, Error b -> check (a = b) (fun () -> sprintf "Tweedie(2) refused %A, Gamma %A" a b)
            | a, b -> Some(sprintf "Tweedie(2) %A against Gamma %A" a b)

        Option.orElse pTwo pOne

    /// The sandwich (Phase 598, D57) on a homoskedastic least-squares fit: with noise of one scale on
    /// every row, HC0 approaches the classical errors (within a quarter of them over a thousand rows);
    /// HC1 is HC0 scaled by `√(n / (n − rank))`; and each column's HC0 ≤ HC2 ≤ HC3, every row's
    /// adjustment dividing by a leverage complement no greater than one.
    let private sandwichHomoskedastic (d: Draw) =
        let k = d.Int 1 3
        let n = 1000
        let x = Matrix.init n (k + 1) (fun _ j -> if j = 0 then 1.0 else d.Unit())
        let beta = Array.init (k + 1) (fun _ -> d.Unit())

        let y =
            Array.init n (fun i ->
                let mutable acc = 0.0

                for j in 0..k do
                    acc <- acc + x[i, j] * beta[j]

                acc + d.Unit())

        let ones = Vector.adopt (Array.create n 1.0)
        let ls = LeastSquares.solve x (Vector.adopt y) |> ok
        let b = ls.Coefficients |> List.map (Option.defaultValue 0.0) |> List.toArray

        let resid =
            Array.init n (fun i ->
                let mutable acc = 0.0

                for j in 0..k do
                    acc <- acc + x[i, j] * b[j]

                y[i] - acc)

        let rss = resid |> Array.fold (fun acc r -> acc + r * r) 0.0

        let classical =
            LeastSquares.standardErrors (rss / float (n - k - 1)) ls |> List.map Option.get

        let sandwich v =
            LeastSquares.robustStandardErrors v x ones (Vector.adopt resid) ls
            |> ok
            |> List.map Option.get

        let hc0 = sandwich RobustVariant.HC0
        let hc1 = sandwich RobustVariant.HC1
        let hc2 = sandwich RobustVariant.HC2
        let hc3 = sandwich RobustVariant.HC3
        let scale = sqrt (float n / float (n - k - 1))
        let slack (a: float) (b: float) = a <= b * (1.0 + 1e-12)

        check
            (List.forall2 (fun h c -> abs (h / c - 1.0) <= 0.25) hc0 classical
             && List.forall2 (fun h1 h0 -> abs (h1 - h0 * scale) <= 1e-12 * h1) hc1 hc0
             && List.forall2 slack hc0 hc2
             && List.forall2 slack hc2 hc3)
            (fun () -> sprintf "HC0 %A, HC2 %A, HC3 %A against the classical %A" hc0 hc2 hc3 classical)

    /// Every refusal and undefined answer Phase 598 adds is answered as stated: a Tweedie power strictly
    /// between zero and one or not finite, a response outside Tweedie's range for its power, quasi-Poisson
    /// outside its range and link, the sandwich's shape, weight and residual refusals, HC1 with no
    /// residual degree of freedom, and HC2 and HC3 at a row of leverage one (where HC0 is defined).
    let private robustAndTweedieRefusals (d: Draw) =
        let x, w, o, k = glmDesign d
        let n = x.Rows
        let y = counts d n k
        let yPos = positives d n
        let at = d.Int 0 (n - 1)
        let c = GlmControl.defaults

        let withAt (v: Vector<float>) (value: float) =
            Vector.adopt (Vector.toArray v |> Array.mapi (fun i e -> if i = at then value else e))

        let tweedie p y =
            Glm.fit (GlmFamily.Tweedie p) GlmLink.Log c x y w o

        let ls = LeastSquares.solve x y |> ok
        let ones = Vector.adopt (Array.create n 1.0)
        let short = Vector.adopt (Array.zeroCreate (n - 1))

        let robust v w r =
            LeastSquares.robustStandardErrors v x w r ls

        // A square design: every row's leverage is one and no residual degree of freedom is left.
        let p = x.Cols
        let sq = Matrix.init p p (fun i j -> if i = j then 1.0 else 0.0)
        let sqY = Vector.adopt (Array.init p (fun i -> float (i + 1)))
        let sqLs = LeastSquares.solve sq sqY |> ok
        let sqOnes = Vector.adopt (Array.create p 1.0)
        let sqZero = Vector.adopt (Array.zeroCreate p)

        let allNone v =
            LeastSquares.robustStandardErrors v sq sqOnes sqZero sqLs = Ok(List.replicate p None)

        let refusedAsPower (r: Result<GlmFit, LinAlgError>) (isPower: float -> bool) =
            match r with
            | Error(LinAlgError.InvalidPower v) -> isPower v
            | _ -> false

        let expected: (string * bool) list =
            [ "power half", refusedAsPower (tweedie 0.5 y) (fun v -> v = 0.5)
              "power nan", refusedAsPower (tweedie nan y) Double.IsNaN
              "power infinite", refusedAsPower (tweedie Double.PositiveInfinity y) Double.IsPositiveInfinity
              "power zero admitted",
              (match tweedie 0.0 yPos with
               | Error(LinAlgError.InvalidPower _)
               | Error(LinAlgError.UnsupportedModel _) -> false
               | _ -> true)
              "tweedie negative", tweedie 1.5 (withAt y -1.0) = Error(LinAlgError.InvalidResponse at)
              "tweedie zero at p = 3", tweedie 3.0 (withAt yPos 0.0) = Error(LinAlgError.InvalidResponse at)
              "quasi-Poisson negative",
              Glm.fit GlmFamily.QuasiPoisson GlmLink.Log c x (withAt y -1.0) w o = Error(LinAlgError.InvalidResponse at)
              "quasi-Poisson inverse",
              Glm.fit GlmFamily.QuasiPoisson GlmLink.Inverse c x y w o =
                  Error(LinAlgError.UnsupportedModel("quasiPoisson", "inverse"))
              "robust weights shape",
              robust RobustVariant.HC3 short y =
                  Error(
                      LinAlgError.ShapeMismatch("LeastSquares.robustStandardErrors", x.Dims, { Rows = n - 1; Cols = 1 })
                  )
              "robust weight", robust RobustVariant.HC0 (withAt ones -1.0) y = Error(LinAlgError.InvalidWeight at)
              "robust residual", robust RobustVariant.HC0 ones (withAt y nan) = Error(LinAlgError.NotFinite(at, p))
              "HC1 no df", allNone RobustVariant.HC1
              "HC2 leverage one", allNone RobustVariant.HC2
              "HC3 leverage one", allNone RobustVariant.HC3
              "HC0 leverage one",
              LeastSquares.robustStandardErrors RobustVariant.HC0 sq sqOnes sqZero sqLs =
                  Ok(List.replicate p (Some 0.0)) ]

        match expected |> List.filter (snd >> not) with
        | [] -> None
        | failed ->
            Some(
                sprintf
                    "Tweedie and sandwich refusals not answered as stated: %s"
                    (failed |> List.map fst |> String.concat ", ")
            )

    // ---- the general GLM (Phase 600, DECISIONS.md D58) -------------------------------------------

    /// A 0/1 outcome over a drawn design of thirty to forty rows: a third of the rows one and a third
    /// zero whatever their covariates, so a separating plane is out of reach of the draw, and the last
    /// third one where `2·Σⱼ xᵢⱼ` plus a drawn disturbance is positive, so the slopes carry signal.
    let private binaryDesign (d: Draw) : Matrix * Vector<float> * Vector<float> =
        let k = d.Int 1 3
        let n = d.Int 30 40
        let x = Matrix.init n (k + 1) (fun _ j -> if j = 0 then 1.0 else d.Unit())

        let y =
            Vector.adopt (
                Array.init n (fun i ->
                    if i % 3 = 0 then
                        1.0
                    elif i % 3 = 1 then
                        0.0
                    else
                        let mutable s = 0.0

                        for j in 1..k do
                            s <- s + x[i, j]

                        if 2.0 * s + d.Unit() > 0.0 then 1.0 else 0.0)
            )

        let w = Vector.adopt (Array.init n (fun _ -> 0.5 + abs (d.Unit())))
        x, y, w

    let private zeros (n: int) : Vector<float> = Vector.adopt (Array.zeroCreate n)

    /// `d²μ/dη²` and `V′(μ)` of the five canonical pairs, at `η` and the fitted `μ`.
    let private canonicalCurvature (family: GlmFamily) (eta: float) (mu: float) : float * float =
        match family with
        | GlmFamily.Gaussian -> 0.0, 0.0
        | GlmFamily.Binomial -> mu * (1.0 - mu) * (1.0 - 2.0 * mu), 1.0 - 2.0 * mu
        | GlmFamily.Poisson -> mu, 1.0
        | GlmFamily.Gamma -> 2.0 / (eta * eta * eta), 2.0 * mu
        | _ -> 0.75 / (eta * eta * sqrt eta), 3.0 * mu * mu

    /// Under a canonical link Fisher scoring is Newton's method (D58 item 11): at convergence the observed
    /// information `Xᵀ diag(w·(μ′²/V − (y − μ)·(μ″/V − μ′²V′/V²))) X` equals the expected `Xᵀ diag(w·μ′²/V) X`
    /// row weight by row weight, within rounding — and under the probit link, which is not canonical, the
    /// correction is observable, so the law measures something.
    let private canonicalFisherIsNewton (d: Draw) =
        let x, w, o, k = glmDesign d
        let n = x.Rows
        let wa = Vector.toArray w

        let pairs =
            [ GlmFamily.Gaussian, GlmLink.Identity, vector d n
              GlmFamily.Binomial, GlmLink.Logit, Vector.adopt (Array.init n (fun i -> if i % 2 = 0 then 0.75 else 0.25))
              GlmFamily.Poisson, GlmLink.Log, counts d n k
              GlmFamily.Gamma, GlmLink.Inverse, positives d n
              GlmFamily.InverseGaussian, GlmLink.InverseSquare, positives d n ]

        let gap (family: GlmFamily) (link: GlmLink) (y: Vector<float>) (g: GlmFit) =
            let ya = Vector.toArray y
            let mu = vecBits g.Fitted
            let eta = vecBits g.LinearPredictor

            [ 0 .. n - 1 ]
            |> List.map (fun i ->
                let m1 = Glm.meanDerivative link eta[i]
                let v = Glm.variance family mu[i]
                let expected = wa[i] * (m1 * m1 / v)

                let m2, dv =
                    match link with
                    | GlmLink.Probit -> -eta[i] * m1, 1.0 - 2.0 * mu[i]
                    | _ -> canonicalCurvature family eta[i] mu[i]

                let correction = wa[i] * (ya[i] - mu[i]) * (m2 / v - m1 * m1 * dv / (v * v))
                abs correction, abs expected)

        let canonical =
            pairs
            |> List.tryPick (fun (family, link, y) ->
                match Glm.fit family link GlmControl.defaults x y w o with
                | Error(LinAlgError.MeanOutOfRange _) -> None
                | Error e -> Some(sprintf "%A %A refused: %A" family link e)
                | Ok g ->
                    gap family link y g
                    |> List.tryPick (fun (c, e) ->
                        check (c <= 1e-9 * (e + 1e-300) + 1e-12) (fun () ->
                            sprintf "%A %A: a correction of %.17g beside an information of %.17g" family link c e)))

        canonical
        |> Option.orElse (
            let bx, by, bw = binaryDesign d

            match Glm.fit GlmFamily.Binomial GlmLink.Probit GlmControl.defaults bx by bw (zeros bx.Rows) with
            | Error e -> Some(sprintf "probit refused: %A" e)
            | Ok g ->
                let ya = Vector.toArray by
                let mu = vecBits g.Fitted
                let eta = vecBits g.LinearPredictor
                let bwa = Vector.toArray bw

                let largest =
                    [ 0 .. bx.Rows - 1 ]
                    |> List.map (fun i ->
                        let m1 = Glm.meanDerivative GlmLink.Probit eta[i]
                        let v = mu[i] * (1.0 - mu[i])
                        let m2 = -eta[i] * m1
                        abs (bwa[i] * (ya[i] - mu[i]) * (m2 / v - m1 * m1 * (1.0 - 2.0 * mu[i]) / (v * v))))
                    |> List.max

                check (largest > 1e-6) (fun () -> sprintf "the probit correction is only %.17g" largest)
        )

    /// Binomial under the logit link with an intercept meets its score equations at convergence: with the
    /// intercept, `Σ w·μ̂ = Σ w·y`, within the stopping rule's order of the total weight.
    let private logitMeansMatch (d: Draw) =
        let x, y, w = binaryDesign d

        match Glm.fit GlmFamily.Binomial GlmLink.Logit GlmControl.defaults x y w (zeros x.Rows) with
        | Error e -> Some(sprintf "refused: %A" e)
        | Ok g ->
            let mu = vecBits g.Fitted
            let ya, wa = Vector.toArray y, Vector.toArray w
            let mutable sm = 0.0
            let mutable sy = 0.0
            let mutable sw = 0.0

            for i in 0 .. x.Rows - 1 do
                sm <- sm + wa[i] * mu[i]
                sy <- sy + wa[i] * ya[i]
                sw <- sw + wa[i]

            check (abs (sm - sy) <= 1e-6 * sw) (fun () -> sprintf "Σwμ = %.17g against Σwy = %.17g" sm sy)

    /// The negative binomial tends to Poisson as θ grows: at `θ = 10¹⁰` its coefficients are Poisson's
    /// within `10⁻⁶` of their scale.
    let private negativeBinomialTendsToPoisson (d: Draw) =
        let x, w, o, k = glmDesign d
        let y = counts d x.Rows k

        match
            Glm.fit GlmFamily.Poisson GlmLink.Log GlmControl.defaults x y w o,
            Glm.fit (GlmFamily.NegativeBinomial(Some 1e10)) GlmLink.Log GlmControl.defaults x y w o
        with
        | Ok p, Ok nb ->
            List.zip p.Coefficients nb.Coefficients
            |> List.tryPick (fun (a, b) ->
                let a, b = Option.defaultValue 0.0 a, Option.defaultValue 0.0 b
                check (abs (a - b) <= 1e-6 * (1.0 + abs a)) (fun () -> sprintf "Poisson %.17g against %.17g" a b))
        | a, b -> Some(sprintf "refused: %A, %A" a b)

    /// Logit and probit coefficients on one 0/1 outcome keep the textbook ratio: where the probit
    /// estimate is at least twice its standard error, the logit one is between `1.45` and `1.95` times it
    /// (about `1.6` near `μ = ½`, `π/√3 ≈ 1.81` matching the variances).
    let private logitProbitRatio (d: Draw) =
        let x, y, w = binaryDesign d
        let o = zeros x.Rows

        match
            Glm.fit GlmFamily.Binomial GlmLink.Logit GlmControl.defaults x y w o,
            Glm.fit GlmFamily.Binomial GlmLink.Probit GlmControl.defaults x y w o
        with
        | Ok l, Ok p ->
            List.zip3 l.Coefficients p.Coefficients p.StdErrors
            |> List.tryPick (fun (a, b, se) ->
                match a, b, se with
                | Some a, Some b, Some se when abs b >= 2.0 * se ->
                    let r = a / b
                    check (r >= 1.45 && r <= 1.95) (fun () -> sprintf "logit %.17g over probit %.17g is %.17g" a b r)
                | _ -> None)
        | a, b -> Some(sprintf "refused: %A, %A" a b)

    /// Every Phase 600 refusal is answered as stated: a response outside the binomials, inverse
    /// Gaussian or an estimated negative binomial; a θ that is not finite and positive; a pair not
    /// admitted; a Poisson identity mean below zero; a separated binomial; a θ with no estimate; and
    /// the null deviance of an estimated negative binomial.
    let private generalRefusals (d: Draw) =
        let x, y, w = binaryDesign d
        let n = x.Rows
        let o = zeros n
        let at = d.Int 0 (n - 1)
        let c = GlmControl.defaults

        let withAt (v: Vector<float>) (value: float) =
            Vector.adopt (Vector.toArray v |> Array.mapi (fun i e -> if i = at then value else e))

        let fit family link y = Glm.fit family link c x y w o
        let pos = positives d n
        let cnt = Vector.adopt (Array.init n (fun i -> float (i % 5)))

        // The outcome is one exactly where the first covariate is positive: a separating plane.
        let separable =
            Vector.adopt (Array.init n (fun i -> if x[i, 1] > 0.0 then 1.0 else 0.0))

        // Counts rising steeply along one covariate: least squares from y + 0.1 crosses zero.
        let xl = Matrix.init 8 2 (fun i j -> if j = 0 then 1.0 else float (i + 1))
        let steep = Vector.adopt [| 0.0; 0.0; 0.0; 0.0; 0.0; 0.0; 40.0; 90.0 |]
        let ones8 = Vector.adopt (Array.create 8 1.0)

        // Equal counts: no overdispersion at all, so the moment start for θ is infinite.
        let flat = Vector.adopt (Array.create n 3.0)

        let expected: (string * bool) list =
            [ "binomial above one",
              fit GlmFamily.Binomial GlmLink.Logit (withAt y 1.5) = Error(LinAlgError.InvalidResponse at)
              "binomial negative",
              fit GlmFamily.QuasiBinomial GlmLink.Probit (withAt y -0.25) = Error(LinAlgError.InvalidResponse at)
              "inverse Gaussian zero",
              fit GlmFamily.InverseGaussian GlmLink.InverseSquare (withAt pos 0.0) =
                  Error(LinAlgError.InvalidResponse at)
              "negative binomial fraction",
              fit (GlmFamily.NegativeBinomial None) GlmLink.Log (withAt cnt 1.5) = Error(LinAlgError.InvalidResponse at)
              "negative binomial fixed fraction admitted",
              (match fit (GlmFamily.NegativeBinomial(Some 2.0)) GlmLink.Log (withAt cnt 1.5) with
               | Error(LinAlgError.InvalidResponse _) -> false
               | _ -> true)
              "theta zero",
              fit (GlmFamily.NegativeBinomial(Some 0.0)) GlmLink.Log cnt = Error(LinAlgError.InvalidTheta 0.0)
              "theta nan",
              (match fit (GlmFamily.NegativeBinomial(Some nan)) GlmLink.Log cnt with
               | Error(LinAlgError.InvalidTheta t) -> Double.IsNaN t
               | _ -> false)
              "binomial log",
              fit GlmFamily.Binomial GlmLink.Log y = Error(LinAlgError.UnsupportedModel("binomial", "log"))
              "tweedie inverse",
              fit (GlmFamily.Tweedie 1.5) GlmLink.Inverse pos =
                  Error(LinAlgError.UnsupportedModel("tweedie", "inverse"))
              "gaussian log non-positive",
              fit GlmFamily.Gaussian GlmLink.Log (withAt pos 0.0) = Error(LinAlgError.InvalidResponse at)
              "poisson identity below zero",
              (match Glm.fit GlmFamily.Poisson GlmLink.Identity c xl steep ones8 (zeros 8) with
               | Error(LinAlgError.MeanOutOfRange _) -> true
               | _ -> false)
              "separation",
              (match fit GlmFamily.Binomial GlmLink.Logit separable with
               | Error(LinAlgError.Separation _) -> true
               | _ -> false)
              "theta flat",
              fit (GlmFamily.NegativeBinomial None) GlmLink.Log flat = Error(LinAlgError.ThetaNotConverged 0)
              "null estimated theta",
              (match Glm.nullDeviance (GlmFamily.NegativeBinomial None) GlmLink.Log c true cnt w o with
               | Error(LinAlgError.InvalidTheta t) -> Double.IsNaN t
               | _ -> false) ]

        match expected |> List.filter (snd >> not) with
        | [] -> None
        | failed ->
            Some(
                sprintf "general GLM refusals not answered as stated: %s" (failed |> List.map fst |> String.concat ", ")
            )

    /// Each link's `g`, `g⁻¹` and `dμ/dη` agree: `g⁻¹(g(μ))` returns `μ` within `10⁻¹²` of its scale over
    /// each link's domain, and `dμ/dη` is the central difference of `g⁻¹` within `10⁻⁵` of its scale.
    let private linksInvert (d: Draw) =
        let unit = 0.02 + 0.96 * (0.5 + 0.5 * d.Unit())
        let posv = 0.1 + 4.0 * (0.5 + 0.5 * d.Unit())

        [ GlmLink.Identity, posv
          GlmLink.Log, posv
          GlmLink.Logit, unit
          GlmLink.Probit, unit
          GlmLink.Cloglog, unit
          GlmLink.Inverse, posv
          GlmLink.InverseSquare, posv
          GlmLink.Sqrt, posv ]
        |> List.tryPick (fun (link, mu) ->
            let eta = Glm.linkFunction link mu
            let back = Glm.inverseLink link eta
            let h = 1e-6 * (1.0 + abs eta)

            let central =
                (Glm.inverseLink link (eta + h) - Glm.inverseLink link (eta - h)) / (2.0 * h)

            let deriv = Glm.meanDerivative link eta

            check
                (abs (back - mu) <= 1e-12 * (1.0 + abs mu)
                 && abs (central - deriv) <= 1e-5 * (1.0 + abs deriv))
                (fun () -> sprintf "%A at μ = %.17g: back %.17g, dμ/dη %.17g against %.17g" link mu back deriv central))

    /// The laws, by name, each run at `count` iterations drawn from `seed`.
    let laws (seed: int) (count: int) : (string * Result<unit, string>) list =
        [ "the accumulator is its spelling through Givens.make and Givens.apply, byte for byte", accumulatorIsSpelled
          "the accumulator over the native rotation is the portable one, byte for byte", nativeAccumulatorIsPortable
          "addRows is addRow row by row, refusals included, byte for byte", addRowsIsRepeatedAddRow
          "appended rows answer a fresh fold of the whole set, byte for byte", appendIsFresh
          "an accumulator through the codec continues as one that never left memory, byte for byte", codecContinues
          "a malformed accumulator document is refused", codecRefusals
          "R is upper triangular with RᵀR = XᵀWX within its bound", rIsTriangularAndGram
          "the fit satisfies the normal equations within their bound", normalEquations
          "the Householder R is the streaming R within its bound", householderAgrees
          "a copied column is aliased, and the rest is the fit without it", aliasing
          "R⁻¹ inverts R and Qᵀy solves to the coefficients", inverseAndEffects
          "every QR refusal is answered as stated", qrRefusals
          "Gaussian under Identity through Glm.fit is the least-squares fit, byte for byte", gaussianIsLeastSquares
          "a Poisson log-link fit meets its score equations at convergence", poissonScore
          "an offset of c is the intercept shifted by c", offsetShiftsIntercept
          "Gamma working weights are the prior weights, exactly, on every iteration", gammaWeightsArePrior
          "every Glm refusal is answered as stated", glmRefusals
          "quasi-Poisson iterates as Poisson, byte for byte, its dispersion Pearson's", quasiPoissonIsPoisson
          "Tweedie at p = 1 agrees with quasi-Poisson, and at p = 2 is Gamma byte for byte", tweedieEndpoints
          "HC0 on homoskedastic rows approaches the classical errors, and HC0 <= HC2 <= HC3", sandwichHomoskedastic
          "every Tweedie and sandwich refusal is answered as stated", robustAndTweedieRefusals
          "under a canonical link Fisher scoring is Newton's method at convergence", canonicalFisherIsNewton
          "binomial under the logit link with an intercept fits Σwμ = Σwy", logitMeansMatch
          "the negative binomial tends to Poisson as θ grows", negativeBinomialTendsToPoisson
          "logit and probit coefficients keep their textbook ratio", logitProbitRatio
          "every general-GLM refusal is answered as stated", generalRefusals
          "every link's g, inverse and derivative agree", linksInvert ]
        |> List.map (fun (name, body) -> name, law seed count body)
