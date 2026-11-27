## 0.39.0 — released 2026-10-14 as `v0.39.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-10-14, in the
receiving host: Fuaran.UI's .NET repository (`fuaran-dotnet` at `96e2981`, pinning `Fuaran.Core.*`
`0.36.0` and the compute packages at `0.38.1`) ran its Core Fable gate as `pwsh -NoProfile -File
tests/core-fable/core-fable.ps1 -CoreVersion 0.37.0 -CoreFeed <folder> -ComputeVersion 0.39.0
-ComputeFeed <candidate folder>`, with the four packages at `0.39.0` packed from this commit's tree
(`Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery` and
`Fuaran.Compute.Conformance`) and the Core folder holding every `Fuaran.Core.*` package the public
registry serves at `0.37.0` (19 packages). Both producers were passed because this version pins Core
`0.37.0`: against the host's own `0.36.0` pin the compute packages do not compile, which is the substrate
raise below and not a defect. Both legs were green: the compile leg (21 referenced packages, 2 excluded,
23 on the candidate surface) transpiled under Fable 5 with the compute packages at `0.39.0`, and the
parity leg read 445 of 445 vectors byte-identical on .NET and under node. The law vectors re-emitted from
this commit (`dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws`) are byte-identical to
the committed `conformance/laws/transform-laws.json`, whose `kitVersion` stamp already read `0.39.0`; the
corpus copy follows the release (`DECISIONS.md` D6 (4)). The gate (`verify.ps1`) ran green on this commit
in Debug and in Release under its release tag, its clock leg included; this commit moves the release
heading, this record, a README row and the comment above `<Version>` only.

**Receiving gate run:** `fuaran-dotnet` `96e2981`, `-CoreVersion 0.37.0 -ComputeVersion 0.39.0`, compile
leg green, parity 445/445.

**The substrate is Core `0.37.0`.** Where an entry below names Core `1.0.0`, it names the Core draft that
was released as `0.37.0` (Fuaran.Core's Phase 444); this version pins `FuaranCoreVersion` `0.37.0`, raised
from `0.36.0`, and a consumer raises `Fuaran.Core.*` to `0.37.0` with `Fuaran.Compute.*`.

This version carries the changes entered below over `0.38.1`. One is a `retype`: every public signature
that carried a schema carries Core's `Field list`, and the frame is a view over Core's typed column
(Phase 423). Five are BREAKING in behaviour: a join never names two columns alike (Phase 411); a sort or
window order key that names no column is refused (Phase 412); a derive its params decide is typed by them,
and `Round`, `Floor` and `Ceil` over an undecided operand are no longer typed `float` (Phase 413); a
partition key that names no column is refused (Phase 441); and a window output that names an existing
column is refused (Phase 450). One is additive (`DataFrame.prepareRows`, Phase 415, beside Phase 411's
`DataFrame.joinRightNames`), one documents the bare `Rank` as deprecated (Phase 414), and one is a
performance change that moves no answer (Phase 448). Phases 439 and 449 changed the suite's clock leg
only, so they carry no entry.

`0.38.1` is tagged, so the change below ADVANCED the slot to `0.39.0`. It was a draft until it was
tagged: an additive change rode it, a breaking one would have advanced it.
