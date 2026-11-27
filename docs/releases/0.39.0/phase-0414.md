### The bare `Rank` window function is deprecated in favour of `DenseRank` and `CompetitionRank` (Phase 414, `DECISIONS.md` D24) — documentation, no class

**What changed.** `WindowFn.Rank`'s documentation opens with its deprecation and names the explicit
spellings. `DenseRank` is the same computation, the gapless rank (`1, 1, 2`). `CompetitionRank` is
SQL's `RANK()` (`1, 1, 3`). It is marked in documentation only, not by `[<Obsolete>]`. An obsolete
union case warns at every exhaustive match over `WindowFn`, which a consumer that builds with
warnings as errors could not remove without a wildcard.

**What did not change.** `Rank` evaluates, encodes as `"rank"` and decodes from `"rank"` exactly as
before, so no pipeline's output or wire form moves. A pipeline should write `DenseRank`, which encodes
as `"denseRank"`.

**What is planned.** `Rank` is never re-pointed at the gapped semantics. It may be removed no
earlier than `1.0.0`, and only with `"rank"` kept as a decode alias of `DenseRank` (D24).

**The class.** No public signature, no output and no wire byte moves. It rides the `0.39.0` draft
and moves no number.
