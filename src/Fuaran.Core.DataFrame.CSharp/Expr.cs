// ============================================================================
//  Fuaran.Core.DataFrame.CSharp — the scalar expression (Phase 128), a facade over
//  `Fuaran.Core.ColExpr`.
//
//  Twelve cases, twelve factories, one total `Match`. The list-shaped payloads
//  (`Coalesce`, `ApplyFn`, `InList`, the `Case` arms) arrive as ordinary C#
//  sequences and read back as `IReadOnlyList<T>`, so an authoring veneer never
//  names an F# list; the `Case` arm pair is a `CaseArm`, not a tuple, so it never
//  names an F# tuple either.
// ============================================================================

namespace Fuaran.Core.CSharp;

/// <summary>One <c>when / then</c> arm of a <see cref="Expr.Case" /> expression.</summary>
public sealed class CaseArm : IEquatable<CaseArm>
{
    /// <summary>Create an arm.</summary>
    public CaseArm(Expr when, Expr then)
    {
        When = Interop.NotNull(when, nameof(when));
        Then = Interop.NotNull(then, nameof(then));
    }

    /// <summary>The arm's condition.</summary>
    public Expr When { get; }

    /// <summary>The value the arm yields when its condition holds.</summary>
    public Expr Then { get; }

    /// <inheritdoc />
    public bool Equals(CaseArm? other) => other is not null && When.Equals(other.When) && Then.Equals(other.Then);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CaseArm);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(When, Then);

    /// <inheritdoc />
    public override string ToString() => $"when {When} then {Then}";
}

/// <summary>A scalar expression over a row's columns, literals and named parameters.</summary>
public sealed class Expr : IEquatable<Expr>
{
    private readonly ColExpr _core;

    private Expr(ColExpr core) => _core = core;

    // ---- construction ----

    /// <summary>A reference to a column by name.</summary>
    public static Expr Col(string name) => new(ColExpr.NewCol(Interop.NotNull(name, nameof(name))));

    /// <summary>A literal cell.</summary>
    public static Expr Literal(CellValue value) =>
        new(ColExpr.NewLit(Interop.NotNull(value, nameof(value)).ToCore()));

    /// <summary>A named parameter, resolved per-evaluation from the host's binding environment.</summary>
    public static Expr Param(string name) => new(ColExpr.NewParam(Interop.NotNull(name, nameof(name))));

    /// <summary>A binary operation.</summary>
    public static Expr Binary(BinaryOperator op, Expr left, Expr right) =>
        new(
            ColExpr.NewBinary(
                Vocab.ToCore(op),
                Interop.NotNull(left, nameof(left))._core,
                Interop.NotNull(right, nameof(right))._core
            )
        );

    /// <summary>Three-valued logical negation.</summary>
    public static Expr Not(Expr inner) => new(ColExpr.NewNot(Interop.NotNull(inner, nameof(inner))._core));

    /// <summary>The first non-null of its alternatives.</summary>
    public static Expr Coalesce(IEnumerable<Expr> alternatives) =>
        new(ColExpr.NewCoalesce(Interop.List(Interop.Items(alternatives, nameof(alternatives)).Select(e => e._core))));

    /// <summary>The first non-null of its alternatives.</summary>
    public static Expr Coalesce(params Expr[] alternatives) => Coalesce((IEnumerable<Expr>)alternatives);

    /// <summary>An ordered <c>when / then</c> chain with a mandatory else branch.</summary>
    public static Expr Case(IEnumerable<CaseArm> arms, Expr elseExpr) =>
        new(
            ColExpr.NewCase(
                Interop.List(
                    Interop.Items(arms, nameof(arms)).Select(a => Tuple.Create(a.When._core, a.Then._core))
                ),
                Interop.NotNull(elseExpr, nameof(elseExpr))._core
            )
        );

    /// <summary>A cast to a declared scalar type.</summary>
    public static Expr Cast(ColumnKind kind, Expr inner) =>
        new(ColExpr.NewCast(Vocab.ToCore(kind), Interop.NotNull(inner, nameof(inner))._core));

    /// <summary>An application of one of the fixed scalar functions.</summary>
    public static Expr Apply(ScalarFunction fn, IEnumerable<Expr> arguments) =>
        new(
            ColExpr.NewApplyFn(
                Vocab.ToCore(fn),
                Interop.List(Interop.Items(arguments, nameof(arguments)).Select(e => e._core))
            )
        );

    /// <summary>An application of one of the fixed scalar functions.</summary>
    public static Expr Apply(ScalarFunction fn, params Expr[] arguments) =>
        Apply(fn, (IEnumerable<Expr>)arguments);

    /// <summary>Three-valued membership over a literal list of alternatives.</summary>
    public static Expr InList(Expr subject, IEnumerable<Expr> items) =>
        new(
            ColExpr.NewInList(
                Interop.NotNull(subject, nameof(subject))._core,
                Interop.List(Interop.Items(items, nameof(items)).Select(e => e._core))
            )
        );

    /// <summary>Three-valued membership over a literal list of alternatives.</summary>
    public static Expr InList(Expr subject, params Expr[] items) => InList(subject, (IEnumerable<Expr>)items);

    /// <summary>The total presence test — always boolean, never null.</summary>
    public static Expr IsNull(Expr inner) => new(ColExpr.NewIsNull(Interop.NotNull(inner, nameof(inner))._core));

    /// <summary>Membership over a LIST-valued named parameter — the multi-select binding.</summary>
    public static Expr InParam(Expr subject, string name) =>
        new(
            ColExpr.NewInParam(
                Interop.NotNull(subject, nameof(subject))._core,
                Interop.NotNull(name, nameof(name))
            )
        );

    /// <summary>
    /// The current moment at the given grain — a <c>now</c> the pipeline names itself. Resolved
    /// against a clock the caller pins before evaluation; one that reaches evaluation unpinned is
    /// refused rather than read from the host's real clock.
    /// </summary>
    public static Expr Now(ClockGrain grain) => new(ColExpr.NewNow(Vocab.ToCore(grain)));

    // ---- reading ----

    /// <summary>Read this expression by case. Total: exactly one branch runs.</summary>
    public T Match<T>(
        Func<string, T> onCol,
        Func<CellValue, T> onLiteral,
        Func<string, T> onParam,
        Func<BinaryOperator, Expr, Expr, T> onBinary,
        Func<Expr, T> onNot,
        Func<IReadOnlyList<Expr>, T> onCoalesce,
        Func<IReadOnlyList<CaseArm>, Expr, T> onCase,
        Func<ColumnKind, Expr, T> onCast,
        Func<ScalarFunction, IReadOnlyList<Expr>, T> onApply,
        Func<Expr, IReadOnlyList<Expr>, T> onInList,
        Func<Expr, T> onIsNull,
        Func<Expr, string, T> onInParam,
        Func<ClockGrain, T> onNow
    )
    {
        switch (_core.Tag)
        {
            case ColExpr.Tags.Col:
                return onCol(((ColExpr.Col)_core).Item);
            case ColExpr.Tags.Lit:
                return onLiteral(CellValue.FromCore(((ColExpr.Lit)_core).Item));
            case ColExpr.Tags.Param:
                return onParam(((ColExpr.Param)_core).name);
            case ColExpr.Tags.Binary:
            {
                var c = (ColExpr.Binary)_core;
                return onBinary(Vocab.BinaryOperatorOf(c.Item1), new Expr(c.Item2), new Expr(c.Item3));
            }
            case ColExpr.Tags.Not:
                return onNot(new Expr(((ColExpr.Not)_core).Item));
            case ColExpr.Tags.Coalesce:
                return onCoalesce(Wrap(((ColExpr.Coalesce)_core).Item));
            case ColExpr.Tags.Case:
            {
                var c = (ColExpr.Case)_core;
                var arms = Interop
                    .Read(c.cases)
                    .Select(t => new CaseArm(new Expr(t.Item1), new Expr(t.Item2)))
                    .ToArray();
                return onCase(arms, new Expr(c.elseExpr));
            }
            case ColExpr.Tags.Cast:
            {
                var c = (ColExpr.Cast)_core;
                return onCast(Vocab.ColumnKindOf(c.Item1), new Expr(c.Item2));
            }
            case ColExpr.Tags.ApplyFn:
            {
                var c = (ColExpr.ApplyFn)_core;
                return onApply(Vocab.ScalarFunctionOf(c.Item1), Wrap(c.Item2));
            }
            case ColExpr.Tags.InList:
            {
                var c = (ColExpr.InList)_core;
                return onInList(new Expr(c.Item1), Wrap(c.Item2));
            }
            case ColExpr.Tags.IsNull:
                return onIsNull(new Expr(((ColExpr.IsNull)_core).Item));
            case ColExpr.Tags.InParam:
            {
                var c = (ColExpr.InParam)_core;
                return onInParam(new Expr(c.Item1), c.name);
            }
            case ColExpr.Tags.Now:
                return onNow(Vocab.ClockGrainOf(((ColExpr.Now)_core).grain));
            default:
                throw Interop.UnknownCase(nameof(ColExpr), _core.Tag);
        }
    }

    /// <summary>Read this expression by case for effect. Total: exactly one branch runs.</summary>
    public void Switch(
        Action<string> onCol,
        Action<CellValue> onLiteral,
        Action<string> onParam,
        Action<BinaryOperator, Expr, Expr> onBinary,
        Action<Expr> onNot,
        Action<IReadOnlyList<Expr>> onCoalesce,
        Action<IReadOnlyList<CaseArm>, Expr> onCase,
        Action<ColumnKind, Expr> onCast,
        Action<ScalarFunction, IReadOnlyList<Expr>> onApply,
        Action<Expr, IReadOnlyList<Expr>> onInList,
        Action<Expr> onIsNull,
        Action<Expr, string> onInParam,
        Action<ClockGrain> onNow
    ) =>
        Match(
            n =>
            {
                onCol(n);
                return true;
            },
            v =>
            {
                onLiteral(v);
                return true;
            },
            n =>
            {
                onParam(n);
                return true;
            },
            (op, l, r) =>
            {
                onBinary(op, l, r);
                return true;
            },
            e =>
            {
                onNot(e);
                return true;
            },
            xs =>
            {
                onCoalesce(xs);
                return true;
            },
            (arms, els) =>
            {
                onCase(arms, els);
                return true;
            },
            (k, e) =>
            {
                onCast(k, e);
                return true;
            },
            (fn, xs) =>
            {
                onApply(fn, xs);
                return true;
            },
            (s, xs) =>
            {
                onInList(s, xs);
                return true;
            },
            e =>
            {
                onIsNull(e);
                return true;
            },
            (s, n) =>
            {
                onInParam(s, n);
                return true;
            },
            g =>
            {
                onNow(g);
                return true;
            }
        );

    // ---- the bridge ----

    /// <summary>The bridge OUT: the wrapped F# value.</summary>
    public ColExpr ToCore() => _core;

    /// <summary>The bridge IN: wrap an F# value.</summary>
    public static Expr FromCore(ColExpr core) => new(Interop.NotNull(core, nameof(core)));

    internal static Expr Wrap(ColExpr core) => new(core);

    internal ColExpr Core => _core;

    private static IReadOnlyList<Expr> Wrap(Microsoft.FSharp.Collections.FSharpList<ColExpr> xs) =>
        Interop.Read(xs).Select(e => new Expr(e)).ToArray();

    /// <inheritdoc />
    public bool Equals(Expr? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Expr);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _core.ToString();
}
