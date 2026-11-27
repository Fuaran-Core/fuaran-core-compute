### The substrate pin moves to `Fuaran.Core.*` `0.34.0` — a dependency raise; no public surface moves

**What changed.** Every `Fuaran.Core.*` package this repository pins moves from `0.33.0` to the released
`0.34.0` (`FuaranCoreVersion` in `Directory.Packages.props`), so the four packages at `0.37.0` carry a
`0.34.0` floor on the substrate. One source break in the substrate reached this repository:
`QueryCodec.decode` now answers its refusal as a `string` rather than an `ExecutionFailed` (the
substrate's Phase 295), and `PipelineQueryCodec.decodeJson` reads it as one — a refused `query` member
is still reported as `query: <reason>`. No `api/` baseline and no wire-surface record moved.

**Migrating.** A consumer raises `Fuaran.Core.*` to `0.34.0` with this version, and takes the
substrate's own `0.34.0` source breaks (its `STABILITY.md`) for any substrate type it uses directly.
