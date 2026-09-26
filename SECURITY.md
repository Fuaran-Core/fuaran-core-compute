# Security Policy

## Supported versions

Fuaran.Core.Compute is pre-1.0. Security fixes are applied to the latest released `0.x` version on
the `main` branch. Older pre-releases are not maintained.

## Reporting a vulnerability

Please report suspected vulnerabilities privately — do **not** open a public issue.

- **Preferred:** GitHub's private vulnerability reporting (the repository's **Security** tab →
  **Report a vulnerability**).
- **Or email:** andrew@fuaran.com — include a description, the affected version, and steps
  to reproduce.

We aim to acknowledge a report within five business days and to agree a disclosure timeline with
you. Please allow a reasonable window to ship a fix before any public disclosure.

## Scope — what is and isn't a vulnerability

The packages here are **libraries of pure functions** with no I/O, no network, no process model and
no ambient authority (FSharp.Core-only, plus the Fuaran.Core substrate packages). Their threat
surface is correspondingly narrow. Two documented, by-design properties are **not** vulnerabilities:

- **The columnar op-stream's default hash chain is not cryptographic.** It inherits the substrate's
  `OpStream` posture: the default hash is FNV-1a — tamper-evident against accidental corruption only.
  For adversarial tamper-evidence, supply a cryptographic hash at the host boundary.
- **Evaluation cost is bounded by the pipeline and the table, not by a budget the library imposes.**
  A pipeline over a very large table, or an expression of very many nodes, costs what it costs; a
  host that evaluates untrusted pipelines bounds them itself.

Genuine issues we want to hear about include: a totality violation (a public function throwing
instead of returning a typed error), a decode path that admits a malformed pipeline or table as
valid, an incremental refresh whose result differs from the reference evaluation, or a law family
that is itself unsound.
