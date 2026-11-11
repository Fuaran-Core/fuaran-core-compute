namespace Fuaran.Compute.Distributions

open System
open Fuaran.Compute.Elementary

/// The laws the pinned distribution functions are held to, on whichever host runs them (Phase 618,
/// DECISIONS.md D79): every row of the special-value table answered as stated, the normal pair the
/// elementary package's own bit for bit, each quantile inverted by its distribution function, the
/// identity between the F distribution with one numerator degree of freedom and the square of a t,
/// the t's symmetry, and no distribution function falling as its argument rises.
///
/// Every input is drawn by a pinned generator whose every step is an exact float operation, so a seed
/// draws the same numbers on every host, and running the laws under Fable runs them over the same
/// numbers as on .NET. The generator is the one `ElementaryLaws` draws with. A law answers `Ok ()`
/// when it held at every iteration, else the first failure with its iteration. Each bound is stated
/// on its law in units of `u = 2⁻⁵³`; they are bounds over the drawn ranges (parameters from one
/// quarter to five hundred and twelve), and the accuracy against the reference is the vectors' to
/// hold.
[<RequireQualifiedAccess>]
module DistributionsLaws =

    /// The unit roundoff of binary64, `2⁻⁵³`.
    let private u: float = 1.0 / 9007199254740992.0

    let private bits (x: float) : int64 = BitConverter.DoubleToInt64Bits x

    /// The pinned generator: Park and Miller's minimal standard, run in float arithmetic. Every
    /// intermediate is an integer below `2⁵³`, so each step is exact on every host.
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

    /// The first of `checks` that is false, named, with the inputs it failed at.
    let private firstFailure (inputs: string) (checks: (string * bool) list) : string option =
        checks
        |> List.tryFind (fun (_, held) -> not held)
        |> Option.map (fun (name, _) -> sprintf "%s failed at %s" name inputs)

    /// A parameter of magnitude `2^e · (1 + m)` for a drawn `e` in `−2 .. 8`: one quarter to 512.
    let private parameter (d: Draw) : float =
        pow2 (d.Int -2 8) * (1.0 + (d.Unit() + 1.0) * 0.5)

    /// A probability in `[0.001, 0.999]`.
    let private probability (d: Draw) : float = 0.5 + d.Unit() * 0.499

    /// Every row of the special-value table (DECISIONS.md D79 item 7).
    let private specialValues (_: Draw) =
        let inf = Double.PositiveInfinity
        let nan = Double.NaN
        let isNaN (x: float) = Double.IsNaN x
        let is (expected: float) (x: float) = bits x = bits expected

        firstFailure
            "the table"
            [ "Normal.cdf NaN", isNaN (Normal.cdf nan)
              "Normal.cdf -inf", is 0.0 (Normal.cdf -inf)
              "Normal.cdf +inf", is 1.0 (Normal.cdf inf)
              "Normal.quantile 0", is -inf (Normal.quantile 0.0)
              "Normal.quantile 1", is inf (Normal.quantile 1.0)
              "Normal.quantile 1/2", is 0.0 (Normal.quantile 0.5)
              "Normal.quantile below 0", isNaN (Normal.quantile -0.25)
              "Normal.quantile above 1", isNaN (Normal.quantile 1.25)
              "Normal.quantile NaN", isNaN (Normal.quantile nan)
              "StudentT.cdf NaN", isNaN (StudentT.cdf 3.0 nan)
              "StudentT.cdf nu NaN", isNaN (StudentT.cdf nan 1.0)
              "StudentT.cdf nu = 0", isNaN (StudentT.cdf 0.0 1.0)
              "StudentT.cdf nu < 0", isNaN (StudentT.cdf -2.0 1.0)
              "StudentT.cdf nu = inf", isNaN (StudentT.cdf inf 1.0)
              "StudentT.cdf -inf", is 0.0 (StudentT.cdf 3.0 -inf)
              "StudentT.cdf +inf", is 1.0 (StudentT.cdf 3.0 inf)
              "StudentT.cdf 0", is 0.5 (StudentT.cdf 3.0 0.0)
              "StudentT.cdf -0", is 0.5 (StudentT.cdf 3.0 -0.0)
              "StudentT.quantile 0", is -inf (StudentT.quantile 3.0 0.0)
              "StudentT.quantile 1", is inf (StudentT.quantile 3.0 1.0)
              "StudentT.quantile 1/2", is 0.0 (StudentT.quantile 3.0 0.5)
              "StudentT.quantile below 0", isNaN (StudentT.quantile 3.0 -0.25)
              "StudentT.quantile above 1", isNaN (StudentT.quantile 3.0 1.25)
              "StudentT.quantile NaN", isNaN (StudentT.quantile 3.0 nan)
              "StudentT.quantile nu = 0", isNaN (StudentT.quantile 0.0 0.25)
              "StudentT.quantile nu < 0", isNaN (StudentT.quantile -1.0 0.25)
              "StudentT.quantile nu = inf", isNaN (StudentT.quantile inf 0.25)
              "StudentT.quantile nu NaN", isNaN (StudentT.quantile nan 0.25)
              "ChiSquare.cdf NaN", isNaN (ChiSquare.cdf 3.0 nan)
              "ChiSquare.cdf k = 0", isNaN (ChiSquare.cdf 0.0 1.0)
              "ChiSquare.cdf k < 0", isNaN (ChiSquare.cdf -1.0 1.0)
              "ChiSquare.cdf k = inf", isNaN (ChiSquare.cdf inf 1.0)
              "ChiSquare.cdf k NaN", isNaN (ChiSquare.cdf nan 1.0)
              "ChiSquare.cdf 0", is 0.0 (ChiSquare.cdf 3.0 0.0)
              "ChiSquare.cdf below 0", is 0.0 (ChiSquare.cdf 3.0 -1.0)
              "ChiSquare.cdf -inf", is 0.0 (ChiSquare.cdf 3.0 -inf)
              "ChiSquare.cdf +inf", is 1.0 (ChiSquare.cdf 3.0 inf)
              "ChiSquare.quantile 0", is 0.0 (ChiSquare.quantile 3.0 0.0)
              "ChiSquare.quantile 1", is inf (ChiSquare.quantile 3.0 1.0)
              "ChiSquare.quantile below 0", isNaN (ChiSquare.quantile 3.0 -0.25)
              "ChiSquare.quantile above 1", isNaN (ChiSquare.quantile 3.0 1.25)
              "ChiSquare.quantile NaN", isNaN (ChiSquare.quantile 3.0 nan)
              "ChiSquare.quantile k = 0", isNaN (ChiSquare.quantile 0.0 0.25)
              "ChiSquare.quantile k = inf", isNaN (ChiSquare.quantile inf 0.25)
              "FisherF.cdf NaN", isNaN (FisherF.cdf 3.0 4.0 nan)
              "FisherF.cdf d1 = 0", isNaN (FisherF.cdf 0.0 4.0 1.0)
              "FisherF.cdf d2 < 0", isNaN (FisherF.cdf 3.0 -4.0 1.0)
              "FisherF.cdf d1 = inf", isNaN (FisherF.cdf inf 4.0 1.0)
              "FisherF.cdf d2 NaN", isNaN (FisherF.cdf 3.0 nan 1.0)
              "FisherF.cdf 0", is 0.0 (FisherF.cdf 3.0 4.0 0.0)
              "FisherF.cdf below 0", is 0.0 (FisherF.cdf 3.0 4.0 -1.0)
              "FisherF.cdf +inf", is 1.0 (FisherF.cdf 3.0 4.0 inf)
              "FisherF.quantile 0", is 0.0 (FisherF.quantile 3.0 4.0 0.0)
              "FisherF.quantile 1", is inf (FisherF.quantile 3.0 4.0 1.0)
              "FisherF.quantile below 0", isNaN (FisherF.quantile 3.0 4.0 -0.25)
              "FisherF.quantile above 1", isNaN (FisherF.quantile 3.0 4.0 1.25)
              "FisherF.quantile NaN", isNaN (FisherF.quantile 3.0 4.0 nan)
              "FisherF.quantile d1 = 0", isNaN (FisherF.quantile 0.0 4.0 0.25)
              "FisherF.quantile d2 = inf", isNaN (FisherF.quantile 3.0 inf 0.25) ]

    /// `Normal.cdf` and `Normal.quantile` are `Elementary.normalCdf` and `Elementary.normalQuantile`
    /// bit for bit: one implementation under two names.
    let private normalIsElementary (d: Draw) =
        let x = d.Unit() * 40.0
        let p = (d.Unit() + 1.0) * 0.5

        firstFailure
            (sprintf "x = %.17g, p = %.17g" x p)
            [ "Normal.cdf = Elementary.normalCdf", bits (Normal.cdf x) = bits (Elementary.normalCdf x)
              "Normal.quantile = Elementary.normalQuantile",
              bits (Normal.quantile p) = bits (Elementary.normalQuantile p) ]

    /// The distribution function of a quantile is `p` within `2⁻⁴⁴`, for each family, parameters from
    /// one quarter to 512 and `p` in `[0.001, 0.999]`.
    let private quantilesInvert (d: Draw) =
        let nu = parameter d
        let k = parameter d
        let d1 = parameter d
        let d2 = parameter d
        let p = probability d
        let bound = pow2 -44
        let near (back: float) = abs (back - p) <= bound

        firstFailure
            (sprintf "nu = %.17g, k = %.17g, d1 = %.17g, d2 = %.17g, p = %.17g" nu k d1 d2 p)
            [ "Normal.cdf (Normal.quantile p) = p", near (Normal.cdf (Normal.quantile p))
              "StudentT.cdf (StudentT.quantile p) = p", near (StudentT.cdf nu (StudentT.quantile nu p))
              "ChiSquare.cdf (ChiSquare.quantile p) = p", near (ChiSquare.cdf k (ChiSquare.quantile k p))
              "FisherF.cdf (FisherF.quantile p) = p", near (FisherF.cdf d1 d2 (FisherF.quantile d1 d2 p)) ]

    /// The square of a t variate with `ν` degrees of freedom is an F variate with `1` and `ν`:
    /// `FisherF.cdf 1 ν (t²) = 2 · StudentT.cdf ν |t| − 1` within `2⁻⁴⁴`, for `ν` from one quarter to 512
    /// and `|t|` up to sixteen. And the t is symmetric: `cdf ν (−t) + cdf ν t` is one within `2u`, and
    /// the quantile above one half is the negation of the quantile at `1 − p` bit for bit.
    let private tSquaredIsFAndTIsSymmetric (d: Draw) =
        let nu = parameter d
        let t = pow2 (d.Int -8 3) * (1.0 + (d.Unit() + 1.0) * 0.5)
        let p = 0.5 + (d.Unit() + 1.0) * 0.2495
        let upper = StudentT.cdf nu t
        let lower = StudentT.cdf nu -t

        firstFailure
            (sprintf "nu = %.17g, t = %.17g, p = %.17g" nu t p)
            [ "FisherF.cdf 1 nu (t * t) = 2 StudentT.cdf nu t - 1",
              abs (FisherF.cdf 1.0 nu (t * t) - (2.0 * upper - 1.0)) <= pow2 -44
              "StudentT.cdf nu (-t) + StudentT.cdf nu t = 1", abs (lower + upper - 1.0) <= 2.0 * u
              "StudentT.quantile nu p = -StudentT.quantile nu (1 - p)",
              bits (StudentT.quantile nu p) = bits (-(StudentT.quantile nu (1.0 - p))) ]

    /// No distribution function falls as its argument rises, beyond `2⁻⁴⁴` of its own value: each is read
    /// at a drawn point and at one up to one part in a hundred above it.
    let private nothingFalls (d: Draw) =
        let nu = parameter d
        let k = parameter d
        let d1 = parameter d
        let d2 = parameter d
        let x = d.Unit() * 12.0
        let rise = (d.Unit() + 1.0) * 0.005
        let xUp = x + (abs x + 1.0) * rise
        let y = pow2 (d.Int -10 6) * (1.0 + (d.Unit() + 1.0) * 0.5)
        let yUp = y * (1.0 + rise)
        let slack = 1.0 - pow2 -44
        let rises (low: float) (high: float) = high >= low * slack

        firstFailure
            (sprintf
                "nu = %.17g, k = %.17g, d1 = %.17g, d2 = %.17g, x = %.17g to %.17g, y = %.17g to %.17g"
                nu
                k
                d1
                d2
                x
                xUp
                y
                yUp)
            [ "Normal.cdf does not fall", rises (Normal.cdf x) (Normal.cdf xUp)
              "StudentT.cdf does not fall", rises (StudentT.cdf nu x) (StudentT.cdf nu xUp)
              "ChiSquare.cdf does not fall", rises (ChiSquare.cdf k y) (ChiSquare.cdf k yUp)
              "FisherF.cdf does not fall", rises (FisherF.cdf d1 d2 y) (FisherF.cdf d1 d2 yUp) ]

    /// The laws, by name, each run at `count` iterations drawn from `seed`.
    let laws (seed: int) (count: int) : (string * Result<unit, string>) list =
        [ "every special-value row of the distribution functions is answered as stated", specialValues
          "Normal.cdf and Normal.quantile are the elementary package's functions bit for bit", normalIsElementary
          "the distribution function of a quantile is p", quantilesInvert
          "the square of a t is an F with one numerator degree of freedom, and the t is symmetric",
          tSquaredIsFAndTIsSymmetric
          "no distribution function falls as its argument rises", nothingFalls ]
        |> List.map (fun (name, body) -> name, law seed count body)
