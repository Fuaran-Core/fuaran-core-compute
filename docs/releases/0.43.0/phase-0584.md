### The transform law vectors pin an unknown sort, partition or order key and a union whose sides do not join (Phase 584, `DECISIONS.md` D46) — additive, no public surface moves

**What changed.** `conformance/laws/transform-laws.json` gains thirteen `evalPipeline` vectors, appended after the
overflow shapes as iterations 91 to 103 (`iterations` reads `104`; every earlier vector is byte-identical apart from
the `kitVersion` stamp the emitter reads off the assembly):

| Vector | Pipeline | Answer |
|---|---|---|
| `ordering-sort-unknown-key-refused` | `sort` by the absent `revenue` | refused, `UnknownColumn` |
| `ordering-sort-known-key` | `sort` by `w` desc, `g` asc | answered |
| `ordering-pivot-sort-unknown-key-refused` | `pivot`, then `sort` by `revenue` (the reported shape) | refused, `UnknownColumn` |
| `ordering-pivot-sort-known-key` | `pivot`, then `sort` by a pivoted column | answered |
| `ordering-sort-limit-unknown-key-refused` | `sort` by `revenue`, `limit 2` (the fused top-n) | refused, `UnknownColumn` |
| `ordering-sort-limit-known-key` | `sort` by `v`, `limit 2` | answered |
| `ordering-window-partition-unknown-key-refused` | `window` partitioned by the absent `dept` | refused, `UnknownColumn` |
| `ordering-window-order-unknown-key-refused` | `window` ordered by `revenue` | refused, `UnknownColumn` |
| `ordering-window-known-keys` | `window` partitioned by `g`, ordered by `v` | answered |
| `union-same-types` | an int column beside an int column | answered, int |
| `union-int-float-widens` | an int column beside a float column | answered, float |
| `union-string-int-refused` | an int column beside a string column | refused, `TypeError` |
| `union-name-mismatch-refused` | sides whose second column's names differ | refused, `JoinError` |

A refused vector's `expected` carries `"error"` naming the class, as the overflow vectors' do (D45).

**Why.** The reference has refused all three ordering keys since `0.39.0` (Phases 412 and 441) and an unjoinable
union pair since Phase 469, but no vector reached any of them, so a host that sorts by an absent key and returns its
input order, or keeps the left side's types whatever the right side holds, was certified green. With these vectors
such a host is red on the refused vectors, and a host that refuses too eagerly is red on the answered neighbours.

**What a consumer must do.** Nothing in .NET: no package's public surface or behaviour moves. A host certifying
against the corpus copy must resolve every sort, partition and order key against the step's input schema and refuse
one that names no column as `UnknownColumn`, type a union column by the column-type join and refuse a pair with no
join as `TypeError`, and compare `expected.error` where a vector carries it. The corpus copy follows the release
(`DECISIONS.md` D6 (4)), so the vectors reach the hosts when this version is tagged.
