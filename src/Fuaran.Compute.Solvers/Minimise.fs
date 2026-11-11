namespace Fuaran.Compute.Solvers

/// One-dimensional minimisation over a bracket, generic over the caller's function (DECISIONS.md D77
/// item 6).
[<RequireQualifiedAccess>]
module Minimise =

    open Floats

    /// `(√5 − 1)/2` as the binary64 `0x3FE3C6EF372FE950`, written as a literal: no host takes a root.
    let private upper: float = 0.6180339887498949

    /// `(3 − √5)/2` as the binary64 `0x3FD8722191A02D60`.
    let private lower: float = 0.3819660112501051

    /// Golden-section search for a minimum of `f` over `[lo, hi]`. Two interior points are kept,
    /// `c = lo + lower × (hi − lo)` and `d = lo + upper × (hi − lo)`, with `lower` and `upper` the two
    /// literals above; `f c` is read before `f d`. Iteration `k`: where `f c < f d` (strictly) the
    /// bracket becomes `[lo, d]`, the old `c` is its new `d` and a new `c` is evaluated; otherwise it
    /// becomes `[c, hi]`, the old `d` is its new `c` and a new `d` is evaluated — one evaluation an
    /// iteration. The estimate `x` is the interior point with the smaller value, `c` on a tie.
    ///
    /// The iteration stops when its new point does not lie strictly between its neighbours (`Bracket`:
    /// the points have closed up), then on `f x = 0` under `ExactZero`, then on the rule's test, then
    /// at the cap. The rule reads `previous` as the last estimate (`lo` before the first iteration),
    /// `current` as `x`, `step` as the bracket's width `hi − lo` and the bracket as it stands. The
    /// answer's `State` is `x` and its `Value` `f x`.
    ///
    /// Refused: `RuleInvalid`; `BracketInvalid` (ends out of order or not finite, or no float strictly
    /// between the first two interior points and the ends); `NotFinite` at an interior point whose
    /// value is not finite; `NotConverged` at the cap.
    let goldenSection
        (rule: StoppingRule)
        (f: float -> float)
        (lo: float)
        (hi: float)
        : Result<Stopped<float>, SolverError<float, 'reason>> =
        if not (StoppingRule.valid rule) then
            Error SolverError.RuleInvalid
        elif not (finite lo && finite hi && lo < hi) then
            Error SolverError.BracketInvalid
        else
            let mutable a = lo
            let mutable b = hi
            let mutable c = a + lower * (b - a)
            let mutable d = a + upper * (b - a)

            if not (a < c && c < d && d < b) then
                Error SolverError.BracketInvalid
            else
                let mutable fc = f c
                let mutable fd = f d
                let mutable x = lo
                let mutable k = 0
                let mutable answer = None

                if not (finite fc) then
                    answer <- Some(Error(SolverError.NotFinite c))
                elif not (finite fd) then
                    answer <- Some(Error(SolverError.NotFinite d))

                while Option.isNone answer do
                    k <- k + 1
                    let mutable closed = false
                    let mutable bad = None
                    let movedLeft = fc < fd

                    if movedLeft then
                        b <- d
                        d <- c
                        fd <- fc
                        c <- a + lower * (b - a)

                        if a < c && c < d then
                            fc <- f c

                            if not (finite fc) then
                                bad <- Some c
                        else
                            closed <- true
                    else
                        a <- c
                        c <- d
                        fc <- fd
                        d <- a + upper * (b - a)

                        if c < d && d < b then
                            fd <- f d

                            if not (finite fd) then
                                bad <- Some d
                        else
                            closed <- true

                    match bad with
                    | Some p -> answer <- Some(Error(SolverError.NotFinite p))
                    | None ->
                        // On a closed bracket the estimate is the interior point that was evaluated: `d`
                        // after a move left, `c` after a move right.
                        let xn, fn =
                            if closed then (if movedLeft then d, fd else c, fc)
                            elif fd < fc then d, fd
                            else c, fc

                        let at arm =
                            { State = xn
                              Value = fn
                              Iterations = k
                              Arm = arm }

                        if closed then
                            answer <- Some(Ok(at StoppingArm.Bracket))
                        elif rule.Test = StoppingTest.ExactZero && fn = 0.0 then
                            answer <- Some(Ok(at StoppingArm.ExactZero))
                        elif StoppingRule.stops rule.Test x xn (b - a) a b then
                            answer <- Some(Ok(at (StoppingRule.armOf rule.Test)))
                        elif k >= rule.Cap then
                            answer <- Some(Error(SolverError.NotConverged(at StoppingArm.Cap)))
                        else
                            x <- xn

                answer.Value
