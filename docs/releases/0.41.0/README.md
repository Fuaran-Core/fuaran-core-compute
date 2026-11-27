## 0.41.0 — released 2026-10-22 as `v0.41.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-10-22, in the receiving host:
Fuaran.UI's .NET repository (`fuaran-dotnet` at `38e738e3`, its branch carrying the raise onto `Fuaran.Core.*`
`0.40.2`) ran its Core Fable gate as `pwsh -NoProfile -File tests/core-fable/core-fable.ps1 -CoreVersion 0.40.2
-CoreFeed <folder> -ComputeVersion 0.41.0 -ComputeFeed <candidate folder>`, with the four packages at `0.41.0`
packed from `f56a3cf`, this commit's parent (`Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`,
`Fuaran.Compute.PipelineQuery` and `Fuaran.Compute.Conformance`), and the Core folder holding the 21 `Fuaran.Core.*`
packages at `0.40.2` packed from Core's release candidate (`435c638`, the commit `v0.40.2` was cut over). Both producers
were passed because this version pins Core `0.40.2`, which the host's `main` does not yet pin. Both legs were green: the
compile leg transpiled 23 packages under Fable 5 with the compute packages at `0.41.0`, and the parity leg read 451 of
451 vectors byte-identical on .NET and under node. The law vectors re-emitted from `f56a3cf` (`dotnet run --project
tests/Fuaran.Core.Compute.Tests -- --emit-laws`) are byte-identical to the committed `conformance/laws/` files, whose
`kitVersion` stamp already read `0.41.0`; the corpus copy follows the release (`DECISIONS.md` D6 (4)). The `ci` workflow
ran green on `f56a3cf` in Debug and in Release, with its proof leg (run 39774020330; the Debug leg's first attempt
failed one wall-clock budget in the `Clock` family and passed on re-run, with Release green on the same commit). This
commit moves the release heading, this record and the comment above `<Version>` only.

**Receiving gate run:** `fuaran-dotnet` `38e738e3`, `-CoreVersion 0.40.2 -ComputeVersion 0.41.0`, compile leg green,
parity 451/451.

This version moves the substrate to Core `0.40.2` (from `0.40.1`). `0.40.0` is tagged, so the first change below
(Phase 468) ADVANCED the slot to `0.41.0`.
