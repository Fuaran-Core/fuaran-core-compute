### A pipeline carries its own named functions and interpolated curves (Phase 596, `DECISIONS.md` D48) — BREAKING, `union-widening`

**What changed.** `Transform` gains `Define of FnDef` — a named function the pipeline carries, `FnDef = { Name;
Params: FnParam list; Body }` with `FnParam = { Name; Default: Slot<Cell> option }` (new public types) — and
`ColExpr` gains `Arg` (a body's read of its parameter), `Call` (a call of a defined function) and `Interpolate`
(the operand read through `(x, y)` knots under a new `InterpKind` — `Linear | Step | MonotoneCubic` — and a new
`InterpOutside` — `Clamp | Null`). `FnDef.expand` registers a pipeline's functions and expands every call inline;
every evaluating entry point, the planner, the incremental seam and the schema walk read the expansion, so a
call's type, unit, exactness and incrementality are its body's. `EvalError` gains `FunctionError`,
`RecursiveFunction`, `ExpansionTooLarge` (the bound is §21.8's 512 nodes per expression) and `InvalidKnots`; a
wrong argument count is the existing `ArityError`. Wire: `{"$type":"define","name":…,"params":[{"name":…,
"default":…?}],"body":…}`, `{"$type":"arg","name":…}`, `{"$type":"invoke","name":…,"args":[…]}` (not `call`,
which has been a lenient spelling of `apply` since Phase 93) and `{"$type":"interpolate","expr":…,"knots":[[x,y],…],
"kind":"linear"|"step"|"monotoneCubic","outside":"clamp"|"null"}`. `Interpolate` is exact at every knot and
byte-identical on every host; the monotone cubic's slope rule is D48 item 7.

**The class.** Three published unions gain cases — `ColExpr`, `Transform` and `EvalError` — so an exhaustive
`match` over any of them stops compiling (`union-widening`); the surface guard classes the move from the
regenerated `api/Fuaran.Compute.DataFrame.txt`. The wire baselines of `Fuaran.Compute.DataFrame`,
`Fuaran.Compute.ColumnOps` and `Fuaran.Compute.PipelineQuery` gain one document per new case (no existing
document's bytes moved). **It RIDES `0.43.0`.** The slot is untagged and publicly unpinned, and it already
stands one minor past the newest tag (`v0.42.0`); before 1.0 that minor is the breaking signal, so a second one
(`0.44.0`) would tell a consumer nothing the first does not, and would leave a number never released.

**The law vectors.** Thirteen `evalPipeline` vectors join `conformance/laws/transform-laws.json` after the union
shapes, as iterations 104 to 116 (`iterations` reads `117`; every earlier vector is byte-identical): a call beside
its body spelled out and a column comparing the two; nested calls and defaults; the four registration refusals,
each naming its class (`RecursiveFunction`, `FunctionError`, `ArityError`, `ExpansionTooLarge`); every curve
kind under both outside policies over the float column and over fixed probes below, between, on and above the
knots; the curve at each knot; an int operand; and the two curve refusals (`InvalidKnots`, `TypeError`). Every
vector is pinned bytes, compared byte for byte. The corpus copy follows the release rule (D6 (4)). A language
twin that does not yet expand calls or read curves reports these vectors as unsupported rather than skipping
them.

**What a consumer does.** Add the new arms to any exhaustive `match` over `ColExpr`, `Transform` or
`EvalError`. A consumer that hands pipelines to this evaluator and matches none of the three changes nothing.
