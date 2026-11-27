## 0.43.0 — released 2026-11-05 as `v0.43.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-11-05 in the receiving host,
Fuaran.UI's .NET repository (`fuaran-dotnet` at `a6fc759`, its `main`, which pins `Fuaran.Core.*` `0.40.2`). The command
was `pwsh -NoProfile -File tests/core-fable/core-fable.ps1 -ComputeVersion 0.43.0 -ComputeFeed <candidate folder>`. The
five packages were packed at `0.43.0` from `9e338e0`, this commit's parent: `Fuaran.Compute.LinearAlgebra`, which is new
in this version, plus `Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery` and
`Fuaran.Compute.Conformance`. The host's gate first had to learn the new package (`a6fc759`): before that, it routed
only the four older ids to the candidate folder. Only the compute producer was passed, because this version pins Core
`0.40.2`, which the host's `main` already pins. Both legs were green. The compile leg transpiled 24 packages under
Fable 5, with the compute packages at `0.43.0`. The parity leg read 451 of 451 vectors byte-identical on .NET and under
node. The law vectors re-emitted from `9e338e0` (`dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws`)
are byte-identical to the committed `conformance/laws/` files, `linear-algebra-laws.json` included.

`0.42.0` is tagged, so the first change below (Phase 584) ADVANCED the slot to `0.43.0`.
