# Fuaran.Core.Compute — API stability

**Status:** pre-1.0. The version is single-sourced from `<Version>` in `Directory.Build.props`;
this document is the contract every version is held to, and what each version changed, by class, is
recorded under [`docs/releases/`](docs/releases/README.md).

## Versioning policy

Every package this repository produces ships at the one `<Version>`. A change to a package's
public contract ships on a version AHEAD of every version that has been tagged. The standing
`<Version>` is a **draft slot** while it is untagged: an additive change rides it (its entry is one
more file in the slot's directory, the number does not move), and a change of a higher class than the draft already carries
advances it, because the number is what tells a consumer what adopting it costs. The class of a
move is not an argument — the `Public surface` family computes it from the committed baselines under
[`api/`](api/) (below).

A release is a `v*` tag, which runs the publish workflow (see the README's release section). Every
version this repository has released has a slot under [`docs/releases/`](docs/releases/README.md)
headed by it; the `Package roster` family holds the record to the repository's release tags, and
while every slot there is headed DRAFT it reads a clone with no release tag as "nothing released
yet" rather than as a clone missing its tags.

**The history before this repository.** The four packages shipped from the
[Fuaran.Core](https://github.com/Fuaran-Core/fuaran-core) repository at every version from `0.9.0`
(the first release that carried `Fuaran.Core.DataFrame`) through `0.32.0`, in step with the rest of
that repository's packages; `Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp`
first shipped there at `0.32.0`, when Phase 257 cut them beside the other two. **Every one of those
versions is recorded in [that repository's release ledger](https://github.com/Fuaran-Core/fuaran-core/blob/main/docs/releases/README.md)**,
and this repository does not restate them: two copies of one record is how a record comes to
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

## The release record

What each version changed, by class, is [`docs/releases/`](docs/releases/README.md): a directory per version and a
file per change, so a change adds a file and edits none another change's entry lives in (`DECISIONS.md` D91).
