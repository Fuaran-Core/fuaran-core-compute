### `Pipeline.hash` mints `compute-pipeline-sha256-v1`, and the suite certifies against the pipeline wire specification (Phase 604, `DECISIONS.md` D83) — additive

The draft already carries breaking moves, so this change RIDES `0.45.0`.

**What changed.** `Fuaran.Compute.DataFrame` gains the module `Pipeline`: `Pipeline.hashAlgorithm` (the literal
`"compute-pipeline-sha256-v1"`), `Pipeline.canonicalText : Transform list -> Result<string, string>` and
`Pipeline.hash : Transform list -> Result<string, string>`, the content address `sha256:<64 lowercase hex>` over the
canonical text's UTF-8 bytes, or the refusal of a pipeline that has no canonical text (a non-finite float, an
ill-formed string, a repeated member). The package's baseline, `api/Fuaran.Compute.DataFrame.txt`, gains those four
lines and loses nothing. The rule is stated normatively in the
[pipeline wire specification](https://github.com/Fuaran-Core/fuaran-pipeline-specification) §7.

**What a reader refuses that it read before.** A `lit` cell (or a function parameter's default) of `$type` `Date` or
`Timestamp` whose `value` is not the canonical text (`TemporalText`) is refused with the `TypeMismatch` a mistyped
literal gets. A column's cells were already held to that text; no emitter writes another, and no committed document
moved.

**The wire.** No emitted byte moves: every file under `api/wire/` is unchanged, and `Pipeline.hash` of any pipeline is
`sha256:` and `IncrementalCodec.pipelineHash` of it, so a saved incremental state's key is unchanged.

**What a consumer must do.** Nothing to compile. A consumer that identifies a pipeline by a hash it computed itself
reads `Pipeline.hash` instead and carries `Pipeline.hashAlgorithm` beside the value. A machine that runs this
repository's suite needs the specification's checkout as `Fuaran-Pipeline-Specification` beside its main working tree,
or named by `FUARAN_PIPELINE_SPEC`: without it the suite fails.
