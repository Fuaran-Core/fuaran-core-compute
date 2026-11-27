### The transform law vectors pin int32 overflow on `Add`, `Sub`, `Mul` and integer `Pow` (Phase 572, `DECISIONS.md` D45) — additive, no public surface moves

**What changed.** `conformance/laws/transform-laws.json` gains twelve `evalPipeline` vectors, appended after the
power shapes as iterations 79 to 90 (`iterations` reads `91`; every earlier vector is byte-identical). For each of
`Add`, `Sub`, `Mul` and an integer-exponent `Pow`, one vector's result lies past the upper edge of the int32 band,
one past the lower edge, and one in-band neighbour lands on or beside the edges themselves:

| Operator | Above the band (refused) | Below the band (refused) | In-band neighbour (answered) |
|---|---|---|---|
| `add` | `MaxValue + 1` | `MinValue + -1` | `(MaxValue - 1) + 1`, `(MinValue + 1) + -1` |
| `sub` | `MaxValue - -1` | `MinValue - 1` | `(MaxValue - 1) - -1`, `(MinValue + 1) - 1` |
| `mul` | `MinValue * -1` | `-65536 * 32769` | `65536 * 32767`, `65536 * -32768`, `MaxValue * -1` |
| `pow` | `2 ^ 31` | `(-1291) ^ 3` | `(-2) ^ 31`, `1290 ^ 3`, `(-1290) ^ 3` |

Each operand is derived into a column first, so the operation runs over a column. A refused vector's `expected`
carries a new member, `"error": "OverflowError"`, beside `"verdict": "error"`: the class a host must refuse with.
No other vector names a refusal class, so a host reading the file as before still reads every earlier vector the
same way.

**Why.** The reference has refused an out-of-band integer result by name since Phase 39 (`checkedInt`, and
`powIntExact` since Phase 557), but no vector reached the band, so a host that computes over a wider integer and
answers without checking was certified green. With these vectors such a host is red on the six refused vectors,
and a host that refuses too eagerly is red on the four in-band ones.

**What a consumer must do.** Nothing in .NET: no package's public surface or behaviour moves. A host certifying
against the corpus copy must range-check every integer `add`, `sub`, `mul` and integer `pow` result against the
int32 band and refuse an out-of-band result as `OverflowError`, and should compare `expected.error` where a vector
carries it. The corpus copy follows the release (`DECISIONS.md` D6 (4)), so the vectors reach the hosts when this
version is tagged.
