// ============================================================================
//  The dataframe half of the reconstruction walk (Phase 257) — READ every
//  `ColExpr` and `Transform` case through `Match` / `Switch`, and re-CONSTRUCT it
//  through the factories, using nothing but the two facades. There is deliberately
//  no `using Microsoft.FSharp.*` in it, and no cast to a Core type.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal static partial class Rebuild
{
    internal static Expr Expression(Expr e) =>
        e.Match(
            onCol: n => Expr.Col(n),
            onLiteral: c => Expr.Literal(Cell(c)),
            onParam: n => Expr.Param(n),
            onBinary: (op, l, r) => Expr.Binary(op, Expression(l), Expression(r)),
            onNot: x => Expr.Not(Expression(x)),
            onCoalesce: xs => Expr.Coalesce(xs.Select(x => Expression(x))),
            onCase: (arms, els) =>
                Expr.Case(arms.Select(a => new CaseArm(Expression(a.When), Expression(a.Then))), Expression(els)),
            onCast: (k, x) => Expr.Cast(k, Expression(x)),
            onApply: (fn, xs) => Expr.Apply(fn, xs.Select(x => Expression(x))),
            onInList: (s, xs) => Expr.InList(Expression(s), xs.Select(x => Expression(x))),
            onIsNull: x => Expr.IsNull(Expression(x)),
            onInParam: (s, n) => Expr.InParam(Expression(s), n),
            onNow: g => Expr.Now(g)
        );

    internal static WindowFunction Window(WindowFunction f) =>
        f.Kind switch
        {
            WindowFunctionKind.RowNumber => WindowFunction.RowNumber,
            WindowFunctionKind.Rank => WindowFunction.Rank,
            WindowFunctionKind.Lag => WindowFunction.Lag,
            WindowFunctionKind.Lead => WindowFunction.Lead,
            WindowFunctionKind.CumulSum => WindowFunction.CumulSum,
            WindowFunctionKind.RollingMean => WindowFunction.RollingMean,
            WindowFunctionKind.DenseRank => WindowFunction.DenseRank,
            WindowFunctionKind.CompetitionRank => WindowFunction.CompetitionRank,
            WindowFunctionKind.NTile => WindowFunction.NTile(
                f.Buckets ?? throw new InvalidOperationException("NTile with no bucket count")
            ),
            WindowFunctionKind.CumulMax => WindowFunction.CumulMax,
            WindowFunctionKind.CumulMin => WindowFunction.CumulMin,
            WindowFunctionKind.RollingSum => WindowFunction.RollingSum,
            _ => throw new InvalidOperationException($"unmodelled window function {f.Kind}"),
        };

    internal static AggregateSpec Aggregate(AggregateSpec a) => AggregateSpec.Of(a.Name, a.Function, a.OfColumn);

    internal static WindowStepSpec WindowStep(WindowStepSpec w) =>
        WindowStepSpec.Of(
            w.PartitionBy,
            w.OrderBy.Select(k => new SortKey(k.Column, k.Order)),
            Window(w.Function),
            w.OfColumn,
            w.AsColumn
        );

    internal static PivotStepSpec PivotStep(PivotStepSpec p) =>
        PivotStepSpec.Of(p.Index, p.OnColumn, p.ValuesColumn, p.Aggregate);

    internal static Step TransformStep(Step s) =>
        s.Match(
            onFilter: e => Step.Filter(Expression(e)),
            onProject: rs => Step.Project(rs.Select(r => new ColumnRename(r.Source, r.Output))),
            onDerive: (n, e) => Step.Derive(n, Expression(e)),
            onGroupBy: (keys, aggs) => Step.GroupBy(keys, aggs.Select(a => Aggregate(a))),
            onJoin: (src, keys, mode) =>
                Step.Join(Source(src), keys.Select(k => new JoinKey(k.LeftColumn, k.RightColumn)), mode),
            onWindow: w => Step.Window(WindowStep(w)),
            onPivot: p => Step.Pivot(PivotStep(p)),
            onUnpivot: (ids, vals) => Step.Unpivot(ids, vals),
            onSort: keys => Step.Sort(keys.Select(k => new SortSlot(k.Column, k.Order))),
            onDistinct: () => Step.Distinct,
            onLimit: (n, o) => Step.Limit(n, o),
            onUnion: src => Step.Union(Source(src)),
            onIntersect: src => Step.Intersect(Source(src)),
            onExcept: src => Step.Except(Source(src))
        );

    internal static Pipeline PipelineOf(Pipeline p) => Pipeline.Of(p.Steps.Select(s => TransformStep(s)));
}
