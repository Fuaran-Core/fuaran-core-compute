# Units in a pipeline — what the evaluator checks and carries

A one-page note for a consumer whose schemas state units (`Field.withUnit`, Core's `Fuaran.Core.Unit`):
what `Fuaran.Compute.DataFrame` does with them since `0.40.0` (Phase 429; the decision record is
`DECISIONS.md` D29). Read it beside Core's `docs/units.md`, which is the algebra's own note — what a
unit is, which texts parse, how two units compare.

Nothing here changes a pipeline over columns that state no unit: such a column is **unconstrained**, it
composes with anything, and the evaluator answers what it answered before. The check becomes visible the
moment a schema states a unit or an expression names a conversion.

## The rules, in one table

| Where | What happens |
|---|---|
| A column | Carries its field's unit. No unit stated: unconstrained. |
| A literal, a param, a `now` | Unconstrained. Give a literal a unit with `Convert(Lit 5, m)`. |
| `d + t`, `d - t`, `d > t`, `Coalesce`, a `Case`'s arms, `Least`, `Greatest`, `InList` | The stated units must be **one unit** — one canonical text. `km` beside `m` is refused until converted. The answer carries the unit (a comparison answers a boolean and carries none); an unconstrained operand takes the other's. |
| `d * t`, `d / t`, `Quotient`, `Mod` | Composed through the algebra: `m * m` is `m2`, `m / s` is `m/s`, `1 / s` is `/s`, `m * 2` is `m`, `m mod 7` is `m`. |
| `Cast`, `Abs`, `Round`, `Floor`, `Ceil`, `Rounded` | Keep the operand's unit. |
| `Sqrt`, the text and date functions | Carry none (the algebra states integer powers only). |
| `Pow(x, n)` with a literal integer `n` | Composed through `Unit.pow`: `m` squared is `m2`, `s` to the power `-1` is `/s`, anything to the power `0` is unconstrained. An exponent product the algebra cannot carry is refused as a mismatch against `a power the algebra can carry` (the node in the error carries the exponent). |
| `Pow` under any other exponent, `Exp`, `Ln`, `Log10` | Require a **dimensionless** operand: an unconstrained one, or one whose unit is `1` (a ratio of one unit, `t / t`). A dimensioned operand — or a dimensioned exponent — is refused as a mismatch against `1`. The answer carries none. |
| `Convert(expr, unit)` | The explicit conversion: a tag over an unconstrained operand, the identity over the same unit, the exact ratio between two units of one dimension, a refusal across dimensions. Always answers a **float**. |
| `Sum`, `Mean`, `Min`, `Max`, `Median`, `First`, `Last`, `StdDev` | Keep the column's unit; `Count` and `CountDistinct` carry none. |
| `Lag`, `Lead`, `CumulSum`, `CumulMax`, `CumulMin`, `RollingSum`, `RollingMean` | Keep the column's unit; `RowNumber`, the ranks and `NTile` carry none. |
| A join key pair | Must be one unit (it is an equality); an unconstrained side matches anything. Non-key columns keep their fields. |
| A union / intersect / except column pair | Must be one unit; the left's field is kept, a left stating none takes the right's. |
| `Project`, `Filter`, `Sort`, `Limit`, `Distinct`, `Window` | Keep every field's unit, label and description. |
| `Derive`, an aggregate column, a window column | Carry the unit the rule derives and no label or description. |
| `Pivot` | Index columns keep their fields; each value column carries the aggregate's unit. |
| `Unpivot` | Id columns keep their fields; `value` carries the one unit every value column states, none otherwise. |

## The refusal

```fsharp
match DataFrame.evalPipeline [ Derive("x", Binary(Add, Col "d", Col "t")) ] measures with
| Error(UnitMismatch(step, expr, left, right)) ->
    // step  = "derive 'x'"           the step the units met in
    // expr  = Binary(Add, Col "d", Col "t")   the node, as written
    // left  = "m", right = "s"       both units, by canonical text
    printfn "%s" (DataFrame.errorString e)   // unit mismatch in derive 'x': m against s in add('d', 't')
| _ -> ()
```

It is decided over the step's **schema**, before any row is read: an empty frame is refused exactly as a
full one. Steps name themselves as `derive '<name>'`, `filter`, `join key '<l>' = '<r>'`,
`union column '<name>'` (and `intersect` / `except`).

## Converting

```fsharp
// kilometres beside metres: refused until converted
Derive("total", Binary(Add, Convert(Col "dk", km), Col "d"))     // total is in m, dk scaled by 1000 as a float
Derive("k", Convert(Col "d", km))                                 // 100 m -> 0.1 km, a float
Derive("five", Convert(Lit(Int 5), m))                            // a literal given a unit: 5.0 m
Derive("x", Convert(Col "d", s))                                  // refused: m against s
```

A conversion resolves **before** evaluation into the arithmetic that performs it — `Cast(FloatType, …)`
under the target, times the exact ratio as a float literal where the unit differs — so the evaluator
proper never meets one, and a conversion always answers a float. A decimal operand is cast as `Cast`
casts it; a caller that needs the exact product writes `Quotient` with the factor.

Wire: `{"$type":"convert","expr":…,"unit":"km"}` — the unit by its canonical text.

## Reading units without evaluating

- `DataFrame.unitOf cols expr` — the unit an expression would carry over a schema, or the mismatch.
- `DataFrame.resolveUnits step cols expr` — the same, with the expression's conversions resolved: what
  every `Filter` and `Derive` runs before its first row.
- `SchemaWalk.ofPipeline schema pipeline` — every output column's name, type, unit, label and
  description, statically; `SchemaWalk.unitOf name knowledge` reads one.
- A registered pipeline query (`Fuaran.Compute.PipelineQuery`) checks its declared `ResultSchema`'s
  units against the walk's and refuses a difference (`ResultDisagreement.UnitDiffers`).

## What is proved, and what is assumed

`proofs/Pipeline.fst` section 13 models the check over the unit algebra as a parameter and proves that an
accepted expression evaluates convert-free, that a schema stating no unit leaves every convert-free
expression — and so every step of a unitless pipeline — unchanged, that every refusal names two different
units or a conversion the algebra has no factor for, and each rule above as the unfolding of the check.
That the algebra is Core's, canonical in its texts and exact in its factors, is a named premise
(`proofs.json`: `pipeline-unit-algebra-is-core`), Core's own `proofs/Unit.fst`'s to prove; the unit
differential in `ProofOracleTests` runs the extracted check at Core's algebra beside production's.
