// ============================================================================
//  Fuaran.Core.DataFrame.CSharp — the scalar slots (Phase 125), a facade over
//  `Fuaran.Core.Slot<'T>`.
//
//  A `Limit`'s count and a `Sort` key's column are LITERAL-OR-PARAMETER as of
//  `0.23.0`. `Slot<'T>` is an F# union, so it may not appear on a public member of
//  this facade (the surface rule); these two types are the C#-shaped reading of it,
//  one per instantiation the algebra uses.
//
//  Two concrete types rather than one generic `Slot<T>` wrapper: the bridge in and
//  out is per-instantiation anyway (there is no generic `Slot.NewLit` a C# caller
//  can reach without naming the F# type), and a `ColumnSlot` says what it holds at
//  the place an author reads it, where a `Slot<string>` would not.
// ============================================================================

namespace Fuaran.Core.CSharp;

/// <summary>A row count or offset: a literal, or a named parameter bound per evaluation.</summary>
public sealed class CountSlot : IEquatable<CountSlot>
{
    private readonly string? _param;

    private CountSlot(int value, string? param)
    {
        Value = value;
        _param = param;
    }

    /// <summary>The count itself.</summary>
    public static CountSlot Literal(int value) => new(value, null);

    /// <summary>
    /// A named parameter, resolved per evaluation from the same binding environment an expression
    /// parameter reads. Unbound at evaluation is refused by name, never defaulted.
    /// </summary>
    public static CountSlot Parameter(string name) => new(0, Interop.NotNull(name, nameof(name)));

    /// <summary>Is this slot a parameter rather than a literal?</summary>
    public bool IsParameter => _param is not null;

    /// <summary>The literal, when this slot holds one. Meaningless when <see cref="IsParameter" />.</summary>
    public int Value { get; }

    /// <summary>The parameter name, when this slot holds one; <c>null</c> otherwise.</summary>
    public string? ParameterName => _param;

    /// <summary>Read this slot by case. Total: exactly one branch runs.</summary>
    public T Match<T>(Func<int, T> onLiteral, Func<string, T> onParameter) =>
        _param is null
            ? Interop.NotNull(onLiteral, nameof(onLiteral))(Value)
            : Interop.NotNull(onParameter, nameof(onParameter))(_param);

    internal Slot<int> ToCoreSlot() => _param is null ? Slot<int>.NewLit(Value) : Slot<int>.NewParam(_param);

    internal static CountSlot FromCoreSlot(Slot<int> core) =>
        core.Tag switch
        {
            Slot<int>.Tags.Lit => Literal(((Slot<int>.Lit)core).Item),
            Slot<int>.Tags.Param => Parameter(((Slot<int>.Param)core).name),
            _ => throw Interop.UnknownCase("Slot", core.Tag),
        };

    /// <inheritdoc />
    public bool Equals(CountSlot? other) => other is not null && _param == other._param && Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CountSlot);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_param, Value);

    /// <inheritdoc />
    public override string ToString() => _param is null ? Value.ToString() : "$" + _param;
}

/// <summary>A column name: a literal, or a named parameter bound per evaluation.</summary>
public sealed class ColumnSlot : IEquatable<ColumnSlot>
{
    private readonly string _text;

    private ColumnSlot(string text, bool isParameter)
    {
        _text = text;
        IsParameter = isParameter;
    }

    /// <summary>The column name itself.</summary>
    public static ColumnSlot Literal(string column) => new(Interop.NotNull(column, nameof(column)), false);

    /// <summary>
    /// A named parameter, resolved per evaluation from the same binding environment an expression
    /// parameter reads. Unbound at evaluation is refused by name, never defaulted.
    /// </summary>
    public static ColumnSlot Parameter(string name) => new(Interop.NotNull(name, nameof(name)), true);

    /// <summary>Is this slot a parameter rather than a literal?</summary>
    public bool IsParameter { get; }

    /// <summary>The column name, when this slot holds one; <c>null</c> otherwise.</summary>
    public string? Column => IsParameter ? null : _text;

    /// <summary>The parameter name, when this slot holds one; <c>null</c> otherwise.</summary>
    public string? ParameterName => IsParameter ? _text : null;

    /// <summary>Read this slot by case. Total: exactly one branch runs.</summary>
    public T Match<T>(Func<string, T> onLiteral, Func<string, T> onParameter) =>
        IsParameter
            ? Interop.NotNull(onParameter, nameof(onParameter))(_text)
            : Interop.NotNull(onLiteral, nameof(onLiteral))(_text);

    internal Slot<string> ToCoreSlot() =>
        IsParameter ? Slot<string>.NewParam(_text) : Slot<string>.NewLit(_text);

    internal static ColumnSlot FromCoreSlot(Slot<string> core) =>
        core.Tag switch
        {
            Slot<string>.Tags.Lit => Literal(((Slot<string>.Lit)core).Item),
            Slot<string>.Tags.Param => Parameter(((Slot<string>.Param)core).name),
            _ => throw Interop.UnknownCase("Slot", core.Tag),
        };

    /// <inheritdoc />
    public bool Equals(ColumnSlot? other) =>
        other is not null && IsParameter == other.IsParameter && _text == other._text;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ColumnSlot);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsParameter, _text);

    /// <inheritdoc />
    public override string ToString() => IsParameter ? "$" + _text : _text;
}

/// <summary>
/// One ordering key of a <c>Sort</c> step: a column SLOT plus a direction. Distinct from
/// <see cref="SortKey" />, which is the plain key a window's frame ordering takes — a window's
/// order was not widened, so a type that admitted a parameter there would have to refuse it at
/// evaluation instead of at the keyboard.
/// </summary>
public sealed class SortSlot : IEquatable<SortSlot>
{
    /// <summary>Create an ordering key over a column slot.</summary>
    public SortSlot(ColumnSlot column, SortOrder order)
    {
        Column = Interop.NotNull(column, nameof(column));
        Order = order;
    }

    /// <summary>Create an ordering key over a literal column — the everyday form.</summary>
    public SortSlot(string column, SortOrder order)
        : this(ColumnSlot.Literal(column), order) { }

    /// <summary>The column ordered on.</summary>
    public ColumnSlot Column { get; }

    /// <summary>The direction.</summary>
    public SortOrder Order { get; }

    /// <inheritdoc />
    public bool Equals(SortSlot? other) => other is not null && Column.Equals(other.Column) && Order == other.Order;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SortSlot);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Column, Order);

    /// <inheritdoc />
    public override string ToString() => $"{Column} {Order}";
}
