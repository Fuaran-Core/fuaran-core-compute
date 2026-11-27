## 0.42.0 — released 2026-10-24 as `v0.42.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-10-24, in the receiving host:
Fuaran.UI's .NET repository (`fuaran-dotnet` at `a734a21c`, its `main`, which pins `Fuaran.Core.*` `0.40.2`) ran its
Core Fable gate as `pwsh -NoProfile -File tests/core-fable/core-fable.ps1 -ComputeVersion 0.42.0 -ComputeFeed <candidate
folder>`, with the four packages at `0.42.0` packed from `e9132d8`, this commit's parent (`Fuaran.Compute.DataFrame`,
`Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery` and `Fuaran.Compute.Conformance`). Only the compute producer
was passed, because this version pins Core `0.40.2`, which the host's `main` already pins. Both legs were green: the
compile leg transpiled 23 packages under Fable 5 with the compute packages at `0.42.0`, and the parity leg read 451 of
451 vectors byte-identical on .NET and under node. The law vectors re-emitted from `e9132d8` (`dotnet run --project
tests/Fuaran.Core.Compute.Tests -- --emit-laws`) are byte-identical to the committed `conformance/laws/` files.

`0.41.0` is tagged, so the first change below (Phase 572) ADVANCED the slot to `0.42.0`.
