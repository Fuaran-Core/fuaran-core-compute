// ============================================================================
//  The reconstruction walk — READ every case through `Match` / `Switch`, and
//  re-CONSTRUCT the same value through the factories, using nothing but
//  `Fuaran.Core.CSharp`. PARTIAL since Phase 257: the dataframe half
//  (`ColExpr`, `Transform`) is tests/Fuaran.Core.DataFrame.CSharp.Proof/DataFrameRebuild.cs.
//
//  This file is the phase's acceptance criterion written as code: it constructs
//  and reads `ColExpr`, `Transform`, the hole-declaration family and `JVal`
//  without naming an F# option, list, tuple or union case anywhere. There is
//  deliberately no `using Microsoft.FSharp.*` in it, and no cast to a Core type —
//  if the facade were incomplete in any case, this file could not be written, and
//  if a reader and its factory disagreed, the round-trip law would say so.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal static partial class Rebuild
{
    internal static CellValue Cell(CellValue v) =>
        v.Match(
            onInt: x => CellValue.Int(x),
            onFloat: x => CellValue.Float(x),
            onBool: x => CellValue.Bool(x),
            onStr: x => CellValue.Str(x),
            onDate: x => CellValue.Date(x),
            onTimestamp: x => CellValue.Timestamp(x),
            onNull: () => CellValue.Null
        );

    internal static ColumnValue ColumnOf(ColumnValue c) =>
        ColumnValue.Of(c.Name, c.Kind, c.Cells.Select(x => Cell(x)));

    internal static TableValue Table(TableValue t) => TableValue.Of(t.Columns.Select(c => ColumnOf(c)));

    internal static SourceValue Source(SourceValue s) =>
        s.Match(onEmbedded: t => SourceValue.Embedded(Table(t)), onReference: n => SourceValue.Reference(n));

    internal static JsonValue Json(JsonValue v) =>
        v.Match(
            onStr: x => JsonValue.Str(x),
            onInt: x => JsonValue.Int(x),
            onBool: x => JsonValue.Bool(x),
            onFloat: x => JsonValue.Float(x),
            onArray: xs => JsonValue.Array(xs.Select(x => Json(x))),
            onObject: ms => JsonValue.Object(ms.Select(m => new JsonMember(m.Name, Json(m.Value))))
        );

    internal static EffectSignature Effect(EffectSignature e) => EffectSignature.Of(e.Host, e.Determinism);

    internal static HoleSpace Space(HoleSpace s) =>
        s.Match(
            onIntRange: (lo, hi) => HoleSpace.IntRange(lo, hi),
            onFloatRange: (lo, hi) => HoleSpace.FloatRange(lo, hi),
            onStringLen: (lo, hi) => HoleSpace.StringLen(lo, hi),
            onEnumeration: ms => HoleSpace.Enumeration(ms),
            onAnyString: () => HoleSpace.AnyString,
            onSlotTree: k => HoleSpace.SlotTree(k)
        );

    internal static HoleShape Shape(HoleShape k) =>
        k.Match(
            onValue: s => HoleShape.Value(Space(s)),
            onSlot: c => HoleShape.Slot(c),
            onRepeat: s => HoleShape.Repeat(Space(s)),
            onAction: e => HoleShape.Action(Effect(e))
        );

    internal static HoleSpec Hole(HoleSpec h) => HoleSpec.Of(h.Address, h.Name, Shape(h.Shape));

    internal static SignatureHoleView SignatureHole(SignatureHoleView h) =>
        SignatureHoleView.Of(
            h.Address,
            h.Name,
            h.Kind,
            h.Space is null ? null : Space(h.Space),
            h.Slot,
            h.Action is null ? null : Effect(h.Action),
            h.Required
        );

    internal static SignatureView Signature(SignatureView s) =>
        SignatureView.Of(s.Name, s.Holes.Select(h => SignatureHole(h)), Effect(s.Effect));
}
