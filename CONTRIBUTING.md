# Contributing to Fuaran.Core.Compute

Thanks for your interest. This repository produces a typed dataframe for F# and Fable — the
`Transform` / `ColExpr` algebra with its pure reference evaluator, the certified incremental
evaluator, the columnar op algebra, their law families and a C#-shaped facade — over the
[Fuaran.Core](https://github.com/Fuaran-Core/fuaran-core) substrate, which it consumes by package.

## Building and testing

Requirements: the .NET SDK pinned in [`global.json`](global.json).

```powershell
./run.ps1            # restore tools, format, sweep, build, test
./verify.ps1         # format-check + publication sweep + build + tests + facade proof (the green gate)
./verify.ps1 -Proofs # ... plus the F* proof leg (downloads the pinned prover once)
```

A change is ready to propose when `./verify.ps1` is green.

The law-vector legs compare this repository's `conformance/laws/transform-laws.json` with the copy in
the shared wire-format corpus (`fuaran-ui/fuaran-ui-specification`) when a clone of it is found
beside this repository; set `FUARAN_CORE_CORPUS_FRESHNESS=1` to make an absent or stale copy fail
rather than print, and `FUARAN_CORE_CORPUS_DIR` to name a clone kept elsewhere.

## Coding standards

- **F# formatting is Fantomas.** Run `./run.ps1` (or `dotnet fantomas src tests`) before every
  commit; `./verify.ps1` fails on unformatted code.
- **Totality — no exceptions in the public surface.** Failures are typed values (`Result`, a
  rejection, or a named error envelope), never a thrown exception.
- **FSharp.Core only, Fable-clean** for every package: no `System.Text.Json`, no host or native
  dependency. `fable-exclusions.json` records any package deliberately off that surface, and why;
  it is empty.
- **The substrate by package, and only four packages of it.** `Fuaran.Core.Column`, `Wire`,
  `OpStream` and `Conformance`, at the one `FuaranCoreVersion` pin. The `Compute boundary`
  tests refuse anything more; a change that needs more is a design conversation, not a reference.
- **Public-surface moves are classified, not argued.** A change to a package's public contract
  regenerates its baseline under `api/` (`CORE_APPROVE_API=1 dotnet run --project
  tests/Fuaran.Core.Compute.Tests`) in the same commit, and records the change under the standing
  version in [`STABILITY.md`](STABILITY.md).

A change that adds a public surface should add or extend a law family that certifies it.

## Developer Certificate of Origin (DCO)

Contributions are accepted under the [Developer Certificate of Origin](https://developercertificate.org/).
Sign off every commit with `git commit -s`, certifying you wrote the change (or otherwise have the
right to submit it) under the project's Apache-2.0 license:

```
Signed-off-by: Your Name <you@example.com>
```

## Pull requests

Keep PRs focused and describe what changed and why. Make sure `./verify.ps1` passes. By contributing
you agree that your contribution is licensed under the Apache License, Version 2.0.
