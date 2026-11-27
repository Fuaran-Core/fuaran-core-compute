### `Pow`, `Exp`, `Ln` and `Log10` enter the scalar set; the static walk carries an exactness mark (Phase 557, `DECISIONS.md` D43) — BREAKING, `union-widening` + `record-widening`

**What changed.** `ScalarFn` gains four cases: `Pow` (an integer exponent runs one pinned routine, exact
and byte-identical on every host; a float exponent is host-rounded), `Exp`, `Ln` and `Log10`
(host-rounded, within 2 ulp of the correctly rounded result across hosts, deterministic within one), each
behind a special-value table evaluated before any host call; wire tags `pow` / `exp` / `ln` / `log10`.
`ColumnKnowledge` gains `Exactness` (`Pinned | HostRounded`, a new public type), computed by `SchemaWalk`
per output column and read by `SchemaWalk.exactnessOf`; `SchemaWalk.hostRoundedDecisions` (and
`hostRoundedDecisionsFrom`) list every discrete decision a host-rounded value reaches as
`HostRoundedDecision` records — admitted, never refused. `Typing.powLit` types a power under a literal
exponent. `ColumnKnowledge.withExactness` / `joinExactness` are the builders. Units: a literal-integer
`Pow` composes `Unit.pow`; the transcendentals and any other `Pow` require a dimensionless operand and
refuse a dimensioned one as `UnitMismatch` against `1`; a power the algebra cannot raise the unit to is
refused against `a power the algebra can carry`.

**The class.** `ScalarFn` gaining four cases breaks every exhaustive `match` over it (`union-widening`),
and `ColumnKnowledge` gaining a field breaks every full-literal construction of it (`record-widening`;
construct through `ColumnKnowledge.named` / `ofField`, which is what they are for). The surface guard
classifies both from the regenerated `api/` baselines; the `0.41.0` draft already carries a BREAKING class,
so the change RIDES the draft and the number does not move.

**What a consumer does.** Add the four arms to any exhaustive `match` over `ScalarFn`; construct
`ColumnKnowledge` through its builders. A consumer that never names either changes nothing.

**Content hashes and cached results are host-scoped where a `HostRounded` column is in play.** Two hosts
agree on such a column within the bound, not byte for byte, so a digest over the output bytes of a
pipeline with a host-rounded output names the host that produced them: any cache keyed on output bytes
must carry the host identity, and a consumer addressing results across hosts keys on the pinned columns
or on the input. A pipeline reading none of the four functions is as byte-exact across hosts as before.

**The law vectors.** Eight `power-` vectors join `conformance/laws/transform-laws.json` after the repeat
shapes; a vector whose output the walk marks host-rounded carries `expected.tolerance`, naming each such
column with the bound in ulps a certifying host compares within, and every earlier vector is byte-identical.
The corpus copy follows the release rule (D6 (4)) and moves when this version is released. The language
twins follow on their own side: a host that does not implement the functions reports the new vectors as
unsupported rather than skipping them.
