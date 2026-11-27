### The static schema walk types a `Union` as the evaluator does (Phase 469, `DECISIONS.md` D36) — BREAKING, `behaviour`

**What changed.** `SchemaWalk.ofTransform` answered a `Union` with its input knowledge, so a union column
kept the LEFT side's type where the evaluator widens it to the join of the two sides' types (Phase 423,
D18: an `int` beside a `float` is a `float` column). The walk now reads the right operand as a join does
and types each column through the evaluator's own per-column rule, so over a declared or embedded right it
answers the evaluator's names, types and units. A column pair the evaluator refuses (two types no column
holds together, two units) reads as a column with no decided type; the walk still carries no refusal
verdict (D26), and the evaluator still refuses the pipeline. Over an undeclared `Ref` right every name
stays and no type is decided, because the right may widen any of them. Every other verb's walk is
unchanged.

**The class.** No public signature moves, and `api/` is unchanged. What a consumer can observe is a
static schema answer: a `behaviour` change, the class of Phases 412, 441 and 450. A registered pipeline
query is checked against the walk, so a declaration that relied on the old answer is now refused at
registration: `TypeDiffers` where it declared the left type of a widening union column, `TypeUndecidable`
where the pair is one the evaluator refuses on every frame.

**What a consumer does.** Read or declare the joined type of a union column (`float` for `int` beside
`float`, `decimal` for `int` beside `decimal`); declare the right operand's schema where a decided type is
wanted from a walk over a `Ref`; and cast one side to the other's type where the evaluator refuses the
pair.
