namespace Fuaran.Compute

open Fuaran.Core

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

    /// The refusable-family audit rows for the families above — Phase 220's vocabulary, moved
    /// verbatim except `aggregateParityLaws`, whose null-skip law stayed in the kit.
    let refusalAudit: RefusalAudit list =
        let r family population why =
            { Family = family
              Population = population
              Why = why }

        [ // Phase 281 — every refusal registration has is built each iteration.
          r
              "PipelineQueryConformance.laws"
              Built
              "every agreement, parameter, registry and dispatch refusal is built each iteration over the drawn table"
          r
              "Conformance.transformLaws"
              Drawn
              "the Error/Error parity arm is reached only when the caller's generator yields an evaluation error"
          r
              "Conformance.aggregateParityLaws"
              NoRefusal
              "an Error only lands in a parity bucket, and the kit draws no type or magnitude that can raise one (its decimals are hundredths far inside the float range)"
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
          r
              "Conformance.plannerLaws"
              Drawn
              "the Error/Error parity arm is reached when a drawn pipeline refuses over a drawn table (a cast that parses over strings that do not); guarded on the refused population"
          r "IncrementalDelta.laws" Drawn "delegates to lawsWith"
          r
              "IncrementalDelta.lawsWith"
              Drawn
              "declined pipelines are picked from a fixed menu by the kit's roll; guarded on refresh class" ]

    /// The adequacy-census rows for the families above — `SampleAdequacy.census`'s vocabulary,
    /// moved verbatim except `aggregateParityLaws`, which now compares parity alone.
    let census: (string * AdequacyClass) list =
        [
          // Phase 281 — unconditional: the reference pair and one variant per refusal are built every
          // iteration; the draw varies the table and the arguments, never which branch is taken.
          "PipelineQueryConformance.laws",
          Unconditional
              "each iteration builds the reference pair, one variant per refusal, and dispatches it settled, pending, refused and failed"

          // ---- guarded: a law branches on something the sample can miss ----
          // Phase 212 — the third dimension is the shape that let a wrong answer reach a published
          // release: a ROW-LOCAL step reading a column a cross-row step appended, per producer
          // class. The corpus carried ten window-bearing pipelines and not one of them, so the
          // family that exists to see that defect certified green against an evaluator carrying it.
          // Phase 321 — and the decimal column read by a maintained step.
          "IncrementalDelta.lawsWith",
          Guarded [ "refresh class"; "cross-row column read"; "decimal column"; "source rows" ]
          "IncrementalDelta.laws",
          Guarded
              [ "refresh class"
                "cross-row column read"
                "decimal column (delegates to lawsWith)"
                "source rows (delegates to lawsWith)" ]
          // Phase 181. Every other arm is BUILT each iteration — an op applied, inverted, chained
          // and replayed — but the inverse-only-for-applicable law is about the ops the table
          // REFUSES, and whether the generator refused an INVERTIBLE one is a property of the run.
          "Conformance.columnarOpLawsWith", Guarded [ "invert's refusal population" ]
          // Phase 321 — the kit's own roll writes decimals into its decimal column, guarded on them.
          "Conformance.columnarOpLaws",
          Guarded
              [ "invert's refusal population (delegates to columnarOpLawsWith)"
                "decimal cell" ]
          "Conformance.schemaWalkLaws", Guarded [ "derivation verdict (its own parity vacuity guard)"; "decimal step" ]
          // `evalFrom` answers every change but a value edit by evaluating in full, so only a value
          // edit can tell the incremental path from the full one.
          "Conformance.incrementalLawsWith", Guarded [ "value edit" ]
          // The Error/Error arm of the parity law is reached only when the caller's generator yields
          // a pipeline the reference refuses.
          "Conformance.transformLaws", Guarded [ "accepted"; "refused" ]
          // Phase 269 — a sample the planner leaves as written certifies the parity of nothing:
          // each rewrite class must be reached, a reorder must be declined, and the Error/Error
          // arm must be drawn.
          // Phase 277 — and a decimal sample: a pipeline over the decimal column with a present decimal.
          "Conformance.plannerLaws",
          Guarded
              [ "fusion"
                "pruning"
                "reorder"
                "declined reorder"
                "refused pipeline"
                "decimal sample" ]

          // ---- unconditional: every iteration builds the evidence for every branch ----
          // Phase 321 — guarded since the column type became a three-way draw with the decimal.
          "Conformance.aggregateParityLaws", Guarded [ "decimal column" ]
          "Conformance.incrementalLaws",
          Unconditional "each iteration compares evalFrom against a full evalPipeline over the same change"
          // Phase 321 — guarded on the decimal params it binds (one draw in three).
          "Conformance.paramLaws", Guarded [ "decimal param" ]
          "Conformance.slotParamLaws",
          Unconditional
              "each iteration BUILDS the bound, substituted, unbound, mistyped and literal-only runs over the same drawn table — the draw varies the table, the page size and which column is ordered on, never which branch is taken"
          "Conformance.nowLaws",
          Unconditional
              "each iteration BUILDS both grains, a clock-bearing pipeline and a clock-free one over the same input, and runs the constant-witness, counting-witness and unpinned cases — the draw varies the reading and the row count, never which branch is taken" ]

    // Core `0.33.0` (its Phase 297) carries each family's adequacy class and refusal verdict ON the
    // `LawFamily` record. This package declares them once, in the two lists above, keyed by family
    // id, and `families` reads its row's pair from them as it is built, so the record is complete
    // and each fact still has one declaration. A family with no row in either list fails at load,
    // naming it, rather than shipping a record that cannot say how its run is read.
    let private adequacyOf (id: string) : AdequacyClass =
        match census |> List.tryFind (fun (k, _) -> k = id) with
        | Some(_, cls) -> cls
        | None -> failwithf "DataFrameFamilies: %s has no adequacy-census row" id

    let private refusalOf (id: string) : RefusalVerdict =
        match refusalAudit |> List.tryFind (fun a -> a.Family = id) with
        | Some a ->
            { Population = a.Population
              Why = a.Why }
        | None -> failwithf "DataFrameFamilies: %s has no refusal-audit row" id

    /// The families this package ships, in declaration order. Renderings sort by `Id`.
    let families: LawFamily list =
        let f m entry witness reason discharges =
            let id = m + "." + entry

            { Id = id
              Module = m
              Entry = entry
              Witness = witness
              OptIn = Option.isSome reason
              Reason = reason
              Discharges = discharges
              Adequacy = adequacyOf id
              Refusal = refusalOf id }

        let c entry witness reason discharges =
            f "Conformance" entry witness reason discharges

        let none: string list = []

        [ // Phase 281 — the registered pipeline query, in its own module.
          f "PipelineQueryConformance" "laws" none (Some SeamNotEveryDomainHas) []
          c "transformLaws" none (Some SeamNotEveryDomainHas) []
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
          // Phase 269 — the planner held to the reference as written.
          c "plannerLaws" none (Some SeamNotEveryDomainHas) []

          f "IncrementalDelta" "laws" none (Some SeamNotEveryDomainHas) []
          f "IncrementalDelta" "lawsWith" none (Some SeamNotEveryDomainHas) [] ]

    /// This package's share, for a reader composing it with the kit's `Families.roster`.
    let roster: Roster =
        { Package = "Fuaran.Compute.Conformance"
          Families = families
          RefusalAudit = refusalAudit
          Census = census }
