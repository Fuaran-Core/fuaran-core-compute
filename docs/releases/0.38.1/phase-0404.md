### Every float the evaluator computes carries one NaN, and the gate runs Release (Phase 404, `DECISIONS.md` D16) — none, `determinism`

**What changed.** No public surface moves, and no answer changes its value: a NaN answer is still
`NaN`, every other answer is the same to the bit. What moves is the NaN's own bits. One rule,
evaluator-wide: every float the evaluator computes — an expression's (`Derive`, `evalExprInRow`), an
aggregate's (`GroupBy`, `Pivot`, `DataFrame.aggregateCells`) and a window function's — leaves it with
`Double.NaN`'s bit pattern wherever it is a NaN, whatever the arithmetic produced: `-inf + +inf`,
`0 * inf`, a running total or a `Sum` over both infinities, a `Lag`, `First`, `Max` or `Col` carrying a
source NaN of another payload. The batch evaluator, the row form and the incremental refresh answer
through the same exits, so they agree to the bit. A step that computes nothing (`Project`, `Filter`,
`Sort`, a join) hands on the caller's cells as given. IEEE 754 leaves a NaN result's sign and payload
open, and the hosts and builds took that latitude: the hardware's default NaN has its sign clear on
Arm64 and set on x64, and a Release build commuted an addition the Debug build did not, so one window
answered two bit patterns from one input. On the canonical wire a NaN was already one token (`NaN`),
so no serialised byte moves; the bits are what an in-process comparison, a parity vector's .NET half
and a content address over raw values see.

`verify.ps1` takes `-Configuration Debug|Release` (Debug by default) and runs the build and the main
suite in it; the clock leg stays Release under either. `ci.yml` runs both configurations, and `publish-packages.yml`
verifies in Release and packs `--no-build` from what it verified.

**Nothing to adopt.** A consumer comparing answers by `DoubleToInt64Bits` now sees one NaN.
