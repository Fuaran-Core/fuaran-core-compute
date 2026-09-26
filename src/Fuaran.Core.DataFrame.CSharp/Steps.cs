// ============================================================================
//  Fuaran.Core.DataFrame.CSharp — the transform step and the pipeline (Phase 128), a facade
//  over `Fuaran.Core.Transform` and `Transform list`.
//
//  Every place the F# algebra uses a tuple in a list — a project rename, a join key
//  pair, a sort key — gets a named two-property type here, because `Item1` /
//  `Item2` at an authoring site is exactly the kind of positional reading a veneer
//  cannot check.
//
//  `Pipeline` exists for one reason worth stating: a pipeline IS an F# list on the
//  Core side, so without it the very first thing a C# caller would have to do with
//  a well-typed `Step` is build an `FSharpList` to hand it over.
// ============================================================================

using Microsoft.FSharp.Collections;

namespace Fuaran.Core.CSharp;

/// <summary>One <c>(source, output)</c> rename of a projection.</summary>
public sealed class ColumnRename : IEquatable<ColumnRename>
{
    /// <summary>Create a rename. Pass the same name twice to keep a column unrenamed.</summary>
    public ColumnRename(string source, string output)
    {
        Source = Interop.NotNull(source, nameof(source));
        Output = Interop.NotNull(output, nameof(output));
    }

    /// <summary>The input column name.</summary>
    public string Source { get; }

    /// <summary>The output column name.</summary>
    public string Output { get; }

    /// <inheritdoc />
    public bool Equals(ColumnRename? other) =>
        other is not null && Source == other.Source && Output == other.Output;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ColumnRename);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Source, Output);

    /// <inheritdoc />
    public override string ToString() => $"{Source} -> {Output}";
}

/// <summary>One <c>(leftColumn, rightColumn)</c> key pair of a join.</summary>
public sealed class JoinKey : IEquatable<JoinKey>
{
    /// <summary>Create a join key pair.</summary>
    public JoinKey(string leftColumn, string rightColumn)
    {
        LeftColumn = Interop.NotNull(leftColumn, nameof(leftColumn));
        RightColumn = Interop.NotNull(rightColumn, nameof(rightColumn));
    }

    /// <summary>The left side's key column.</summary>
    public string LeftColumn { get; }

    /// <summary>The right side's key column.</summary>
    public string RightColumn { get; }

    /// <inheritdoc />
    public bool Equals(JoinKey? other) =>
        other is not null && LeftColumn == other.LeftColumn && RightColumn == other.RightColumn;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as JoinKey);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(LeftColumn, RightColumn);

    /// <inheritdoc />
    public override string ToString() => $"{LeftColumn} = {RightColumn}";
}

/// <summary>One <c>(column, direction)</c> entry of a sort or window ordering.</summary>
public sealed class SortKey : IEquatable<SortKey>
{
    /// <summary>Create a sort key.</summary>
    public SortKey(string column, SortOrder order)
    {
        Column = Interop.NotNull(column, nameof(column));
        Order = order;
    }

    /// <summary>The column ordered on.</summary>
    public string Column { get; }

    /// <summary>The direction.</summary>
    public SortOrder Order { get; }

    /// <inheritdoc />
    public bool Equals(SortKey? other) => other is not null && Column == other.Column && Order == other.Order;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SortKey);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Column, Order);

    /// <inheritdoc />
    public override string ToString() => $"{Column} {Order}";
}

/// <summary>One aggregate of a group-by or pivot: an output name, the function, and the column it reads.</summary>
public sealed class AggregateSpec : IEquatable<AggregateSpec>
{
    private readonly Agg _core;

    private AggregateSpec(Agg core) => _core = core;

    /// <summary>Create an aggregate.</summary>
    public static AggregateSpec Of(string name, AggregateFunction function, string ofColumn) =>
        new(
            new Agg(
                Interop.NotNull(name, nameof(name)),
                Vocab.ToCore(function),
                Interop.NotNull(ofColumn, nameof(ofColumn))
            )
        );

    /// <summary>The output column name.</summary>
    public string Name => _core.Name;

    /// <summary>The aggregate function.</summary>
    public AggregateFunction Function => Vocab.AggregateFunctionOf(_core.Fn);

    /// <summary>The input column the aggregate reads.</summary>
    public string OfColumn => _core.Of;

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public Agg ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static AggregateSpec FromCore(Agg core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(AggregateSpec? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as AggregateSpec);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>Which window function a window step computes.</summary>
public enum WindowFunctionKind
{
    RowNumber,
    Rank,
    Lag,
    Lead,
    CumulSum,
    RollingMean,
    DenseRank,
    CompetitionRank,
    NTile,
    CumulMax,
    CumulMin,
    RollingSum,
}

/// <summary>
/// A window function. Every case but <c>NTile</c> is nullary, so this is a <see cref="Kind" /> a caller
/// switches over, with <see cref="Buckets" /> carrying <c>NTile</c>'s bucket count and null everywhere else.
/// </summary>
public sealed class WindowFunction : IEquatable<WindowFunction>
{
    private readonly WindowFn _core;

    private WindowFunction(WindowFn core) => _core = core;

    /// <summary>The row's 1-based ordinal within its partition.</summary>
    public static WindowFunction RowNumber { get; } = new(WindowFn.RowNumber);

    /// <summary>Gapless ranking — the same computation as <see cref="DenseRank" />.</summary>
    public static WindowFunction Rank { get; } = new(WindowFn.Rank);

    /// <summary>The previous row's value within the partition.</summary>
    public static WindowFunction Lag { get; } = new(WindowFn.Lag);

    /// <summary>The next row's value within the partition.</summary>
    public static WindowFunction Lead { get; } = new(WindowFn.Lead);

    /// <summary>The running total.</summary>
    public static WindowFunction CumulSum { get; } = new(WindowFn.CumulSum);

    /// <summary>The trailing-window mean.</summary>
    public static WindowFunction RollingMean { get; } = new(WindowFn.RollingMean);

    /// <summary>Gapless ranking, spelled explicitly.</summary>
    public static WindowFunction DenseRank { get; } = new(WindowFn.DenseRank);

    /// <summary>SQL <c>RANK()</c> — ties share the lowest rank and the next distinct key skips.</summary>
    public static WindowFunction CompetitionRank { get; } = new(WindowFn.CompetitionRank);

    /// <summary>The running maximum over present values.</summary>
    public static WindowFunction CumulMax { get; } = new(WindowFn.CumulMax);

    /// <summary>The running minimum over present values.</summary>
    public static WindowFunction CumulMin { get; } = new(WindowFn.CumulMin);

    /// <summary>The trailing-window total.</summary>
    public static WindowFunction RollingSum { get; } = new(WindowFn.RollingSum);

    /// <summary>SQL <c>NTILE(n)</c> — distribute the partition's ordered rows into <paramref name="buckets" /> buckets.</summary>
    public static WindowFunction NTile(int buckets) => new(WindowFn.NewNTile(buckets));

    /// <summary>Which window function this is.</summary>
    public WindowFunctionKind Kind =>
        _core.Tag switch
        {
            WindowFn.Tags.RowNumber => WindowFunctionKind.RowNumber,
            WindowFn.Tags.Rank => WindowFunctionKind.Rank,
            WindowFn.Tags.Lag => WindowFunctionKind.Lag,
            WindowFn.Tags.Lead => WindowFunctionKind.Lead,
            WindowFn.Tags.CumulSum => WindowFunctionKind.CumulSum,
            WindowFn.Tags.RollingMean => WindowFunctionKind.RollingMean,
            WindowFn.Tags.DenseRank => WindowFunctionKind.DenseRank,
            WindowFn.Tags.CompetitionRank => WindowFunctionKind.CompetitionRank,
            WindowFn.Tags.NTile => WindowFunctionKind.NTile,
            WindowFn.Tags.CumulMax => WindowFunctionKind.CumulMax,
            WindowFn.Tags.CumulMin => WindowFunctionKind.CumulMin,
            WindowFn.Tags.RollingSum => WindowFunctionKind.RollingSum,
            _ => throw Interop.UnknownCase(nameof(WindowFn), _core.Tag),
        };

    /// <summary><c>NTile</c>'s bucket count; null for every other kind.</summary>
    public int? Buckets => _core.Tag == WindowFn.Tags.NTile ? ((WindowFn.NTile)_core).buckets : null;

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public WindowFn ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static WindowFunction FromCore(WindowFn core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(WindowFunction? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as WindowFunction);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>A window step's specification.</summary>
public sealed class WindowStepSpec : IEquatable<WindowStepSpec>
{
    private readonly WindowSpec _core;

    private WindowStepSpec(WindowSpec core) => _core = core;

    /// <summary>Create a window specification.</summary>
    public static WindowStepSpec Of(
        IEnumerable<string> partitionBy,
        IEnumerable<SortKey> orderBy,
        WindowFunction function,
        string ofColumn,
        string asColumn
    ) =>
        new(
            new WindowSpec(
                Interop.List(Interop.Items(partitionBy, nameof(partitionBy))),
                Interop.List(
                    Interop
                        .Items(orderBy, nameof(orderBy))
                        .Select(k => Tuple.Create(k.Column, Vocab.ToCore(k.Order)))
                ),
                Interop.NotNull(function, nameof(function)).ToCore(),
                Interop.NotNull(ofColumn, nameof(ofColumn)),
                Interop.NotNull(asColumn, nameof(asColumn))
            )
        );

    /// <summary>The partition key columns.</summary>
    public IReadOnlyList<string> PartitionBy => Interop.Read(_core.PartitionBy);

    /// <summary>The ordering within each partition.</summary>
    public IReadOnlyList<SortKey> OrderBy =>
        Interop.Read(_core.OrderBy).Select(t => new SortKey(t.Item1, Vocab.SortOrderOf(t.Item2))).ToArray();

    /// <summary>The window function.</summary>
    public WindowFunction Function => WindowFunction.FromCore(_core.Fn);

    /// <summary>The input column.</summary>
    public string OfColumn => _core.Of;

    /// <summary>The output column.</summary>
    public string AsColumn => _core.As;

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public WindowSpec ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static WindowStepSpec FromCore(WindowSpec core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(WindowStepSpec? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as WindowStepSpec);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>A pivot step's specification.</summary>
public sealed class PivotStepSpec : IEquatable<PivotStepSpec>
{
    private readonly PivotSpec _core;

    private PivotStepSpec(PivotSpec core) => _core = core;

    /// <summary>Create a pivot specification.</summary>
    public static PivotStepSpec Of(
        IEnumerable<string> index,
        string onColumn,
        string valuesColumn,
        AggregateFunction aggregate
    ) =>
        new(
            new PivotSpec(
                Interop.List(Interop.Items(index, nameof(index))),
                Interop.NotNull(onColumn, nameof(onColumn)),
                Interop.NotNull(valuesColumn, nameof(valuesColumn)),
                Vocab.ToCore(aggregate)
            )
        );

    /// <summary>The columns kept as the pivot's row index.</summary>
    public IReadOnlyList<string> Index => Interop.Read(_core.Index);

    /// <summary>The column whose distinct values become output columns.</summary>
    public string OnColumn => _core.On;

    /// <summary>The column aggregated into each cell.</summary>
    public string ValuesColumn => _core.Values;

    /// <summary>The aggregate applied per cell.</summary>
    public AggregateFunction Aggregate => Vocab.AggregateFunctionOf(_core.Agg);

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public PivotSpec ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static PivotStepSpec FromCore(PivotSpec core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(PivotStepSpec? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PivotStepSpec);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>One step of a transform pipeline — the full relational verb set.</summary>
public sealed class Step : IEquatable<Step>
{
    private readonly Transform _core;

    private Step(Transform core) => _core = core;

    // ---- construction ----

    /// <summary>Keep rows whose predicate evaluates to true; null and false drop.</summary>
    public static Step Filter(Expr predicate) =>
        new(Transform.NewFilter(Interop.NotNull(predicate, nameof(predicate)).ToCore()));

    /// <summary>Keep and rename columns, in the order given.</summary>
    public static Step Project(IEnumerable<ColumnRename> renames) =>
        new(
            Transform.NewProject(
                Interop.List(Interop.Items(renames, nameof(renames)).Select(r => Tuple.Create(r.Source, r.Output)))
            )
        );

    /// <summary>Keep and rename columns, in the order given.</summary>
    public static Step Project(params ColumnRename[] renames) => Project((IEnumerable<ColumnRename>)renames);

    /// <summary>Add a computed column, overwriting one of the same name.</summary>
    public static Step Derive(string name, Expr expression) =>
        new(
            Transform.NewDerive(
                Interop.NotNull(name, nameof(name)),
                Interop.NotNull(expression, nameof(expression)).ToCore()
            )
        );

    /// <summary>Group by the given keys, one row per group with the listed aggregates.</summary>
    public static Step GroupBy(IEnumerable<string> keys, IEnumerable<AggregateSpec> aggregates) =>
        new(
            Transform.NewGroupBy(
                Interop.List(Interop.Items(keys, nameof(keys))),
                Interop.List(Interop.Items(aggregates, nameof(aggregates)).Select(a => a.ToCore()))
            )
        );

    /// <summary>Join another source on the given key pairs.</summary>
    public static Step Join(SourceValue source, IEnumerable<JoinKey> keys, JoinMode mode) =>
        new(
            Transform.NewJoin(
                Interop.NotNull(source, nameof(source)).ToCore(),
                Interop.List(
                    Interop.Items(keys, nameof(keys)).Select(k => Tuple.Create(k.LeftColumn, k.RightColumn))
                ),
                Vocab.ToCore(mode)
            )
        );

    /// <summary>Compute a window function into a new column.</summary>
    public static Step Window(WindowStepSpec spec) =>
        new(Transform.NewWindow(Interop.NotNull(spec, nameof(spec)).ToCore()));

    /// <summary>Pivot long to wide.</summary>
    public static Step Pivot(PivotStepSpec spec) =>
        new(Transform.NewPivot(Interop.NotNull(spec, nameof(spec)).ToCore()));

    /// <summary>Melt the value columns into <c>(variable, value)</c> rows, keeping the id columns.</summary>
    public static Step Unpivot(IEnumerable<string> idColumns, IEnumerable<string> valueColumns) =>
        new(
            Transform.NewUnpivot(
                Interop.List(Interop.Items(idColumns, nameof(idColumns))),
                Interop.List(Interop.Items(valueColumns, nameof(valueColumns)))
            )
        );

    /// <summary>Sort by the given keys, whose columns may be bound parameters.</summary>
    public static Step Sort(IEnumerable<SortSlot> keys) =>
        new(
            Transform.NewSort(
                Interop.List(
                    Interop
                        .Items(keys, nameof(keys))
                        .Select(k => Tuple.Create(k.Column.ToCoreSlot(), Vocab.ToCore(k.Order)))
                )
            )
        );

    /// <summary>Sort by the given keys, whose columns may be bound parameters.</summary>
    public static Step Sort(params SortSlot[] keys) => Sort((IEnumerable<SortSlot>)keys);

    /// <summary>Sort by the given literal-column keys — the everyday form.</summary>
    public static Step Sort(IEnumerable<SortKey> keys) =>
        Sort(Interop.Items(keys, nameof(keys)).Select(k => new SortSlot(k.Column, k.Order)));

    /// <summary>Sort by the given literal-column keys — the everyday form.</summary>
    public static Step Sort(params SortKey[] keys) => Sort((IEnumerable<SortKey>)keys);

    /// <summary>Drop duplicate rows.</summary>
    public static Step Distinct { get; } = new(Transform.Distinct);

    /// <summary>Take <paramref name="count" /> rows after skipping <paramref name="offset" />.</summary>
    public static Step Limit(int count, int offset) =>
        Limit(CountSlot.Literal(count), CountSlot.Literal(offset));

    /// <summary>
    /// Take <paramref name="count" /> rows after skipping <paramref name="offset" />, either of
    /// which may be a bound parameter — the page-size and page-offset a host binds most often.
    /// </summary>
    public static Step Limit(CountSlot count, CountSlot offset) =>
        new(
            Transform.NewLimit(
                Interop.NotNull(count, nameof(count)).ToCoreSlot(),
                Interop.NotNull(offset, nameof(offset)).ToCoreSlot()
            )
        );

    /// <summary>Concatenate another source.</summary>
    public static Step Union(SourceValue source) =>
        new(Transform.NewUnion(Interop.NotNull(source, nameof(source)).ToCore()));

    /// <summary>Keep the left rows whose whole row also appears in the other source.</summary>
    public static Step Intersect(SourceValue source) =>
        new(Transform.NewIntersect(Interop.NotNull(source, nameof(source)).ToCore()));

    /// <summary>Keep the left rows whose whole row does not appear in the other source.</summary>
    public static Step Except(SourceValue source) =>
        new(Transform.NewExcept(Interop.NotNull(source, nameof(source)).ToCore()));

    // ---- reading ----

    /// <summary>Read this step by case. Total: exactly one branch runs.</summary>
    public T Match<T>(
        Func<Expr, T> onFilter,
        Func<IReadOnlyList<ColumnRename>, T> onProject,
        Func<string, Expr, T> onDerive,
        Func<IReadOnlyList<string>, IReadOnlyList<AggregateSpec>, T> onGroupBy,
        Func<SourceValue, IReadOnlyList<JoinKey>, JoinMode, T> onJoin,
        Func<WindowStepSpec, T> onWindow,
        Func<PivotStepSpec, T> onPivot,
        Func<IReadOnlyList<string>, IReadOnlyList<string>, T> onUnpivot,
        Func<IReadOnlyList<SortSlot>, T> onSort,
        Func<T> onDistinct,
        Func<CountSlot, CountSlot, T> onLimit,
        Func<SourceValue, T> onUnion,
        Func<SourceValue, T> onIntersect,
        Func<SourceValue, T> onExcept
    )
    {
        switch (_core.Tag)
        {
            case Transform.Tags.Filter:
                return onFilter(Expr.FromCore(((Transform.Filter)_core).Item));
            case Transform.Tags.Project:
                return onProject(
                    Interop
                        .Read(((Transform.Project)_core).Item)
                        .Select(t => new ColumnRename(t.Item1, t.Item2))
                        .ToArray()
                );
            case Transform.Tags.Derive:
            {
                var c = (Transform.Derive)_core;
                return onDerive(c.Item1, Expr.FromCore(c.Item2));
            }
            case Transform.Tags.GroupBy:
            {
                var c = (Transform.GroupBy)_core;
                return onGroupBy(
                    Interop.Read(c.Item1),
                    Interop.Read(c.Item2).Select(AggregateSpec.FromCore).ToArray()
                );
            }
            case Transform.Tags.Join:
            {
                var c = (Transform.Join)_core;
                return onJoin(
                    SourceValue.FromCore(c.Item1),
                    Interop.Read(c.Item2).Select(t => new JoinKey(t.Item1, t.Item2)).ToArray(),
                    Vocab.JoinModeOf(c.Item3)
                );
            }
            case Transform.Tags.Window:
                return onWindow(WindowStepSpec.FromCore(((Transform.Window)_core).Item));
            case Transform.Tags.Pivot:
                return onPivot(PivotStepSpec.FromCore(((Transform.Pivot)_core).Item));
            case Transform.Tags.Unpivot:
            {
                var c = (Transform.Unpivot)_core;
                return onUnpivot(Interop.Read(c.idVars), Interop.Read(c.valueVars));
            }
            case Transform.Tags.Sort:
                return onSort(
                    Interop
                        .Read(((Transform.Sort)_core).Item)
                        .Select(t => new SortSlot(ColumnSlot.FromCoreSlot(t.Item1), Vocab.SortOrderOf(t.Item2)))
                        .ToArray()
                );
            case Transform.Tags.Distinct:
                return onDistinct();
            case Transform.Tags.Limit:
            {
                var c = (Transform.Limit)_core;
                return onLimit(CountSlot.FromCoreSlot(c.n), CountSlot.FromCoreSlot(c.offset));
            }
            case Transform.Tags.Union:
                return onUnion(SourceValue.FromCore(((Transform.Union)_core).Item));
            case Transform.Tags.Intersect:
                return onIntersect(SourceValue.FromCore(((Transform.Intersect)_core).Item));
            case Transform.Tags.Except:
                return onExcept(SourceValue.FromCore(((Transform.Except)_core).Item));
            default:
                throw Interop.UnknownCase(nameof(Transform), _core.Tag);
        }
    }

    /// <summary>Read this step by case for effect. Total: exactly one branch runs.</summary>
    public void Switch(
        Action<Expr> onFilter,
        Action<IReadOnlyList<ColumnRename>> onProject,
        Action<string, Expr> onDerive,
        Action<IReadOnlyList<string>, IReadOnlyList<AggregateSpec>> onGroupBy,
        Action<SourceValue, IReadOnlyList<JoinKey>, JoinMode> onJoin,
        Action<WindowStepSpec> onWindow,
        Action<PivotStepSpec> onPivot,
        Action<IReadOnlyList<string>, IReadOnlyList<string>> onUnpivot,
        Action<IReadOnlyList<SortSlot>> onSort,
        Action onDistinct,
        Action<CountSlot, CountSlot> onLimit,
        Action<SourceValue> onUnion,
        Action<SourceValue> onIntersect,
        Action<SourceValue> onExcept
    ) =>
        Match(
            e =>
            {
                onFilter(e);
                return true;
            },
            rs =>
            {
                onProject(rs);
                return true;
            },
            (n, e) =>
            {
                onDerive(n, e);
                return true;
            },
            (ks, aggs) =>
            {
                onGroupBy(ks, aggs);
                return true;
            },
            (s, ks, m) =>
            {
                onJoin(s, ks, m);
                return true;
            },
            w =>
            {
                onWindow(w);
                return true;
            },
            p =>
            {
                onPivot(p);
                return true;
            },
            (ids, vals) =>
            {
                onUnpivot(ids, vals);
                return true;
            },
            ks =>
            {
                onSort(ks);
                return true;
            },
            () =>
            {
                onDistinct();
                return true;
            },
            (n, o) =>
            {
                onLimit(n, o);
                return true;
            },
            s =>
            {
                onUnion(s);
                return true;
            },
            s =>
            {
                onIntersect(s);
                return true;
            },
            s =>
            {
                onExcept(s);
                return true;
            }
        );

    // ---- the bridge ----

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public Transform ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static Step FromCore(Transform core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(Step? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Step);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}

/// <summary>An ordered pipeline of transform steps — the F# <c>Transform list</c>, carried as a C# value.</summary>
public sealed class Pipeline : IEquatable<Pipeline>
{
    private readonly FSharpList<Transform> _core;

    private Pipeline(FSharpList<Transform> core) => _core = core;

    /// <summary>Create a pipeline from its steps, in order.</summary>
    public static Pipeline Of(IEnumerable<Step> steps) =>
        new(Interop.List(Interop.Items(steps, nameof(steps)).Select(s => s.ToCore())));

    /// <summary>Create a pipeline from its steps, in order.</summary>
    public static Pipeline Of(params Step[] steps) => Of((IEnumerable<Step>)steps);

    /// <summary>The steps, in order.</summary>
    public IReadOnlyList<Step> Steps => Interop.Read(_core).Select(Step.FromCore).ToArray();

    /// <summary>The pipeline's canonical wire JSON.</summary>
    public string Encode() => DataFrameCodec.encodePipeline(_core);

    /// <summary>
    /// Read a pipeline back from its canonical wire JSON. Returns false and names the decode failure
    /// rather than throwing — the wrapped codec's recoverable-envelope discipline, carried across.
    /// </summary>
    public static bool TryDecode(string json, out Pipeline? pipeline, out string? error)
    {
        var result = DataFrameCodec.decodePipeline(Interop.NotNull(json, nameof(json)));

        if (result.IsOk)
        {
            pipeline = new Pipeline(result.ResultValue);
            error = null;
            return true;
        }

        pipeline = null;
        error = result.ErrorValue.ToString();
        return false;
    }

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public FSharpList<Transform> ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static Pipeline FromCore(FSharpList<Transform> core) => new(Interop.NotNull(core, nameof(core)));

    /// <inheritdoc />
    public bool Equals(Pipeline? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Pipeline);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}
