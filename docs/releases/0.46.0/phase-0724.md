### The substrate moves to `Fuaran.Core` 0.45.0 (Phase 724) — breaking

**What changed.** `FuaranCoreVersion` moves from `0.41.0` to `0.45.0`, so every package this repository produces is
built on, and depends on, the `Fuaran.Core` that the UI tier and `Fuaran.MVU` run on. Up to `0.45.0` every package
was built on `0.41.0`, and under Fable `Fuaran.Compute.PipelineQuery` and `Fuaran.Compute.Conformance` did not compile
against Core `0.45.0`: Core `0.43.0` to `0.45.0` made `QueryRegistry` generic over its decision context
(`QueryRegistry<'Ctx>`; its `dispatch` takes a context and a `Grant option`) and put `AdmittedQuery` between a
declaration and the codec (`QueryCodec.encode` takes one, `QueryCodec.decode`, `QueryRegistry.tryFind` and
`QueryRegistry.enumerate` answer one). Only `Fuaran.Compute.PipelineQuery` meets either change; every other package
compiles unchanged.

- `PipelineQueryRegistry.declarations` answers a `QueryRegistry<unit>` (it answered the non-generic `QueryRegistry`).
  It is the one line `api/Fuaran.Compute.PipelineQuery.txt` moves. The pipeline-query registry adds no gate, so it is
  the unit-context registry, and `PipelineQueryRegistry.dispatch` keeps its signature: it dispatches through the
  substrate under the unit context with no grant, which for a registry without gates is exactly the dispatch it ran
  before — every outcome is the one it was.
- `PipelineQueryCodec.encode` and `encodeJson` admit the declaration (`QueryRegistry.admit`) before writing it, as
  the substrate's writer requires, and raise `ArgumentException` naming the refusal for a pair whose declaration the
  gate refuses. This is the one behaviour that moves, and it moves because Core's writer no longer accepts an
  unadmitted declaration. Nothing that read back moves: a pair `PipelineQueryRegistry.register` accepted is always
  admitted, and the substrate's declaration reader refused such a declaration on Core `0.41.0` too, so the text
  written for one never decoded as a pipeline query. `decode` and `decodeJson` answer the admitted declaration's
  `Query`, as before.
- `PipelineQueryConformance.laws` reads the registry's declarations as admitted ones (`.Declaration.Id`); its laws
  and their verdicts do not move.

**The wire surface.** The bytes the encoders write for a pair with an admitted declaration do not move. The pipeline-query root's sample
set does (`api/wire/Fuaran.Compute.PipelineQuery.txt`, class `breaking` since `v0.45.0`), because a sampled
declaration must now be one the gate admits: as on the substrate's own wire surface since Core `0.45.0`, a sampled
declaration's comparison literal and its column are made to agree and an embedded table is restated as one its
schema carries, so 23 documents move, `pipelineQuery / Cell.Null` is no longer written (no admitted declaration
compares with a `Null`; the root names it omitted), and `pipelineQuery / PositiveInt.PositiveInt` is new, the
substrate's declaration now reaching that union.

**The law vectors.** Re-emitted at Core `0.45.0` (`dotnet run --project tests/Fuaran.Core.Compute.Tests --
--emit-laws`): `transform-laws.json` moves only in its `kitVersion` stamp (`0.46.0`), and `dataframe-parity.json` and
every other file under `conformance/laws/` are byte-identical to their `0.45.0` emission. The raised Core moves no
vector.

**What a consumer must do.** Restore `Fuaran.Core` `0.45.0` or later beside these packages. A consumer that reads
`PipelineQueryRegistry.declarations` names its type `QueryRegistry<unit>` and reads the substrate's enumeration as
`AdmittedQuery` values (`.Declaration`); one that encodes a pair it never registered admits its declaration first
(`QueryRegistry.admit`), or registers the pair.
