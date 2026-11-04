namespace Fuaran.Compute.LinearAlgebra

open Fuaran.Core

/// Levels 1 to 3 over the substrate's `Vector<float>` and `Matrix` (Phase 590). Every operation runs
/// in the one order DECISIONS.md D49 pins and is `Pinned`: its answer is the same bits on every
/// conformant host. Inputs are read where they lie — a float column handed in is never copied — and
/// every answer is fresh, with any NaN in it `Double.NaN`'s bits. The module is named for the
/// operations' conventional levels (DECISIONS.md D50); it is not the reference BLAS's calling
/// convention, and carries no `alpha` / `beta` scaling.
[<RequireQualifiedAccess>]
module Blas =

    let private vecDims (v: Vector<float>) : Dims = { Rows = v.Length; Cols = 1 }

    let private mismatch (operation: string) (left: Dims) (right: Dims) : Result<'T, LinAlgError> =
        Error(LinAlgError.ShapeMismatch(operation, left, right))

    /// An elementwise map of two equal-length vectors into a fresh one.
    let private zip
        (operation: string)
        (f: float -> float -> float)
        (x: Vector<float>)
        (y: Vector<float>)
        : Result<Vector<float>, LinAlgError> =
        if x.Length <> y.Length then
            mismatch operation (vecDims x) (vecDims y)
        else
            let xa, xo = Kernels.lend x
            let ya, yo = Kernels.lend y
            let out = Array.zeroCreate<float> x.Length

            for i in 0 .. x.Length - 1 do
                out[i] <- f xa[xo + i] ya[yo + i]

            Ok(Kernels.adopt out)

    // ---- level 1 -------------------------------------------------------------------------------

    /// `Σ x_i · y_i`, `i` ascending from `+0.0`, each product rounded before it is added (D49 items 1
    /// and 2). Vectors of unequal length are a shape mismatch.
    let dot (x: Vector<float>) (y: Vector<float>) : Result<float, LinAlgError> =
        if x.Length <> y.Length then
            mismatch "Blas.dot" (vecDims x) (vecDims y)
        else
            let xa, xo = Kernels.lend x
            let ya, yo = Kernels.lend y
            Ok(Kernels.canonical (Kernels.dot xa xo ya yo x.Length))

    /// `a · x + y`, element by element: one rounded product, then one rounded sum.
    let axpy (a: float) (x: Vector<float>) (y: Vector<float>) : Result<Vector<float>, LinAlgError> =
        zip
            "Blas.axpy"
            (fun xi yi ->
                let p = a * xi
                p + yi)
            x
            y

    /// `a · x`, element by element.
    let scale (a: float) (x: Vector<float>) : Vector<float> =
        let xa, xo = Kernels.lend x
        let out = Array.zeroCreate<float> x.Length

        for i in 0 .. x.Length - 1 do
            out[i] <- a * xa[xo + i]

        Kernels.adopt out

    /// `x + y`, element by element.
    let add (x: Vector<float>) (y: Vector<float>) : Result<Vector<float>, LinAlgError> = zip "Blas.add" (+) x y

    /// `x − y`, element by element.
    let sub (x: Vector<float>) (y: Vector<float>) : Result<Vector<float>, LinAlgError> = zip "Blas.sub" (-) x y

    /// The scaled 2-norm (D49 item 4): `max |x_i|` times the square root of the sum of the squared
    /// quotients, folded in index order — never the host's `hypot`. `0` for an empty or all-zero vector,
    /// NaN when any element is NaN, `+∞` when any is infinite.
    let norm2 (x: Vector<float>) : float =
        let xa, xo = Kernels.lend x
        Kernels.canonical (Kernels.norm2 xa xo x.Length)

    // ---- level 2 -------------------------------------------------------------------------------

    /// `A · x`, column by column — `y += x_j · A[:,j]`, `j` ascending, a SIMD lane per row on .NET — so
    /// each `y_i` is the dot of row `i` with `x` in `j` order, byte for byte (D49 items 1 and 3). `x`
    /// must have `cols A` elements.
    let gemv (a: Matrix) (x: Vector<float>) : Result<Vector<float>, LinAlgError> =
        if x.Length <> a.Cols then
            mismatch "Blas.gemv" a.Dims (vecDims x)
        else
            let aa, ao = Kernels.lend a.Storage
            let xa, xo = Kernels.lend x
            Ok(Kernels.adopt (Kernels.gemv aa ao a.Rows a.Cols xa xo))

    // ---- level 3 -------------------------------------------------------------------------------

    /// `A · B`: element `(i, j)` is the dot of row `i` of `A` with column `j` of `B` in `k` order from
    /// `+0.0` (D49 item 1). It is computed a column of `B` at a time through `gemv`'s column update,
    /// which forms exactly those roundings in exactly that order. `cols A` must equal `rows B`.
    let gemm (a: Matrix) (b: Matrix) : Result<Matrix, LinAlgError> =
        if a.Cols <> b.Rows then
            mismatch "Blas.gemm" a.Dims b.Dims
        else
            let aa, ao = Kernels.lend a.Storage
            let ba, bo = Kernels.lend b.Storage
            let rows, inner, cols = a.Rows, a.Cols, b.Cols
            let data = Array.zeroCreate<float> (rows * cols)

            for j in 0 .. cols - 1 do
                let cj = Kernels.gemv aa ao rows inner ba (bo + j * inner)
                Array.blit cj 0 data (j * rows) rows

            Ok(Matrix(rows, cols, Kernels.adopt data))

    /// The Gram matrix `Aᵀ · A`: element `(i, j)` is the dot of columns `i` and `j` of `A`, rows
    /// ascending from `+0.0`, and the matrix is exactly symmetric (each pair is computed once). For
    /// diagnostics: a fit solves through a factorization of `A`, never through its Gram matrix, whose
    /// condition number is the square of `A`'s.
    let gram (a: Matrix) : Matrix =
        let aa, ao = Kernels.lend a.Storage
        let rows, cols = a.Rows, a.Cols
        let data = Array.zeroCreate<float> (cols * cols)

        for j in 0 .. cols - 1 do
            for i in 0..j do
                let g = Kernels.dot aa (ao + i * rows) aa (ao + j * rows) rows
                data[j * cols + i] <- g
                data[i * cols + j] <- g

        Matrix(cols, cols, Kernels.adopt data)
