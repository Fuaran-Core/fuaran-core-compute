namespace Fuaran.Compute.LinearAlgebra

// ============================================================================
//  Phase 591 — the batch Householder factorizations (DECISIONS.md D49 item
//  10): pinned in their own order, documented as NOT byte-equal to the
//  streaming least-squares kernel, and held to it by a tolerance law. The
//  streaming accumulator and least squares left this file for their own
//  package, Fuaran.Compute.Regression, in Phase 632 (DECISIONS.md D75).
// ============================================================================

/// The batch factorization of `QR.pivoted`: `R` over the columns in pivot order, the order, and the
/// rank by D49 item 11's test.
[<Sealed>]
type PivotedQR internal (r: UpperTriangular, permutation: int list, rank: int, aliased: int list) =

    /// `R`, `cols` by `cols`, over the columns in pivot order, every diagonal entry non-negative.
    member _.R: UpperTriangular = r

    /// The pivot order: position `k` of `R` holds this original column.
    member _.Permutation: int list = permutation

    /// The number of columns not aliased.
    member _.Rank: int = rank

    /// The original indices of the aliased columns, ascending.
    member _.Aliased: int list = aliased

/// The batch factorizations (DECISIONS.md D49 item 10): secondary to the streaming kernel, pinned in
/// their own order, and NOT byte-equal to it — a law holds the two within a tolerance. Both refuse a
/// NaN or infinite entry as `NotFinite` (the first, in column-major order) before any arithmetic.
[<RequireQualifiedAccess>]
module QR =

    /// Householder over a column-major copy of `a` (`n` rows, `p` columns), pivoting when asked.
    /// Step `k` (for `k` below `min n p`): with pivoting, the remaining column of largest norm over rows
    /// `k ..` (each norm the scaled `norm2`, recomputed at each step; the lowest index on a tie) is
    /// swapped to position `k`; `α = norm2 a[k.., k]`; when `α = 0` the step reflects nothing;
    /// otherwise `v = a[k.., k]` with `v₀ ← a[k,k] + σα` (`σ = −1` when `a[k,k] < 0`, else `1`),
    /// `a[k,k] ← −σα`, the rest of column `k` `+0.0`, and for each later column `j` (ascending)
    /// `f = (2 · (v · a[k.., j])) / (v · v)` and `a[i,j] ← a[i,j] − f · v_i`, `i` ascending — every dot
    /// a left-to-right fold from `+0.0`. Then every row of `R` with a negative diagonal entry is
    /// negated, so the diagonal is non-negative as the streaming kernel's is (D49 item 8).
    let private factor (pivot: bool) (a: Matrix) : Result<UpperTriangular * int[], LinAlgError> =
        let n, p = a.Rows, a.Cols
        let src, so = Kernels.lend a.Storage

        let firstBad =
            seq {
                for j in 0 .. p - 1 do
                    for i in 0 .. n - 1 do
                        let v = src[so + j * n + i]

                        if System.Double.IsNaN v || System.Double.IsInfinity v then
                            yield i, j
            }
            |> Seq.tryHead

        match firstBad with
        | Some(i, j) -> Error(LinAlgError.NotFinite(i, j))
        | None ->
            let w = Array.init (n * p) (fun k -> src[so + k])
            let perm = Array.init p id
            let v = Array.zeroCreate<float> n

            for k in 0 .. (min n p) - 1 do
                if pivot then
                    let mutable best = k
                    let mutable bestNorm = Kernels.norm2 w (k * n + k) (n - k)

                    for j in k + 1 .. p - 1 do
                        let nj = Kernels.norm2 w (j * n + k) (n - k)

                        if nj > bestNorm then
                            best <- j
                            bestNorm <- nj

                    if best <> k then
                        for i in 0 .. n - 1 do
                            let t = w[k * n + i]
                            w[k * n + i] <- w[best * n + i]
                            w[best * n + i] <- t

                        let t = perm[k]
                        perm[k] <- perm[best]
                        perm[best] <- t

                let len = n - k
                let alpha = Kernels.norm2 w (k * n + k) len

                if alpha <> 0.0 then
                    let x0 = w[k * n + k]
                    let sigma = if x0 < 0.0 then -1.0 else 1.0
                    let sa = sigma * alpha

                    for i in 0 .. len - 1 do
                        v[i] <- w[k * n + k + i]

                    v[0] <- x0 + sa
                    let vtv = Kernels.dot v 0 v 0 len
                    w[k * n + k] <- -sa

                    for i in k + 1 .. n - 1 do
                        w[k * n + i] <- 0.0

                    for j in k + 1 .. p - 1 do
                        let d = Kernels.dot v 0 w (j * n + k) len
                        let d2 = 2.0 * d
                        let f = d2 / vtv

                        for i in 0 .. len - 1 do
                            let q = f * v[i]
                            w[j * n + k + i] <- w[j * n + k + i] - q

            let r =
                Matrix.init p p (fun i j ->
                    if i > j || i >= n then
                        0.0
                    else
                        let x = w[j * n + i]
                        if w[i * n + i] < 0.0 then -x else x)

            UpperTriangular.ofMatrix r |> Result.map (fun r -> r, perm)

    /// The Householder factor `R` of `a`, `cols a` by `cols a`, with a non-negative diagonal — the batch
    /// factorization. NOT byte-equal to Fuaran.Compute.Regression's `QRAccumulator.r` over the same rows
    /// (a different sequence of roundings); the two agree within the tolerance `RegressionLaws` states, and a fit the law
    /// vectors pin is always the streaming one (D49 item 10). Rows past `rows a` are zero.
    let householder (a: Matrix) : Result<UpperTriangular, LinAlgError> = factor false a |> Result.map fst

    /// Householder with column pivoting (D49 item 11): at each step the remaining column of largest
    /// norm, the lowest index on a tie. The rank is the number of positions whose diagonal entry is
    /// not negligible by D49 item 11's test — `|R[k,k]| ≤ τ · norm2 R[0..k, k]`, `τ = max(rows, cols) ·
    /// 2⁻⁵²` — and the aliased columns are the original indices at the negligible positions.
    let pivoted (a: Matrix) : Result<PivotedQR, LinAlgError> =
        factor true a
        |> Result.map (fun (r, perm) ->
            let p = a.Cols
            let tau = Rank.tolerance a.Rows p
            let rm, ro = Kernels.lend r.Matrix.Storage

            let aliased =
                [ for k in 0 .. p - 1 do
                      if Rank.negligible tau rm[ro + k * p + k] rm (ro + k * p) (k + 1) then
                          yield perm[k] ]
                |> List.sort

            PivotedQR(r, List.ofArray perm, p - List.length aliased, aliased))
