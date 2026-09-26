namespace Fuaran.Core

// Phase 257 — this package's share of the law-family roster.
//
// `Families` in the kit enumerates the families the kit ships, with their refusal audit and
// `SampleAdequacy.census` beside it. The families that read the dataframe layer moved here, and the
// kit cannot name them without referencing this package, which is the upward reference the
// boundary test refuses. So their rows moved with them: the same `LawFamily`, `RefusalAudit` and
// census records, keyed exactly as before, in a `Families.Roster` a reader composes with the kit's
// own (`Families.toMarkdownOf [ Families.roster; DataFrameFamilies.roster ]`). The kit's suite holds
// the composition to reflection over BOTH assemblies by return type, so a family added here without
// a row fails to ship, as one added to the kit does.
//
// The keys are the spellings a consumer calls today: `Conformance.<family>` through the forwarding
// module in `Forwards.fs`, and `IncrementalDelta.<entry>`, which moved under its own name. They are
// re-keyed to `DataFrameConformance.<family>` when the forwards are removed in Phase 258.

/// This package's share of the kit's law-family roster (Phase 257).
module DataFrameFamilies =

    open Families

    /// The families this package ships, in declaration order. Renderings sort by `Id`.
    let families: LawFamily list =
        let f m entry witness reason discharges =
            { Id = m + "." + entry
              Module = m
              Entry = entry
              Witness = witness
              OptIn = Option.isSome reason
              Reason = reason
              Discharges = discharges }

        let c entry witness reason discharges =
            f "Conformance" entry witness reason discharges

        let none: string list = []

        [ c "transformLaws" none (Some SeamNotEveryDomainHas) []
          // Phase 257 — the `GroupBy` half. The `Column.aggregate` null-skip half stays in the kit
          // as `Conformance.aggregateNullSkipLaws`.
          c "aggregateParityLaws" none (Some SeamNotEveryDomainHas) []
          c "columnarOpLaws" none (Some SeamNotEveryDomainHas) []
          // Phase 246 — the columnar pair at a domain's `StreamGen<ColumnOp, Table>`. `StreamGen` is
          // a base-run witness, so the reason stays `SeamNotEveryDomainHas`.
          c "columnarOpLawsWith" [ "StreamGen" ] (Some SeamNotEveryDomainHas) []
          c "incrementalLaws" none (Some SeamNotEveryDomainHas) []
          c "incrementalLawsWith" [ "StreamGen" ] (Some SeamNotEveryDomainHas) []
          c "paramLaws" none (Some SeamNotEveryDomainHas) []
          c "schemaWalkLaws" none (Some SeamNotEveryDomainHas) []
          c "nowLaws" none (Some SeamNotEveryDomainHas) []
          c "slotParamLaws" none (Some SeamNotEveryDomainHas) []

          f "IncrementalDelta" "laws" none (Some SeamNotEveryDomainHas) []
          f "IncrementalDelta" "lawsWith" none (Some SeamNotEveryDomainHas) [] ]

    /// The refusable-family audit rows for the families above — Phase 220's vocabulary, moved
    /// verbatim except `aggregateParityLaws`, whose null-skip law stayed in the kit.
    let refusalAudit: RefusalAudit list =
        let r family population why =
            { Family = family
              Population = population
              Why = why }

        [ r
              "Conformance.transformLaws"
              Drawn
              "the Error/Error parity arm is reached only when the caller's generator yields an evaluation error"
          r
              "Conformance.aggregateParityLaws"
              NoRefusal
              "an Error only lands in a parity bucket, and the kit draws no type that can raise one"
          r "Conformance.columnarOpLaws" Drawn "delegates to columnarOpLawsWith"
          r
              "Conformance.columnarOpLawsWith"
              Drawn
              "the refused ops are drawn by the caller's StreamGen (columnarOpLaws: the kit's own roll); guarded on invert's refusal population"
          r "Conformance.incrementalLaws" NoRefusal "an Error is only skipped"
          r
              "Conformance.incrementalLawsWith"
              NoRefusal
              "an op the table refuses and a pipeline that does not evaluate are only skipped"
          r "Conformance.paramLaws" Built "one paramsOf member is dropped each iteration and must refuse UnboundParam"
          r "Conformance.schemaWalkLaws" NoRefusal "an evaluator rejection is skipped"
          r "Conformance.nowLaws" Built "the unpinned clock must refuse UnpinnedClock, built each iteration"
          r "Conformance.slotParamLaws" Built "the unbound and mistyped slots are built each iteration"
          r "IncrementalDelta.laws" Drawn "delegates to lawsWith"
          r
              "IncrementalDelta.lawsWith"
              Drawn
              "declined pipelines are picked from a fixed menu by the kit's roll; guarded on refresh class" ]

    /// The adequacy-census rows for the families above — `SampleAdequacy.census`'s vocabulary,
    /// moved verbatim except `aggregateParityLaws`, which now compares parity alone.
    let census: (string * AdequacyClass) list =
        [
          // ---- guarded: a law branches on something the sample can miss ----
          // Phase 212 — the third dimension is the shape that let a wrong answer reach a published
          // release: a ROW-LOCAL step reading a column a cross-row step appended, per producer
          // class. The corpus carried ten window-bearing pipelines and not one of them, so the
          // family that exists to see that defect certified green against an evaluator carrying it.
          "IncrementalDelta.lawsWith", Guarded [ "refresh class"; "cross-row column read"; "source rows" ]
          "IncrementalDelta.laws",
          Guarded
              [ "refresh class"
                "cross-row column read"
                "source rows (delegates to lawsWith)" ]
          // Phase 181. Every other arm is BUILT each iteration — an op applied, inverted, chained
          // and replayed — but the inverse-only-for-applicable law is about the ops the table
          // REFUSES, and whether the generator refused an INVERTIBLE one is a property of the run.
          "Conformance.columnarOpLawsWith", Guarded [ "invert's refusal population" ]
          "Conformance.columnarOpLaws", Guarded [ "invert's refusal population (delegates to columnarOpLawsWith)" ]
          "Conformance.schemaWalkLaws", Guarded [ "derivation verdict (its own parity vacuity guard)" ]
          // `evalFrom` answers every change but a value edit by evaluating in full, so only a value
          // edit can tell the incremental path from the full one.
          "Conformance.incrementalLawsWith", Guarded [ "value edit" ]
          // The Error/Error arm of the parity law is reached only when the caller's generator yields
          // a pipeline the reference refuses.
          "Conformance.transformLaws", Guarded [ "accepted"; "refused" ]

          // ---- unconditional: every iteration builds the evidence for every branch ----
          "Conformance.aggregateParityLaws",
          Unconditional "each iteration compares aggregate against a single-group groupBy on the same column"
          "Conformance.incrementalLaws",
          Unconditional "each iteration compares evalFrom against a full evalPipeline over the same change"
          "Conformance.paramLaws",
          Unconditional "each iteration binds a param, leaves one unbound, and round-trips the pipeline"
          "Conformance.slotParamLaws",
          Unconditional
              "each iteration BUILDS the bound, substituted, unbound, mistyped and literal-only runs over the same drawn table — the draw varies the table, the page size and which column is ordered on, never which branch is taken"
          "Conformance.nowLaws",
          Unconditional
              "each iteration BUILDS both grains, a clock-bearing pipeline and a clock-free one over the same input, and runs the constant-witness, counting-witness and unpinned cases — the draw varies the reading and the row count, never which branch is taken" ]

    /// This package's share, for a reader composing it with the kit's `Families.roster`.
    let roster: Roster =
        { Package = "Fuaran.Core.DataFrame.Conformance"
          Families = families
          RefusalAudit = refusalAudit
          Census = census }
