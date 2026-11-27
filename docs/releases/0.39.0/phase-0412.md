### A sort key that names no column is refused (Phase 412, `DECISIONS.md` D21) — BREAKING, `behaviour`

**What changed.** A `Sort` key, or a `Window`'s `OrderBy` key, that names no column of the frame is
refused with `UnknownColumn(name, available)` — the error every other verb answers — where it was
silently dropped and the input order returned as `Ok`. The fused sort-then-limit and the incremental
seam (prime and refresh) refuse the same way, and `Plan.isTotal` counts a `Sort` as total only when
every key is a literal the schema carries. The exported comparator `DataFrame.rowCompareBy` still
drops an unknown key (a comparator has no error to answer); its documentation says the reference
refuses such a key first. A `Window`'s `PartitionBy` is unchanged.

**The class.** No public signature moves (the surface family prints no move for this change); the
change a consumer can observe is the refusal, a `behaviour` change. The `0.39.0` draft is untagged,
publicly unpinned and already carries a BREAKING class (Phase 423), so this change RIDES it.

**The law vectors.** No vector in `conformance/laws/transform-laws.json` sorts or orders a window on a
key its table lacks, so none moves; the corpus copy follows the release rule (D6 (4)) unchanged.
