namespace Fuaran.Compute.Elementary

open System

/// `elementaryLaws` (Phase 593; its own package since Phase 630): the laws the pinned elementary
/// functions are held to, on whichever host runs them — every row of D43's special-value table and
/// IEEE-754's edge rules answered as stated, `pow`'s exact cases byte for byte, and the inverses and
/// functional equations within the bounds a one-ulp routine admits. Since Phase 725 the trigonometric
/// functions' tables, exact symmetries and identities are held the same way.
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

    // ---- Phase 621 (DECISIONS.md D76): the pinned generator, `Rng` ----

    /// A drawn 64-bit value: four drawn 16-bit limbs.
    let private drawn64 (d: Draw) : uint64 =
        (uint64 (d.Int 0 65535) <<< 48)
        ||| (uint64 (d.Int 0 65535) <<< 32)
        ||| (uint64 (d.Int 0 65535) <<< 16)
        ||| uint64 (d.Int 0 65535)

    /// SplitMix64 as its authors state it, in the host's own 64-bit integers: the twin the limb
    /// arithmetic is held to. Answers the draw and the state it leaves.
    let private reference (state: uint64) : struct (uint64 * uint64) =
        let s = state + 0x9E3779B97F4A7C15UL
        let z = (s ^^^ (s >>> 30)) * 0xBF58476D1CE4E5B9UL
        let z = (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
        struct (z ^^^ (z >>> 31), s)

    /// The draws the reference implementation publishes for the states 1234567 and 0, the limb product
    /// against the host's own 64-bit product over drawn operands (D76's probe: a host whose product
    /// collapsed would fail here), and a drawn state's first draws against the recurrence in the host's
    /// 64-bit integers.
    let private rngIsSplitMix64 (d: Draw) =
        let published =
            [ 6457827717110365317UL
              3203168211198807973UL
              9817491932198370423UL
              4593380528125082431UL
              16408922859458223821UL ]

        let g = Rng.ofState 1234567UL
        let drawnPublished = published |> List.map (fun _ -> Rng.next g)
        let a = drawn64 d
        let b = drawn64 d
        let state = drawn64 d
        let h = Rng.ofState state
        let mutable s = state
        let mutable diverged = None

        for i in 0..7 do
            let struct (want, s') = reference s
            s <- s'
            let got = Rng.next h

            if got <> want && Option.isNone diverged then
                diverged <- Some(i, got, want)

        if drawnPublished <> published then
            Some(sprintf "the draws from state 1234567 are %A" drawnPublished)
        elif Rng.next (Rng.ofState 0UL) <> 0xE220A8397B1DCDAFUL then
            Some "the first draw from state 0 is not 0xE220A8397B1DCDAF"
        elif Rng.limbProduct a b <> a * b then
            Some(sprintf "the limb product of %d and %d is %d, the host's %d" a b (Rng.limbProduct a b) (a * b))
        else
            diverged
            |> Option.map (fun (i, got, want) ->
                sprintf "draw %d from state %d is %d, the recurrence's %d" i state got want)

    /// A seed's stream `k` starts at the seed's `(k + 1)`-th draw, two streams of the same arguments
    /// draw alike and a different stream differently; `uniform53` is the draw's top fifty-three bits
    /// times `2⁻⁵³`; `below n` is the first draw whose top thirty-two bits clear `2³² mod n`, modulo `n`.
    let private rngStreamsAndRanges (d: Draw) =
        let seed = int64 (drawn64 d)
        // One draw in four names a stream past the index's first limb.
        let stream = if d.Int 0 3 = 0 then 65536 + d.Int 0 50 else d.Int 0 300
        let n = d.Int 1 2000000000
        let root = Rng.ofState (uint64 seed)
        let mutable start = 0UL

        for _ in 0..stream do
            start <- Rng.next root

        let twin = Rng.ofState start
        let g = Rng.ofSeed seed stream
        let first = Rng.next g
        let other = Rng.next (Rng.ofSeed seed (stream + 1))
        let u = Rng.uniform53 g

        let uTwin =
            (ignore (Rng.next twin)
             float (Rng.next twin >>> 11) * (1.0 / 9007199254740992.0))

        let v = Rng.below g n
        let threshold = (4294967296UL - uint64 n) % uint64 n
        let mutable x = Rng.next twin >>> 32

        while x < threshold do
            x <- Rng.next twin >>> 32

        let after = Rng.next g

        if first <> Rng.next (Rng.ofState start) then
            Some(sprintf "stream %d of seed %d does not start at the seed's draw %d" stream seed (stream + 1))
        elif other = first then
            Some(sprintf "streams %d and %d of seed %d draw alike" stream (stream + 1) seed)
        elif bits u <> bits uTwin || u < 0.0 || u >= 1.0 then
            Some(sprintf "uniform53 = %.17g, the draw's top 53 bits give %.17g" u uTwin)
        elif v < 0 || v >= n || uint64 v <> x % uint64 n then
            Some(sprintf "below %d = %d, the rule gives %d" n v (x % uint64 n))
        elif after <> Rng.next twin then
            Some(sprintf "below %d consumed a different number of draws than the rule" n)
        else
            None

    // ---- Phase 631 (DECISIONS.md D78): the special functions, `Special` ----

    /// The first of the named checks that failed, with the inputs it failed at.
    let private firstFailure (inputs: string) (checks: (string * bool) list) : string option =
        match checks |> List.filter (snd >> not) with
        | [] -> None
        | failed -> Some(sprintf "%s at %s" (failed |> List.map fst |> String.concat ", ") inputs)

    /// Every row of the special functions' special-value tables, answered as stated (D78 item 9): a
    /// NaN, an argument outside the domain and the poles of `gamma` and the polygamma functions answer
    /// NaN; `gammaLn` answers `+∞` at its poles; the bounds answer exactly.
    let private specialTables (_: Draw) =
        let nan = Double.NaN
        let inf = Double.PositiveInfinity
        let isNaN (x: float) = Double.IsNaN x
        let is (want: float) (x: float) = bits x = bits want

        firstFailure
            "the tables"
            [ "gammaLn NaN", isNaN (Special.gammaLn nan)
              "gammaLn +inf", is inf (Special.gammaLn inf)
              "gammaLn -inf", is inf (Special.gammaLn -inf)
              "gammaLn 0", is inf (Special.gammaLn 0.0)
              "gammaLn -3", is inf (Special.gammaLn -3.0)
              "gammaLn 1", is 0.0 (Special.gammaLn 1.0)
              "gammaLn 2", is 0.0 (Special.gammaLn 2.0)
              "gamma NaN", isNaN (Special.gamma nan)
              "gamma +inf", is inf (Special.gamma inf)
              "gamma -inf", isNaN (Special.gamma -inf)
              "gamma 0", isNaN (Special.gamma 0.0)
              "gamma -0", isNaN (Special.gamma -0.0)
              "gamma -2", isNaN (Special.gamma -2.0)
              "gamma 172", is inf (Special.gamma 172.0)
              "gamma 1", is 1.0 (Special.gamma 1.0)
              "gamma 5", is 24.0 (Special.gamma 5.0)
              "gamma 23", is 1124000727777607680000.0 (Special.gamma 23.0)
              "beta 0", isNaN (Special.beta 0.0 1.0)
              "beta negative", isNaN (Special.beta 2.0 -1.0)
              "beta inf", isNaN (Special.beta inf 1.0)
              "beta NaN", isNaN (Special.beta 1.0 nan)
              "betaLn 0", isNaN (Special.betaLn 1.0 0.0)
              "betaLn inf", isNaN (Special.betaLn 1.0 inf)
              "betaLn NaN", isNaN (Special.betaLn nan 1.0)
              "P x = 0", is 0.0 (Special.regularizedGammaP 2.5 0.0)
              "P x = inf", is 1.0 (Special.regularizedGammaP 2.5 inf)
              "P a = 0", isNaN (Special.regularizedGammaP 0.0 1.0)
              "P a = inf", isNaN (Special.regularizedGammaP inf 1.0)
              "P x < 0", isNaN (Special.regularizedGammaP 2.0 -1.0)
              "P NaN", isNaN (Special.regularizedGammaP nan 1.0)
              "Q x = 0", is 1.0 (Special.regularizedGammaQ 2.5 0.0)
              "Q x = inf", is 0.0 (Special.regularizedGammaQ 2.5 inf)
              "Q a < 0", isNaN (Special.regularizedGammaQ -1.5 1.0)
              "Q NaN", isNaN (Special.regularizedGammaQ 1.0 nan)
              "I x = 0", is 0.0 (Special.regularizedBeta 2.0 3.0 0.0)
              "I x = 1", is 1.0 (Special.regularizedBeta 2.0 3.0 1.0)
              "I x < 0", isNaN (Special.regularizedBeta 2.0 3.0 -0.1)
              "I x > 1", isNaN (Special.regularizedBeta 2.0 3.0 1.5)
              "I a = 0", isNaN (Special.regularizedBeta 0.0 3.0 0.5)
              "I b = inf", isNaN (Special.regularizedBeta 2.0 inf 0.5)
              "I NaN", isNaN (Special.regularizedBeta 2.0 3.0 nan)
              "inverse p = 0", is 0.0 (Special.inverseRegularizedBeta 2.0 3.0 0.0)
              "inverse p = 1", is 1.0 (Special.inverseRegularizedBeta 2.0 3.0 1.0)
              "inverse p < 0", isNaN (Special.inverseRegularizedBeta 2.0 3.0 -0.5)
              "inverse p > 1", isNaN (Special.inverseRegularizedBeta 2.0 3.0 1.5)
              "inverse b < 0", isNaN (Special.inverseRegularizedBeta 2.0 -3.0 0.5)
              "inverse NaN", isNaN (Special.inverseRegularizedBeta nan 3.0 0.5)
              "digamma NaN", isNaN (Special.digamma nan)
              "digamma +inf", is inf (Special.digamma inf)
              "digamma -inf", isNaN (Special.digamma -inf)
              "digamma 0", isNaN (Special.digamma 0.0)
              "digamma -1", isNaN (Special.digamma -1.0)
              "trigamma 0", isNaN (Special.trigamma 0.0)
              "trigamma negative", isNaN (Special.trigamma -1.5)
              "trigamma +inf", is 0.0 (Special.trigamma inf)
              "polygamma order -1", isNaN (Special.polygamma -1.0 2.0)
              "polygamma order 1.5", isNaN (Special.polygamma 1.5 2.0)
              "polygamma order 171", isNaN (Special.polygamma 171.0 5.0)
              "polygamma 2 at 0", isNaN (Special.polygamma 2.0 0.0)
              "polygamma 2 negative", isNaN (Special.polygamma 2.0 -0.5)
              "polygamma 2 at +inf", is 0.0 (Special.polygamma 2.0 inf)
              "polygamma NaN order", isNaN (Special.polygamma nan 2.0)
              "polygamma NaN", isNaN (Special.polygamma 2.0 nan) ]

    /// `Γ(x + 1)` is `x Γ(x)` within `8u (1 + (x + 1)(|ln (x + 1)| + 1)) Γ(x + 1)` (the second term is
    /// what rounding `x + 1` moves the function by), and `B` and `ln B` are symmetric bit for bit.
    let private gammaRecurrenceAndBetaSymmetry (d: Draw) =
        let x = positive d -10 6
        let up = Special.gamma (x + 1.0)
        let here = Special.gamma x
        let a = positive d -20 20
        let b = positive d -20 20

        firstFailure
            (sprintf "x = %.17g, a = %.17g, b = %.17g" x a b)
            [ "gamma (x + 1) = x gamma x",
              abs (up - x * here)
              <= 8.0 * u * (1.0 + (x + 1.0) * (abs (Elementary.ln (x + 1.0)) + 1.0)) * up
              "beta a b = beta b a", bits (Special.beta a b) = bits (Special.beta b a)
              "betaLn a b = betaLn b a", bits (Special.betaLn a b) = bits (Special.betaLn b a) ]

    /// `P + Q` is one within `64u`; `I_x(a, b) + I_{1 − x}(b, a)` is one within `64u` for an `x` of at
    /// least a half (where `1 − x` is exact); and over a rise in `x` neither `P` nor `I_x` falls by more
    /// than `64u` of itself.
    let private complementsAndMonotonicity (d: Draw) =
        let a = positive d -20 13
        let x = a * pow2 (d.Int -7 3) * (1.0 + (d.Unit() + 1.0) * 0.5)
        let rise = 1.0 + (d.Unit() + 1.0) * 0.005
        let p = Special.regularizedGammaP a x
        let q = Special.regularizedGammaQ a x
        let pUp = Special.regularizedGammaP a (x * rise)
        let ba = positive d -4 8
        let bb = positive d -4 8
        let high = 0.75 + d.Unit() * 0.25
        let t = (d.Unit() + 1.0) * 0.5
        let tUp = t + (1.0 - t) * (d.Unit() + 1.0) * 0.005
        let lower = Special.regularizedBeta ba bb t
        let lowerUp = Special.regularizedBeta ba bb tUp

        firstFailure
            (sprintf "a = %.17g, x = %.17g; a = %.17g, b = %.17g, x = %.17g and %.17g" a x ba bb high t)
            [ "P + Q = 1", abs (p + q - 1.0) <= 64.0 * u
              "P does not fall", pUp >= p * (1.0 - 64.0 * u)
              "I_x(a, b) + I_(1 - x)(b, a) = 1",
              abs (
                  Special.regularizedBeta ba bb high + Special.regularizedBeta bb ba (1.0 - high)
                  - 1.0
              )
              <= 64.0 * u
              "I_x does not fall", lowerUp >= lower * (1.0 - 64.0 * u) ]

    /// The incomplete beta function of its inverse is `p` within `2⁻⁴⁰`, for parameters in `[½, 256)`
    /// and `p` in `[0.001, 0.999]`, and the inverse of `1 − p` under the parameters exchanged is its
    /// mirror within `64u`.
    let private inverseRoundTrip (d: Draw) =
        let a = positive d -1 7
        let b = positive d -1 7
        let p = 0.5 + d.Unit() * 0.499
        let x = Special.inverseRegularizedBeta a b p
        let back = Special.regularizedBeta a b x
        let mirror = Special.inverseRegularizedBeta b a (1.0 - p)

        firstFailure
            (sprintf "a = %.17g, b = %.17g, p = %.17g (x = %.17g, I = %.17g, mirror = %.17g)" a b p x back mirror)
            [ "I (inverse p) = p", abs (back - p) <= pow2 -40
              "inverse a b p = 1 - inverse b a (1 - p)", abs (x + mirror - 1.0) <= 64.0 * u ]

    /// `ψ(x + 1)` is `ψ(x) + 1 / x` within `8u (|ψ(x)| + 1 / x + |ψ(x + 1)| + 2)`; `ψ(1)` is `−γ` and
    /// `ψ′(1)` is `π² / 6`, each within one unit in the last place of the constant; and `digamma` and
    /// `trigamma` are `polygamma 0` and `polygamma 1` bit for bit.
    let private polygammaIdentities (d: Draw) =
        let x = positive d -7 6
        let here = Special.digamma x
        let up = Special.digamma (x + 1.0)
        let y = if d.Unit() < 0.0 then -x - 0.25 else x

        firstFailure
            (sprintf "x = %.17g, y = %.17g" x y)
            [ "digamma (x + 1) = digamma x + 1 / x",
              abs (up - here - 1.0 / x) <= 8.0 * u * (abs here + 1.0 / x + abs up + 2.0)
              "digamma 1 = -gamma", ulpsApart (Special.digamma 1.0) -0.5772156649015329 <= 1L
              "trigamma 1 = pi^2 / 6", ulpsApart (Special.trigamma 1.0) 1.6449340668482264 <= 1L
              "digamma = polygamma 0",
              (let named = Special.digamma y
               let ordered = Special.polygamma 0.0 y
               (Double.IsNaN named && Double.IsNaN ordered) || bits named = bits ordered)
              "trigamma = polygamma 1", bits (Special.trigamma x) = bits (Special.polygamma 1.0 x) ]

    /// A central difference of `gammaLn` at a step of `2⁻¹⁴` agrees with `digamma` within `2⁻²⁶`, over
    /// `x` in `[3, 50)`: the difference's own truncation is below `10⁻¹⁰` there and its rounding below
    /// `2 · 10⁻⁹`.
    let private gammaLnDerivative (d: Draw) =
        let x = 3.0 + (d.Unit() + 1.0) * 23.5
        let h = pow2 -14

        let difference = (Special.gammaLn (x + h) - Special.gammaLn (x - h)) / (2.0 * h)

        let derivative = Special.digamma x

        if abs (difference - derivative) <= pow2 -26 then
            None
        else
            Some(sprintf "at x = %.17g the central difference is %.17g and digamma %.17g" x difference derivative)

    /// The two complements (D81). Their table rows answer as stated. `I_x(a, b)` and its complement sum
    /// to one within `8u`. For an `x` of at least a half, where `1 − x` is exact, the complement is the
    /// mirrored function `I_{1 − x}(b, a)` within `2⁻⁴⁰` of itself. That inverse and the mirrored
    /// function's sum to one within `64u`, and for a `q` of at least a half it is
    /// `inverseRegularizedBeta` at `1 − q`, bit for bit. The parameters of those run from a quarter to
    /// `2¹²`, so both of the function's fractions are read. The complement of its own inverse is `q`
    /// within `2⁻⁴⁰` for parameters in `[1, 64)` and `q` in `[0.001, 0.999]`, where the density at the
    /// root is small enough that the root's own rounding does not show.
    let private betaComplements (d: Draw) =
        let nan = Double.NaN
        let inf = Double.PositiveInfinity
        let isNaN (x: float) = Double.IsNaN x
        let is (want: float) (x: float) = bits x = bits want
        let a = positive d -2 11
        let b = positive d -2 11
        let x = (d.Unit() + 1.0) * 0.5
        let high = 0.5 + (d.Unit() + 1.0) * 0.25
        let lower = Special.regularizedBeta a b x
        let upper = Special.regularizedBetaComplement a b x
        let upperHigh = Special.regularizedBetaComplement a b high
        let mirrored = Special.regularizedBeta b a (1.0 - high)
        let q = 0.5 + d.Unit() * 0.499
        let root = Special.inverseRegularizedBetaComplement a b q
        let ar = positive d 0 5
        let br = positive d 0 5

        let back =
            Special.regularizedBetaComplement ar br (Special.inverseRegularizedBetaComplement ar br q)

        let mirror = Special.inverseRegularizedBeta b a q
        let qHigh = 0.5 + (d.Unit() + 1.0) * 0.2495

        firstFailure
            (sprintf
                "a = %.17g, b = %.17g, x = %.17g and %.17g, q = %.17g and %.17g (root = %.17g); a = %.17g, b = %.17g (back = %.17g)"
                a
                b
                x
                high
                q
                qHigh
                root
                ar
                br
                back)
            [ "complement x = 0", is 1.0 (Special.regularizedBetaComplement 2.0 3.0 0.0)
              "complement x = 1", is 0.0 (Special.regularizedBetaComplement 2.0 3.0 1.0)
              "complement x < 0", isNaN (Special.regularizedBetaComplement 2.0 3.0 -0.1)
              "complement x > 1", isNaN (Special.regularizedBetaComplement 2.0 3.0 1.5)
              "complement a = 0", isNaN (Special.regularizedBetaComplement 0.0 3.0 0.5)
              "complement b = inf", isNaN (Special.regularizedBetaComplement 2.0 inf 0.5)
              "complement NaN", isNaN (Special.regularizedBetaComplement 2.0 3.0 nan)
              "inverse complement q = 0", is 1.0 (Special.inverseRegularizedBetaComplement 2.0 3.0 0.0)
              "inverse complement q = 1", is 0.0 (Special.inverseRegularizedBetaComplement 2.0 3.0 1.0)
              "inverse complement q < 0", isNaN (Special.inverseRegularizedBetaComplement 2.0 3.0 -0.5)
              "inverse complement q > 1", isNaN (Special.inverseRegularizedBetaComplement 2.0 3.0 1.5)
              "inverse complement b < 0", isNaN (Special.inverseRegularizedBetaComplement 2.0 -3.0 0.5)
              "inverse complement NaN", isNaN (Special.inverseRegularizedBetaComplement nan 3.0 0.5)
              "I + complement = 1", abs (lower + upper - 1.0) <= 8.0 * u
              "complement a b x = I_(1 - x)(b, a)", abs (upperHigh - mirrored) <= pow2 -40 * mirrored
              "complement (inverse complement q) = q", abs (back - q) <= pow2 -40
              "inverse complement a b q = 1 - inverse b a q", abs (root + mirror - 1.0) <= 64.0 * u
              "inverse complement a b q = inverse a b (1 - q)",
              bits (Special.inverseRegularizedBetaComplement a b qHigh) =
                  bits (Special.inverseRegularizedBeta a b (1.0 - qHigh)) ]

    // ---- Phase 725 (DECISIONS.md D84): sin, cos, sinCos, atan and atan2 ----

    /// Every row of the four trigonometric tables and IEEE 754's edge rules, each answered as stated
    /// (D84 item 2): a NaN propagates, an infinity is outside the reals for `sin` and `cos`, a zero
    /// keeps its sign through `sin`, `atan` and `atan2`, and `atan2` answers IEEE 754's whole table,
    /// each multiple of `π/4` the double nearest it.
    let private trigTables (_: Draw) =
        let nan = Double.NaN
        let inf = Double.PositiveInfinity
        let is (want: float) (x: float) = bits x = bits want
        let pi = 3.141592653589793
        let halfPi = 1.5707963267948966
        let quarterPi = 0.7853981633974483
        let threeQuarterPi = 2.356194490192345
        let struct (sinZero, cosZero) = Elementary.sinCos -0.0
        let struct (sinInf, cosInf) = Elementary.sinCos inf

        firstFailure
            "the tables"
            [ "sinTable NaN", Elementary.sinTable nan = SpecialValue.NaN
              "sinTable Inf", Elementary.sinTable inf = SpecialValue.Undefined
              "sinTable -Inf", Elementary.sinTable -inf = SpecialValue.Undefined
              "sinTable 0", Elementary.sinTable 0.0 = SpecialValue.Value 0.0
              "sinTable the least subnormal", Elementary.sinTable Double.Epsilon = SpecialValue.Compute
              "sinTable the largest double", Elementary.sinTable Double.MaxValue = SpecialValue.Compute
              "cosTable NaN", Elementary.cosTable nan = SpecialValue.NaN
              "cosTable -Inf", Elementary.cosTable -inf = SpecialValue.Undefined
              "cosTable 0", Elementary.cosTable 0.0 = SpecialValue.Compute
              "atanTable NaN", Elementary.atanTable nan = SpecialValue.NaN
              "atanTable Inf", Elementary.atanTable inf = SpecialValue.Value halfPi
              "atanTable 1", Elementary.atanTable 1.0 = SpecialValue.Compute
              "atan2Table (NaN, 1)", Elementary.atan2Table nan 1.0 = SpecialValue.NaN
              "atan2Table (1, NaN)", Elementary.atan2Table 1.0 nan = SpecialValue.NaN
              "atan2Table (1, 1)", Elementary.atan2Table 1.0 1.0 = SpecialValue.Compute
              "atan2Table (1, -1)", Elementary.atan2Table 1.0 -1.0 = SpecialValue.Compute
              "sin NaN is the canonical NaN", canonicalNaNBits (Elementary.sin nan)
              "sin Inf is the canonical NaN", canonicalNaNBits (Elementary.sin inf)
              "sin -Inf is the canonical NaN", canonicalNaNBits (Elementary.sin -inf)
              "sin 0 is +0", is 0.0 (Elementary.sin 0.0)
              "sin -0 is -0", is -0.0 (Elementary.sin -0.0)
              "sin of the least subnormal is itself", is Double.Epsilon (Elementary.sin Double.Epsilon)
              "cos NaN is the canonical NaN", canonicalNaNBits (Elementary.cos nan)
              "cos Inf is the canonical NaN", canonicalNaNBits (Elementary.cos inf)
              "cos 0 is 1", is 1.0 (Elementary.cos 0.0)
              "cos -0 is 1", is 1.0 (Elementary.cos -0.0)
              "sinCos -0 is (-0, 1)", is -0.0 sinZero && is 1.0 cosZero
              "sinCos Inf is (NaN, NaN)", canonicalNaNBits sinInf && canonicalNaNBits cosInf
              "atan NaN is the canonical NaN", canonicalNaNBits (Elementary.atan nan)
              "atan 0 is +0", is 0.0 (Elementary.atan 0.0)
              "atan -0 is -0", is -0.0 (Elementary.atan -0.0)
              "atan Inf is pi/2", is halfPi (Elementary.atan inf)
              "atan -Inf is -pi/2", is -halfPi (Elementary.atan -inf)
              "atan of the largest double is pi/2", is halfPi (Elementary.atan Double.MaxValue)
              "atan 1 is pi/4", is quarterPi (Elementary.atan 1.0)
              "atan2 (NaN, 1) is the canonical NaN", canonicalNaNBits (Elementary.atan2 nan 1.0)
              "atan2 (1, NaN) is the canonical NaN", canonicalNaNBits (Elementary.atan2 1.0 nan)
              "atan2 (+0, +0) is +0", is 0.0 (Elementary.atan2 0.0 0.0)
              "atan2 (-0, +0) is -0", is -0.0 (Elementary.atan2 -0.0 0.0)
              "atan2 (+0, -0) is pi", is pi (Elementary.atan2 0.0 -0.0)
              "atan2 (-0, -0) is -pi", is -pi (Elementary.atan2 -0.0 -0.0)
              "atan2 (+0, 2) is +0", is 0.0 (Elementary.atan2 0.0 2.0)
              "atan2 (-0, 2) is -0", is -0.0 (Elementary.atan2 -0.0 2.0)
              "atan2 (+0, -2) is pi", is pi (Elementary.atan2 0.0 -2.0)
              "atan2 (-0, -2) is -pi", is -pi (Elementary.atan2 -0.0 -2.0)
              "atan2 (2, +0) is pi/2", is halfPi (Elementary.atan2 2.0 0.0)
              "atan2 (2, -0) is pi/2", is halfPi (Elementary.atan2 2.0 -0.0)
              "atan2 (-2, +0) is -pi/2", is -halfPi (Elementary.atan2 -2.0 0.0)
              "atan2 (-2, -0) is -pi/2", is -halfPi (Elementary.atan2 -2.0 -0.0)
              "atan2 (Inf, Inf) is pi/4", is quarterPi (Elementary.atan2 inf inf)
              "atan2 (-Inf, Inf) is -pi/4", is -quarterPi (Elementary.atan2 -inf inf)
              "atan2 (Inf, -Inf) is 3pi/4", is threeQuarterPi (Elementary.atan2 inf -inf)
              "atan2 (-Inf, -Inf) is -3pi/4", is -threeQuarterPi (Elementary.atan2 -inf -inf)
              "atan2 (Inf, 2) is pi/2", is halfPi (Elementary.atan2 inf 2.0)
              "atan2 (-Inf, -2) is -pi/2", is -halfPi (Elementary.atan2 -inf -2.0)
              "atan2 (2, Inf) is +0", is 0.0 (Elementary.atan2 2.0 inf)
              "atan2 (-2, Inf) is -0", is -0.0 (Elementary.atan2 -2.0 inf)
              "atan2 (2, -Inf) is pi", is pi (Elementary.atan2 2.0 -inf)
              "atan2 (-2, -Inf) is -pi", is -pi (Elementary.atan2 -2.0 -inf)
              "atan2 (1, 1) is pi/4", is quarterPi (Elementary.atan2 1.0 1.0)
              "atan2 (1, -1) is 3pi/4", is threeQuarterPi (Elementary.atan2 1.0 -1.0)
              "atan2 (-1, -1) is -3pi/4", is -threeQuarterPi (Elementary.atan2 -1.0 -1.0)
              "atan2 (-1, 1) is -pi/4", is -quarterPi (Elementary.atan2 -1.0 1.0) ]

    /// The pair is the two functions and the symmetries are exact, bit for bit, at every magnitude:
    /// `sinCos x` is `(sin x, cos x)`; `sin` and `atan` are odd and `cos` is even; `atan2` is odd in
    /// `y`; and `atan2 y 1` is `atan y`.
    let private trigSymmetries (d: Draw) =
        let x = positive d -60 1000
        let y = positive d -40 70
        let w = positive d -40 40 * (if d.Unit() < 0.0 then -1.0 else 1.0)
        let struct (s, c) = Elementary.sinCos x
        let struct (sn, cn) = Elementary.sinCos -x

        firstFailure
            (sprintf "x = %.17g, y = %.17g, w = %.17g" x y w)
            [ "sinCos x = (sin x, cos x)", bits s = bits (Elementary.sin x) && bits c = bits (Elementary.cos x)
              "sinCos -x = (sin -x, cos -x)", bits sn = bits (Elementary.sin -x) && bits cn = bits (Elementary.cos -x)
              "sin -x = -(sin x)", bits (Elementary.sin -x) = bits (-(Elementary.sin x))
              "cos -x = cos x", bits (Elementary.cos -x) = bits (Elementary.cos x)
              "atan -y = -(atan y)", bits (Elementary.atan -y) = bits (-(Elementary.atan y))
              "atan2 y 1 = atan y", bits (Elementary.atan2 y 1.0) = bits (Elementary.atan y)
              "atan2 -y w = -(atan2 y w)", bits (Elementary.atan2 -y w) = bits (-(Elementary.atan2 y w)) ]

    /// The identities hold within the bounds a one-ulp routine admits, at every magnitude the
    /// reduction meets (doubling an argument is exact, so the double-angle formulas test the reduction
    /// itself): `sin² + cos²` is one within `8u`; `sin 2x` is `2 sin x cos x` and `cos 2x` is
    /// `1 − 2 sin² x`, each within `16u`; `atan y + atan (1/y)` is `π/2` within `8u`; and the angle
    /// `atan2` answers for a point is the point's own, its sine and cosine times the point's distance
    /// within `16u` of that distance from the point.
    let private trigIdentities (d: Draw) =
        let x = positive d -30 1000 * (if d.Unit() < 0.0 then -1.0 else 1.0)
        let y = positive d -30 30
        let px = positive d -20 20 * (if d.Unit() < 0.0 then -1.0 else 1.0)
        let py = positive d -20 20 * (if d.Unit() < 0.0 then -1.0 else 1.0)
        let s = Elementary.sin x
        let c = Elementary.cos x
        let angle = Elementary.atan2 py px
        let distance = sqrt (px * px + py * py)

        firstFailure
            (sprintf "x = %.17g, y = %.17g, the point (%.17g, %.17g)" x y px py)
            [ "sin^2 + cos^2 = 1", abs (s * s + c * c - 1.0) <= 8.0 * u
              "sin 2x = 2 sin x cos x", abs (Elementary.sin (2.0 * x) - 2.0 * s * c) <= 16.0 * u
              "cos 2x = 1 - 2 sin^2 x", abs (Elementary.cos (2.0 * x) - (1.0 - 2.0 * s * s)) <= 16.0 * u
              "atan y + atan (1 / y) = pi / 2",
              abs (Elementary.atan y + Elementary.atan (1.0 / y) - 1.5707963267948966)
              <= 8.0 * u
              "the angle of a point is the point's",
              abs (Elementary.cos angle * distance - px) <= 16.0 * u * distance
              && abs (Elementary.sin angle * distance - py) <= 16.0 * u * distance ]

    /// `Special`'s reflections answer the bits they answered before their sine and cosine kernels
    /// became `Elementary`'s (D84 item 5): `gammaLn`, `gamma` and `digamma` below zero, at arguments
    /// on both sides of the reflection's quarter and at both parities, each against the answer
    /// recorded at `0.46.0`.
    let private reflectionsReproduce (_: Draw) =
        let is (want: float) (x: float) = bits x = bits want

        firstFailure
            "the recorded answers"
            [ "gammaLn -0.5", is 1.2655121234846454 (Special.gammaLn -0.5)
              "gamma -0.5", is -3.5449077018110318 (Special.gamma -0.5)
              "digamma -0.5", is 0.03648997397857652 (Special.digamma -0.5)
              "gammaLn -0.625", is 1.333077611516383 (Special.gammaLn -0.625)
              "gamma -0.625", is -3.792697895066561 (Special.gamma -0.625)
              "digamma -0.625", is -1.1539990491451397 (Special.digamma -0.625)
              "gammaLn -0.875", is 2.152949750178319 (Special.gammaLn -0.875)
              "gamma -0.875", is -8.610218970054413 (Special.gamma -0.875)
              "digamma -0.875", is -7.245635520438711 (Special.digamma -0.875)
              "gammaLn -1.25", is 1.3664317612369763 (Special.gammaLn -1.25)
              "gamma -1.25", is 3.9213334478885686 (Special.gamma -1.25)
              "digamma -1.25", is 3.7141391202135283 (Special.digamma -1.25)
              "gammaLn -2.75", is 0.004487897535955909 (Special.gammaLn -2.75)
              "gamma -2.75", is -1.0044979832303123 (Special.gamma -2.75)
              "digamma -2.75", is -1.9590552649779973 (Special.digamma -2.75)
              "gammaLn -7.9", is -8.072039736012833 (Special.gammaLn -7.9)
              "gamma -7.9", is 0.0003121459359719531 (Special.gamma -7.9)
              "digamma -7.9", is -7.54000722441948 (Special.digamma -7.9)
              "gammaLn -100.25", is -363.4009232278215 (Special.gammaLn -100.25)
              "gamma -100.25", is -1.5030877093227506e-158 (Special.gamma -100.25)
              "digamma -100.25", is 7.754238959208646 (Special.digamma -100.25)
              "gammaLn -170.4", is -707.434114102398 (Special.gammaLn -170.4)
              "gamma -170.4", is -5.824624534443869e-308 (Special.gamma -170.4)
              "digamma -170.4", is 6.161845347412788 (Special.digamma -170.4) ]

    /// The laws, by name, each run at `count` iterations drawn from `seed`.
    let laws (seed: int) (count: int) : (string * Result<unit, string>) list =
        [ "every special-value row and IEEE edge of the elementary functions is answered as stated", elementaryTables
          "pow's exact cases and the sign of a negative base are the named operations, byte for byte", powExactCases
          "exp and ln invert within their one-ulp bounds", elementaryInverses
          "the elementary functional equations hold within their bounds", elementaryIdentities
          "the generator is SplitMix64: the published draws, the limb product, the recurrence", rngIsSplitMix64
          "a seed's streams, uniform53 and below are the stated functions of the draws", rngStreamsAndRanges
          "every special-value row of the special functions is answered as stated", specialTables
          "gamma (x + 1) is x gamma x, and beta and betaLn are symmetric bit for bit", gammaRecurrenceAndBetaSymmetry
          "P + Q is one, the incomplete beta function and its mirror sum to one, and neither falls as x rises",
          complementsAndMonotonicity
          "the incomplete beta function of its inverse is p, and the inverse mirrors", inverseRoundTrip
          "digamma's recurrence and constants hold, and digamma and trigamma are polygamma 0 and 1 bit for bit",
          polygammaIdentities
          "a central difference of gammaLn agrees with digamma", gammaLnDerivative
          "the incomplete beta function's complement and its inverse are the stated functions of it", betaComplements
          "every special-value row and IEEE edge of the trigonometric functions is answered as stated", trigTables
          "sinCos is sin and cos, and the symmetries of sin, cos, atan and atan2 hold, bit for bit", trigSymmetries
          "the trigonometric identities hold within their bounds at every magnitude", trigIdentities
          "the special functions' reflections answer the bits recorded before their kernels moved", reflectionsReproduce ]
        |> List.map (fun (name, body) -> name, law seed count body)
