### The evaluator's `Exp`, `Ln`, `Log10` and float-exponent `Pow` are exact on the pinned elementary functions (Phase 594, `DECISIONS.md` D54) — BREAKING, `behaviour`

**What changed.** The evaluator's float-exponent `Pow`, `Exp`, `Ln` and `Log10` read `Elementary`'s published
special-value tables (`powTable`, `expTable`, `lnTable`) first and answer every other input with the pinned
`Elementary.pow` / `exp` / `ln` / `log10` (D52), never the host's maths library; D43's private copy of the table in
`DataFrame.fs` is gone. So every column they compute is `Exactness.Pinned` in the static walk
(`SchemaWalk.exactnessOf`), `SchemaWalk.hostRoundedDecisions` reports no decision for a pipeline over pinned
inputs, and the columns are byte-identical on every host that transcribes `Elementary`. A named function built
from them (Phase 596), a curve fed by them and a fit over their terms become exact with them. `Null` outside the
reals, D16's one NaN and the integer-exponent routine are unchanged. The `HostRounded` mark, the decision report
and the vector tolerance stay, for a value its caller declares host-rounded (the seam Phase 599's registered host
functions take); no scalar function produces one.

**The class.** No public surface moves: every changed function is internal, and the regenerated baselines under
`api/` are byte-identical. What a consumer RECEIVES moves — a column the walk marked `HostRounded` is `Pinned`, a
decision report that named one of these functions is empty, and a value off the table may differ in its last bit
from the host library's answer on some input — so the class is `behaviour`, breaking. **It RIDES `0.43.0`**: the
slot is untagged, publicly unpinned, and already carries breaking moves one minor past `v0.42.0`.

**The law vectors.** In `conformance/laws/transform-laws.json` the four power vectors that carried
`expected.tolerance` (`power-special-values`, `power-float-exponent`, `power-exp`, `power-ln-log10`) lose it and
are compared byte for byte; their recorded tables are unchanged — the pinned routines answer the bits .NET's library
did at every drawn input — and so is every other vector. No vector carries a tolerance now; the member and its
specification in the file's description stay for a vector whose walk marks a declared host-rounded column.

**What a consumer does.** A consumer that read `Exactness.HostRounded` or the decision report to decide where to
compare in ulps compares these columns byte for byte. A language twin that certifies the transform laws replaces
its host `pow` / `exp` / `log` / `log10` behind the table with a transcription of `Elementary` (certified by the
elementary vectors in `linear-algebra-laws.json`) and drops the tolerance from the four power vectors; until it
does, those vectors are red on it at any input where its library and the pinned routine differ.
