namespace Fuaran.Compute.Solvers

/// The termination test of a `StoppingRule`: a closed set, each arm one comparison written out here so
/// another host transcribes the same decision (DECISIONS.md D77 item 2). What `previous`, `current`,
/// `step` and the bracket are is each method's to say, and each says it.
[<RequireQualifiedAccess>]
type StoppingTest =
    /// Stop when `|step| ≤ tolerance`. The comparison is not strict.
    | AbsoluteStep of tolerance: float
    /// Stop when `|current − previous| / (|current| + floor) < tolerance`. The comparison is strict and
    /// the floor is stated by the caller (`Glm` states `0.1`).
    | RelativeChange of tolerance: float * floor: float
    /// Stop when `hi − lo ≤ ulps × 2⁻⁵² × max(|lo|, |hi|)`: the bracket is at most `ulps` units in the last
    /// place of its larger end wide, a unit measured at the top of that end's binade.
    | BracketUlps of ulps: float
    /// Stop only on a function value of exactly zero (or at the cap).
    | ExactZero

/// The one value every iterative solver takes: a cap on iterations and a termination test.
type StoppingRule =
    {
        /// The most iterations a run takes. At least one.
        Cap: int
        Test: StoppingTest
    }

/// Which decision ended a run. `Cap` is never a convergence: a solver answers it as `NotConverged`.
[<RequireQualifiedAccess>]
type StoppingArm =
    | AbsoluteStep
    | RelativeChange
    /// The bracket met `BracketUlps`, or closed: no float lies strictly inside it.
    | Bracket
    | ExactZero
    | Cap

/// Where a run ended: the state, the function or objective value there, how many iterations ran and
/// the arm that ended it.
type Stopped<'state> =
    { State: 'state
      Value: float
      Iterations: int
      Arm: StoppingArm }

/// Every refusal a solver makes, by name. `'reason` is the caller's own refusal type, passed through.
[<RequireQualifiedAccess>]
type SolverError<'state, 'reason> =
    /// The rule is not one the method can run: a cap below one, a tolerance or floor that is negative
    /// or not finite, a floor of zero, or an arm the method does not read.
    | RuleInvalid
    /// The bracket does not bracket: its ends are out of order or not finite, a function value at an
    /// end is not finite, the values at the ends share a sign, or a start lies outside it.
    | BracketInvalid
    /// The caller's function answered a value that is not finite at this point.
    | NotFinite of at: 'state
    /// The cap was reached. `last.Arm` is `Cap`; `last.State` is the last state reached.
    | NotConverged of last: Stopped<'state>
    /// The caller's step refused, for the caller's reason.
    | StepRefused of reason: 'reason
    /// No halving made the proposal of this iteration admissible with an objective that did not rise.
    /// `reason` is the caller's for the last candidate when that candidate was inadmissible, and
    /// `None` when it was admissible and its objective still rose.
    | HalvingExhausted of iteration: int * reason: 'reason option

/// The two float tests every method reads, each a subtraction or a negation and a comparison: no host
/// function is called for either.
module internal Floats =

    /// Neither an infinity nor a NaN: only a finite value minus itself is zero.
    let finite (v: float) : bool = v - v = 0.0

    let magnitude (v: float) : float = if v < 0.0 then -v else v

/// The stopping decisions, each one comparison in one order (DECISIONS.md D77 item 2).
[<RequireQualifiedAccess>]
module StoppingRule =

    open Floats

    /// `2⁻⁵²`, the gap between one and the next float above it.
    let private eps: float = 2.220446049250313e-16

    /// The rule with this cap and test.
    let create (cap: int) (test: StoppingTest) : StoppingRule = { Cap = cap; Test = test }

    /// Whether a solver can run the rule: a cap of at least one; a tolerance, a floor and an ulp count
    /// that are finite and not negative; a floor above zero.
    let valid (rule: StoppingRule) : bool =
        rule.Cap >= 1
        && (match rule.Test with
            | StoppingTest.AbsoluteStep tol -> finite tol && tol >= 0.0
            | StoppingTest.RelativeChange(tol, floor) -> finite tol && tol >= 0.0 && finite floor && floor > 0.0
            | StoppingTest.BracketUlps ulps -> finite ulps && ulps >= 0.0
            | StoppingTest.ExactZero -> true)

    /// The arm a test answers when it stops a run.
    let armOf (test: StoppingTest) : StoppingArm =
        match test with
        | StoppingTest.AbsoluteStep _ -> StoppingArm.AbsoluteStep
        | StoppingTest.RelativeChange _ -> StoppingArm.RelativeChange
        | StoppingTest.BracketUlps _ -> StoppingArm.Bracket
        | StoppingTest.ExactZero -> StoppingArm.ExactZero

    /// Whether the bracket `[lo, hi]` is at most `ulps` units wide: `hi − lo ≤ ulps × 2⁻⁵² × max(|lo|, |hi|)`,
    /// the product formed left to right.
    let bracketWithin (ulps: float) (lo: float) (hi: float) : bool =
        let a = magnitude lo
        let b = magnitude hi
        hi - lo <= ulps * eps * (if a < b then b else a)

    /// Whether the test stops a run that moved from `previous` to `current` by `step`, with bracket
    /// `[lo, hi]`. `ExactZero` answers `false` here: a method reads a zero of its own function before
    /// it reads the rule.
    let stops (test: StoppingTest) (previous: float) (current: float) (step: float) (lo: float) (hi: float) : bool =
        match test with
        | StoppingTest.AbsoluteStep tol -> magnitude step <= tol
        | StoppingTest.RelativeChange(tol, floor) -> magnitude (current - previous) / (magnitude current + floor) < tol
        | StoppingTest.BracketUlps ulps -> bracketWithin ulps lo hi
        | StoppingTest.ExactZero -> false

    /// Whether an objective that moved from `previous` to `current` ROSE, as the rule's test would see
    /// it: the test's own comparison over the signed change. Under `RelativeChange` a rise is
    /// `not ((current − previous) / (|current| + floor) < tolerance)`; under `AbsoluteStep`,
    /// `not (current − previous ≤ tolerance)`; under `ExactZero`, `not (current ≤ previous)`. A value
    /// that is not a number is a rise under every arm. `BracketUlps` reads no objective and answers
    /// `true`; `Damped.iterate` refuses it before asking.
    let rises (test: StoppingTest) (previous: float) (current: float) : bool =
        match test with
        | StoppingTest.AbsoluteStep tol -> not (current - previous <= tol)
        | StoppingTest.RelativeChange(tol, floor) -> not ((current - previous) / (magnitude current + floor) < tol)
        | StoppingTest.BracketUlps _ -> true
        | StoppingTest.ExactZero -> not (current <= previous)
