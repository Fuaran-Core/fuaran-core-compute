## 0.40.0 — released 2026-10-15 as `v0.40.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-10-15, in the
receiving host: Fuaran.UI's .NET repository (`fuaran-dotnet` at `853f110`, pinning `Fuaran.Core.*`
`0.36.0` and the compute packages at `0.38.1`) ran its Core Fable gate as `pwsh -NoProfile -File
tests/core-fable/core-fable.ps1 -CoreVersion 0.37.0 -CoreFeed <folder> -ComputeVersion 0.40.0
-ComputeFeed <candidate folder>`, with the four packages at `0.40.0` packed from `1cd5331`, this
commit's parent (`Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery`
and `Fuaran.Compute.Conformance`), and the Core folder holding every `Fuaran.Core.*` package the
public registry serves at `0.37.0` (19 packages). Both producers were passed because this version pins
Core `0.37.0`, which the host does not yet pin. Both legs were green: the compile leg (21 referenced
packages, 2 excluded, 23 on the candidate surface) transpiled under Fable 5 with the compute packages at
`0.40.0`, and the parity leg read 445 of 445 vectors byte-identical on .NET and under node. The law
vectors re-emitted from `1cd5331` (`dotnet run --project tests/Fuaran.Core.Compute.Tests --
--emit-laws`) are byte-identical to the committed `conformance/laws/transform-laws.json`, whose
`kitVersion` stamp already read `0.40.0`; the corpus copy follows the release (`DECISIONS.md` D6 (4)).
The `ci` workflow ran green on `1cd5331` in Debug and in Release, its clock leg included (run
38742825952); this commit moves the release heading, this record, a README row and the comment above
`<Version>` only.

**Receiving gate run:** `fuaran-dotnet` `853f110`, `-CoreVersion 0.37.0 -ComputeVersion 0.40.0`, compile
leg green, parity 445/445.

This version carries three changes over `0.39.0`. One is BREAKING: the evaluator propagates and
checks units of measure (Phase 429, `DECISIONS.md` D29). The other two move no surface and no answer: a
decimal column enters the frame as a view (Phase 440), and a refresh stops walking an embedded relation
to learn that its pipeline did not move (Phase 459). The substrate is unchanged: Core `0.37.0`.
