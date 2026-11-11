namespace Fuaran.Compute.Solvers

/// Root finding over a bracket, generic over the caller's function (DECISIONS.md D77 items 3 to 5).
/// Every method reads the caller's function, `+ − × ÷` and comparisons, in one order, so a method
/// given a pinned function answers the same bits and stops at the same iteration on every host.
[<RequireQualifiedAccess>]
module Root =

    open Floats

    /// What every bracketed method checks before its first iteration, in this order: the rule, the
    /// ends (finite, `lo < hi`), the values at them (finite), a zero at `lo` and then at `hi` (answered
    /// at once, zero iterations), and a sign change between them.
    let private enter
        (rule: StoppingRule)
        (flo: float)
        (fhi: float)
        (lo: float)
        (hi: float)
        : Result<Stopped<float> option, SolverError<float, 'reason>> =
        if not (StoppingRule.valid rule) then
            Error SolverError.RuleInvalid
        elif not (finite lo && finite hi && lo < hi) then
            Error SolverError.BracketInvalid
        elif not (finite flo && finite fhi) then
            Error SolverError.BracketInvalid
        elif flo = 0.0 then
            Ok(
                Some
                    { State = lo
                      Value = flo
                      Iterations = 0
                      Arm = StoppingArm.ExactZero }
            )
        elif fhi = 0.0 then
            Ok(
                Some
                    { State = hi
                      Value = fhi
                      Iterations = 0
                      Arm = StoppingArm.ExactZero }
            )
        elif (flo < 0.0) = (fhi < 0.0) then
            Error SolverError.BracketInvalid
        else
            Ok None

    /// How one iteration that moved from `x` to `xn` by `step`, with `f xn = fn` and the bracket already
    /// narrowed to `[lo, hi]`, ends: a zero of the function first, then the rule's test, then the cap.
    let private close
        (rule: StoppingRule)
        (k: int)
        (x: float)
        (xn: float)
        (step: float)
        (fn: float)
        (lo: float)
        (hi: float)
        : Result<Stopped<float>, SolverError<float, 'reason>> option =
        let at arm =
            { State = xn
              Value = fn
              Iterations = k
              Arm = arm }

        if fn = 0.0 then
            Some(Ok(at StoppingArm.ExactZero))
        elif StoppingRule.stops rule.Test x xn step lo hi then
            Some(Ok(at (StoppingRule.armOf rule.Test)))
        elif k >= rule.Cap then
            Some(Error(SolverError.NotConverged(at StoppingArm.Cap)))
        else
            None

    /// Bisection of `f` over `[lo, hi]`. Iteration `k` evaluates `f` at `mid = lo + 0.5 × (hi − lo)`,
    /// replaces the end whose value shares `mid`'s sign, and stops on `f mid = 0`, then on the rule's
    /// test, then at the cap. The rule reads `previous` as the last midpoint (`lo` before the first),
    /// `current` as `mid` and `step` as `mid − previous`. A bracket with no float strictly inside it
    /// stops as `Bracket` at the end `mid` rounded to, without evaluating.
    ///
    /// Refused: `RuleInvalid`; `BracketInvalid` (ends out of order or not finite, a value at an end not
    /// finite, the two values of one sign); `NotFinite mid`; `NotConverged` at the cap.
    let bisect
        (rule: StoppingRule)
        (f: float -> float)
        (lo: float)
        (hi: float)
        : Result<Stopped<float>, SolverError<float, 'reason>> =
        let flo0 = f lo
        let fhi0 = f hi

        match enter rule flo0 fhi0 lo hi with
        | Error e -> Error e
        | Ok(Some at) -> Ok at
        | Ok None ->
            let mutable lo = lo
            let mutable hi = hi
            let mutable flo = flo0
            let mutable fhi = fhi0
            let mutable x = lo
            let mutable k = 0
            let mutable answer = None

            while Option.isNone answer do
                k <- k + 1
                let mid = lo + 0.5 * (hi - lo)

                if not (lo < mid && mid < hi) then
                    answer <-
                        Some(
                            Ok
                                { State = mid
                                  Value = (if mid = lo then flo else fhi)
                                  Iterations = k
                                  Arm = StoppingArm.Bracket }
                        )
                else
                    let fm = f mid

                    if not (finite fm) then
                        answer <- Some(Error(SolverError.NotFinite mid))
                    else
                        if (fm < 0.0) = (flo < 0.0) then
                            lo <- mid
                            flo <- fm
                        else
                            hi <- mid
                            fhi <- fm

                        answer <- close rule k x mid (mid - x) fm lo hi
                        x <- mid

            answer.Value

    /// Newton's method on `f` kept inside a bracket, with a bisection safeguard. `fdf x` answers
    /// `(f x, f′ x)`; `x0` is the start, strictly inside `[lo, hi]`.
    ///
    /// Before the first iteration the bracket is narrowed by `x0`: the end whose value shares `f x0`'s
    /// sign becomes `x0`. Iteration `k`, at `x` with `(fx, dfx)` and a bracket of width `w = hi − lo`:
    ///
    ///   1. the proposal is Newton's, `x − fx / dfx`, when the last iteration left Newton allowed and
    ///      the point lies inside the bracket, its ends included (a converged step leaves `x` where it
    ///      is, and `x` is an end); otherwise the midpoint `lo + 0.5 × (hi − lo)`. The step is
    ///      `fx / dfx` for a Newton proposal and `x − midpoint` for a bisection.
    ///   2. a midpoint with no float strictly inside the bracket stops as `Bracket`, without evaluating.
    ///   3. `fdf` is evaluated at the proposal, and the end whose value shares its sign becomes it.
    ///   4. Newton is allowed next iteration when the narrowed bracket is at most `shrink × w` wide —
    ///      the fraction the caller states, in `(0, 1]`. At `1` every narrowing passes, and the only
    ///      fallback is a Newton point outside the bracket.
    ///   5. the iteration stops on a function value of zero, then on the rule's test (`previous = x`,
    ///      `current` the proposal, `step` as in 1), then at the cap.
    ///
    /// Newton is allowed in the first iteration. Refused: `RuleInvalid` (the rule, or a `shrink` outside
    /// `(0, 1]`); `BracketInvalid` (as `bisect`, or `x0` not strictly inside); `NotFinite` at a point
    /// whose function value is not finite; `NotConverged` at the cap.
    let bracketedNewton
        (rule: StoppingRule)
        (shrink: float)
        (fdf: float -> float * float)
        (lo: float)
        (hi: float)
        (x0: float)
        : Result<Stopped<float>, SolverError<float, 'reason>> =
        let flo0, _ = fdf lo
        let fhi0, _ = fdf hi

        match enter rule flo0 fhi0 lo hi with
        | Error e -> Error e
        | Ok(Some at) -> Ok at
        | Ok None when not (shrink > 0.0 && shrink <= 1.0) -> Error SolverError.RuleInvalid
        | Ok None when not (lo < x0 && x0 < hi) -> Error SolverError.BracketInvalid
        | Ok None ->
            let f0, df0 = fdf x0

            if not (finite f0) then
                Error(SolverError.NotFinite x0)
            elif f0 = 0.0 then
                Ok
                    { State = x0
                      Value = f0
                      Iterations = 0
                      Arm = StoppingArm.ExactZero }
            else
                let lowNegative = flo0 < 0.0
                let mutable lo = lo
                let mutable hi = hi
                let mutable flo = flo0
                let mutable fhi = fhi0
                let mutable x = x0
                let mutable fx = f0
                let mutable dfx = df0
                let mutable newton = true
                let mutable k = 0
                let mutable answer = None

                if (f0 < 0.0) = lowNegative then
                    lo <- x0
                    flo <- f0
                else
                    hi <- x0
                    fhi <- f0

                while Option.isNone answer do
                    k <- k + 1
                    let w = hi - lo
                    let ns = fx / dfx
                    let nx = x - ns
                    let useNewton = newton && lo <= nx && nx <= hi
                    let xn = if useNewton then nx else lo + 0.5 * (hi - lo)
                    let step = if useNewton then ns else x - xn

                    if not (useNewton || (lo < xn && xn < hi)) then
                        answer <-
                            Some(
                                Ok
                                    { State = xn
                                      Value = (if xn = lo then flo else fhi)
                                      Iterations = k
                                      Arm = StoppingArm.Bracket }
                            )
                    else
                        let fn, dfn = fdf xn

                        if not (finite fn) then
                            answer <- Some(Error(SolverError.NotFinite xn))
                        else
                            if (fn < 0.0) = lowNegative then
                                lo <- xn
                                flo <- fn
                            else
                                hi <- xn
                                fhi <- fn

                            newton <- hi - lo <= shrink * w
                            answer <- close rule k x xn step fn lo hi
                            x <- xn
                            fx <- fn
                            dfx <- dfn

                answer.Value

    /// The smallest integer `k` in `[lo, hi]` with `g k ≥ target`, for a `g` that does not fall as `k`
    /// rises. `g hi` is read first and `g lo` second; then the bracket is halved at
    /// `mid = lo + (hi − lo) / 2` (integer division) — `g mid ≥ target` moves `hi` to `mid`, anything else
    /// (a value that is not a number included) moves `lo` to `mid` — until `hi − lo = 1`, at most
    /// thirty-one halvings, so no rule is taken. The answer's `Value` is `g k`, `Iterations` the
    /// halvings and `Arm` `Bracket`.
    ///
    /// Refused as `BracketInvalid`: `lo > hi`, a width `hi − lo` beyond a 32-bit integer, or
    /// `g hi ≥ target` not holding (no such `k`).
    let integerSearch
        (g: int -> float)
        (target: float)
        (lo: int)
        (hi: int)
        : Result<Stopped<int>, SolverError<int, 'reason>> =
        if lo > hi || hi - lo < 0 then
            Error SolverError.BracketInvalid
        else
            let ghi = g hi

            if not (ghi >= target) then
                Error SolverError.BracketInvalid
            else
                let glo = if lo = hi then ghi else g lo

                if glo >= target then
                    Ok
                        { State = lo
                          Value = glo
                          Iterations = 0
                          Arm = StoppingArm.Bracket }
                else
                    let mutable lo = lo
                    let mutable hi = hi
                    let mutable ghi = ghi
                    let mutable k = 0

                    while hi - lo > 1 do
                        k <- k + 1
                        let mid = lo + (hi - lo) / 2
                        let gm = g mid

                        if gm >= target then
                            hi <- mid
                            ghi <- gm
                        else
                            lo <- mid

                    Ok
                        { State = hi
                          Value = ghi
                          Iterations = k
                          Arm = StoppingArm.Bracket }
