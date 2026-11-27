### The substrate pin moves to `Fuaran.Core.*` `0.40.1`, and the law vectors are written by the substrate's one corpus writer (Phase 543) — a dependency raise; no public surface moves

**What changed.** Every `Fuaran.Core.*` package this repository pins moves to the released `0.40.1`
(`FuaranCoreVersion` in `Directory.Packages.props`), so the four packages at `0.41.0` carry a `0.40.1` floor on
the substrate. `0.40.0` was built on Core `0.37.0`; the pin passed through `0.38.1`, whose Breaking entries
reached no call site here, and now crosses the substrate's `0.39.0` and `0.40.0` breaking cuts. The moves
that reached source, each taken from the substrate's release ledger's Migration lines:

- `Canon.render`, `tryRender`, `typed` and `canonicalFloat` are `Canonical.write Rendering.CrossHost`,
  `Canonical.tryWrite Rendering.CrossHost`, `Canonical.typed` and `FloatLayout.canonical`; the bytes are the
  same.
- `Json.parseDetailed` is removed. The codecs read wire text through one internal reader, `Json.read
  ReadOptions.strict` with its refusal projected as the substrate's column codec projects it, so every
  `NotJson` refusal, the depth cap's included, keeps its spelling.
- The pipeline-query codec's string readers move from `Decode` onto `Decoder.field`, `str` and `items` with
  `DecodeError.describe`; its refusal sentences (`missing property: <name>`, `expected <kind>, got <kind>`)
  are the ones it wrote.
- This package's roster share is built as the reshaped `Families.LawFamily` (`Takes`, `Election`,
  `Operations`, `Spellings`), and `DataFrameFamilies.roster` drops the `RefusalAudit` and `Census` fields
  `Roster` now projects as members. `DataFrameFamilies.refusalAudit` and `census` stay: they are the
  declarations each row reads.
- Every law result is built through `LawResult`'s constructors, so it carries `Family` (the roster key),
  `Kind` and `Evidence` (the assertions it stands on, counted in the family's run). A law whose arm the
  family's adequacy guard counts is `LawKind.Subject (Some <dimension>)`. No verdict, law text or guard
  moved.
- `OpStream.append` and `verifyChain` take one `Stream` (`OpStream.canonical`). In the suite, `Ops.apply`
  takes a policy and a scope, and `Conformance.propagationEvaluatorLawsWith` is `propagationPriorLawsAt`.
- `Fuaran.Core.Text`, new in the substrate's `0.40.0` and below `Wire` and `OpStream`, is referenced
  transitively, and the `Compute boundary` family admits it beside `Fuaran.Core.Unit`.

No `api/` baseline and no wire baseline moved: the `Public surface` family reads no baseline moved since
`v0.40.0`.

**The law vectors' writer.** `conformance/laws/transform-laws.json` and `conformance/laws/dataframe-parity.json`
are written by the substrate's `CorpusFile.render` (public since Core `0.40.1`), and the suite holds each
committed file to its render through `CorpusFile.firstDifference`, which names the first line that differs.
The test project's own JSON escaper and renderer are deleted. That escaper wrote `\b`, `\f`, `\n`, `\r` and
`\t` in short form where the substrate's writer writes `\u00xx`. No committed string carries one of those
characters, so both files are byte-identical across the raise and the writer change: a fresh `--emit-laws`
matches the committed files' sha256.

**The class.** `none` for this repository's surface: no signature, answer or wire byte moves. It rides the
`0.41.0` draft, which already carries a BREAKING class.

**Migrating.** A consumer raises `Fuaran.Core.*` to `0.40.1` with this version, and takes the substrate's
`0.39.0`, `0.40.0` and `0.40.1` Migration lines for any substrate type it uses directly. A consumer that reads
a compute family's `LawResult` finds `Family`, `Kind` and `Evidence` filled.
