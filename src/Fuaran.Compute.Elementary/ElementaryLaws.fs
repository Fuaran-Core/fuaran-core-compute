namespace Fuaran.Compute.Elementary

open System

/// `elementaryLaws` (Phase 593; its own package since Phase 630): the laws the pinned elementary
/// functions are held to, on whichever host runs them — every row of D43's special-value table and
/// IEEE-754's edge rules answered as stated, `pow`'s exact cases byte for byte, and the inverses and
/// functional equations within the bounds a one-ulp routine admits.
///
/// Every input is drawn by a pinned generator whose every step is an exact float operation, so a seed
/// draws the same numbers on every host, and running the laws under Fable runs them over the same
/// numbers as on .NET. The generator is the one `LinearAlgebraLaws` draws with, so a seed draws here
/// what it drew there before the move (DECISIONS.md D74). A law answers `Ok ()` when it held at every
/// iteration, else the first failure with its iteration. FSharp.Core only; the `Result<unit, string>`
/// verdict is the substrate codec laws' own (`Corpus.codecLaws`).
[<RequireQualifiedAccess>]
module ElementaryLaws =

    /// The unit roundoff of binary64, `2⁻⁵³`.
    let private u: float = 1.0 / 9007199254740992.0

    let private bits (x: float) : int64 = BitConverter.DoubleToInt64Bits x

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

    /// The laws, by name, each run at `count` iterations drawn from `seed`.
    let laws (seed: int) (count: int) : (string * Result<unit, string>) list =
        [ "every special-value row and IEEE edge of the elementary functions is answered as stated", elementaryTables
          "pow's exact cases and the sign of a negative base are the named operations, byte for byte", powExactCases
          "exp and ln invert within their one-ulp bounds", elementaryInverses
          "the elementary functional equations hold within their bounds", elementaryIdentities ]
        |> List.map (fun (name, body) -> name, law seed count body)
