## 0.38.1 — released 2026-10-08 as `v0.38.1`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-10-08, in the
receiving host: Fuaran.UI's .NET repository (`fuaran-dotnet` at `78f858e`, pinning `Fuaran.Core.*`
`0.36.0`) ran its Core Fable gate as `pwsh -NoProfile -File tests/core-fable/core-fable.ps1
-ComputeVersion 0.38.1 -ComputeFeed <candidate folder>`, with the four packages at `0.38.1` packed from
this commit's tree (`Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery`
and `Fuaran.Compute.Conformance`) and every `Fuaran.Core.*` package at that pin. Both legs were green: the
compile leg (20 referenced packages, 2 excluded, 21 on the candidate surface) transpiled under Fable 5 with
the compute packages at `0.38.1`, and the parity leg read 426 of 426 vectors byte-identical on .NET and
under node. That host's gate was red before this release for the reason this version exists to remove:
`0.37.0` was built on the substrate's `0.34.0` and does not compile against `0.36.0`, whose `Query` gained
`Where` and `OrderBy`. The law vectors re-emitted from this commit differ from the `0.38.0` draft's in the
`kitVersion` stamp alone (`0.38.1`); the corpus copy stays under its declared lag (`DECISIONS.md` D6). The
gate (`verify.ps1`) ran green on this commit in Debug and in Release under its release tag, and CI ran both
configurations and the proof leg green on the code this commit carries unchanged (`df21373`, run
37768767075; this commit moves `<Version>`, the derived stamp and this document only).

**Receiving gate run:** `fuaran-dotnet` `78f858e`, `-ComputeVersion 0.38.1`, compile leg green, parity
426/426.

**Moved number (2026-10-08).** `0.38.0` was packed as a draft with an earlier public surface than the
release commit's (`Fuaran.Compute.DataFrame` and `Fuaran.Compute.PipelineQuery` have moved since), and a
released version names one contract, so the slot advances to `0.38.1` and `0.38.0` is never released.
Every entry below that says it rides or names the `0.38.0` slot ships in `0.38.1`; nothing else changes.

This version carries the changes entered below over `0.37.0`: five additive surfaces (the prepared result,
the incremental state's wire form, concurrent pipelines over one source, the opt-in worker pool and its
asynchronous entry point, and the pipeline-query registry's lifecycle verbs); performance and determinism
changes that move no answer's value; and the substrate pin raised through `0.35.1` to `0.36.0`, so a
consumer raises `Fuaran.Core.*` to `0.36.0` with `Fuaran.Compute.*`.

`0.37.0` is tagged, so the change below ADVANCED the slot to `0.38.0`, which became `0.38.1` at release
(above). It was a draft until it was tagged: an additive change rode it, a breaking one would have
advanced it.
