### A refresh reads a derived column the reference BOXES as the reference reads it, and a pack verdict that flips under one type clears stability (Phase 470, `DECISIONS.md` D38) — none, `behaviour`

**What changed.** Phase 464 made the incremental walk carry a derived column's cells widened to the
decided type (an int under a float or decimal column as that type's value), as the reference's
`Vec.packAt` carries them — but `packAt` carries them so only while its typed pass holds: a present cell
the type does not carry, or a decimal column whose texts `ScaledDecimal.scaleOf` cannot carry at one
scale (a text past the width among them), abandons the typed carrier for the BOXED cells, and every cell
then reads as it was, an int included. The walk widened unconditionally, so on a derived column a named
row retyped from int to decimal with a text past the width, `Incremental.refresh` refused `arithmetic
between a decimal and a float` where `DataFrame.evalPipelineWithInEnv` answered `Float 1.5` (an `Add` of a
float over the standing rows' ints); and a verdict flipping under ONE type — the wide text arriving or
leaving while the column stayed decimal — reused every cell computed over the other reading, because the
stability check compared the type alone. The walk now widens exactly under the verdict `packAt` takes
(`typedPack`), and a derive whose type OR verdict is not the prior evaluation's clears every cached cell
and every reused group downstream of it. The result equals the full evaluation on these shapes, as the
seam has always claimed.

**The class.** No public signature moves and no wire form moves (`api/`, the state codec and the law
vectors are unchanged). What a consumer can observe is a result — a value where a refusal was answered,
an int cell where a decimal was — on a pipeline whose derived column boxes between refreshes, which is
the full evaluation's answer: a `behaviour` change, carried on the open `0.41.0` draft as an additive
one (the draft already carries a BREAKING class, so it rides).

**What a consumer does.** Nothing, unless a pipeline relied on the walk's widened reading of a column the
reference boxes; a consumer that wants every int in a decimal column read as a decimal casts the int arm
(`Cast(decimal, …)`), which keeps the typed pass whatever the texts beside it.
