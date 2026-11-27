## 0.44.0 — released 2026-11-10 as `v0.44.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-11-10 in the receiving host,
Fuaran.UI's .NET repository (`fuaran-dotnet` at `50ef0bf5`, its `main`, which pins `Fuaran.Core.*` `0.41.0`). The command
was `pwsh -NoProfile -File tests/core-fable/core-fable.ps1 -ComputeVersion 0.44.0 -ComputeFeed <candidate folder>`. The
five packages were packed at `0.44.0` from `f951cd6`, this commit's parent: `Fuaran.Compute.DataFrame`,
`Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery`, `Fuaran.Compute.Conformance` and
`Fuaran.Compute.LinearAlgebra`. Only the compute producer was passed, because this version pins Core `0.41.0`, which the
host's `main` already pins. Both legs were green. The compile leg transpiled 24 packages under Fable 5, with the compute
packages at `0.44.0`. The parity leg read 451 of 451 vectors byte-identical on .NET and under node. The law vectors
re-emitted from `f951cd6` (`dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws`) are byte-identical to
the committed `conformance/laws/` files.

`0.43.0` is tagged, so the first change below (Phase 626) ADVANCED the slot to `0.44.0`.
