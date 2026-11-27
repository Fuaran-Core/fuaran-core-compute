### `Glm` calls the solvers, and the negative binomial's θ is a bracketed Newton (Phase 634, `DECISIONS.md` D80) — breaking

The draft already carries breaking moves, so this change RIDES `0.45.0`.

**What changed.** `Fuaran.Compute.Regression` references `Fuaran.Compute.Solvers` directly (it did through
Elementary), and `Glm`'s two iterations are calls of it: IRLS is `Damped.iterate`, and θ's Newton is
`Root.bracketedNewton` over `[2⁻²⁰, 2²⁰]`. No public surface moves: `api/Fuaran.Compute.Regression.txt`,
`api/Fuaran.Compute.Solvers.txt` and every other baseline are unchanged. The refusals keep their names and payloads
(`Separation`, `MeanOutOfRange`, `NotConverged`, `ThetaNotConverged`).

**What does not move.** Every IRLS fit, and every negative binomial fit with θ fixed, answers the bits and the
iteration count it answered. Every file under `conformance/laws/` is byte-identical, among them the three negative
binomial `Fit` rows of `transform-laws.json`. `Glm.fit` answers the same bits on every case of the GLM reference
corpus and on the authored cases that halve and that are refused, under three controls (87 recorded lines).

**What breaks: θ where the old iteration reflected or overshot.** θ was MASS's `theta.ml`, an unbracketed Newton that
reflected a negative θ and refused a θ above `2⁵⁰`. It now stays inside `[2⁻²⁰, 2²⁰]`, bisecting where a Newton step
leaves it, so:

- data whose θ is small (0.005 to 0.4 in the measurement), where the reflected iteration overshot, reflected and
  reached its cap, was refused as `ThetaNotConverged 0` and now has its θ estimated: 205 of 3,995 seeded designs,
  solved at the first alternation's means;
- where the two iterations take different paths and both answer, θ (and so the coefficients of the alternations
  after it) differs in its last digits: 94 of the 3,995;
- a θ whose start lies below `2⁻²⁰`, or whose estimate lies above `2²⁰`, is refused as `ThetaNotConverged`: 2 of the
  3,995 (starts below), both of whose old answers had no correct digit;
- a fit whose means reproduce the counts (every count equal) is refused as before.

Where both iterations answer the same path the answer is the same bits, and that is every case the corpus and the
vectors pin.

**What a consumer must do.** Nothing to compile. A consumer that stores or compares an estimated θ re-takes it at
`0.45.0` where the data's θ is small; one that relied on the refusal of such data now receives a fit. A host that
transcribes `Glm` transcribes the two solver calls and their arguments (D80 items 1 and 2) in place of the two loops.
