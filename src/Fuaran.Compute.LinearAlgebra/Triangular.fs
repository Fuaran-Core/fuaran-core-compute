namespace Fuaran.Compute.LinearAlgebra

open Fuaran.Core

/// A square upper-triangular matrix: every entry below the diagonal is zero and every entry is finite.
/// Built only by a factorization in this package or by the checked `UpperTriangular.ofMatrix`, so a
/// solve never meets an entry it would have to refuse. A zero on the diagonal is allowed (it is what
/// rank deficiency looks like); a solve through one is refused as `Singular`.
[<Sealed>]
type UpperTriangular internal (m: Matrix) =

    /// The order: rows, equal to columns.
    member _.Order: int = m.Rows

    /// The triangle as a plain matrix.
    member _.Matrix: Matrix = m

    override _.Equals(other: obj) : bool =
        match other with
        | :? UpperTriangular as that -> m.Equals that.Matrix
        | _ -> false

    override _.GetHashCode() : int = m.GetHashCode()

/// The checked construction and the two triangular solves.
[<RequireQualifiedAccess>]
module UpperTriangular =

    /// `m` as an upper-triangular factor, after three checks in this order: it is square (else a shape
    /// mismatch naming its shape and the square it is not); every entry is finite (else `NotFinite`,
    /// the first offender in column-major order); every entry below the diagonal is exactly zero, a
    /// `-0.0` included (else `NotTriangular`, the first offender in column-major order).
    let ofMatrix (m: Matrix) : Result<UpperTriangular, LinAlgError> =
        if m.Rows <> m.Cols then
            Error(LinAlgError.ShapeMismatch("UpperTriangular.ofMatrix", m.Dims, { Rows = m.Cols; Cols = m.Cols }))
        else
            let a, o = Kernels.lend m.Storage
            let n = m.Rows

            let firstWhere (bad: int -> int -> float -> bool) =
                seq {
                    for j in 0 .. n - 1 do
                        for i in 0 .. n - 1 do
                            if bad i j a[o + j * n + i] then
                                yield i, j
                }
                |> Seq.tryHead

            match firstWhere (fun _ _ v -> System.Double.IsNaN v || System.Double.IsInfinity v) with
            | Some(i, j) -> Error(LinAlgError.NotFinite(i, j))
            | None ->
                match firstWhere (fun i j v -> i > j && v <> 0.0) with
                | Some(i, j) -> Error(LinAlgError.NotTriangular(i, j))
                | None -> Ok(UpperTriangular m)

    /// The triangle as a plain matrix.
    let toMatrix (r: UpperTriangular) : Matrix = r.Matrix

    /// The order: rows, equal to columns.
    let order (r: UpperTriangular) : int = r.Order

    /// The lowest index whose diagonal entry is exactly zero.
    let private firstZeroDiagonal (a: float[]) (o: int) (n: int) : int option =
        let mutable found = None
        let mutable i = 0

        while found.IsNone && i < n do
            if a[o + i * n + i] = 0.0 then
                found <- Some i

            i <- i + 1

        found

    /// `x` with `R · x = b`, by back substitution in column order: for `j` from `n − 1` down to `0`,
    /// `x_j = b'_j / R[j,j]`, then `b'_i ← b'_i − x_j · R[i,j]` for every `i < j` (a lane per row). So
    /// `x_i = (((b_i − x_{n−1}·R[i,n−1]) − x_{n−2}·R[i,n−2]) − …) / R[i,i]`, `j` DESCENDING — the order
    /// is part of the contract (D49 item 1). A zero diagonal entry is refused as `Singular`, the lowest
    /// index, before any arithmetic; `b` must have `order R` elements.
    let solveUpper (r: UpperTriangular) (b: Vector<float>) : Result<Vector<float>, LinAlgError> =
        let n = r.Order

        if b.Length <> n then
            Error(LinAlgError.ShapeMismatch("UpperTriangular.solveUpper", r.Matrix.Dims, { Rows = b.Length; Cols = 1 }))
        else
            let a, o = Kernels.lend r.Matrix.Storage

            match firstZeroDiagonal a o n with
            | Some i -> Error(LinAlgError.Singular i)
            | None ->
                let ba, bo = Kernels.lend b
                let x = Array.init n (fun i -> ba[bo + i])

                for j in n - 1 .. -1 .. 0 do
                    let xj = x[j] / a[o + j * n + j]
                    x[j] <- xj

                    for i in 0 .. j - 1 do
                        let p = xj * a[o + j * n + i]
                        x[i] <- x[i] - p

                Ok(Kernels.adopt x)

    /// `x` with `Rᵀ · x = b`, by forward substitution: `x_i = (((b_i − R[0,i]·x_0) − R[1,i]·x_1) − …) /
    /// R[i,i]`, `j` ASCENDING over column `i` of `R` (D49 item 1). A zero diagonal entry is refused as
    /// `Singular`, the lowest index, before any arithmetic; `b` must have `order R` elements.
    let solveUpperT (r: UpperTriangular) (b: Vector<float>) : Result<Vector<float>, LinAlgError> =
        let n = r.Order

        if b.Length <> n then
            Error(
                LinAlgError.ShapeMismatch("UpperTriangular.solveUpperT", r.Matrix.Dims, { Rows = b.Length; Cols = 1 })
            )
        else
            let a, o = Kernels.lend r.Matrix.Storage

            match firstZeroDiagonal a o n with
            | Some i -> Error(LinAlgError.Singular i)
            | None ->
                let ba, bo = Kernels.lend b
                let x = Array.zeroCreate<float> n

                for i in 0 .. n - 1 do
                    let mutable s = ba[bo + i]

                    for j in 0 .. i - 1 do
                        let p = a[o + i * n + j] * x[j]
                        s <- s - p

                    x[i] <- s / a[o + i * n + i]

                Ok(Kernels.adopt x)

/// One Givens rotation (DECISIONS.md D49 item 8): the cosine, the sine and the radius it leaves in the
/// first slot. Built only by `Givens.make`, so the one construction and its sign convention hold.
[<Sealed>]
type Givens internal (c: float, s: float, r: float, identity: bool) =

    /// The cosine `c`.
    member _.Cos: float = c

    /// The sine `s`.
    member _.Sin: float = s

    /// The radius `r` the rotation leaves in the first slot of the pair it was made from.
    member _.Radius: float = r

    /// Is this the identity rotation, made from a pair whose second element was zero? Applying it
    /// changes nothing, bit for bit — not even an infinity or a zero's sign in the pair.
    member _.IsIdentity: bool = identity

    override _.Equals(other: obj) : bool =
        match other with
        | :? Givens as that ->
            let same (x: float) (y: float) =
                (System.Double.IsNaN x && System.Double.IsNaN y) || x = y

            identity = that.IsIdentity
            && same c that.Cos
            && same s that.Sin
            && same r that.Radius
        | _ -> false

    override _.GetHashCode() : int =
        // Every NaN is canonical here, and a zero of either sign hashes as `+0.0`, as `Equals` reads it.
        let h (x: float) = if x = 0.0 then 0 else hash x
        ((h c * 31 + h s) * 31 + h r) * 31 + (if identity then 1 else 0)

/// The one Givens construction and its application.
[<RequireQualifiedAccess>]
module Givens =

    /// The rotation taking `(a, b)` to `(r, 0)` (D49 item 8). When `b = 0` (either sign) it is the
    /// identity, `(c, s, r) = (1, 0, a)`; otherwise `r = norm2 [a; b]` — the scaled norm, never `hypot`
    /// — and `c = a / r`, `s = b / r`, so `r > 0`. A NaN `b` makes all three NaN; a NaN `a` beside a
    /// zero `b` is the identity with a NaN radius.
    let make (a: float) (b: float) : Givens =
        if b = 0.0 then
            Givens(1.0, 0.0, Kernels.canonical a, true)
        else
            let r = Kernels.norm2 [| a; b |] 0 2
            Givens(Kernels.canonical (a / r), Kernels.canonical (b / r), Kernels.canonical r, false)

    /// The rotation applied to a pair: `x' = c·x + s·y` and `y' = c·y − s·x`, each two rounded products
    /// and one rounded sum or difference, in that operand order (D49 item 8). The identity rotation
    /// returns the pair unchanged, bit for bit (a NaN made canonical).
    let apply (g: Givens) (x: float) (y: float) : float * float =
        if g.IsIdentity then
            Kernels.canonical x, Kernels.canonical y
        else
            let cx = g.Cos * x
            let sy = g.Sin * y
            let cy = g.Cos * y
            let sx = g.Sin * x
            Kernels.canonical (cx + sy), Kernels.canonical (cy - sx)
