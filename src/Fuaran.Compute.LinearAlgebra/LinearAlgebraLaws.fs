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

    // ---- Phase 593: the pinned elementary functions ------------------------------------------------

    let private ordered (x: float) : int64 =
        let b = bits x
        if b < 0L then Int64.MinValue - b else b

    /// The distance between two floats in units in the last place of the binary64 grid.
    let private ulpsApart (a: float) (b: float) : int64 = abs (ordered a - ordered b)

    let private canonicalNaNBits (x: float) : bool = bits x = bits Double.NaN

    /// A positive float of magnitude `2^e · (1 + u)` for a drawn `e` in `lo .. hi`.
    let private positive (d: Draw) (lo: int) (hi: int) : float =
        pow2 (d.Int lo hi) * (1.0 + (d.Unit() + 1.0) * 0.5)

    /// D43's table rows and IEEE-754's edge rules, each answered as stated (DECISIONS.md D52 item 3).
    let private elementaryTables (_: Draw) =
        let inf = Double.PositiveInfinity
        let nan = Double.NaN
        let sameBits (a: float) (b: float) = bits a = bits b

        let expected =
            [ "expTable NaN", Elementary.expTable nan = SpecialValue.NaN
              "expTable above the overflow edge", Elementary.expTable 709.7827128933841 = SpecialValue.Value inf
              "expTable at the overflow edge", Elementary.expTable 709.782712893384 = SpecialValue.Compute
              "expTable below the underflow edge", Elementary.expTable -745.14 = SpecialValue.Value 0.0
              "expTable at the underflow edge", Elementary.expTable -745.1332191019412 = SpecialValue.Compute
              "expTable Inf", Elementary.expTable inf = SpecialValue.Value inf
              "expTable -Inf", Elementary.expTable -inf = SpecialValue.Value 0.0
              "exp NaN is the canonical NaN", canonicalNaNBits (Elementary.exp nan)
              "exp 1000 is Inf", Elementary.exp 1000.0 = inf
              "exp -1000 is +0", sameBits (Elementary.exp -1000.0) 0.0
              "exp 0 is 1", sameBits (Elementary.exp 0.0) 1.0
              "exp -0 is 1", sameBits (Elementary.exp -0.0) 1.0
              "exp at the edge is finite", Elementary.exp 709.782712893384 < inf
              "lnTable NaN", Elementary.lnTable nan = SpecialValue.NaN
              "lnTable 0", Elementary.lnTable 0.0 = SpecialValue.Undefined
              "lnTable -0", Elementary.lnTable -0.0 = SpecialValue.Undefined
              "lnTable negative", Elementary.lnTable -1.0 = SpecialValue.Undefined
              "lnTable 1", Elementary.lnTable 1.0 = SpecialValue.Value 0.0
              "lnTable Inf", Elementary.lnTable inf = SpecialValue.Value inf
              "lnTable 2", Elementary.lnTable 2.0 = SpecialValue.Compute
              "lnTable the least subnormal", Elementary.lnTable Double.Epsilon = SpecialValue.Compute
              "ln 0 is the canonical NaN", canonicalNaNBits (Elementary.ln 0.0)
              "ln negative is the canonical NaN", canonicalNaNBits (Elementary.ln -2.5)
              "ln 1 is +0", sameBits (Elementary.ln 1.0) 0.0
              "ln Inf is Inf", Elementary.ln inf = inf
              "log10 0 is the canonical NaN", canonicalNaNBits (Elementary.log10 0.0)
              "log10 1 is +0", sameBits (Elementary.log10 1.0) 0.0
              "log10 NaN is the canonical NaN", canonicalNaNBits (Elementary.log10 nan)
              "powTable (1, NaN)", Elementary.powTable 1.0 nan = SpecialValue.NaN
              "powTable (NaN, 0)", Elementary.powTable nan 0.0 = SpecialValue.NaN
              "powTable (0, 0)", Elementary.powTable 0.0 0.0 = SpecialValue.Value 1.0
              "powTable (x, 0)", Elementary.powTable -5.5 0.0 = SpecialValue.Value 1.0
              "powTable (Inf, 0)", Elementary.powTable inf 0.0 = SpecialValue.Value 1.0
              "powTable (1, y)", Elementary.powTable 1.0 7.5 = SpecialValue.Value 1.0
              "powTable (1, Inf)", Elementary.powTable 1.0 inf = SpecialValue.Value 1.0
              "powTable (0, negative)", Elementary.powTable 0.0 -1.0 = SpecialValue.Undefined
              "powTable (-0, negative)", Elementary.powTable -0.0 -0.5 = SpecialValue.Undefined
              "powTable (-1, Inf)", Elementary.powTable -1.0 inf = SpecialValue.Value 1.0
              "powTable (-1, -Inf)", Elementary.powTable -1.0 -inf = SpecialValue.Value 1.0
              "powTable negative base, non-integer exponent",
              Elementary.powTable -8.0 (1.0 / 3.0) = SpecialValue.Undefined
              "powTable negative base, integer exponent", Elementary.powTable -8.0 3.0 = SpecialValue.Compute
              "powTable -Inf base, non-integer exponent", Elementary.powTable -inf 0.5 = SpecialValue.Compute
              "powTable (2, 0.5)", Elementary.powTable 2.0 0.5 = SpecialValue.Compute
              "pow (0, -1) is the canonical NaN", canonicalNaNBits (Elementary.pow 0.0 -1.0)
              "pow (-8, 1/3) is the canonical NaN", canonicalNaNBits (Elementary.pow -8.0 (1.0 / 3.0))
              "pow (1, NaN) is the canonical NaN", canonicalNaNBits (Elementary.pow 1.0 nan)
              "pow (-2, 3) is -8", sameBits (Elementary.pow -2.0 3.0) -8.0
              "pow (-2, 2) is 4", sameBits (Elementary.pow -2.0 2.0) 4.0
              "pow (-0, 3) is -0", sameBits (Elementary.pow -0.0 3.0) -0.0
              "pow (-0, 2) is +0", sameBits (Elementary.pow -0.0 2.0) 0.0
              "pow (0, 3) is +0", sameBits (Elementary.pow 0.0 3.0) 0.0
              "pow (Inf, -2) is +0", sameBits (Elementary.pow inf -2.0) 0.0
              "pow (Inf, 2) is Inf", Elementary.pow inf 2.0 = inf
              "pow (-Inf, 3) is -Inf", Elementary.pow -inf 3.0 = -inf
              "pow (-Inf, 2) is Inf", Elementary.pow -inf 2.0 = inf
              "pow (-Inf, -3) is -0", sameBits (Elementary.pow -inf -3.0) -0.0
              "pow (-Inf, -2) is +0", sameBits (Elementary.pow -inf -2.0) 0.0
              "pow (0.5, Inf) is +0", sameBits (Elementary.pow 0.5 inf) 0.0
              "pow (0.5, -Inf) is Inf", Elementary.pow 0.5 -inf = inf
              "pow (2, Inf) is Inf", Elementary.pow 2.0 inf = inf
              "pow (2, -Inf) is +0", sameBits (Elementary.pow 2.0 -inf) 0.0
              "pow (-2, Inf) is Inf", Elementary.pow -2.0 inf = inf
              "pow (-0.5, -Inf) is Inf", Elementary.pow -0.5 -inf = inf
              // D43's table, not C99: a zero base under ANY negative exponent is outside the reals.
              "powTable (-0, -Inf)", Elementary.powTable -0.0 -inf = SpecialValue.Undefined
              "pow (-0, -Inf) is the canonical NaN", canonicalNaNBits (Elementary.pow -0.0 -inf)
              "pow (1e308, 1.1) overflows to Inf", Elementary.pow 1e308 1.1 = inf
              "pow (1e-308, 1.1) underflows to +0", sameBits (Elementary.pow 1e-308 1.1) 0.0 ]

        match expected |> List.filter (snd >> not) with
        | [] -> None
        | failed ->
            Some(
                sprintf
                    "elementary tables and edges not answered as stated: %s"
                    (failed |> List.map fst |> String.concat ", ")
            )

    /// `pow` at its four exact cases is the one operation each names, and a negative base under an
    /// integer exponent is the positive base's power with the exponent's parity for a sign, byte for
    /// byte (D52 item 3).
    let private powExactCases (d: Draw) =
        let x = positive d -500 500
        let n = float (d.Int 1 40) * (if d.Unit() < 0.0 then -1.0 else 1.0)
        let odd = (n % 2.0) <> 0.0
        let magnitude = Elementary.pow x n

        let expected =
            [ "pow (x, 1) is x", bits (Elementary.pow x 1.0) = bits x
              "pow (x, -1) is 1 / x", bits (Elementary.pow x -1.0) = bits (1.0 / x)
              "pow (x, 2) is x * x", bits (Elementary.pow x 2.0) = bits (x * x)
              "pow (x, 1/2) is sqrt x", bits (Elementary.pow x 0.5) = bits (sqrt x)
              "pow (-x, n) is ±pow (x, n) by parity",
              bits (Elementary.pow -x n) = bits (if odd then -magnitude else magnitude) ]

        match expected |> List.filter (snd >> not) with
        | [] -> None
        | failed ->
            Some(
                sprintf
                    "pow exact cases failed at x = %.17g, n = %g: %s"
                    x
                    n
                    (failed |> List.map fst |> String.concat ", ")
            )

    /// The functions invert within their one-ulp bounds: `ln (exp x)` within `4u · (|x| + 1)` of `x`,
    /// `exp (ln y)` within `4u · y · (|ln y| + 1)` of `y` — the second bound carries `|ln y|` because
    /// one ulp of `ln y` is `u · |ln y|` of the exponent, whatever the routines do.
    let private elementaryInverses (d: Draw) =
        let x = d.Unit() * 700.0
        let y = positive d -900 900
        let backX = Elementary.ln (Elementary.exp x)
        let backY = Elementary.exp (Elementary.ln y)
        let lnY = Elementary.ln y

        if abs (backX - x) > 4.0 * u * (abs x + 1.0) then
            Some(sprintf "ln (exp %.17g) = %.17g" x backX)
        elif abs (backY - y) > 4.0 * u * y * (abs lnY + 1.0) then
            Some(sprintf "exp (ln %.17g) = %.17g" y backY)
        else
            None

    /// The functional equations hold within the bounds a one-ulp routine admits: `exp (a + b)` against
    /// `exp a · exp b`, `ln (x · y)` against `ln x + ln y`, `pow (x, y) · pow (x, −y)` against one,
    /// and `log10 x` against `ln x / ln 10`.
    let private elementaryIdentities (d: Draw) =
        let a = d.Unit() * 300.0
        let b = d.Unit() * 300.0
        let x = positive d -400 400
        let y = positive d -400 400
        let z = positive d -20 20
        let p = d.Unit() * 10.0
        let expSum = Elementary.exp (a + b)
        let expProduct = Elementary.exp a * Elementary.exp b
        let lnProduct = Elementary.ln (x * y)
        let lnSum = Elementary.ln x + Elementary.ln y
        let powRound = Elementary.pow z p * Elementary.pow z -p
        let log10Z = Elementary.log10 z
        let lnOverLn10 = Elementary.ln z / 2.302585092994046

        if abs (expSum - expProduct) > 2.0 * u * (abs (a + b) + 8.0) * expSum then
            Some(sprintf "exp (%.17g + %.17g) = %.17g against the product %.17g" a b expSum expProduct)
        elif abs (lnProduct - lnSum) > 4.0 * u * (abs (Elementary.ln x) + abs (Elementary.ln y) + 1.0) then
            Some(sprintf "ln (%.17g * %.17g) = %.17g against the sum %.17g" x y lnProduct lnSum)
        elif abs (powRound - 1.0) > 8.0 * u then
            Some(sprintf "pow (%.17g, %.17g) * pow (%.17g, -%.17g) = %.17g" z p z p powRound)
        elif ulpsApart log10Z lnOverLn10 > 4L then
            Some(sprintf "log10 %.17g = %.17g against ln / ln 10 = %.17g" z log10Z lnOverLn10)
        else
            None

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
          "every QR refusal is answered as stated", qrRefusals
          "every special-value row and IEEE edge of the elementary functions is answered as stated", elementaryTables
          "pow's exact cases and the sign of a negative base are the named operations, byte for byte", powExactCases
          "exp and ln invert within their one-ulp bounds", elementaryInverses
          "the elementary functional equations hold within their bounds", elementaryIdentities
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
