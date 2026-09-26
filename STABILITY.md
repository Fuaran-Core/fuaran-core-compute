# Fuaran.Core.Compute — API stability

**Status:** pre-1.0. The version is single-sourced from `<Version>` in `Directory.Build.props`;
this document records what each version changed, by class.

## Versioning policy

Every package this repository produces ships at the one `<Version>`. A change to a package's
public contract ships on a version AHEAD of every version that has been tagged. The standing
`<Version>` is a **draft slot** while it is untagged: an additive change rides it (its entry grows
here, the number does not move), and a change of a higher class than the draft already carries
advances it, because the number is what tells a consumer what adopting it costs. The class of a
move is not an argument — the `Public surface` family computes it from the committed baselines under
[`api/`](api/) (below).

A release is a `v*` tag, which runs the publish workflow (see the README's release section). An
entry header here names every version this repository has released; the `Package roster` family
holds the document to the repository's release tags, and while every entry header below is a DRAFT
it reads a clone with no release tag as "nothing released yet" rather than as a clone missing its
tags.

**The history before this repository.** The four packages shipped from the
[Fuaran.Core](https://github.com/Fuaran-Core/fuaran-core) repository at every version from `0.9.0`
(the first release that carried `Fuaran.Core.DataFrame`) through `0.32.0`, in step with the rest of
that repository's packages; `Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp`
first shipped there at `0.32.0`, when Phase 257 cut them beside the other two. **Every one of those
versions is recorded in [that repository's `STABILITY.md`](https://github.com/Fuaran-Core/fuaran-core/blob/main/STABILITY.md)**,
and this document does not restate them: two copies of one record is how a record comes to
disagree with itself. This repository's first version is `0.33.0`, the next minor above the last
emission there (`DECISIONS.md` D1), so a consumer's version floor never moves backwards across the
change of producer.

## Public-surface baselines

Every packable package carries a committed baseline of its public contract at
[`api/<package>.txt`](api/), rendered from the built assembly's IL metadata; the wire-bearing
packages also carry a wire-surface record under [`api/wire/`](api/wire/). The `Public surface`
family renders each package afresh on every gate run and diffs it against its baseline, and refuses
a surface that moved while its baseline stood still — additive or breaking, a move whose baseline
moved with it passes. Regenerate with:

```
CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Compute.Tests
```

It rewrites EVERY drifted baseline, not only the one you were looking at: stage the baselines you
meant to move by name.

## 0.33.0 — DRAFT

**This section describes a DRAFT slot.** `<Version>` reads `0.33.0` and no `v0.33.0` tag exists; the
slot is the first this repository emits (Phase 259).

- **The producer changed; the contract did not (no class).** `Fuaran.Core.DataFrame`,
  `Fuaran.Core.Column.Ops`, `Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp`
  are built here from the sources they shipped with at `0.32.0`, under the same package ids and the
  same namespaces, and their public surfaces are the `0.32.0` surfaces unchanged — the committed
  baselines under `api/` were carried with their history and the gate holds each package to its
  own. Adopting `0.33.0` from `0.32.0` is a version raise and nothing else.
- **The substrate is taken by package (no class for a consumer).** The four packages now depend on
  `Fuaran.Core.Column`, `Wire`, `OpStream`, `Conformance` and `CSharp` at `0.32.0` as package
  dependencies, where at `0.32.0` those were built in the same tree at the same number. A consumer
  that pins the substrate packages itself keeps them at `0.32.0` or later.
- **`conformance/laws/transform-laws.json` is emitted here (no class).** The reference answers the
  other hosts certify against are rendered from this repository's evaluator, and the file's
  `kitVersion` stamp reads this repository's `<Version>`. The vectors themselves are byte-identical
  to `0.32.0`'s.
