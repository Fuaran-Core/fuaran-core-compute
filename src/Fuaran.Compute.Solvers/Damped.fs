namespace Fuaran.Compute.Solvers

/// What `Damped.iterate` is given: the caller's iteration, as functions over the caller's state
/// (DECISIONS.md D77 item 7). `'reason` is the caller's own refusal.
type DampedProblem<'state, 'reason> =
    {
        /// Proposes the next state from the current one, or refuses.
        Step: 'state -> Result<'state, 'reason>
        /// `None` for a state the iteration may stand on, else why not.
        Inadmissible: 'state -> 'reason option
        /// The value that must not rise. Read only at an admissible state, and at the start.
        Objective: 'state -> float
        /// `Midpoint old candidate`: the state halfway from the candidate towards the state the
        /// iteration stood on.
        Midpoint: 'state -> 'state -> 'state
        /// The most halvings one iteration takes.
        Halvings: int
        /// Whether the start is a seed rather than a point of the iteration. From a seed the first
        /// proposal is not halved (there is nothing to halve towards) and its objective is not held
        /// to the seed's: an inadmissible first proposal is refused at once.
        Seeded: bool
    }

/// Damped fixed-point iteration: the accept-or-halve loop of an iteratively reweighted fit, generic
/// over the caller's state (DECISIONS.md D77 item 7).
[<RequireQualifiedAccess>]
module Damped =

    open Floats

    /// Runs the problem from `start`. With `previous` the objective at the state the iteration stands
    /// on (at `start` before the first iteration), iteration `k`:
    ///
    ///   1. `Step` proposes a candidate, or refuses (`StepRefused`, the caller's reason).
    ///   2. the candidate is judged: it FAILS when `Inadmissible` answers a reason, when its objective
    ///      is not finite, or when its objective rose as the rule's test sees a rise
    ///      (`StoppingRule.rises`). The objective is not read at an inadmissible candidate.
    ///   3. a failing candidate is replaced by `Midpoint standing candidate` and judged again, at most
    ///      `Halvings` times; one that still fails is `HalvingExhausted k`, with the caller's reason
    ///      where the last candidate was inadmissible. In the first iteration of a `Seeded` problem
    ///      nothing is halved and no rise is read: an inadmissible proposal is `HalvingExhausted 1`
    ///      with its reason, and any other is accepted.
    ///   4. the accepted candidate is stood on, and the iteration stops on an objective of exactly
    ///      zero under `ExactZero`, then on the rule's test, then at the cap. The rule reads `previous`
    ///      and `current` as the objective before and after, and `step` as their difference.
    ///
    /// The answer's `State` is the accepted state and its `Value` the objective there. `NotConverged`
    /// carries the last accepted state. `BracketUlps` reads no objective and is `RuleInvalid`, as is a
    /// negative `Halvings`.
    let iterate
        (rule: StoppingRule)
        (problem: DampedProblem<'state, 'reason>)
        (start: 'state)
        : Result<Stopped<'state>, SolverError<'state, 'reason>> =
        let unsupported =
            match rule.Test with
            | StoppingTest.BracketUlps _ -> true
            | _ -> false

        if not (StoppingRule.valid rule) || unsupported || problem.Halvings < 0 then
            Error SolverError.RuleInvalid
        else
            let mutable standing = start
            let mutable previous = problem.Objective start
            let mutable k = 0
            let mutable answer = None

            while Option.isNone answer do
                k <- k + 1

                match problem.Step standing with
                | Error why -> answer <- Some(Error(SolverError.StepRefused why))
                | Ok proposed ->
                    let seedStep = problem.Seeded && k = 1
                    let mutable candidate = proposed
                    let mutable halvings = 0
                    let mutable accepted = None
                    let mutable refused = None

                    while Option.isNone accepted && Option.isNone refused do
                        let bad = problem.Inadmissible candidate

                        let value =
                            match bad with
                            | Some _ -> nan
                            | None -> problem.Objective candidate

                        let fails =
                            if seedStep then
                                Option.isSome bad
                            else
                                Option.isSome bad
                                || not (finite value)
                                || StoppingRule.rises rule.Test previous value

                        if not fails then
                            accepted <- Some value
                        elif seedStep || halvings = problem.Halvings then
                            refused <- Some(SolverError.HalvingExhausted(k, bad))
                        else
                            candidate <- problem.Midpoint standing candidate
                            halvings <- halvings + 1

                    match refused, accepted with
                    | Some e, _ -> answer <- Some(Error e)
                    | None, None -> ()
                    | None, Some value ->
                        let at arm =
                            { State = candidate
                              Value = value
                              Iterations = k
                              Arm = arm }

                        if rule.Test = StoppingTest.ExactZero && value = 0.0 then
                            answer <- Some(Ok(at StoppingArm.ExactZero))
                        elif StoppingRule.stops rule.Test previous value (value - previous) previous value then
                            answer <- Some(Ok(at (StoppingRule.armOf rule.Test)))
                        elif k >= rule.Cap then
                            answer <- Some(Error(SolverError.NotConverged(at StoppingArm.Cap)))
                        else
                            standing <- candidate
                            previous <- value

            answer.Value
