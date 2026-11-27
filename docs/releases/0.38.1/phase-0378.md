### The pipeline-query registry's lifecycle verbs (Phase 378, `DECISIONS.md` D15) — additive, `surface`

**What changed.** Four new functions in `Fuaran.Compute.PipelineQueryRegistry`, each the substrate's
`QueryRegistry` verb of the same name over the declarations, the bodies following only on success:

- **`unregister`** — `id -> PipelineQueryRegistry -> Result<PipelineQueryRegistry, PipelineQueryError>`;
  an unregistered id is `QueryRefused(NoSuchQuery …)`.
- **`replace`** — `PipelineQuery -> PipelineQueryRegistry -> Result<…>`; `QueryRefused` from the
  substrate, then the new pair held to `PipelineQuery.check` as `register` holds it. A refusal leaves
  the registry unchanged.
- **`restrict`** — `Set<string> -> PipelineQueryRegistry -> PipelineQueryRegistry`; never widens.
- **`union`** — `PipelineQueryRegistry -> PipelineQueryRegistry -> Result<…>`; a shared id is
  `QueryRefused(DuplicateQuery …)`. Associative.

**To adopt.** Optional; the registry was add-only before. Needs `Fuaran.Core` `0.35.1` or later, the
release that carries the substrate's `QueryRegistry` verbs; the pin has since moved to `0.36.0`.
