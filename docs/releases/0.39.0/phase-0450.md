### A window output that names an existing column is refused (Phase 450, `DECISIONS.md` D28) — BREAKING, `behaviour`

**What changed.** A `Window` whose `As` names a column its input already carries is refused with
`TypeError("window output 'x' names a column the input already carries; …")`. Before, the window
appended a second column of that name and answered `Ok`, a result that re-entered the evaluator by name
as the first of the two columns and that Core's `Table.validate` refuses. The evaluator, the public row
form (`windowStep`) and the incremental seam (prime and refresh) all refuse, before any row is read. An
unknown partition or order key is still named first.

**The class.** No public signature moves; the function that carries the rule is internal. What a
consumer can observe is the refusal: a `behaviour` change, the class of Phases 412 and 441. The `0.39.0`
draft is untagged, publicly unpinned and already carries a BREAKING class, so this change RIDES it.

**What a consumer does.** Give the window's output a name its input does not carry, or `Project` the
existing column away ahead of the window. A consumer reading a window's result by name reads the
window's column under the name it chose, as before.

**The law vectors.** No vector in `conformance/laws/transform-laws.json` names a window's output after
an existing column, so none moves. The corpus copy follows the release rule (D6 (4)) unchanged.
