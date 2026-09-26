namespace Fuaran.Core

// ============================================================================
//  The forwards (Phase 257; marked for removal in Phase 258).
//
//  Until Phase 257 every family in `DataFrameConformance` was spelled
//  `Conformance.<family>`, and consumers call it that way — `open Fuaran.Core`
//  then `Conformance.columnarOpLaws`, or through a module abbreviation of
//  `Fuaran.Core.Conformance`. This module keeps every one of those spellings
//  compiling when both packages are referenced. It is a SECOND module named
//  `Fuaran.Core.Conformance`, in this assembly, not an extension of the kit's:
//  F# resolves a qualified name against every module of that name in the
//  referenced assemblies, on .NET and under Fable, so the kit needs no reference
//  back to this package (which is the upward reference the boundary test
//  refuses). Each member is a plain call to its home in `DataFrameConformance`;
//  nothing is defined here that is not defined there. See DECISIONS.md D68.
// ============================================================================

/// Forwards from the pre-Phase-257 spellings to `DataFrameConformance`. Marked for removal in
/// Phase 258, when the dataframe layer leaves this repository; spell the families
/// `DataFrameConformance.<family>` to be unaffected by that removal.
module Conformance =

    /// Forward of `DataFrameConformance.transformLaws` (removal: Phase 258).
    let transformLaws
        (under: Transform list -> Table -> Result<Table, EvalError>)
        (gen: ConfRng.T -> (Table * Transform list) * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DataFrameConformance.transformLaws under gen seed iterations

    /// Forward of `DataFrameConformance.aggregateParityLaws` (removal: Phase 258).
    let aggregateParityLaws (seed: int) (iterations: int) : LawResult list =
        DataFrameConformance.aggregateParityLaws seed iterations

    /// Forward of `DataFrameConformance.columnarOpStreamGen` (removal: Phase 258).
    let columnarOpStreamGen: StreamGen<ColumnOp, Table> =
        DataFrameConformance.columnarOpStreamGen

    /// Forward of `DataFrameConformance.columnarOpLawsWith` (removal: Phase 258).
    let columnarOpLawsWith
        (invertUnderTest: ColumnOp -> Table -> Result<ColumnOp, ColumnRejection>)
        (gen: StreamGen<ColumnOp, Table>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DataFrameConformance.columnarOpLawsWith invertUnderTest gen seed iterations

    /// Forward of `DataFrameConformance.columnarOpLaws` (removal: Phase 258).
    let columnarOpLaws (seed: int) (iterations: int) : LawResult list =
        DataFrameConformance.columnarOpLaws seed iterations

    /// Forward of `DataFrameConformance.incrementalLaws` (removal: Phase 258).
    let incrementalLaws (seed: int) (iterations: int) : LawResult list =
        DataFrameConformance.incrementalLaws seed iterations

    /// Forward of `DataFrameConformance.incrementalLawsWith` (removal: Phase 258).
    let incrementalLawsWith
        (pipelines: Transform list list)
        (gen: StreamGen<ColumnOp, Table>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DataFrameConformance.incrementalLawsWith pipelines gen seed iterations

    /// Forward of `DataFrameConformance.paramLaws` (removal: Phase 258).
    let paramLaws (seed: int) (iterations: int) : LawResult list =
        DataFrameConformance.paramLaws seed iterations

    /// Forward of `DataFrameConformance.schemaWalkLaws` (removal: Phase 258).
    let schemaWalkLaws (seed: int) (iterations: int) : LawResult list =
        DataFrameConformance.schemaWalkLaws seed iterations

    /// Forward of `DataFrameConformance.nowLaws` (removal: Phase 258).
    let nowLaws (seed: int) (iterations: int) : LawResult list =
        DataFrameConformance.nowLaws seed iterations

    /// Forward of `DataFrameConformance.slotParamLaws` (removal: Phase 258).
    let slotParamLaws (seed: int) (iterations: int) : LawResult list =
        DataFrameConformance.slotParamLaws seed iterations
