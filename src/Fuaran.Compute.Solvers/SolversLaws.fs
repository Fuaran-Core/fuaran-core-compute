namespace Fuaran.Compute.Solvers

/// `solversLaws` (Phase 633): the laws the solvers are held to, on whichever host runs them — each
/// method finds what it is asked for on problems whose answer is known, the bracketed Newton with
/// every narrowing admitted is the plain Newton iteration bit for bit while its points stay inside
/// the bracket, the cap is answered as `NotConverged` and never as a convergence, and each refusal is
/// the named one.
///
/// Every input is drawn by a pinned generator whose every step is an exact float operation (Park and
/// Miller's minimal standard, as `ElementaryLaws` draws), so a seed draws the same numbers on every
/// host and the laws run under Fable over the numbers they run over on .NET. A law answers `Ok ()`
/// when it held at every iteration, else the first failure with its iteration. FSharp.Core only.
[<RequireQualifiedAccess>]
module SolversLaws =

    type private Draw(seed: int) =
        let mutable state =
            ((float seed % 2147483646.0) + 2147483646.0) % 2147483646.0 + 1.0

        member _.Next() : float =
            state <- (state * 48271.0) % 2147483647.0
            state

        /// An integer in `lo .. hi`.
        member this.Int (lo: int) (hi: int) : int =
            lo + int (this.Next() % float (hi - lo + 1))

        /// A float in `[lo, hi)`, a multiple of `(hi − lo)/2³¹` above `lo`.
        member this.Between (lo: float) (hi: float) : float =
            lo + (hi - lo) * (this.Next() / 2147483648.0)

    let private eps: float = 2.220446049250313e-16

    let private run (seed: int) (count: int) (law: Draw -> Result<unit, string>) : Result<unit, string> =
        let draw = Draw seed
        let mutable verdict = Ok()
        let mutable i = 0

        while Result.isOk verdict && i < count do
            match law draw with
            | Ok() -> ()
            | Error why -> verdict <- Error(sprintf "iteration %d: %s" i why)

            i <- i + 1

        verdict

    let private check (holds: bool) (why: string) : Result<unit, string> = if holds then Ok() else Error why

    let private all (checks: Result<unit, string> list) : Result<unit, string> =
        checks |> List.tryFind Result.isError |> Option.defaultValue (Ok())

    let private tight = StoppingRule.create 200 (StoppingTest.AbsoluteStep 1e-13)

    /// Bisection of `x − r` closes on `r`: the answer is within the rule's bracket width of it.
    let private bisectFindsTheRoot (d: Draw) =
        let lo = d.Between -100.0 0.0
        let hi = d.Between 1.0 100.0
        let r = d.Between (lo + 0.25) (hi - 0.25)
        let rule = StoppingRule.create 200 (StoppingTest.BracketUlps 4.0)

        match Root.bisect rule (fun x -> x - r) lo hi with
        | Ok s ->
            let width = 4.0 * eps * (if -lo < hi then hi else -lo)

            all
                [ check (abs (s.State - r) <= width) (sprintf "bisect answered %g for the root %g" s.State r)
                  check
                      (s.Arm = StoppingArm.Bracket || s.Arm = StoppingArm.ExactZero)
                      "bisect stopped on an arm the rule did not name" ]
        | Error(_: SolverError<float, unit>) -> Error "bisect refused a bracket with a sign change"

    /// The bracketed Newton finds the cube root of `c` from any start in the bracket, under a
    /// safeguard that bisects (`shrink = 0.5`) and one that does not (`shrink = 1`).
    let private newtonFindsTheRoot (d: Draw) =
        let c = d.Between 0.5 50.0
        let x0 = d.Between 0.001 (c + 0.999)
        let fdf (x: float) = x * x * x - c, 3.0 * x * x

        [ 0.5; 1.0 ]
        |> List.map (fun shrink ->
            match Root.bracketedNewton tight shrink fdf 0.0 (c + 1.0) x0 with
            | Ok s ->
                check
                    (abs (s.State * s.State * s.State - c) <= 1e-9 * c)
                    (sprintf "shrink %g: the cube of %g is not %g" shrink s.State c)
            | Error(_: SolverError<float, unit>) -> Error(sprintf "shrink %g: refused from %g for c = %g" shrink x0 c))
        |> all

    /// With every narrowing admitted and the points inside the bracket, the bracketed Newton IS the
    /// plain Newton iteration: the same bits after the same number of steps. Newton on `x² − c` from
    /// the right of the root falls monotonically, so it never leaves `[0, x0 + 1]`.
    let private newtonIsPlainNewton (d: Draw) =
        let c = d.Between 0.5 50.0
        let x0 = d.Between (c + 1.0) (c + 5.0)
        let tol = 1e-12
        let cap = 100
        let fdf (x: float) = x * x - c, 2.0 * x

        let mutable x = x0
        let mutable step = 1.0
        let mutable k = 0

        while k < cap && abs step > tol do
            let f, df = fdf x
            step <- f / df
            x <- x - step
            k <- k + 1

        match
            Root.bracketedNewton (StoppingRule.create cap (StoppingTest.AbsoluteStep tol)) 1.0 fdf 0.0 (x0 + 1.0) x0
        with
        | Ok s when s.Arm = StoppingArm.AbsoluteStep ->
            all
                [ check (s.State = x) (sprintf "the solver answered %g and the plain iteration %g" s.State x)
                  check
                      (s.Iterations = k)
                      (sprintf "the solver took %d steps and the plain iteration %d" s.Iterations k) ]
        // A step that lands on the root exactly stops the solver one zero-length step early.
        | Ok s when s.Arm = StoppingArm.ExactZero ->
            check (s.State = x) "an exact root is not the plain iteration's answer"
        | Ok _ -> Error "the solver stopped on an arm the rule did not name"
        | Error(_: SolverError<float, unit>) -> Error "the solver refused where the plain iteration ran"

    /// The integer search answers the smallest `k` with `k² ≥ target`, as a scan finds it.
    let private integerSearchIsTheSmallest (d: Draw) =
        let lo = d.Int -20 20
        let hi = lo + d.Int 0 200

        let g (k: int) =
            float k * float (if k < 0 then -1 else 1) * float k

        let target = d.Between (g lo - 5.0) (g hi + 5.0)
        let scan = [ lo..hi ] |> List.tryFind (fun k -> g k >= target)

        match Root.integerSearch g target lo hi, scan with
        | Ok s, Some k -> check (s.State = k && s.Value = g k) (sprintf "answered %d, the scan %d" s.State k)
        | Error SolverError.BracketInvalid, None -> Ok()
        | Ok s, None -> Error(sprintf "answered %d where no k reaches the target" s.State)
        | Error(_: SolverError<int, unit>), _ -> Error "refused a bracket that holds an answer"

    /// The golden section closes on the minimum of `(x − m)²`, at `m` inside the bracket and at the
    /// nearer end outside it.
    let private goldenSectionFindsTheMinimum (d: Draw) =
        let lo = d.Between -10.0 0.0
        let hi = d.Between 1.0 10.0
        let m = d.Between (lo - 2.0) (hi + 2.0)

        let expected =
            if m < lo then lo
            else if m > hi then hi
            else m

        let rule = StoppingRule.create 200 (StoppingTest.AbsoluteStep 1e-9)

        match Minimise.goldenSection rule (fun x -> (x - m) * (x - m)) lo hi with
        | Ok s ->
            check
                (abs (s.State - expected) <= 1e-6 && lo < s.State && s.State < hi)
                (sprintf "answered %g for the minimum %g of [%g, %g]" s.State expected lo hi)
        | Error(_: SolverError<float, unit>) -> Error "refused a valid bracket"

    /// The relaxation `x ← x + g·(m − x)` on the objective `(x − m)²`, at a gain `g`.
    let private relaxation (m: float) (gain: float) : DampedProblem<float, string> =
        { Step = fun x -> Ok(x + gain * (m - x))
          Inadmissible = fun _ -> None
          Objective = fun x -> (x - m) * (x - m)
          Midpoint = fun standing candidate -> 0.5 * standing + 0.5 * candidate
          Halvings = 10
          Seeded = false }

    /// A damped relaxation converges on its fixed point at every gain, an overshooting one by halving,
    /// and the objective it stops at is no higher than the one it started from.
    let private dampedConverges (d: Draw) =
        let m = d.Between -5.0 5.0
        // Below 1.6 the relaxation contracts on its own; from 2.4 it overshoots and every step is halved.
        let gain =
            if d.Int 0 1 = 0 then
                d.Between 0.1 1.6
            else
                d.Between 2.4 3.0

        let start = d.Between (m + 1.0) (m + 10.0)
        let rule = StoppingRule.create 500 (StoppingTest.RelativeChange(1e-12, 0.1))

        match Damped.iterate rule (relaxation m gain) start with
        | Ok s ->
            all
                [ check (abs (s.State - m) <= 1e-4) (sprintf "gain %g stopped at %g, not %g" gain s.State m)
                  check (s.Value <= (start - m) * (start - m)) "the objective rose over the run"
                  check (s.Arm = StoppingArm.RelativeChange) "stopped on an arm the rule did not name" ]
        | Error _ -> Error(sprintf "gain %g from %g was refused" gain start)

    /// A run cut at its cap answers `NotConverged` carrying the cap, the arm `Cap` and the state it
    /// reached; the same run under a cap one higher goes on from that state.
    let private capIsNotConvergence (d: Draw) =
        let m = d.Between -5.0 5.0
        let start = d.Between (m + 1.0) (m + 10.0)
        let cap = d.Int 1 5

        let rule (c: int) =
            StoppingRule.create c (StoppingTest.RelativeChange(1e-300, 0.1))

        let slow = relaxation m 0.25

        match Damped.iterate (rule cap) slow start, Damped.iterate (rule (cap + 1)) slow start with
        | Error(SolverError.NotConverged a), Error(SolverError.NotConverged b) ->
            all
                [ check (a.Arm = StoppingArm.Cap && a.Iterations = cap) "the cap is not reported as the cap"
                  check (b.Iterations = cap + 1) "one more iteration was not taken"
                  check (b.State = a.State + 0.25 * (m - a.State)) "the longer run did not go on from the shorter" ]
        | _ -> Error "a run cut at its cap answered something other than NotConverged"

    /// Each refusal is the named one.
    let private refusals (d: Draw) =
        let r = d.Between 1.0 9.0
        let rule = StoppingRule.create 50 (StoppingTest.AbsoluteStep 1e-9)
        let noCap = StoppingRule.create 0 (StoppingTest.AbsoluteStep 1e-9)
        let line (x: float) = x - r
        let lineDf (x: float) = x - r, 1.0

        let refusing: DampedProblem<float, string> =
            { relaxation r 0.5 with
                Step = fun _ -> Error "no" }

        let rising: DampedProblem<float, string> =
            { relaxation r 0.5 with
                Step = fun x -> Ok(x + 4096.0) }

        let outside: DampedProblem<float, string> =
            { relaxation r 0.5 with
                Inadmissible = fun x -> if x < r + 2.0 then Some "low" else None }

        let bracketRule = StoppingRule.create 50 (StoppingTest.BracketUlps 1.0)

        all
            [ check (Root.bisect rule line (r + 1.0) (r + 2.0) = Error SolverError.BracketInvalid) "one sign"
              check (Root.bisect rule line (r + 2.0) (r - 2.0) = Error SolverError.BracketInvalid) "ends reversed"
              check (Root.bisect noCap line 0.0 10.0 = Error SolverError.RuleInvalid) "a cap of zero"
              check
                  (Root.bisect rule (fun x -> if x = 5.0 then nan else x - r) 0.0 10.0 = Error(
                      SolverError.NotFinite 5.0
                  ))
                  "a value that is not a number"
              check
                  (Root.bracketedNewton rule 0.0 lineDf 0.0 10.0 5.0 = Error SolverError.RuleInvalid)
                  "a shrink of zero"
              check
                  (Root.bracketedNewton rule 0.5 lineDf 0.0 10.0 10.5 = Error SolverError.BracketInvalid)
                  "a start outside the bracket"
              check (Root.integerSearch float 11.0 0 10 = Error SolverError.BracketInvalid) "no k reaches the target"
              check
                  (Minimise.goldenSection rule line 3.0 3.0 = Error SolverError.BracketInvalid)
                  "an empty bracket to minimise over"
              check (Damped.iterate rule refusing (r + 1.0) = Error(SolverError.StepRefused "no")) "a refused step"
              check
                  (Damped.iterate rule rising (r + 1.0) = Error(SolverError.HalvingExhausted(1, None)))
                  "a rise no halving removes"
              check
                  (Damped.iterate rule outside (r + 1.0) = Error(SolverError.HalvingExhausted(1, Some "low")))
                  "an inadmissible state no halving removes"
              check
                  (Damped.iterate bracketRule (relaxation r 0.5) (r + 1.0) = Error SolverError.RuleInvalid)
                  "a bracket rule" ]

    /// The laws, each run `count` times from `seed`: `(name, verdict)`.
    let laws (seed: int) (count: int) : (string * Result<unit, string>) list =
        [ "bisect closes on the root of a line", bisectFindsTheRoot
          "bracketedNewton finds a cube root under either safeguard", newtonFindsTheRoot
          "bracketedNewton with every narrowing admitted is plain Newton, bit for bit", newtonIsPlainNewton
          "integerSearch answers the smallest k a scan finds", integerSearchIsTheSmallest
          "goldenSection closes on the minimum, inside the bracket or at its nearer end", goldenSectionFindsTheMinimum
          "Damped.iterate converges on a relaxation's fixed point at every gain", dampedConverges
          "a run cut at its cap is NotConverged, never a convergence", capIsNotConvergence
          "each refusal is the named one", refusals ]
        |> List.map (fun (name, law) -> name, run seed count law)
