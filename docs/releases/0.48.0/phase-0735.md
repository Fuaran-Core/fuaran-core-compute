### A decision is a file of its own under `docs/decisions/`, named for the phase that made it; `DECISIONS.md` is closed at D91 (Phase 735, `D735.1`) — none, no package surface moves

**What changed.** A decision recorded from this phase on is a file of its own,
`docs/decisions/D<phase>.<n>.md`, named for the phase that made it and cited `D<phase>.<n>`: this phase's is
[`D735.1`](../../decisions/D0735.1.md), which fixes the layout. Nothing is allocated when a decision is written,
so two changes in flight can no longer edit one file or take one number. `DECISIONS.md` keeps D1 to D91 word for
word and gains a head note saying it is closed; every citation of those entries resolves as before.
`CONTRIBUTING.md` says where a decision is written and how it is cited, and `README.md` points at both records.

**What the roster family holds.** Two properties are new: `DECISIONS.md` holds exactly D1 to D91, each once,
with no other `## ` heading, so an entry added to it, renumbered or removed is refused by line; and every name
under `docs/decisions/` is a decision file whose header names its own decision and phase, a phase's decisions
running from 1 with no gap.

**For a consumer, nothing.** No source, signature or wire byte moves, and every baseline under `api/` is
unchanged.
