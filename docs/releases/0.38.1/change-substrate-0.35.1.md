### The substrate pin moves to `Fuaran.Core.*` `0.35.1` — a dependency raise; no public surface moves

**What changed.** Every `Fuaran.Core.*` package this repository pins moves from `0.34.0` to the released
`0.35.1` (`FuaranCoreVersion` in `Directory.Packages.props`), so the four packages at `0.38.0` carry a
`0.35.1` floor on the substrate. The substrate's slot is `breaking (source)` (its Phase 307:
`InvokeError`, `ApplyError` and `QueryError` widened, new refusals, a stricter reader); no exhaustive
match here met a widened union, so no source moved for it. One behavioural break reached this repository:
`Query.validateParams` now refuses a name bound twice (`DuplicateParam`), and a list-read parameter (the
`ColExpr.InParam` membership test) is bound by naming it once per element. `PipelineQueryRegistry.dispatch`
keeps that binding: the substrate's gate is handed the first binding of each list-read name, and every
further binding is held to the same gate in the first one's place (its declared type) before the resolver
runs. A scalar-read name bound twice is now refused `DuplicateParam` where it was resolved to its last
binding. The substrate's Phase 353 half (`Column.aggregate` by pattern) arrives with the raise; no test
here asserts a figure it moves. No `api/` baseline and no wire-surface record moved.

**Migrating.** A consumer raises `Fuaran.Core.*` to `0.35.1` with this version, and takes the
substrate's own `0.35.1` source breaks (its `STABILITY.md`) for any substrate type it uses directly. A
caller that bound a scalar pipeline-query parameter twice binds it once.
