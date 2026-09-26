// ============================================================================
//  The sample. Values are drawn off `ConfRng` — the seeded, Fable-identical draw
//  stream every Core conformance family already generates from — rather than off a
//  generator invented here, so a recorded seed reproduces a counterexample the same
//  way it does everywhere else in this repo.
//
//  Two deliberate choices about the DISTRIBUTION, both of which exist to keep the
//  round-trip law from certifying a sample that never reached most of the algebra:
//
//   * the TOP-LEVEL case is forced, round-robin, rather than drawn — so a run of N
//     iterations reaches every case of the union it generates by construction; and
//   * the nullary vocabularies (`AggFn`, `ColumnType`, and in the dataframe half
//     `BinOp`, `ScalarFn`, `JoinKind`, `SortDir`, `WindowFn`) rotate rather than
//     draw, for the same reason.
//
//  Since Phase 257 the class is PARTIAL: this half draws the column layer, the wire
//  JSON model and the hole family; the dataframe half is
//  tests/Fuaran.Core.DataFrame.CSharp.Proof/DataFrameGenerators.cs, which compiles
//  this file beside it.
//
//  Neither makes the law weaker: the law is an identity over whatever is drawn.
//  What they buy is that `Coverage` — the vacuity guard beside it — reports a
//  missing case as a FACADE gap rather than as an unlucky sample.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal sealed partial class Gen
{
    private static readonly AggregateFunction[] AggFns = Enum.GetValues<AggregateFunction>();
    private static readonly ColumnKind[] ColumnKinds = Enum.GetValues<ColumnKind>();

    internal const int JsonCases = 6;
    internal const int SpaceCases = 6;
    internal const int ShapeCases = 4;

    private ConfRng.T _rng;
    private int _aggFn;
    private int _columnKind;

    private int _cellCase;
    private int _names;

    internal Gen(int seed) => _rng = ConfRng.ofSeed(seed);

    private int Below(int n)
    {
        var drawn = ConfRng.intBelow(n, _rng);
        _rng = drawn.Item2;
        return drawn.Item1;
    }

    private string Name() => "c" + (_names++ % 7);

    // ---- leaves ----

    internal CellValue Cell()
    {
        switch (_cellCase++ % 7)
        {
            case 0:
                return CellValue.Int(Below(100) - 50);
            case 1:
                return CellValue.Float(Below(1000) / 8.0);
            case 2:
                return CellValue.Bool(Below(2) == 0);
            case 3:
                return CellValue.Str("s" + Below(20));
            case 4:
                return CellValue.Date($"2026-09-{(Below(27) + 1):00}");
            case 5:
                return CellValue.Timestamp($"2026-09-{(Below(27) + 1):00}T00:00:00Z");
            default:
                return CellValue.Null;
        }
    }

    internal ColumnValue Column()
    {
        var kind = ColumnKinds[_columnKind++ % ColumnKinds.Length];
        var cells = Enumerable.Range(0, 1 + Below(3)).Select(_ => Cell()).ToArray();
        return ColumnValue.Of(Name(), kind, cells);
    }

    internal TableValue Table() =>
        TableValue.Of(Enumerable.Range(0, 1 + Below(2)).Select(_ => Column()).ToArray());

    internal SourceValue Source() => Below(2) == 0 ? SourceValue.Embedded(Table()) : SourceValue.Reference(Name());

    // ---- the wire JSON model ----

    internal JsonValue Json(int forcedCase, int depth)
    {
        var c = depth <= 0 ? forcedCase % 4 : forcedCase % JsonCases;

        return c switch
        {
            0 => JsonValue.Str("j" + Below(20)),
            1 => JsonValue.Int(Below(200) - 100),
            2 => JsonValue.Bool(Below(2) == 0),
            3 => JsonValue.Float(Below(1000) / 16.0),
            4 => JsonValue.Array(
                Enumerable.Range(0, 1 + Below(3)).Select(_ => Json(Below(JsonCases), depth - 1)).ToArray()
            ),
            _ => JsonValue.Object(
                Enumerable
                    .Range(0, 1 + Below(3))
                    .Select(_ => new JsonMember(Name(), Json(Below(JsonCases), depth - 1)))
                    .ToArray()
            ),
        };
    }

    // ---- the artifact-function declaration family ----

    internal EffectSignature Effect() =>
        EffectSignature.Of(
            (HostEffectKind)Below(Enum.GetValues<HostEffectKind>().Length),
            (DeterminismKind)Below(Enum.GetValues<DeterminismKind>().Length)
        );

    internal HoleSpace Space(int forcedCase) =>
        (forcedCase % SpaceCases) switch
        {
            0 => HoleSpace.IntRange(Below(5), 5 + Below(20)),
            1 => HoleSpace.FloatRange(Below(5) / 2.0, 5.0 + Below(20)),
            2 => HoleSpace.StringLen(Below(3), 3 + Below(30)),
            3 => HoleSpace.Enumeration(Enumerable.Range(0, 1 + Below(3)).Select(_ => Name()).ToArray()),
            // Phase 229 — the tree space; with AnyString after it, the two unbounded cases are last.
            4 => HoleSpace.SlotTree(Below(2) == 0 ? null : "k" + Below(4)),
            _ => HoleSpace.AnyString,
        };

    internal HoleShape Shape(int forcedCase) =>
        (forcedCase % ShapeCases) switch
        {
            0 => HoleShape.Value(Space(Below(SpaceCases))),
            1 => HoleShape.Slot(Below(2) == 0 ? null : "k" + Below(4)),
            2 => HoleShape.Repeat(Space(Below(SpaceCases - 2))), // bounded spaces only
            _ => HoleShape.Action(Effect()),
        };

    internal HoleSpec Hole(int forcedCase) => HoleSpec.Of("/a/" + Below(9), Name(), Shape(forcedCase));

    internal SignatureHoleView SignatureHole(int forcedCase) =>
        SignatureHoleView.Of(
            "/a/" + Below(9),
            Name(),
            "kind" + Below(4),
            forcedCase % 2 == 0 ? Space(Below(SpaceCases)) : null,
            forcedCase % 3 == 0 ? "slot" + Below(3) : null,
            forcedCase % 5 == 0 ? Effect() : null,
            Below(2) == 0
        );

    internal SignatureView Signature(int forcedCase) =>
        SignatureView.Of(
            Name(),
            Enumerable.Range(0, 1 + Below(3)).Select(i => SignatureHole(forcedCase + i)).ToArray(),
            Effect()
        );

    // ---- the column-layer vocabulary the dataframe half used to reach (Phase 257) ----

    internal AggregateFunction Aggregate() => AggFns[_aggFn++ % AggFns.Length];
}
