### The pinned elementary functions leave `Fuaran.Compute.LinearAlgebra` for a package of their own, `Fuaran.Compute.Elementary` (Phase 630, `DECISIONS.md` D74) — breaking

**What changed.** A sixth package, `Fuaran.Compute.Elementary`, holds the scalar pinned mathematics at the bottom of
the package graph and depends on FSharp.Core alone: the `Elementary` module (`exp`, `ln`, `log10`, `pow`, the tables
`expTable`, `lnTable` and `powTable`, `erfc`, `normalCdf`, `normalDensity`, `normalQuantile`), the `SpecialValue` union
the tables answer, its own exactness table `ElementaryOperations.exactness` over its own `ElementaryOperations.Exactness` union, and `ElementaryLaws.laws` — the four
elementary laws `LinearAlgebraLaws.laws` ran until now. Its namespace is `Fuaran.Compute.Elementary`; the module keeps its
name. `Fuaran.Compute.LinearAlgebra` loses `Elementary`, `SpecialValue` and the eleven `Elementary.*` rows of its
`Operations.exactness`, and references the new package (its `Glm` is the only reader). `api/Fuaran.Compute.Elementary.txt`
is new; `api/Fuaran.Compute.LinearAlgebra.txt` loses 23 lines and gains none. No body moved: every function answers the
bits it answered.

**The law vectors.** The 110 elementary vectors leave `conformance/laws/linear-algebra-laws.json` for a new
`conformance/laws/elementary-laws.json` (family `elementary`), each row byte for byte as it was. The 74 vectors that stay
are byte-identical; the linear-algebra file's description loses its Phase 593 sentence for a pointer to the new file.
`transform-laws.json` moves only in its head — the `kitVersion` stamp (`0.45.0`) and one clause of its description that
named the old namespace and file — and no transform vector moves. `dataframe-parity.json` does not move.

**What a consumer must do.** Reference `Fuaran.Compute.Elementary` and add `open Fuaran.Compute.Elementary`; calls stay
spelled `Elementary.exp`. A consumer that referenced only `Fuaran.Compute.LinearAlgebra` or `Fuaran.Compute.DataFrame`
receives the new package transitively and needs only the `open`. The package's exactness type is
`ElementaryOperations.Exactness` (nested, and in a module named for the package, so the `open` shadows neither a
caller's own `Exactness` nor LinearAlgebra's `Operations`).
A host that certifies against the elementary vectors reads them from `elementary-laws.json`. Known consumer:
`fuaran-dotnet`'s `tests/core-fable/Program.fs` names `Fuaran.Compute.LinearAlgebra`'s `Elementary`, and adapts in the
next cohort raise of `fuaran-dotnet` onto this version, not in this one.
