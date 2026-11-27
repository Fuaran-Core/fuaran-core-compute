### Opt-in `ColExpr` authoring helpers ship as `Fuaran.Compute.Syntax` (Phase 626, `DECISIONS.md` D67) — additive

**What changed.** `Fuaran.Compute.DataFrame` gains one module, `Fuaran.Compute.Syntax` (35 additions to
`api/Fuaran.Compute.DataFrame.txt`, nothing removed or changed): `col`, `param`, `lit`, a literal per `Cell` case
(`litInt`, `litFloat`, `litBool`, `litStr`, `litDate`, `litTimestamp`, `litDecimal`, `litNull`), `now`; the operators
`.+ .- .* ./ .% .= .<> .< .<= .> .>=`; `and'`, `or'`, `contains`, `startsWith`, `endsWith`; `not'`, `isNull'`,
`coalesce`, `inList`, `inParam` and `cast`. Each builds exactly the node its constructor builds.

**Why.** Hand-written expressions were the verbose part of an F# pipeline, and suites had started to mint their own
shorthands one file at a time. One shared spelling replaces them.

**What a consumer must do.** Nothing: the module is opt-in by `open Fuaran.Compute.Syntax`, and no node, wire shape,
evaluator path or planner path moves. A consumer that opens it and defines its own `col`, `param` or `lit` keeps its own
binding wherever it is declared after the `open`. No FSharp.Core name is shadowed (`not'` and `isNull'` carry a prime).
