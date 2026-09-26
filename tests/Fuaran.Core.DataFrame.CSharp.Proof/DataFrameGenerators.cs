// ============================================================================
//  The dataframe half of the sample (Phase 257) — the expression algebra and the
//  verb set, drawn off the same `ConfRng` stream as the column-layer half this
//  project compiles beside it (tests/Fuaran.Core.CSharp.Proof/Generators.cs). The
//  distribution rules are stated there: the top-level case is forced round-robin,
//  and the nullary vocabularies rotate rather than draw.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal sealed partial class Gen
{
    private static readonly BinaryOperator[] BinOps = Enum.GetValues<BinaryOperator>();
    private static readonly ScalarFunction[] ScalarFns = Enum.GetValues<ScalarFunction>();
    private static readonly JoinMode[] JoinModes = Enum.GetValues<JoinMode>();
    private static readonly SortOrder[] SortOrders = Enum.GetValues<SortOrder>();
    private static readonly ClockGrain[] ClockGrains = Enum.GetValues<ClockGrain>();
    private static readonly WindowFunctionKind[] WindowKinds = Enum.GetValues<WindowFunctionKind>();

    internal const int ExprCases = 13;
    internal const int StepCases = 16;

    private int _binOp;
    private int _scalarFn;
    private int _joinMode;
    private int _sortOrder;
    private int _clockGrain;
    private int _windowKind;

    // ---- leaves ----

    internal WindowFunction WindowFn()
    {
        var kind = WindowKinds[_windowKind++ % WindowKinds.Length];

        return kind switch
        {
            WindowFunctionKind.RowNumber => WindowFunction.RowNumber,
            WindowFunctionKind.Rank => WindowFunction.Rank,
            WindowFunctionKind.Lag => WindowFunction.Lag,
            WindowFunctionKind.Lead => WindowFunction.Lead,
            WindowFunctionKind.CumulSum => WindowFunction.CumulSum,
            WindowFunctionKind.RollingMean => WindowFunction.RollingMean,
            WindowFunctionKind.DenseRank => WindowFunction.DenseRank,
            WindowFunctionKind.CompetitionRank => WindowFunction.CompetitionRank,
            WindowFunctionKind.NTile => WindowFunction.NTile(1 + Below(8)),
            WindowFunctionKind.CumulMax => WindowFunction.CumulMax,
            WindowFunctionKind.CumulMin => WindowFunction.CumulMin,
            _ => WindowFunction.RollingSum,
        };
    }

    // ---- the expression algebra ----

    internal Expr Expression(int forcedCase, int depth)
    {
        var c = depth <= 0 ? forcedCase % 3 : forcedCase % ExprCases;

        switch (c)
        {
            case 0:
                return Expr.Col(Name());
            case 1:
                return Expr.Literal(Cell());
            case 2:
                return Expr.Param("p" + Below(4));
            case 3:
                return Expr.Binary(
                    BinOps[_binOp++ % BinOps.Length],
                    Expression(Below(ExprCases), depth - 1),
                    Expression(Below(ExprCases), depth - 1)
                );
            case 4:
                return Expr.Not(Expression(Below(ExprCases), depth - 1));
            case 5:
                return Expr.Coalesce(
                    Enumerable.Range(0, 1 + Below(3)).Select(_ => Expression(Below(ExprCases), depth - 1)).ToArray()
                );
            case 6:
                return Expr.Case(
                    Enumerable
                        .Range(0, 1 + Below(2))
                        .Select(_ => new CaseArm(
                            Expression(Below(ExprCases), depth - 1),
                            Expression(Below(ExprCases), depth - 1)
                        ))
                        .ToArray(),
                    Expression(Below(ExprCases), depth - 1)
                );
            case 7:
                return Expr.Cast(ColumnKinds[_columnKind++ % ColumnKinds.Length], Expression(Below(ExprCases), depth - 1));
            case 8:
                return Expr.Apply(
                    ScalarFns[_scalarFn++ % ScalarFns.Length],
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Expression(Below(ExprCases), depth - 1)).ToArray()
                );
            case 9:
                return Expr.InList(
                    Expression(Below(ExprCases), depth - 1),
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Expression(Below(ExprCases), depth - 1)).ToArray()
                );
            case 10:
                return Expr.IsNull(Expression(Below(ExprCases), depth - 1));
            case 11:
                return Expr.Now(ClockGrains[_clockGrain++ % ClockGrains.Length]);
            default:
                return Expr.InParam(Expression(Below(ExprCases), depth - 1), "lp" + Below(3));
        }
    }

    // ---- the verb set ----

    internal Step TransformStep(int forcedCase)
    {
        switch (forcedCase % StepCases)
        {
            case 0:
                return Step.Filter(Expression(Below(ExprCases), 2));
            case 1:
                return Step.Project(
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => new ColumnRename(Name(), Name())).ToArray()
                );
            case 2:
                return Step.Derive(Name(), Expression(Below(ExprCases), 2));
            case 3:
                return Step.GroupBy(
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Name()).ToArray(),
                    Enumerable
                        .Range(0, 1 + Below(2))
                        .Select(_ => AggregateSpec.Of(Name(), AggFns[_aggFn++ % AggFns.Length], Name()))
                        .ToArray()
                );
            case 4:
                return Step.Join(
                    Source(),
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => new JoinKey(Name(), Name())).ToArray(),
                    JoinModes[_joinMode++ % JoinModes.Length]
                );
            case 5:
                return Step.Window(
                    WindowStepSpec.Of(
                        Enumerable.Range(0, Below(2)).Select(_ => Name()).ToArray(),
                        Enumerable
                            .Range(0, 1 + Below(2))
                            .Select(_ => new SortKey(Name(), SortOrders[_sortOrder++ % SortOrders.Length]))
                            .ToArray(),
                        WindowFn(),
                        Name(),
                        Name()
                    )
                );
            case 6:
                return Step.Pivot(
                    PivotStepSpec.Of(
                        Enumerable.Range(0, 1 + Below(2)).Select(_ => Name()).ToArray(),
                        Name(),
                        Name(),
                        AggFns[_aggFn++ % AggFns.Length]
                    )
                );
            case 7:
                return Step.Unpivot(
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Name()).ToArray(),
                    Enumerable.Range(0, 1 + Below(2)).Select(_ => Name()).ToArray()
                );
            case 8:
                return Step.Sort(
                    Enumerable
                        .Range(0, 1 + Below(2))
                        .Select(_ => new SortKey(Name(), SortOrders[_sortOrder++ % SortOrders.Length]))
                        .ToArray()
                );
            case 9:
                return Step.Distinct;
            case 10:
                return Step.Limit(Below(50), Below(10));
            // Phase 125 — the PARAMETER halves of the two slots. Drawn as their own step cases
            // rather than mixed into the literal ones above, because the coverage guard reads the
            // `Slot` union's cases off the F# type: without a case that builds `Slot.Param`, the
            // round-trip law would report green about the very half the slot exists for.
            case 11:
                return Step.Sort(
                    new SortSlot(ColumnSlot.Parameter("sortCol" + Below(3)), SortOrders[_sortOrder++ % SortOrders.Length]),
                    new SortSlot(Name(), SortOrders[_sortOrder++ % SortOrders.Length])
                );
            case 12:
                return Step.Limit(CountSlot.Parameter("take" + Below(3)), CountSlot.Parameter("skip" + Below(3)));
            case 13:
                return Step.Union(Source());
            case 14:
                return Step.Intersect(Source());
            default:
                return Step.Except(Source());
        }
    }

    internal Pipeline PipelineOf(int forcedCase) =>
        Pipeline.Of(
            Enumerable.Range(0, 1 + Below(2)).Select(i => TransformStep(forcedCase + i)).ToArray()
        );
}
