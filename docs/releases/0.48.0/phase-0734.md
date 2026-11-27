### The release record leaves `STABILITY.md`: a directory per version under `docs/releases/`, a file per change (Phase 734, `DECISIONS.md` D91) — none, no package surface moves

**What changed.** `STABILITY.md` keeps the contract (the versioning policy and the public-surface baselines) and
points at [`docs/releases/`](../README.md). Every version section it held is a directory there, `0.33.0` to
`0.47.0`: the section's heading and preamble are that directory's `README.md`, and each of its 71 entries is a
file of its own, `phase-<n>.md` (or `change-<slug>.md` for the five no phase of this record made), moved word for
word. A change now records itself by adding one file to the standing version's directory, so two changes in
flight no longer edit one file.

**One heading was restated.** `v0.47.0` was tagged on 2026-11-25 while its heading still read `0.47.0 — DRAFT`; it
reads `0.47.0 — released 2026-11-25 as v0.47.0`, and this change opens `0.48.0` rather than riding a released
number.

**What the roster family holds.** The `Package roster` checks read the new layout and refuse what they refused
before: a standing `<Version>` with no slot, a release tag from `0.33.0` up with no slot, a draft preamble that
denies a tag that exists. Three refusals are new: a slot headed DRAFT whose version is tagged, or that is not
the standing `<Version>`; a slot headed released whose tag does not exist; and a file in a slot that is not an
entry, or a release heading written back into `STABILITY.md`.

**For a consumer, nothing.** No source, signature or wire byte moves, and every baseline under `api/` is
unchanged. `conformance/laws/transform-laws.json` re-reads its derived `kitVersion` stamp as `0.48.0`; no vector
moves.
