namespace Fuaran.Compute.LinearAlgebra

open Fuaran.Core

// ============================================================================
//  Phase 590 — the storage, the error and exactness vocabularies, and the
//  internal kernels every operation in this package is built from.
//
//  The rules are DECISIONS.md D49, and every kernel below cites the item it
//  keeps: each reduction is a left-to-right fold from +0.0 (item 1), a product
//  and a sum are two roundings and never one (item 2), SIMD runs across
//  independent outputs only (item 3), the 2-norm is the scaled one (item 4),
//  and a NaN leaves a public function as `Double.NaN`'s bits (item 5). The
//  module names and the vocabularies are D50's.
//
//  FSharp.Core and Fuaran.Core.Column only; Fable-clean. The one native member
//  (the SIMD column update) is compiled on .NET only, and a law holds it
//  byte-equal to the portable one.
// ============================================================================

/// A shape, rows by columns: a vector of length `n` is `n` by `1`.
[<Struct>]
type Dims = { Rows: int; Cols: int }

/// Why a linear-algebra operation refused its input (DECISIONS.md D49 item 6). Nothing in this package
/// throws on bad data: an operation that can fail returns one of these.
[<RequireQualifiedAccess>]
type LinAlgError =
    /// The operands' shapes do not fit the operation: its name, the left operand's shape and the right's.
    | ShapeMismatch of operation: string * left: Dims * right: Dims
    /// A triangular solve met an exactly zero diagonal entry; the lowest such index.
    | Singular of index: int
    /// A factorization found the columns spanning fewer dimensions than there are columns; the rank.
    | RankDeficient of rank: int
    /// An entry that must be finite is a NaN or an infinity; its row and column.
    | NotFinite of row: int * col: int
    /// A matrix offered as upper triangular holds a nonzero entry below its diagonal; its row and column.
    | NotTriangular of row: int * col: int
    /// An iteration stopped at its bound without meeting its stopping rule; the iterations run.
    | NotConverged of iterations: int
    /// A row's prior weight is negative, a NaN or an infinity (DECISIONS.md D49 item 9); the row.
    | InvalidWeight of row: int
    /// A response outside its family's range (DECISIONS.md D49 item 15; Phase 595): a NaN or an
    /// infinity, a negative count under Poisson, a zero or negative value under Gamma; the first row.
    | InvalidResponse of row: int
    /// An iteration's mean left the range its link allows — an infinity, a NaN, zero, or a unit
    /// deviance that overflowed — and no halving of the step recovered it (D49 item 16, the separation
    /// refusal; Phase 595); the first such row.
    | Separation of row: int
    /// A family and link this package does not fit (Phase 595), each by its spelling.
    | UnsupportedModel of family: string * link: string
    /// A Tweedie variance power no Tweedie distribution has — strictly between zero and one — or a NaN
    /// or an infinity (Phase 598); the power.
    | InvalidPower of power: float
    /// An iteration's mean left the range its family and link allow — not positive where the family's
    /// mean is, or a linear predictor at or below zero under the inverse-square or square-root link —
    /// and no halving of the step recovered it (Phase 600, DECISIONS.md D58 item 3); the first such row.
    /// Under the log link and the binomial families the same event is `Separation`.
    | MeanOutOfRange of row: int
    /// A negative binomial θ that is not finite and positive (Phase 600); the θ.
    | InvalidTheta of theta: float
    /// The negative binomial's estimate of θ did not converge — its Newton step on θ stalled, left the
    /// positive reals, or the alternation with the coefficients reached its bound (Phase 600, D58 item
    /// 8); the alternation it failed at.
    | ThetaNotConverged of alternation: int

/// How exact an operation's bytes are across hosts — the vocabulary `SchemaWalk` reads a column's
/// lineage in (DECISIONS.md D43): `Pinned`, every conformant host computes the same bits; `HostRounded`,
/// hosts agree within a stated ulp bound only. Every operation this package ships is `Pinned`
/// (DECISIONS.md D49 item 7); `Operations.exactness` classifies each one.
[<RequireQualifiedAccess>]
type Exactness =
    | Pinned
    | HostRounded

/// An opaque, immutable, dense matrix of floats, stored column-major: the element at row `i`, column
/// `j` sits at `j * Rows + i` of one backing vector. Nothing public writes; every operation answers a
/// new matrix. Its storage holds no NaN but `Double.NaN`'s bits, so a column read out of it is already
/// canonical.
///
/// Equality is by shape and by element under the substrate's float identity, the one `Vector<float>`
/// compares under: every NaN one value, `-0.0` equal to `0.0`. A byte-level comparison is a law's,
/// over `Matrix.toColumnMajor`, never `=`.
[<Sealed>]
type Matrix internal (rows: int, cols: int, storage: Vector<float>) =

    /// The number of rows.
    member _.Rows: int = rows

    /// The number of columns.
    member _.Cols: int = cols

    /// The shape.
    member _.Dims: Dims = { Rows = rows; Cols = cols }

    /// The column-major backing, shared with every column view handed out; never written.
    member internal _.Storage: Vector<float> = storage

    /// The element at row `i`, column `j`. Bounds-checked against the shape; the exception is
    /// `IndexOutOfRangeException`, as an array's would be.
    member _.Item
        with get (i: int, j: int): float =
            if i < 0 || i >= rows || j < 0 || j >= cols then
                raise (System.IndexOutOfRangeException())

            storage[j * rows + i]

    override _.Equals(other: obj) : bool =
        match other with
        | :? Matrix as that -> rows = that.Rows && cols = that.Cols && storage.Equals that.Storage
        | _ -> false

    override _.GetHashCode() : int =
        (rows * 31 + cols) * 31 + storage.GetHashCode()

/// The internal kernels: raw arrays in, raw arrays out, each in the order DECISIONS.md D49 pins. A
/// public operation borrows its inputs' backing arrays, runs a kernel into a fresh array it owns, and
/// adopts that array — so no input is copied and no output is shared.
module internal Kernels =

#if FABLE_COMPILER
    /// `a[i]` with no bounds check under JavaScript (Phase 591), where Fable's shared checked helper
    /// costs more than the rotation around it (the dataframe package's `Raw` measured the same in Phase
    /// 326). Used only in the rotation loops, whose callers allocate every array they index.
    [<Fable.Core.Emit("$1[$0]")>]
    let get (i: int) (a: float[]) : float = Fable.Core.Util.jsNative

    /// `a[i] <- v` with no bounds check under JavaScript; the same rule as `get`.
    [<Fable.Core.Emit("$0[$1] = $2")>]
    let set (a: float[]) (i: int) (v: float) : unit = Fable.Core.Util.jsNative
#else
    /// `a[i]`: the plain index on .NET.
    let inline get (i: int) (a: float[]) : float = a[i]

    /// `a[i] <- v`: the plain index on .NET.
    let inline set (a: float[]) (i: int) (v: float) : unit = a[i] <- v
#endif

    /// THE NaN a float leaves this package with (D49 item 5, D16): `Double.NaN`'s bit pattern.
    let canonical (x: float) : float =
        if System.Double.IsNaN x then System.Double.NaN else x

    /// Canonicalise a fresh array in place, then hand it to a vector without a copy.
    let adopt (xs: float[]) : Vector<float> =
        for i in 0 .. xs.Length - 1 do
            xs[i] <- canonical xs[i]

        Vector.adopt xs

    /// The backing array of a vector and where the vector starts in it — a read-only loan.
    let lend (v: Vector<float>) : float[] * int =
        let b = Vector.Unsafe.borrow v
        b.Array, b.Offset

    /// `Σ x[xo + k] · y[yo + k]` for `k = 0 .. n - 1`, folded left to right from `+0.0`, each product
    /// rounded before it is added (D49 items 1 and 2).
    let dot (x: float[]) (xo: int) (y: float[]) (yo: int) (n: int) : float =
        let mutable acc = 0.0

        for k in 0 .. n - 1 do
            let p = x[xo + k] * y[yo + k]
            acc <- acc + p

        acc

    /// `y[i] <- y[i] + a · col[co + i]` for `i = 0 .. n - 1`: the portable column update. Each `y[i]`
    /// is an independent output, so the loop is elementwise in D49 item 3's sense.
    let axpyColumnPortable (a: float) (col: float[]) (co: int) (y: float[]) (n: int) : unit =
        for i in 0 .. n - 1 do
            let p = a * col[co + i]
            y[i] <- y[i] + p

    /// The same update, a SIMD lane per row on .NET (D49 item 3): each lane forms one rounded product
    /// and one rounded sum for its own `y[i]`, so the answer is the portable member's byte for byte —
    /// which `LinearAlgebraLaws` holds. Under Fable it IS the portable member.
    let axpyColumnNative (a: float) (col: float[]) (co: int) (y: float[]) (n: int) : unit =
#if FABLE_COMPILER
        axpyColumnPortable a col co y n
#else
        let width = System.Numerics.Vector<float>.Count

        if System.Numerics.Vector.IsHardwareAccelerated && n >= width then
            let av = System.Numerics.Vector<float>(a)
            let mutable i = 0

            while i <= n - width do
                let yv = System.Numerics.Vector<float>(y, i)
                let cv = System.Numerics.Vector<float>(col, co + i)
                let p = System.Numerics.Vector.Multiply(av, cv)
                System.Numerics.Vector.Add(yv, p).CopyTo(y, i)
                i <- i + width

            while i < n do
                let p = a * col[co + i]
                y[i] <- y[i] + p
                i <- i + 1
        else
            axpyColumnPortable a col co y n
#endif

    /// `y = A · x` for a column-major `a` of `rows` by `cols` starting at `ao`, column by column
    /// (`y += x[j] · A[:,j]`, `j` ascending), so each `y[i]` accumulates in `j` order from `+0.0`
    /// (D49 item 1) — the dot of row `i` with `x`, the same sequence of roundings.
    let gemvWith
        (update: float -> float[] -> int -> float[] -> int -> unit)
        (a: float[])
        (ao: int)
        (rows: int)
        (cols: int)
        (x: float[])
        (xo: int)
        : float[] =
        let y = Array.zeroCreate<float> rows

        for j in 0 .. cols - 1 do
            update x[xo + j] a (ao + j * rows) y rows

        y

    /// `gemvWith` over the native column update.
    let gemv (a: float[]) (ao: int) (rows: int) (cols: int) (x: float[]) (xo: int) : float[] =
        gemvWith axpyColumnNative a ao rows cols x xo

    /// The scaled 2-norm of `x[xo .. xo + n - 1]` (D49 item 4): `m = max |x_i|`; NaN when any element is
    /// NaN; `+∞` when `m = +∞`; `0` when `m = 0`; otherwise `m · √(Σ (x_i / m)²)`, the sum folded in index
    /// order. The host's `hypot` is never called.
    let norm2 (x: float[]) (xo: int) (n: int) : float =
        let mutable m = 0.0
        let mutable nan = false

        for k in 0 .. n - 1 do
            let v = x[xo + k]

            if System.Double.IsNaN v then
                nan <- true
            elif abs v > m then
                m <- abs v

        if nan then
            System.Double.NaN
        elif System.Double.IsPositiveInfinity m then
            System.Double.PositiveInfinity
        elif m = 0.0 then
            0.0
        else
            let mutable acc = 0.0

            for k in 0 .. n - 1 do
                let q = x[xo + k] / m
                let sq = q * q
                acc <- acc + sq

            m * sqrt acc

    /// `norm2 [| a; b |]` without the array: the same comparisons, the same two quotients squared and
    /// folded from `+0.0` in the order `a`, `b`, the same product with the root — so the same bits,
    /// which `LinearAlgebraLaws` holds. The radius of every Givens rotation (D49 item 8).
    let norm2Pair (a: float) (b: float) : float =
        if System.Double.IsNaN a || System.Double.IsNaN b then
            System.Double.NaN
        else
            let mutable m = 0.0

            if abs a > m then
                m <- abs a

            if abs b > m then
                m <- abs b

            if System.Double.IsPositiveInfinity m then
                System.Double.PositiveInfinity
            elif m = 0.0 then
                0.0
            else
                let qa = a / m
                let qb = b / m
                let mutable acc = 0.0
                let sa = qa * qa
                acc <- acc + sa
                let sb = qb * qb
                acc <- acc + sb
                m * sqrt acc

    /// The rotation `(c, s)` applied to `n` pairs, `u[uo + k]` the first element of pair `k` and
    /// `v[vo + k]` the second: `u' = c·u + s·v`, `v' = c·v − s·u`, each two rounded products and one
    /// rounded sum or difference in that operand order (D49 items 2 and 8) — `Givens.apply` pair by
    /// pair. The portable member.
    let rotatePortable (c: float) (s: float) (u: float[]) (uo: int) (v: float[]) (vo: int) (n: int) : unit =
        for k in 0 .. n - 1 do
            let x = get (uo + k) u
            let y = get (vo + k) v
            let cx = c * x
            let sy = s * y
            let cy = c * y
            let sx = s * x
            set u (uo + k) (cx + sy)
            set v (vo + k) (cy - sx)

    /// The same rotation, a SIMD lane per pair on .NET (D49 item 3): each lane forms the same four
    /// rounded products and two rounded sums for its own pair, with no fused multiply-add, so the
    /// answer is the portable member's byte for byte — which `LinearAlgebraLaws` holds. Under Fable it
    /// IS the portable member.
    let rotateNative (c: float) (s: float) (u: float[]) (uo: int) (v: float[]) (vo: int) (n: int) : unit =
#if FABLE_COMPILER
        rotatePortable c s u uo v vo n
#else
        let width = System.Numerics.Vector<float>.Count

        if System.Numerics.Vector.IsHardwareAccelerated && n >= width then
            let cv = System.Numerics.Vector<float>(c)
            let sv = System.Numerics.Vector<float>(s)
            let mutable k = 0

            while k <= n - width do
                let xv = System.Numerics.Vector<float>(u, uo + k)
                let yv = System.Numerics.Vector<float>(v, vo + k)
                let cx = System.Numerics.Vector.Multiply(cv, xv)
                let sy = System.Numerics.Vector.Multiply(sv, yv)
                let cy = System.Numerics.Vector.Multiply(cv, yv)
                let sx = System.Numerics.Vector.Multiply(sv, xv)
                System.Numerics.Vector.Add(cx, sy).CopyTo(u, uo + k)
                System.Numerics.Vector.Subtract(cy, sx).CopyTo(v, vo + k)
                k <- k + width

            rotatePortable c s u (uo + k) v (vo + k) (n - k)
        else
            rotatePortable c s u uo v vo n
#endif

/// The storage operations over `Matrix`: construction, reads and the transpose. Every constructor
/// takes a fresh backing it owns; every read that answers a column answers a zero-copy view.
[<RequireQualifiedAccess>]
module Matrix =

    let private ofStorage (rows: int) (cols: int) (data: float[]) : Matrix = Matrix(rows, cols, Kernels.adopt data)

    /// The number of rows.
    let rows (m: Matrix) : int = m.Rows

    /// The number of columns.
    let cols (m: Matrix) : int = m.Cols

    /// The shape.
    let dims (m: Matrix) : Dims = m.Dims

    /// The element at row `i`, column `j`; raises `IndexOutOfRangeException` outside the shape.
    let item (i: int) (j: int) (m: Matrix) : float = m[i, j]

    /// A `rows` by `cols` matrix whose element at `(i, j)` is `f i j`, called column by column, rows
    /// ascending within a column. Raises `ArgumentOutOfRangeException` on a negative dimension: a
    /// shape is an argument, not data.
    let init (rows: int) (cols: int) (f: int -> int -> float) : Matrix =
        if rows < 0 then
            raise (System.ArgumentOutOfRangeException "rows")

        if cols < 0 then
            raise (System.ArgumentOutOfRangeException "cols")

        let data = Array.zeroCreate<float> (rows * cols)

        for j in 0 .. cols - 1 do
            for i in 0 .. rows - 1 do
                data[j * rows + i] <- f i j

        ofStorage rows cols data

    /// The `n` by `n` identity. Raises `ArgumentOutOfRangeException` on a negative order.
    let identity (n: int) : Matrix =
        init n n (fun i j -> if i = j then 1.0 else 0.0)

    /// A matrix whose columns are `columns`, in order — one copy into contiguous column-major storage.
    /// No column is a `0` by `0` matrix; columns of unequal length are refused as a shape mismatch
    /// naming the first column's shape and the first that differs.
    let ofColumns (columns: Vector<float> list) : Result<Matrix, LinAlgError> =
        match columns with
        | [] -> Ok(Matrix(0, 0, Vector.empty))
        | first :: _ ->
            let rows = first.Length

            match columns |> List.tryFind (fun c -> c.Length <> rows) with
            | Some c ->
                Error(
                    LinAlgError.ShapeMismatch(
                        "Matrix.ofColumns",
                        { Rows = rows; Cols = 1 },
                        { Rows = c.Length; Cols = 1 }
                    )
                )
            | None ->
                let cols = List.length columns
                let data = Array.zeroCreate<float> (rows * cols)

                columns
                |> List.iteri (fun j c ->
                    let src, off = Kernels.lend c

                    for i in 0 .. rows - 1 do
                        data[j * rows + i] <- src[off + i])

                Ok(ofStorage rows cols data)

    /// Column `j` as a vector — a ZERO-COPY view of the matrix's storage. Raises
    /// `IndexOutOfRangeException` outside `0 .. cols - 1`.
    let column (j: int) (m: Matrix) : Vector<float> =
        if j < 0 || j >= m.Cols then
            raise (System.IndexOutOfRangeException())

        Vector.slice (j * m.Rows) m.Rows m.Storage

    /// Every column, in order, each a zero-copy view.
    let columns (m: Matrix) : Vector<float> list =
        [ for j in 0 .. m.Cols - 1 -> column j m ]

    /// The transpose: a new matrix, every element moved and none computed.
    let transpose (m: Matrix) : Matrix =
        let src, off = Kernels.lend m.Storage
        let rows, cols = m.Rows, m.Cols
        let data = Array.zeroCreate<float> (rows * cols)

        for j in 0 .. cols - 1 do
            for i in 0 .. rows - 1 do
                data[i * cols + j] <- src[off + j * rows + i]

        ofStorage cols rows data

    /// The elements in column-major order — a fresh array, never the storage.
    let toColumnMajor (m: Matrix) : float[] = Vector.toArray m.Storage
