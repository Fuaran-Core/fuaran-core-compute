namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 633 — the solver law vectors: each method of Fuaran.Compute.Solvers
//  over fixed functions, its answer computed by calling the package, rendered
//  through the substrate's one corpus writer (`CorpusFile`) into
//  `conformance/laws/solvers-laws.json` (DECISIONS.md D77).
//
//  The functions are a polynomial with a known root, one whose Newton step
//  leaves the bracket, flat regions, roots and minima at a bracket end; each
//  refusal appears by name, the cap beside the convergence it is not, and a
//  pair of rules one unit in the last place apart that stop one iteration
//  apart. Every float is its IEEE-754 binary64 bit pattern.
//
//  Fable-clean: the node leg (LinearAlgebraNode/) compiles this file and
//  renders the same document under JavaScript, and the suite holds the two
//  renderings and the committed file equal.
// ============================================================================

open Fuaran.Core
open Fuaran.Compute.Solvers

module SolversVectors =

    /// The file this family is written to under `laws/`.
    let fileName = "solvers-laws.json"

    /// One authored case: a method over a named function, and the run that answers it.
    type Case =
        { Id: string
          Op: string
          Input: (string * CorpusCell) list
          Run: unit -> CorpusCell }

    let private hex (x: float) : CorpusCell =
        CorpusFile.str (ElementaryVectors.hex x)

    // ---- the fixed functions, each in the order the description states -----------------------------

    /// `x·x·x − 2·x − 5`, Wallis's cubic: one real root, near 2.0945514815423265.
    let private cubic (x: float) : float = x * x * x - 2.0 * x - 5.0

    let private cubicDf (x: float) : float * float = cubic x, 3.0 * x * x - 2.0

    /// `x·x·x − 2·x + 2`: Newton from zero steps to one and back for ever.
    let private cycleDf (x: float) : float * float =
        x * x * x - 2.0 * x + 2.0, 3.0 * x * x - 2.0

    /// `x·x·x − 1`, whose derivative is zero at zero.
    let private unitCubeDf (x: float) : float * float = x * x * x - 1.0, 3.0 * x * x

    /// `(x − 1)·(x − 1)·(x − 1)`, flat at its root.
    let private flatCubeDf (x: float) : float * float =
        let d = x - 1.0
        d * d * d, 3.0 * d * d

    /// `x − 1.5`.
    let private line (x: float) : float = x - 1.5

    let private lineDf (x: float) : float * float = line x, 1.0

    /// `x − 0.3`, zero at no midpoint of `[0, 1]`.
    let private offLine (x: float) : float = x - 0.3

    /// `x − 1` below one, zero from one to two, `x − 2` above two.
    let private plateau (x: float) : float =
        if x < 1.0 then x - 1.0
        elif x > 2.0 then x - 2.0
        else 0.0

    /// `(x − 0.3) + ((x − 1)·0)/(x − 1)`: not a number at one, `x − 0.3` elsewhere.
    let private hole (x: float) : float =
        (x - 0.3) + ((x - 1.0) * 0.0) / (x - 1.0)

    /// `(x − x)/(x − x)`: not a number anywhere.
    let private nowhere (x: float) : float = (x - x) / (x - x)

    /// `(x − 2)·(x − 2)`.
    let private parabola (x: float) : float = (x - 2.0) * (x - 2.0)

    // ---- cells -------------------------------------------------------------------------------------

    let private armName (arm: StoppingArm) : string =
        match arm with
        | StoppingArm.AbsoluteStep -> "absoluteStep"
        | StoppingArm.RelativeChange -> "relativeChange"
        | StoppingArm.Bracket -> "bracket"
        | StoppingArm.ExactZero -> "exactZero"
        | StoppingArm.Cap -> "cap"

    let private stoppedFields (state: 'state -> CorpusCell) (s: Stopped<'state>) : (string * CorpusCell) list =
        [ "state", state s.State
          "value", hex s.Value
          "iterations", CorpusFile.num s.Iterations
          "arm", CorpusFile.str (armName s.Arm) ]

    let private refused (name: string) (more: (string * CorpusCell) list) : CorpusCell =
        CorpusCell.Row([ "verdict", CorpusFile.str "refused"; "error", CorpusFile.str name ] @ more)

    /// A run's answer: `{ verdict: ok, state, value, iterations, arm }`, or `{ verdict: refused, error }`
    /// with the refusal's own fields.
    let private answer
        (state: 'state -> CorpusCell)
        (r: Result<Stopped<'state>, SolverError<'state, string>>)
        : CorpusCell =
        match r with
        | Ok s -> CorpusCell.Row([ "verdict", CorpusFile.str "ok" ] @ stoppedFields state s)
        | Error SolverError.RuleInvalid -> refused "RuleInvalid" []
        | Error SolverError.BracketInvalid -> refused "BracketInvalid" []
        | Error(SolverError.NotFinite at) -> refused "NotFinite" [ "at", state at ]
        | Error(SolverError.NotConverged last) -> refused "NotConverged" (stoppedFields state last)
        | Error(SolverError.StepRefused why) -> refused "StepRefused" [ "reason", CorpusFile.str why ]
        | Error(SolverError.HalvingExhausted(k, why)) ->
            refused
                "HalvingExhausted"
                [ "iteration", CorpusFile.num k
                  "reason", CorpusFile.str (Option.defaultValue "none" why) ]

    let private ruleCell (rule: StoppingRule) : CorpusCell =
        CorpusCell.Row(
            [ "cap", CorpusFile.num rule.Cap ]
            @ (match rule.Test with
               | StoppingTest.AbsoluteStep tol -> [ "test", CorpusFile.str "absoluteStep"; "tolerance", hex tol ]
               | StoppingTest.RelativeChange(tol, floor) ->
                   [ "test", CorpusFile.str "relativeChange"
                     "tolerance", hex tol
                     "floor", hex floor ]
               | StoppingTest.BracketUlps ulps -> [ "test", CorpusFile.str "bracketUlps"; "ulps", hex ulps ]
               | StoppingTest.ExactZero -> [ "test", CorpusFile.str "exactZero" ])
        )

    let private absolute (cap: int) (tol: float) =
        StoppingRule.create cap (StoppingTest.AbsoluteStep tol)

    let private relative (cap: int) (tol: float) =
        StoppingRule.create cap (StoppingTest.RelativeChange(tol, 0.1))

    let private ulps (cap: int) (n: float) =
        StoppingRule.create cap (StoppingTest.BracketUlps n)

    // ---- the cases ---------------------------------------------------------------------------------

    let private bisect
        (id: string)
        (name: string)
        (f: float -> float)
        (rule: StoppingRule)
        (lo: float)
        (hi: float)
        : Case =
        { Id = id
          Op = "bisect"
          Input = [ "f", CorpusFile.str name; "rule", ruleCell rule; "lo", hex lo; "hi", hex hi ]
          Run = fun () -> Root.bisect rule f lo hi |> answer hex }

    let private newton
        (id: string)
        (name: string)
        (fdf: float -> float * float)
        (rule: StoppingRule)
        (shrink: float)
        (lo: float)
        (hi: float)
        (x0: float)
        : Case =
        { Id = id
          Op = "bracketedNewton"
          Input =
            [ "f", CorpusFile.str name
              "rule", ruleCell rule
              "shrink", hex shrink
              "lo", hex lo
              "hi", hex hi
              "x0", hex x0 ]
          Run = fun () -> Root.bracketedNewton rule shrink fdf lo hi x0 |> answer hex }

    let private integer (id: string) (name: string) (g: int -> float) (target: float) (lo: int) (hi: int) : Case =
        { Id = id
          Op = "integerSearch"
          Input =
            [ "g", CorpusFile.str name
              "target", hex target
              "lo", CorpusFile.num lo
              "hi", CorpusFile.num hi ]
          Run = fun () -> Root.integerSearch g target lo hi |> answer CorpusFile.num }

    let private golden
        (id: string)
        (name: string)
        (f: float -> float)
        (rule: StoppingRule)
        (lo: float)
        (hi: float)
        : Case =
        { Id = id
          Op = "goldenSection"
          Input = [ "f", CorpusFile.str name; "rule", ruleCell rule; "lo", hex lo; "hi", hex hi ]
          Run = fun () -> Minimise.goldenSection rule f lo hi |> answer hex }

    /// The damped problem every `iterate` vector runs: the relaxation `x ← x + gain·(2 − x)` on the
    /// objective `(x − 2)·(x − 2)`, the midpoint `0.5·standing + 0.5·candidate`. `stepKind` and
    /// `gate` name the variations: a step that refuses below three or one that adds 4096, and a state
    /// inadmissible below one or below twelve.
    let private damped
        (id: string)
        (rule: StoppingRule)
        (gain: float)
        (stepKind: string)
        (gate: string)
        (halvings: int)
        (seeded: bool)
        (start: float)
        : Case =
        let problem: DampedProblem<float, string> =
            { Step =
                fun x ->
                    match stepKind with
                    | "refuses-below-three" when x < 3.0 -> Error "below three"
                    | "adds-4096" -> Ok(x + 4096.0)
                    | _ -> Ok(x + gain * (2.0 - x))
              Inadmissible =
                fun x ->
                    match gate with
                    | "below-one" when x < 1.0 -> Some "below one"
                    | "below-twelve" when x < 12.0 -> Some "below twelve"
                    | _ -> None
              Objective = parabola
              Midpoint = fun standing candidate -> 0.5 * standing + 0.5 * candidate
              Halvings = halvings
              Seeded = seeded }

        { Id = id
          Op = "iterate"
          Input =
            [ "rule", ruleCell rule
              "gain", hex gain
              "step", CorpusFile.str stepKind
              "inadmissible", CorpusFile.str gate
              "halvings", CorpusFile.num halvings
              "seeded", CorpusFile.str (if seeded then "true" else "false")
              "start", hex start ]
          Run = fun () -> Damped.iterate rule problem start |> answer hex }

    /// `2⁻¹⁰`, and the float one unit in the last place below it.
    let private twoToMinusTen = 9.765625e-4

    let private belowTwoToMinusTen = twoToMinusTen * (1.0 - 1.1102230246251565e-16)

    let private square (k: int) : float = float k * float k

    let cases: Case list =
        [ // ---- bisection ----
          bisect "bisect-cubic" "cubic" cubic (absolute 100 1e-12) 2.0 3.0
          bisect "bisect-cubic-bracket-ulps" "cubic" cubic (ulps 200 2.0) 2.0 3.0
          bisect "bisect-cubic-relative" "cubic" cubic (relative 100 1e-10) 2.0 3.0
          bisect "bisect-cubic-exact-zero-rule" "cubic" cubic (StoppingRule.create 200 StoppingTest.ExactZero) 2.0 3.0
          bisect "bisect-root-at-low-end" "line" line (absolute 100 1e-12) 1.5 4.0
          bisect "bisect-root-at-high-end" "line" line (absolute 100 1e-12) 0.0 1.5
          bisect "bisect-root-at-first-midpoint" "line" line (absolute 100 1e-12) 1.0 2.0
          bisect "bisect-flat-region" "plateau" plateau (absolute 100 1e-12) -1.0 2.5
          // One unit in the last place of the tolerance moves the stopping iteration from ten to eleven.
          bisect "bisect-tolerance-at-step" "offLine" offLine (absolute 100 twoToMinusTen) 0.0 1.0
          bisect "bisect-tolerance-one-ulp-below-step" "offLine" offLine (absolute 100 belowTwoToMinusTen) 0.0 1.0
          // The same run converges in ten iterations under a cap of ten and is cut under a cap of nine.
          bisect "bisect-converges-at-its-cap" "offLine" offLine (absolute 10 twoToMinusTen) 0.0 1.0
          bisect "bisect-cut-at-its-cap" "offLine" offLine (absolute 9 twoToMinusTen) 0.0 1.0
          bisect "bisect-one-sign-refused" "cubic" cubic (absolute 100 1e-12) 3.0 4.0
          bisect "bisect-reversed-ends-refused" "cubic" cubic (absolute 100 1e-12) 3.0 2.0
          bisect "bisect-infinite-end-refused" "cubic" cubic (absolute 100 1e-12) 2.0 infinity
          bisect "bisect-cap-zero-refused" "cubic" cubic (absolute 0 1e-12) 2.0 3.0
          bisect "bisect-negative-tolerance-refused" "cubic" cubic (absolute 100 -1e-12) 2.0 3.0
          bisect
              "bisect-zero-floor-refused"
              "cubic"
              cubic
              (StoppingRule.create 100 (StoppingTest.RelativeChange(1e-10, 0.0)))
              2.0
              3.0
          bisect "bisect-not-finite-refused" "hole" hole (absolute 100 1e-12) 0.0 2.0

          // ---- the bracketed Newton ----
          newton "newton-cubic" "cubic" cubicDf (absolute 100 1e-13) 0.5 2.0 3.0 2.5
          newton "newton-cubic-every-narrowing-admitted" "cubic" cubicDf (absolute 100 1e-13) 1.0 2.0 3.0 2.5
          newton "newton-cubic-relative" "cubic" cubicDf (relative 100 1e-10) 0.5 2.0 3.0 2.5
          newton "newton-cubic-bracket-ulps" "cubic" cubicDf (ulps 200 4.0) 0.5 2.0 3.0 2.5
          newton "newton-step-leaves-bracket" "cycle" cycleDf (absolute 100 1e-13) 0.5 -3.0 0.5 0.0
          newton "newton-zero-derivative" "unitCube" unitCubeDf (absolute 100 1e-13) 0.5 -1.0 3.0 0.0
          newton "newton-flat-region" "flatCube" flatCubeDf (absolute 200 1e-6) 0.5 0.0 3.0 2.5
          newton "newton-flat-region-every-narrowing-admitted" "flatCube" flatCubeDf (absolute 200 1e-6) 1.0 0.0 3.0 2.5
          newton "newton-root-at-low-end" "line" lineDf (absolute 100 1e-13) 0.5 1.5 4.0 3.0
          newton "newton-root-at-high-end" "line" lineDf (absolute 100 1e-13) 0.5 0.0 1.5 1.0
          newton "newton-start-is-the-root" "line" lineDf (absolute 100 1e-13) 0.5 0.0 4.0 1.5
          newton "newton-cut-at-its-cap" "cubic" cubicDf (absolute 2 1e-13) 0.5 2.0 3.0 2.5
          newton "newton-shrink-zero-refused" "cubic" cubicDf (absolute 100 1e-13) 0.0 2.0 3.0 2.5
          newton "newton-shrink-above-one-refused" "cubic" cubicDf (absolute 100 1e-13) 1.5 2.0 3.0 2.5
          newton "newton-start-outside-refused" "cubic" cubicDf (absolute 100 1e-13) 0.5 2.0 3.0 3.5
          newton "newton-start-at-an-end-refused" "cubic" cubicDf (absolute 100 1e-13) 0.5 2.0 3.0 2.0
          newton "newton-one-sign-refused" "cubic" cubicDf (absolute 100 1e-13) 0.5 3.0 4.0 3.5

          // ---- the integer search ----
          integer "integer-square" "square" square 50.0 0 100
          integer "integer-target-met-exactly" "square" square 49.0 0 100
          integer "integer-answer-at-low-end" "square" square 0.0 0 100
          integer "integer-answer-at-high-end" "square" square 9999.5 0 100
          integer "integer-one-point-bracket" "square" square 49.0 7 7
          integer "integer-negative-bracket" "float" float -7.5 -50 50
          integer "integer-no-answer-refused" "square" square 10000.5 0 100
          integer "integer-nan-target-refused" "square" square nan 0 100
          integer "integer-reversed-ends-refused" "square" square 50.0 100 0

          // ---- the golden section ----
          golden "golden-parabola" "parabola" parabola (absolute 200 1e-8) 0.0 5.0
          golden "golden-parabola-relative" "parabola" parabola (relative 200 1e-9) 0.0 5.0
          golden "golden-parabola-bracket-ulps" "parabola" parabola (ulps 500 8.0) 0.0 5.0
          golden
              "golden-parabola-exact-zero-rule"
              "parabola"
              parabola
              (StoppingRule.create 500 StoppingTest.ExactZero)
              0.0
              5.0
          golden "golden-minimum-at-low-end" "line" line (absolute 200 1e-10) 2.0 3.0
          golden "golden-minimum-at-high-end" "parabola" parabola (absolute 200 1e-10) 0.0 1.0
          golden "golden-flat-region" "plateau" plateau (absolute 200 1e-10) 1.0 2.0
          golden "golden-cut-at-its-cap" "parabola" parabola (absolute 3 1e-8) 0.0 5.0
          golden "golden-empty-bracket-refused" "parabola" parabola (absolute 200 1e-8) 2.0 2.0
          golden "golden-not-finite-refused" "nowhere" nowhere (absolute 200 1e-8) 0.0 1.0

          // ---- the damped iteration ----
          damped "damped-relaxation" (relative 100 1e-8) 0.5 "relaxes" "none" 10 false 10.0
          damped "damped-relaxation-absolute" (absolute 100 1e-10) 0.5 "relaxes" "none" 10 false 10.0
          damped "damped-overshoot-halved" (relative 100 1e-8) 2.75 "relaxes" "none" 10 false 10.0
          damped "damped-seeded-first-step-unhalved" (relative 100 1e-8) 2.75 "relaxes" "none" 10 true 10.0
          damped "damped-inadmissible-then-halved" (relative 100 1e-8) 2.75 "relaxes" "below-one" 10 false 3.0
          damped
              "damped-lands-on-zero"
              (StoppingRule.create 100 StoppingTest.ExactZero)
              1.0
              "relaxes"
              "none"
              10
              false
              10.0
          damped "damped-cut-at-its-cap" (relative 3 1e-8) 0.5 "relaxes" "none" 10 false 10.0
          damped "damped-step-refused" (relative 100 1e-8) 0.5 "refuses-below-three" "none" 10 false 10.0
          damped "damped-rise-no-halving-removes" (relative 100 1e-8) 0.5 "adds-4096" "none" 10 false 10.0
          damped "damped-inadmissible-no-halving-removes" (relative 100 1e-8) 0.5 "relaxes" "below-twelve" 10 false 10.0
          damped
              "damped-seeded-inadmissible-refused-at-once"
              (relative 100 1e-8)
              0.5
              "relaxes"
              "below-twelve"
              10
              true
              10.0
          damped "damped-no-halvings-allowed" (relative 100 1e-8) 2.75 "relaxes" "none" 0 false 10.0
          damped "damped-bracket-rule-refused" (ulps 100 4.0) 0.5 "relaxes" "none" 10 false 10.0
          damped "damped-negative-halvings-refused" (relative 100 1e-8) 0.5 "relaxes" "none" -1 false 10.0 ]

    let private caseCell (c: Case) : CorpusCell =
        CorpusCell.Row
            [ "id", CorpusFile.str c.Id
              "op", CorpusFile.str c.Op
              "input", CorpusCell.Row c.Input
              "expected", c.Run() ]

    let private functions =
        "The functions, each evaluated in the order written, left to right: `cubic` is `x*x*x - 2*x - 5` "
        + "with derivative `3*x*x - 2`; `cycle` is `x*x*x - 2*x + 2` with derivative `3*x*x - 2`; `unitCube` "
        + "is `x*x*x - 1` with derivative `3*x*x`; `flatCube` is `d*d*d` with derivative `3*d*d`, `d = x - 1`; "
        + "`line` is `x - 1.5` with derivative `1`; `offLine` is `x - 0.3`; `plateau` is `x - 1` below one, "
        + "`x - 2` above two and `0` between; `hole` is `(x - 0.3) + ((x - 1)*0)/(x - 1)`, not a number at "
        + "one; `nowhere` is `(x - x)/(x - x)`, not a number anywhere; `parabola` is `(x - 2)*(x - 2)`; `square` is `k*k` and `float` is `k`, each as a float. The "
        + "vectors whose `op` is `iterate` run the relaxation `x + gain*(2 - x)` on the objective `parabola` "
        + "with the midpoint `0.5*standing + 0.5*candidate`: `step` is `relaxes`, `refuses-below-three` (the "
        + "step refuses with the reason `below three` at a state below three) or `adds-4096` (the step "
        + "proposes `x + 4096`), and `inadmissible` is `none`, `below-one` or `below-twelve` (a state below "
        + "that bound is inadmissible, with that reason)."

    let private description =
        "The reference answers for Fuaran.Compute.Solvers over AUTHORED functions (Phase 633, DECISIONS.md "
        + "D77): `Root.bisect`, `Root.bracketedNewton`, `Root.integerSearch`, `Minimise.goldenSection` and "
        + "`Damped.iterate`, each over a polynomial with a known root, a function whose Newton step leaves "
        + "the bracket, flat regions, and roots and minima at a bracket end. A float is its IEEE-754 "
        + "binary64 bit pattern as sixteen lowercase hex digits, or `nan`. `op` names the method and `input` "
        + "its arguments; `rule` is the stopping rule: its `cap` and its `test` (`absoluteStep` with a "
        + "`tolerance`, `relativeChange` with a `tolerance` and a `floor`, `bracketUlps` with `ulps`, or "
        + "`exactZero`). Each vector answers `{ verdict: ok, state, value, iterations, arm }`, the state "
        + "the run stopped at, the function or objective value there, the iterations it took and the arm "
        + "that stopped it (`absoluteStep`, `relativeChange`, `bracket`, `exactZero`), or `{ verdict: "
        + "refused, error }` with the refusal's name and its fields: `NotFinite` carries `at`; `NotConverged` "
        + "carries the last `state`, `value` and `iterations` with the arm `cap`, which is never a "
        + "convergence; `StepRefused` carries the caller's `reason`; `HalvingExhausted` carries the "
        + "`iteration` and the caller's `reason` for an inadmissible last candidate, or `none`. Every cell "
        + "is compared byte for byte: a host that stops one iteration apart differs in `iterations` and in "
        + "every bit after. `bisect-tolerance-at-step` and `bisect-tolerance-one-ulp-below-step` differ by "
        + "one unit in the last place of the tolerance and stop one iteration apart."

    /// The family's document as the one corpus writer renders it.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "solvers"
                  "description", CorpusFile.str description
                  "functions", CorpusFile.str functions ]
              Items = "vectors"
              Rows = cases |> List.map caseCell }
