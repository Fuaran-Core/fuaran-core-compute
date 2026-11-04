namespace Fuaran.Compute.LinearAlgebra

open System
open Fuaran.Core

/// `linearAlgebraLaws` (Phase 590): the laws this package's operations are held to, on whichever host
/// runs them. Two kinds. BYTE laws hold a pinned operation to a reference spelling of the same roundings
/// in the same order — `gemv` to the dot of each row, `gemm` to the dot of each row and column, the
/// native column update to the portable one, an exact rescaling of `norm2` — and to inputs on which a
/// fused multiply-add would answer differently, so a contraction anywhere is observable. ALGEBRAIC laws
/// hold what rounding can only approximate — `gemm`'s associativity, a solve's residual, a rotation's
/// zero — within the standard backward-error bound for the operation's length.
///
/// Every input is drawn by a pinned generator whose every step is an exact float operation, so a seed
/// draws the same matrices on every host, and running the laws under Fable runs them over the same
/// numbers as on .NET. A law answers `Ok ()` when it held at every iteration, else the first failure
/// with its iteration. FSharp.Core and Fuaran.Core.Column only, so a consumer runs it without the
/// conformance kit; the `Result<unit, string>` verdict is the substrate codec laws' own
/// (`Corpus.codecLaws`).
[<RequireQualifiedAccess>]
module LinearAlgebraLaws =

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

    /// `2ᵏ`, by exact doublings or halvings — no host `pow` is read.
    let private pow2 (k: int) : float =
        let mutable p = 1.0

        for _ in 1 .. abs k do
            p <- if k > 0 then p * 2.0 else p * 0.5

        p

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

    // ---- byte laws -----------------------------------------------------------------------------

    let private transposeInvolution (d: Draw) =
        let a = matrix d (d.Int 0 6) (d.Int 0 6)
        let t = Matrix.transpose a

        let moved =
            [ for i in 0 .. a.Rows - 1 do
                  for j in 0 .. a.Cols - 1 -> bits a[i, j] = bits t[j, i] ]
            |> List.forall id

        check
            (moved
             && sameBits (Matrix.toColumnMajor a) (Matrix.toColumnMajor (Matrix.transpose t)))
            (fun () -> sprintf "transpose moved an element or did not invert at %dx%d" a.Rows a.Cols)

    let private gemvIsRowDots (d: Draw) =
        let a = matrix d (d.Int 0 9) (d.Int 0 9)
        let x = vector d a.Cols
        let y = Blas.gemv a x |> ok |> vecBits
        let reference = Array.init a.Rows (fun i -> Blas.dot (row a i) x |> ok)
        check (sameBits y reference) (fun () -> sprintf "gemv differs from the row dots at %dx%d" a.Rows a.Cols)

    let private nativeIsPortable (d: Draw) =
        // Rows up to 37 cross every SIMD width a .NET host offers and leave a tail behind each.
        let rows, cols = d.Int 0 37, d.Int 0 5
        let a = Matrix.toColumnMajor (matrix d rows cols)
        let x = Vector.toArray (vector d cols)
        let native = Kernels.gemv a 0 rows cols x 0
        let portable = Kernels.gemvWith Kernels.axpyColumnPortable a 0 rows cols x 0

        check (sameBits native portable) (fun () ->
            sprintf "the native column update differs from the portable one at %dx%d" rows cols)

    let private gemmIsDots (d: Draw) =
        let a = matrix d (d.Int 0 6) (d.Int 0 6)
        let b = matrix d a.Cols (d.Int 0 6)
        let c = Blas.gemm a b |> ok

        let reference =
            Array.init (a.Rows * b.Cols) (fun k ->
                let i, j = k % a.Rows, k / a.Rows
                Blas.dot (row a i) (Matrix.column j b) |> ok)

        let byColumn =
            Array.concat [ for j in 0 .. b.Cols - 1 -> Blas.gemv a (Matrix.column j b) |> ok |> vecBits ]

        check
            (sameBits (Matrix.toColumnMajor c) reference
             && sameBits (Matrix.toColumnMajor c) byColumn)
            (fun () -> sprintf "gemm differs from its k-order dots at %dx%d by %dx%d" a.Rows a.Cols b.Rows b.Cols)

    let private gramIsGemm (d: Draw) =
        let a = matrix d (d.Int 0 7) (d.Int 0 5)
        let g = Blas.gram a
        let reference = Blas.gemm (Matrix.transpose a) a |> ok

        check
            (sameBits (Matrix.toColumnMajor g) (Matrix.toColumnMajor reference)
             && sameBits (Matrix.toColumnMajor g) (Matrix.toColumnMajor (Matrix.transpose g)))
            (fun () -> sprintf "gram is not gemm (Aᵀ, A), or not symmetric, at %dx%d" a.Rows a.Cols)

    /// Inputs on which `a·b + c` rounded twice is `+0.0` and fused once is `2⁻⁶⁰` (times a drawn power of
    /// two): `a = b = 1 + 2⁻³⁰`, whose product `1 + 2⁻²⁹ + 2⁻⁶⁰` rounds to `1 + 2⁻²⁹`, and `c` its negation.
    let private noContraction (d: Draw) =
        let k = d.Int (-20) 20
        let scale = pow2 k
        let a = 1.0 + 1.0 / 1073741824.0
        let c = -(1.0 + 1.0 / 536870912.0) * scale
        let sa = a * scale
        let x = Vector.ofList [ 1.0; sa ]
        let y = Vector.ofList [ c; a ]
        let dot = Blas.dot x y |> ok

        let gemv =
            Blas.gemv (Matrix.init 1 2 (fun _ j -> if j = 0 then 1.0 else sa)) y |> ok

        let gemm =
            Blas.gemm
                (Matrix.init 1 2 (fun _ j -> if j = 0 then 1.0 else sa))
                (Matrix.init 2 1 (fun i _ -> if i = 0 then c else a))
            |> ok

        let axpy = Blas.axpy sa (Vector.ofList [ a ]) (Vector.ofList [ c ]) |> ok
        let zero = bits 0.0

        check
            (bits dot = zero
             && bits gemv[0] = zero
             && bits gemm[0, 0] = zero
             && bits axpy[0] = zero)
            (fun () ->
                sprintf
                    "a product and a sum were fused at scale 2^%d: dot %g, gemv %g, gemm %g, axpy %g"
                    k
                    dot
                    gemv[0]
                    gemm[0, 0]
                    axpy[0])

    let private norm2ScalesExactly (d: Draw) =
        let x = vector d (d.Int 1 9)
        let k = d.Int (-8) 8
        let p = pow2 k
        let scaled = Blas.norm2 (Blas.scale p x)

        check (bits scaled = bits (p * Blas.norm2 x)) (fun () ->
            sprintf "norm2 did not scale exactly by 2^%d over %d elements" k x.Length)

    let private levelOneIdentities (d: Draw) =
        let n = d.Int 0 9
        let x, y = vector d n, vector d n
        let a = d.Unit()
        let xy = Blas.add x y |> ok |> vecBits
        let yx = Blas.add y x |> ok |> vecBits
        let xx = Blas.sub x x |> ok |> vecBits
        let axpy = Blas.axpy a x y |> ok |> vecBits
        let spelled = Blas.add (Blas.scale a x) y |> ok |> vecBits

        check
            (sameBits xy yx
             && Array.forall (fun v -> bits v = bits 0.0) xx
             && sameBits (vecBits (Blas.scale 1.0 x)) (vecBits x)
             && sameBits axpy spelled)
            (fun () -> sprintf "a level-1 identity failed over %d elements" n)

    // ---- algebraic laws ------------------------------------------------------------------------

    let private gemmAssociative (d: Draw) =
        let a = matrix d (d.Int 1 5) (d.Int 1 5)
        let b = matrix d a.Cols (d.Int 1 5)
        let c = matrix d b.Cols (d.Int 1 5)
        let left = Blas.gemm (Blas.gemm a b |> ok) c |> ok
        let right = Blas.gemm a (Blas.gemm b c |> ok) |> ok
        let bound = Blas.gemm (absM a) (Blas.gemm (absM b) (absM c) |> ok) |> ok
        let gamma = 2.0 * float (a.Cols + b.Cols + 2) * u

        let within =
            [ for i in 0 .. left.Rows - 1 do
                  for j in 0 .. left.Cols - 1 -> abs (left[i, j] - right[i, j]) <= gamma * bound[i, j] ]
            |> List.forall id

        check within (fun () -> sprintf "gemm is not associative within its bound at inner sizes %d, %d" a.Cols b.Cols)

    let private solveResiduals (d: Draw) =
        let n = d.Int 1 8
        let r = triangle d n
        let b = vector d n
        let rm = UpperTriangular.toMatrix r
        let gamma = float (2 * n + 4) * u

        let residualHolds (m: Matrix) (x: Vector<float>) =
            let rx = Blas.gemv m x |> ok
            let scale = Blas.gemv (absM m) (Vector.map abs x) |> ok

            [ for i in 0 .. n - 1 -> abs (rx[i] - b[i]) <= gamma * (scale[i] + abs b[i]) ]
            |> List.forall id

        let x = UpperTriangular.solveUpper r b |> ok
        let xt = UpperTriangular.solveUpperT r b |> ok

        check (residualHolds rm x && residualHolds (Matrix.transpose rm) xt) (fun () ->
            sprintf "a triangular solve's residual exceeds its bound at order %d" n)

    let private givensZeroes (d: Draw) =
        let a, b = d.Unit(), d.Unit()
        let g = Givens.make a b
        let x, y = Givens.apply g a b
        let r = g.Radius
        let identity = Givens.make a 0.0
        let ix, iy = Givens.apply identity (-0.0) Double.PositiveInfinity

        check
            (r > 0.0
             && not g.IsIdentity
             && abs (x - r) <= 8.0 * u * r
             && abs y <= 8.0 * u * r
             && identity.IsIdentity
             && bits identity.Radius = bits a
             && bits ix = bits (-0.0)
             && Double.IsPositiveInfinity iy)
            (fun () -> sprintf "the rotation of (%g, %g) did not zero its pair with a positive radius" a b)

    let private norm2NearNaive (d: Draw) =
        let x = vector d (d.Int 1 12)
        let naive = sqrt (Blas.dot x x |> ok)
        let n = Blas.norm2 x

        check (abs (n - naive) <= float (2 * x.Length + 6) * u * naive) (fun () ->
            sprintf "norm2 is %g where the plain root of the sum of squares is %g" n naive)

    // ---- refusals and NaN ----------------------------------------------------------------------

    let private refusals (d: Draw) =
        let n = d.Int 1 5
        let x, y = vector d n, vector d (n + 1)
        let a = matrix d n (n + 1)
        let sq = matrix d n n
        let vd (v: Vector<float>) = { Rows = v.Length; Cols = 1 }
        let zeroAt = d.Int 0 (n - 1)

        let singular =
            Matrix.init n n (fun i j ->
                if i > j then 0.0
                elif i = j && i >= zeroAt then 0.0
                else 1.0)
            |> UpperTriangular.ofMatrix
            |> ok

        let notFinite =
            Matrix.init n n (fun i j ->
                if i = n - 1 && j = n - 1 then
                    Double.PositiveInfinity
                else
                    0.0)

        let lower =
            Matrix.init n n (fun i j -> if i = n - 1 && j = 0 && n > 1 then -0.0 else 0.0)

        let below =
            Matrix.init (n + 1) (n + 1) (fun i j -> if i = n && j = n - 1 then 1.0 else 0.0)

        let expected: (string * bool) list =
            [ "dot", Blas.dot x y = Error(LinAlgError.ShapeMismatch("Blas.dot", vd x, vd y))
              "add", Blas.add x y = Error(LinAlgError.ShapeMismatch("Blas.add", vd x, vd y))
              "gemv", Blas.gemv a x = Error(LinAlgError.ShapeMismatch("Blas.gemv", a.Dims, vd x))
              "gemm", Blas.gemm a a = Error(LinAlgError.ShapeMismatch("Blas.gemm", a.Dims, a.Dims))
              "ofColumns", Matrix.ofColumns [ x; y ] = Error(LinAlgError.ShapeMismatch("Matrix.ofColumns", vd x, vd y))
              "ofMatrix shape",
              UpperTriangular.ofMatrix a =
                  Error(LinAlgError.ShapeMismatch("UpperTriangular.ofMatrix", a.Dims, { Rows = n + 1; Cols = n + 1 }))
              "ofMatrix finite", UpperTriangular.ofMatrix notFinite = Error(LinAlgError.NotFinite(n - 1, n - 1))
              // A negative zero below the diagonal is a zero.
              "ofMatrix -0", Result.isOk (UpperTriangular.ofMatrix lower)
              "ofMatrix triangular", UpperTriangular.ofMatrix below = Error(LinAlgError.NotTriangular(n, n - 1))
              "solveUpper singular", UpperTriangular.solveUpper singular x = Error(LinAlgError.Singular zeroAt)
              "solveUpperT singular", UpperTriangular.solveUpperT singular x = Error(LinAlgError.Singular zeroAt)
              "solveUpper shape",
              UpperTriangular.solveUpper (triangle d n) y =
                  Error(LinAlgError.ShapeMismatch("UpperTriangular.solveUpper", sq.Dims, vd y)) ]

        match expected |> List.filter (snd >> not) with
        | [] -> None
        | failed -> Some(sprintf "refusals not answered as stated: %s" (failed |> List.map fst |> String.concat ", "))

    let private canonicalNaN (d: Draw) =
        let n = d.Int 1 6
        let at = d.Int 0 (n - 1)
        // A NaN carrying the sign bit opposite to the canonical one's: `-NaN` flips it on every host.
        let odd = -Double.NaN
        let x = Vector.adopt (Array.init n (fun i -> if i = at then odd else d.Unit()))
        let a = matrix d 3 n
        let canon = bits Double.NaN
        let isCanon (v: float) = bits v = canon
        let gemv = Blas.gemv a x |> ok
        let scaled = Blas.scale 2.0 x
        let stored = Matrix.ofColumns [ x ] |> ok
        let rotation = Givens.make 1.0 odd

        check
            (Array.forall isCanon (vecBits gemv)
             && isCanon (Blas.dot x x |> ok)
             && isCanon (Blas.norm2 x)
             && isCanon scaled[at]
             && isCanon stored[at, 0]
             && isCanon rotation.Radius)
            (fun () -> "a NaN left an operation with bits other than Double.NaN's")

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

    let private norm2Pair (d: Draw) =
        let special =
            [| 0.0
               -0.0
               Double.Epsilon
               1e-300
               1e300
               Double.MaxValue
               Double.PositiveInfinity
               Double.NaN |]

        let pick () =
            if d.Unit() < 0.0 then
                special[d.Int 0 (special.Length - 1)] * (if d.Unit() < 0.0 then -1.0 else 1.0)
            else
                d.Unit() * pow2 (d.Int (-40) 40)

        let a, b = pick (), pick ()
        let pair = Kernels.norm2Pair a b
        let spelled = Kernels.norm2 [| a; b |] 0 2

        check (bits (Kernels.canonical pair) = bits (Kernels.canonical spelled)) (fun () ->
            sprintf "the pair norm of (%g, %g) is %g where norm2 answers %g" a b pair spelled)

    let private nativeRotationIsPortable (d: Draw) =
        // Lengths up to 37 cross every SIMD width a .NET host offers and leave a tail behind each.
        let n = d.Int 0 37
        let g = Givens.make (d.Unit()) (d.Unit())
        let u = Array.init (n + 3) (fun _ -> d.Unit())
        let v = Array.init (n + 3) (fun _ -> d.Unit())
        let uo, vo = d.Int 0 3, d.Int 0 3
        let un, vn = Array.copy u, Array.copy v
        Kernels.rotatePortable g.Cos g.Sin u uo v vo n
        Kernels.rotateNative g.Cos g.Sin un uo vn vo n

        check (sameBits u un && sameBits v vn) (fun () ->
            sprintf "the native rotation differs from the portable one over %d pairs" n)

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
        let text = LinAlgWire.encodeAccumulator primed
        let restored = LinAlgWire.decodeAccumulator text

        let continued (acc: QRAccumulator) =
            acc |> QRAccumulator.addRows xb yb wb |> ok

        let held =
            match restored with
            | Error _ -> false
            | Ok back ->
                LinAlgWire.encodeAccumulator back = text
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

    let private matrixCodecRoundTrips (d: Draw) =
        let special =
            [| -0.0
               Double.Epsilon
               -Double.MaxValue
               Double.PositiveInfinity
               Double.NegativeInfinity
               nan |]

        let a =
            Matrix.init (d.Int 0 5) (d.Int 0 5) (fun _ _ ->
                if d.Unit() < -0.5 then
                    special[d.Int 0 (special.Length - 1)]
                else
                    d.Unit())

        let text = LinAlgWire.encodeMatrix a

        let held =
            match LinAlgWire.decodeMatrix text with
            | Ok back ->
                sameBits (Matrix.toColumnMajor back) (Matrix.toColumnMajor a)
                && back.Dims = a.Dims
                && LinAlgWire.encodeMatrix back = text
            | Error _ -> false

        check held (fun () -> sprintf "a %dx%d matrix did not round-trip through its codec bit for bit" a.Rows a.Cols)

    let private codecRefusals (d: Draw) =
        let p = d.Int 1 4
        let x, y, w = weightedRows d (p + 3) p
        let acc = QRAccumulator.empty p |> QRAccumulator.addRows x y w |> ok
        let text = LinAlgWire.encodeAccumulator acc

        let refused (t: string) =
            Result.isError (LinAlgWire.decodeAccumulator t)
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
             && Result.isOk (LinAlgWire.decodeAccumulator text))
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

    /// The laws, by name, each run at `count` iterations drawn from `seed`.
    let laws (seed: int) (count: int) : (string * Result<unit, string>) list =
        [ "transpose is an involution, byte for byte", transposeInvolution
          "gemv is the dot of each row with x, byte for byte", gemvIsRowDots
          "the native column update is the portable one, byte for byte", nativeIsPortable
          "gemm is the k-order dot of each row and column, byte for byte", gemmIsDots
          "gram is gemm of the transpose and is symmetric, byte for byte", gramIsGemm
          "no fused multiply-add is observable", noContraction
          "norm2 scales exactly by a power of two", norm2ScalesExactly
          "the level-1 identities hold byte for byte", levelOneIdentities
          "gemm is associative within its rounding bound", gemmAssociative
          "a triangular solve's residual is within its backward-error bound", solveResiduals
          "a Givens rotation zeroes its pair with a positive radius", givensZeroes
          "norm2 is the root of the sum of squares within its bound", norm2NearNaive
          "every refusal is answered as stated", refusals
          "a NaN leaves as Double.NaN's bits", canonicalNaN
          "the pair norm is norm2 of the pair, byte for byte", norm2Pair
          "the native rotation is the portable one, byte for byte", nativeRotationIsPortable
          "the accumulator is its spelling through Givens.make and Givens.apply, byte for byte", accumulatorIsSpelled
          "the accumulator over the native rotation is the portable one, byte for byte", nativeAccumulatorIsPortable
          "addRows is addRow row by row, refusals included, byte for byte", addRowsIsRepeatedAddRow
          "appended rows answer a fresh fold of the whole set, byte for byte", appendIsFresh
          "an accumulator through the codec continues as one that never left memory, byte for byte", codecContinues
          "a matrix round-trips through its codec, byte for byte", matrixCodecRoundTrips
          "a malformed accumulator document is refused", codecRefusals
          "R is upper triangular with RᵀR = XᵀWX within its bound", rIsTriangularAndGram
          "the fit satisfies the normal equations within their bound", normalEquations
          "the Householder R is the streaming R within its bound", householderAgrees
          "a copied column is aliased, and the rest is the fit without it", aliasing
          "R⁻¹ inverts R and Qᵀy solves to the coefficients", inverseAndEffects
          "every QR refusal is answered as stated", qrRefusals ]
        |> List.map (fun (name, body) -> name, law seed count body)
