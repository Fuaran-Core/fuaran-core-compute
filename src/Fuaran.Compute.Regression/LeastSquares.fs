namespace Fuaran.Compute.Regression

open Fuaran.Core
open Fuaran.Compute.LinearAlgebra

// ============================================================================
//  Phase 591 — least squares by a streaming Givens QR (DECISIONS.md D49 items
//  8 to 12, and the entry this phase records).
//
//  The accumulator folds rows into a p×p upper triangle `R` and the first p
//  entries of `Qᵀy`, one row at a time, columns ascending, with the one Givens
//  rotation of D49 item 8. Its state is O(p²) however many rows arrive, its
//  residual sum of squares is the fold of the squared residual each row leaves
//  behind, and it uses only `+ − × ÷ √` in one order — so its bits are the
//  same on every host. The batch Householder factorizations are secondary:
//  pinned in their own order in Fuaran.Compute.LinearAlgebra's `QR`,
//  documented as NOT byte-equal to the streaming kernel, and held to it by a
//  tolerance law in `RegressionLaws`. This file left LinearAlgebra's `QR.fs`
//  for a package of its own in Phase 632 (DECISIONS.md D75).
// ============================================================================

/// The streaming least-squares state (DECISIONS.md D49 items 8 and 9): the upper triangle `R` of the
/// weighted design rows seen so far, the first `cols` entries of `Qᵀy`, the residual sum of squares
/// and the count of rows of positive weight. Immutable; `QRAccumulator.addRow` and `addRows` answer a
/// new state. Its size is O(cols²) whatever the number of rows.
///
/// The state is held as `cols` rows of `cols + 1` floats — row `j` of `R` beside entry `j` of `Qᵀy` —
/// so rotating a design row into row `j` of `R` and the response into `Qᵀy` is one contiguous pass.
[<Sealed>]
type QRAccumulator internal (cols: int, count: int, state: float[], rss: float) =

    /// The number of design columns, `p`.
    member _.Cols: int = cols

    /// The number of rows of positive weight folded in, `n`.
    member _.Count: int = count

    /// The residual sum of squares of the rows folded in so far, `NaN` canonical.
    member _.Rss: float = Kernels.canonical rss

    /// `cols` rows of `cols + 1`: `R[j, k]` at `j * (cols + 1) + k`, `(Qᵀy)[j]` at `j * (cols + 1) + cols`.
    /// Never written once the accumulator is built.
    member internal _.State: float[] = state

    /// The raw residual sum of squares, for the codec and the solve.
    member internal _.RawRss: float = rss

/// Construction, the row folds and the reads of a `QRAccumulator`.
[<RequireQualifiedAccess>]
module QRAccumulator =

    /// The state with no rows: `R` and `Qᵀy` all `+0.0`, the residual sum of squares `+0.0`, the count
    /// zero. Raises `ArgumentOutOfRangeException` on a negative column count: a shape is an argument.
    let empty (cols: int) : QRAccumulator =
        if cols < 0 then
            raise (System.ArgumentOutOfRangeException "cols")

        QRAccumulator(cols, 0, Array.zeroCreate<float> (cols * (cols + 1)), 0.0)

    /// The number of design columns.
    let cols (acc: QRAccumulator) : int = acc.Cols

    /// The number of rows of positive weight folded in.
    let count (acc: QRAccumulator) : int = acc.Count

    /// The residual sum of squares: the squared residual each row left behind, folded in row order
    /// from `+0.0` (DECISIONS.md D49 item 1).
    let rss (acc: QRAccumulator) : float = acc.Rss

    /// `R`, `cols` by `cols`, upper triangular with every diagonal entry non-negative (D49 item 8) and
    /// every entry below the diagonal `+0.0`. A plain matrix: `UpperTriangular.ofMatrix` checks it, and
    /// refuses it as `NotFinite` when an overflowing input left an infinity in it.
    let r (acc: QRAccumulator) : Matrix =
        let p = acc.Cols
        let w = p + 1
        Matrix.init p p (fun i j -> if i > j then 0.0 else acc.State[i * w + j])

    /// The first `cols` entries of `Qᵀy`.
    let qty (acc: QRAccumulator) : Vector<float> =
        let p = acc.Cols
        let w = p + 1
        Kernels.adopt (Array.init p (fun j -> acc.State[j * w + p]))

    /// The checks a row passes before any rotation, in this order (D49 items 9 and 13): its weight is
    /// finite and not negative (`InvalidWeight`), then every design cell, columns ascending, and the
    /// response, as column `cols`, is finite (`NotFinite`). `row` is the index the refusal names.
    let private checkRow
        (row: int)
        (p: int)
        (weight: float)
        (cell: int -> float)
        (y: float)
        : Result<unit, LinAlgError> =
        if System.Double.IsNaN weight || System.Double.IsInfinity weight || weight < 0.0 then
            Error(LinAlgError.InvalidWeight row)
        else
            let finite (v: float) =
                not (System.Double.IsNaN v || System.Double.IsInfinity v)

            let mutable bad = -1
            let mutable k = 0

            while bad < 0 && k < p do
                if not (finite (cell k)) then
                    bad <- k

                k <- k + 1

            if bad >= 0 then
                Error(LinAlgError.NotFinite(row, bad))
            elif not (finite y) then
                Error(LinAlgError.NotFinite(row, p))
            else
                Ok()

    /// Rotate the augmented row `buf` (`cols` design cells then the response, already scaled by `√w`)
    /// into `state`, columns ascending (D49 item 8): for each `j` whose cell is nonzero, the rotation
    /// taking `(R[j,j], buf[j])` to `(r, 0)` sets `R[j,j] ← r` — so the diagonal is only ever replaced
    /// by a radius and stays non-negative — and rotates the pairs `(R[j,k], buf[k])` for
    /// `k = j + 1 .. cols`, the last being `((Qᵀy)[j], y)`. A zero cell (either sign) is the identity
    /// rotation and changes nothing. Answers the residual left in the response slot.
    let internal rotateIn
        (rotate: float -> float -> float[] -> int -> float[] -> int -> int -> unit)
        (p: int)
        (state: float[])
        (buf: float[])
        : float =
        let w = p + 1

        for j in 0 .. p - 1 do
            let b = buf[j]

            if b <> 0.0 then
                let d = j * w + j
                let a = state[d]
                let radius = Kernels.norm2Pair a b
                let c = a / radius
                let s = b / radius
                state[d] <- radius
                buf[j] <- 0.0
                rotate c s state (d + 1) buf (j + 1) (p - j)

        buf[p]

    /// Fold `rows` rows into a COPY of `acc`'s state, reading row `i` through `cell i k` (column `k`),
    /// `response i` and `weight i`; refuse at the first row that fails `checkRow`, naming its index,
    /// with nothing folded. The one fold `addRow` and `addRows` share, over a chosen rotation member.
    let internal foldRowsWith
        (rotate: float -> float -> float[] -> int -> float[] -> int -> int -> unit)
        (rows: int)
        (cell: int -> int -> float)
        (response: int -> float)
        (weight: int -> float)
        (acc: QRAccumulator)
        : Result<QRAccumulator, LinAlgError> =
        let p = acc.Cols
        let state = Array.copy acc.State
        let buf = Array.zeroCreate<float> (p + 1)
        let mutable count = acc.Count
        let mutable rss = acc.RawRss
        let mutable refusal = None
        let mutable i = 0

        while refusal.IsNone && i < rows do
            let w = weight i
            let y = response i

            match checkRow i p w (cell i) y with
            | Error e -> refusal <- Some e
            | Ok() ->
                // A zero weight (either sign) rotates nothing and is not counted (D49 item 9).
                if w > 0.0 then
                    let sw = sqrt w

                    for k in 0 .. p - 1 do
                        buf[k] <- cell i k * sw

                    buf[p] <- y * sw
                    let e = rotateIn rotate p state buf
                    let e2 = e * e
                    rss <- rss + e2
                    count <- count + 1

            i <- i + 1

        match refusal with
        | Some e -> Error e
        | None -> Ok(QRAccumulator(p, count, state, rss))

    /// Fold one row — its design cells `x`, its response `y` and its prior weight — into the state
    /// (D49 items 8 and 9). The row and response are scaled by `√weight`, correctly rounded, before
    /// they are rotated in; a weight of `1` scales nothing. A zero weight is accepted, rotates nothing
    /// and is not counted. Refused, with the state unchanged: `x` not `cols` long (`ShapeMismatch`); a
    /// negative, NaN or infinite weight (`InvalidWeight 0`); a NaN or infinite cell (`NotFinite (0,
    /// column)`, the response being column `cols`).
    let addRow (x: Vector<float>) (y: float) (weight: float) (acc: QRAccumulator) : Result<QRAccumulator, LinAlgError> =
        if x.Length <> acc.Cols then
            Error(
                LinAlgError.ShapeMismatch(
                    "QRAccumulator.addRow",
                    { Rows = acc.Cols; Cols = 1 },
                    { Rows = x.Length; Cols = 1 }
                )
            )
        else
            let xa, xo = Kernels.lend x
            foldRowsWith Kernels.rotateNative 1 (fun _ k -> xa[xo + k]) (fun _ -> y) (fun _ -> weight) acc

    /// Fold the rows of `x`, with responses `y` and prior weights `weights`, in row order — exactly
    /// `addRow` row by row, byte for byte, which `LinearAlgebraLaws` holds. Refused, with the state
    /// unchanged, on a shape mismatch (`x` not `cols` wide, `y` or `weights` not `rows x` long) or at
    /// the first row `addRow` would refuse, naming that row's index in `x`.
    let addRows
        (x: Matrix)
        (y: Vector<float>)
        (weights: Vector<float>)
        (acc: QRAccumulator)
        : Result<QRAccumulator, LinAlgError> =
        let vd (v: Vector<float>) : Dims = { Rows = v.Length; Cols = 1 }

        if x.Cols <> acc.Cols then
            Error(LinAlgError.ShapeMismatch("QRAccumulator.addRows", { Rows = x.Rows; Cols = acc.Cols }, x.Dims))
        elif y.Length <> x.Rows then
            Error(LinAlgError.ShapeMismatch("QRAccumulator.addRows", x.Dims, vd y))
        elif weights.Length <> x.Rows then
            Error(LinAlgError.ShapeMismatch("QRAccumulator.addRows", x.Dims, vd weights))
        else
            let xa, xo = Kernels.lend x.Storage
            let ya, yo = Kernels.lend y
            let wa, wo = Kernels.lend weights
            let n = x.Rows

            foldRowsWith
                Kernels.rotateNative
                n
                (fun i k -> xa[xo + k * n + i])
                (fun i -> ya[yo + i])
                (fun i -> wa[wo + i])
                acc

/// Which heteroskedasticity-consistent ("sandwich") covariance `LeastSquares.robustStandardErrors`
/// forms (Phase 598, DECISIONS.md D57): `(RᵀR)⁻¹ · Σᵢ ψᵢψᵢᵀ · (RᵀR)⁻¹` with the score
/// `ψᵢ = wᵢ · rᵢ · xᵢ`, each row's score adjusted by the variant. A caller that asks for robust errors
/// without naming a variant is given `HC3` (D57).
[<RequireQualifiedAccess>]
type RobustVariant =
    /// White's estimator: each row's score as it stands.
    | HC0
    /// HC0 scaled by `n / (n − rank)`.
    | HC1
    /// Each row's score divided by `√(1 − hᵢ)`, `hᵢ` the row's hat diagonal.
    | HC2
    /// Each row's score divided by `1 − hᵢ`.
    | HC3

/// A least-squares fit read off a `QRAccumulator` (DECISIONS.md D49 item 11): the coefficients, with
/// `None` at an aliased column (the `NA` convention of R's `lm`), the residual sum of squares, the
/// rank and what the covariance needs.
[<Sealed>]
type LeastSquaresFit
    internal
    (
        coefficients: float option list,
        kept: int list,
        aliased: int list,
        rss: float,
        count: int,
        r: UpperTriangular,
        rInverse: Matrix,
        qty: Vector<float>
    ) =

    /// One coefficient per design column, in column order; `None` at an aliased column.
    member _.Coefficients: float option list = coefficients

    /// The original indices of the columns kept, ascending; `rank` of them.
    member _.Kept: int list = kept

    /// The original indices of the aliased columns, ascending.
    member _.Aliased: int list = aliased

    /// The number of columns kept.
    member _.Rank: int = List.length kept

    /// The residual sum of squares: the accumulator's, then the squared residual each aliased column's
    /// removal released, folded in removal order.
    member _.Rss: float = rss

    /// The rows of positive weight, `n`.
    member _.Count: int = count

    /// The residual degrees of freedom, `n − rank`.
    member _.ResidualDf: int = count - List.length kept

    /// `R` over the kept columns, `rank` by `rank`, every diagonal entry positive.
    member _.R: UpperTriangular = r

    /// `R⁻¹`, `rank` by `rank` and upper triangular, each column `j` the back substitution of the unit
    /// vector `e_j` (`UpperTriangular.solveUpper`'s order): the unscaled covariance of the kept
    /// coefficients is `R⁻¹ R⁻ᵀ`.
    member _.RInverse: Matrix = rInverse

    /// The first `rank` entries of `Qᵀy` over the kept columns (the effects), from which `R` solves.
    member _.Qty: Vector<float> = qty

/// Least squares over the streaming factorization.
[<RequireQualifiedAccess>]
module LeastSquares =

    /// The fit the accumulator's rows determine (D49 item 11). In column order, a column whose diagonal
    /// entry is negligible — `|R[j,j]| ≤ τ · norm2 R[0..j, j]` with `τ = max(count, cols) · 2⁻⁵²` — is
    /// aliased: it is removed, the columns after it are re-triangularised with the one rotation (rows
    /// `c` and `c + 1` for `c` ascending, the pairs to the right of the diagonal and `Qᵀy` rotated as the
    /// accumulator rotates them), the `Qᵀy` entry the removal releases is squared into the residual sum
    /// of squares, and the test resumes at the same position. The kept triangle is then solved by
    /// `UpperTriangular.solveUpper` and inverted column by column the same way. Refused as `NotFinite`
    /// (the first entry of `[R | Qᵀy]` in column-major order) when an overflowing row left a non-finite
    /// value in the state. A column of zeros is aliased, so `Singular` does not arise here.
    let ofAccumulator (acc: QRAccumulator) : Result<LeastSquaresFit, LinAlgError> =
        let p = acc.Cols
        let sw = p + 1
        let state = acc.State

        let firstBad =
            seq {
                for j in 0..p do
                    for i in 0 .. (min j (p - 1)) do
                        let v = state[i * sw + j]

                        if System.Double.IsNaN v || System.Double.IsInfinity v then
                            yield i, j
            }
            |> Seq.tryHead

        match firstBad with
        | Some(i, j) -> Error(LinAlgError.NotFinite(i, j))
        | None ->
            let tau = Rank.tolerance acc.Count p
            // The working triangle, column-major over `m` kept columns plus `Qᵀy` as column `m`, `p` rows.
            let mutable m = p

            let work =
                Array.init (p * sw) (fun k ->
                    let j, i = k / p, k % p
                    if i > j && j < p then 0.0 else state[i * sw + j])

            let kept = ResizeArray<int>(List.init p id)
            let aliased = ResizeArray<int>()
            let mutable rss = acc.RawRss
            let mutable pos = 0
            let row = Array.zeroCreate<float> sw
            let below = Array.zeroCreate<float> sw

            while pos < m do
                if Rank.negligible tau work[pos * p + pos] work (pos * p) (pos + 1) then
                    aliased.Add kept[pos]
                    kept.RemoveAt pos
                    // Shift the columns after `pos`, and `Qᵀy`, one place left.
                    for j in pos .. m - 1 do
                        for i in 0 .. p - 1 do
                            work[j * p + i] <- work[(j + 1) * p + i]

                    // Rows `pos .. m − 1` are upper Hessenberg: rotate row `c + 1` into row `c`.
                    for c in pos .. m - 2 do
                        let b = work[c * p + c + 1]

                        if b <> 0.0 then
                            let a = work[c * p + c]
                            let radius = Kernels.norm2Pair a b
                            let cs = a / radius
                            let sn = b / radius
                            work[c * p + c] <- radius
                            work[c * p + c + 1] <- 0.0
                            // The pairs (R[c,k], R[c+1,k]) for k = c + 1 .. m − 1, and (Qᵀy)[c], (Qᵀy)[c+1].
                            let width = m - 1 - c

                            for t in 0 .. width - 1 do
                                row[t] <- work[(c + 1 + t) * p + c]
                                below[t] <- work[(c + 1 + t) * p + c + 1]

                            Kernels.rotateNative cs sn row 0 below 0 width

                            for t in 0 .. width - 1 do
                                work[(c + 1 + t) * p + c] <- row[t]
                                work[(c + 1 + t) * p + c + 1] <- below[t]

                    // Row `m − 1` now holds only its `Qᵀy` entry: the residual the removal released.
                    let released = work[(m - 1) * p + m - 1]
                    let r2 = released * released
                    rss <- rss + r2

                    for i in 0 .. p - 1 do
                        work[m * p + i] <- 0.0

                    m <- m - 1
                else
                    pos <- pos + 1

            let rk = Matrix.init m m (fun i j -> if i > j then 0.0 else work[j * p + i])
            let effects = Kernels.adopt (Array.init m (fun i -> work[m * p + i]))

            UpperTriangular.ofMatrix rk
            |> Result.bind (fun r ->
                UpperTriangular.solveUpper r effects
                |> Result.bind (fun beta ->
                    let unit j =
                        Vector.adopt (Array.init m (fun i -> if i = j then 1.0 else 0.0))

                    let rec inverse j acc =
                        if j < 0 then
                            Ok acc
                        else
                            UpperTriangular.solveUpper r (unit j)
                            |> Result.bind (fun c -> inverse (j - 1) (c :: acc))

                    inverse (m - 1) []
                    |> Result.bind (fun columns ->
                        Matrix.ofColumns columns
                        |> Result.map (fun inv ->
                            let keptList = List.ofSeq kept

                            let coefficients =
                                [ for j in 0 .. p - 1 ->
                                      match List.tryFindIndex ((=) j) keptList with
                                      | Some t -> Some beta[t]
                                      | None -> None ]

                            LeastSquaresFit(
                                coefficients,
                                keptList,
                                List.sort (List.ofSeq aliased),
                                Kernels.canonical rss,
                                acc.Count,
                                r,
                                inv,
                                effects
                            )))))

    /// The least-squares fit of `y` on the columns of `x`, every row of unit weight: the rows folded
    /// into an empty accumulator in order, then `ofAccumulator`. Refused on a shape mismatch, on a NaN or
    /// infinite cell (`NotFinite (row, column)`, the response as column `cols x`) and as
    /// `ofAccumulator` refuses.
    let solve (x: Matrix) (y: Vector<float>) : Result<LeastSquaresFit, LinAlgError> =
        if y.Length <> x.Rows then
            Error(LinAlgError.ShapeMismatch("LeastSquares.solve", x.Dims, { Rows = y.Length; Cols = 1 }))
        else
            QRAccumulator.empty x.Cols
            |> QRAccumulator.addRows x y (Vector.adopt (Array.create x.Rows 1.0))
            |> Result.bind ofAccumulator

    /// The fit when no column is aliased; otherwise `RankDeficient` naming the rank.
    let requireFullRank (fit: LeastSquaresFit) : Result<LeastSquaresFit, LinAlgError> =
        if List.isEmpty fit.Aliased then
            Ok fit
        else
            Error(LinAlgError.RankDeficient fit.Rank)

    /// The classical standard errors under dispersion `phi`, one per design column: at a kept column,
    /// the `t`-th, `√(phi · Σₖ R⁻¹[t,k]²)` with `k` ascending from `t` (`R⁻¹` is upper triangular, so the
    /// sum is `diag((RᵀR)⁻¹)[t]`); `None` at an aliased column (DECISIONS.md D53 item 8, Phase 595).
    let standardErrors (phi: float) (fit: LeastSquaresFit) : float option list =
        let kept = fit.Kept |> List.toArray
        let inv = fit.RInverse
        let se = Array.create (kept.Length + fit.Aliased.Length) None

        for t in 0 .. kept.Length - 1 do
            let mutable c = 0.0

            for k in t .. kept.Length - 1 do
                let v = Matrix.item t k inv
                c <- c + v * v

            se[kept[t]] <- Some(Kernels.canonical (sqrt (phi * c)))

        List.ofArray se

    /// The heteroskedasticity-consistent standard errors of `fit` (Phase 598, DECISIONS.md D57), one
    /// per design column: the square roots of the diagonal of `(RᵀR)⁻¹ · Σᵢ ψᵢψᵢᵀ · (RᵀR)⁻¹` over the
    /// fit's `R`, with the score `ψᵢ = wᵢ · rᵢ · xᵢ` over the kept columns. `x` is the design the fit
    /// was folded from, `weights` the weights its rows were scaled by (`√w`) and `residuals` the
    /// residuals the scores are stated in — for least squares the response residuals `y − ŷ`, for a
    /// generalised linear model its working weights and working residuals (`Glm.robustStandardErrors`).
    ///
    /// One pass over the rows of positive weight, in row order — a zero-weight row is not counted —
    /// each a fixed sequence: `u = R⁻ᵀxᵢ` (`u[t] = Σₖ R⁻¹[k,t]·xᵢ[k]`, `k` ascending to `t`, from `+0`);
    /// the hat diagonal `hᵢ = wᵢ · Σₜ u[t]²` (`t` ascending); the score's multiplier `s = wᵢ·rᵢ`, then
    /// `s / √(1 − hᵢ)` under `HC2` and `s / (1 − hᵢ)` under `HC3`; `v[t] = s · Σₖ R⁻¹[t,k]·u[k]` (`k`
    /// ascending from `t`), which is `(RᵀR)⁻¹ψᵢ`; and `v[t]²` added to the `t`-th sum. `HC1` multiplies
    /// each sum by `n / (n − rank)`, `n` the rows of positive weight. The error is the square root.
    ///
    /// `None` at an aliased column; everywhere under `HC1` when `n ≤ rank`, and under `HC2` and `HC3`
    /// when a row's leverage is one or more (`1 − hᵢ ≤ 0`, where the row's adjusted score is `0 / 0`).
    /// Refused on a shape mismatch, a negative or non-finite weight (`InvalidWeight`), and a non-finite
    /// design cell or residual at a row of positive weight (`NotFinite (row, column)`, the residual as
    /// column `cols x`).
    let robustStandardErrors
        (variant: RobustVariant)
        (x: Matrix)
        (weights: Vector<float>)
        (residuals: Vector<float>)
        (fit: LeastSquaresFit)
        : Result<float option list, LinAlgError> =
        let rows = x.Rows
        let cols = fit.Rank + fit.Aliased.Length

        let finite (v: float) =
            not (System.Double.IsNaN v || System.Double.IsInfinity v)

        if x.Cols <> cols then
            Error(LinAlgError.ShapeMismatch("LeastSquares.robustStandardErrors", x.Dims, { Rows = cols; Cols = 1 }))
        elif weights.Length <> rows || residuals.Length <> rows then
            Error(
                LinAlgError.ShapeMismatch(
                    "LeastSquares.robustStandardErrors",
                    x.Dims,
                    { Rows =
                        (if weights.Length <> rows then
                             weights.Length
                         else
                             residuals.Length)
                      Cols = 1 }
                )
            )
        else
            let w = Vector.toArray weights
            let r = Vector.toArray residuals
            let xs = Matrix.toColumnMajor x
            let kept = fit.Kept |> List.toArray
            let m = kept.Length
            let inv = Matrix.toColumnMajor fit.RInverse
            let sums = Array.zeroCreate<float> m
            let u = Array.zeroCreate<float> m
            let mutable n = 0
            let mutable undefined = false
            let mutable refusal = None
            let mutable i = 0

            while Option.isNone refusal && i < rows do
                let wi = w[i]

                if not (finite wi) || wi < 0.0 then
                    refusal <- Some(LinAlgError.InvalidWeight i)
                elif wi > 0.0 then
                    let mutable j = 0

                    while Option.isNone refusal && j < cols do
                        if not (finite xs[j * rows + i]) then
                            refusal <- Some(LinAlgError.NotFinite(i, j))

                        j <- j + 1

                    if Option.isNone refusal && not (finite r[i]) then
                        refusal <- Some(LinAlgError.NotFinite(i, cols))

                    if Option.isNone refusal then
                        n <- n + 1

                        for t in 0 .. m - 1 do
                            let mutable acc = 0.0

                            for k in 0..t do
                                acc <- acc + inv[t * m + k] * xs[kept[k] * rows + i]

                            u[t] <- acc

                        let mutable q = 0.0

                        for t in 0 .. m - 1 do
                            q <- q + u[t] * u[t]

                        let h = wi * q
                        let s0 = wi * r[i]

                        let s =
                            match variant with
                            | RobustVariant.HC0
                            | RobustVariant.HC1 -> s0
                            | RobustVariant.HC2
                            | RobustVariant.HC3 when 1.0 - h <= 0.0 ->
                                undefined <- true
                                0.0
                            | RobustVariant.HC2 -> s0 / sqrt (1.0 - h)
                            | RobustVariant.HC3 -> s0 / (1.0 - h)

                        for t in 0 .. m - 1 do
                            let mutable acc = 0.0

                            for k in t .. m - 1 do
                                acc <- acc + inv[k * m + t] * u[k]

                            let v = s * acc
                            sums[t] <- sums[t] + v * v

                i <- i + 1

            match refusal with
            | Some e -> Error e
            | None ->
                let se = Array.create cols None

                let scale =
                    match variant with
                    | RobustVariant.HC1 -> if n > m then Some(float n / float (n - m)) else None
                    | _ -> if undefined then None else Some 1.0

                match scale with
                | None -> ()
                | Some c ->
                    for t in 0 .. m - 1 do
                        let v =
                            match variant with
                            | RobustVariant.HC1 -> sums[t] * c
                            | _ -> sums[t]

                        se[kept[t]] <- Some(Kernels.canonical (sqrt v))

                Ok(List.ofArray se)
