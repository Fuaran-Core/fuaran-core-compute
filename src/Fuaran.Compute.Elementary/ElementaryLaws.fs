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
          "a central difference of gammaLn agrees with digamma", gammaLnDerivative ]
        |> List.map (fun (name, body) -> name, law seed count body)
