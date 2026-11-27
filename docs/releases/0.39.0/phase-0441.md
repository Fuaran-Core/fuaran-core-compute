### A partition key that names no column is refused (Phase 441, `DECISIONS.md` D26) — BREAKING, `behaviour`

**What changed.** A `Window` whose `PartitionBy` names a column the frame lacks is refused with
`UnknownColumn(name, available)`. Before, the window dropped the name, partitioned by the columns it
could find, and answered `Ok`. The evaluator, the public row form (`windowStep`) and the incremental
seam (prime and refresh) all refuse, before any row is read. The partition keys and the order keys now
pass through one resolution, the one a `Sort`'s keys pass. Where both kinds are unknown, the first
partition key is named. The function's own checks (a bucket count, the read column) still come first.

**The class.** No public signature moves; the functions that carry the rule are internal. What a
consumer can observe is the refusal: a `behaviour` change, the same class as Phase 412's. The `0.39.0`
draft is untagged, publicly unpinned and already carries a BREAKING class, so this change RIDES it.

**The law vectors.** No vector in `conformance/laws/transform-laws.json` partitions a window on a
column its table lacks, so none moves. The corpus copy follows the release rule (D6 (4)) unchanged.
