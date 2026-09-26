// ============================================================================
//  The vacuity guard. A round-trip law that held on a sample which never contained
//  `Transform.Pivot` says nothing at all about `Transform.Pivot` — and it reports
//  the same green either way. This walks the F# values the sample actually
//  produced, records which case of which union each one used, and compares that
//  against the case list read off the F# TYPE.
//
//  Reading the expected set from `FSharpType.GetUnionCases` rather than from a
//  number written down here is the load-bearing part: a case added to `ColExpr`,
//  `Transform`, `Cell`, `JVal`, `ValueSpace` or `HoleKind` in a later release turns
//  this red, naming the case, on the first gate run after the change — which is the
//  one thing a hand-written facade over a closed union cannot otherwise be told.
//
//  This is the only file in the proof that reflects over F# types. The facade's own
//  promise is about an AUTHORING surface; asking the runtime what cases a union has
//  is not authoring.
// ============================================================================

using System.Reflection;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;

namespace Fuaran.Core.CSharp.Proof;

internal sealed class Coverage
{
    private static readonly FSharpOption<BindingFlags> NoFlags = FSharpOption<BindingFlags>.None;

    private readonly Dictionary<Type, HashSet<string>> _seen = new();

    /// <summary>Record every union case reachable from an F# value.</summary>
    internal void Visit(object? value)
    {
        if (value is null)
        {
            return;
        }

        var t = value.GetType();

        if (t.IsPrimitive || value is string || t.IsEnum)
        {
            return;
        }

        if (FSharpType.IsUnion(t, NoFlags))
        {
            var fields = FSharpValue.GetUnionFields(value, t, NoFlags);
            var info = fields.Item1;
            var declaring = info.DeclaringType;

            if (!_seen.TryGetValue(declaring, out var cases))
            {
                cases = new HashSet<string>(StringComparer.Ordinal);
                _seen[declaring] = cases;
            }

            cases.Add(info.Name);

            foreach (var f in fields.Item2)
            {
                Visit(f);
            }

            return;
        }

        if (FSharpType.IsRecord(t, NoFlags))
        {
            foreach (var f in FSharpValue.GetRecordFields(value, NoFlags))
            {
                Visit(f);
            }

            return;
        }

        if (FSharpType.IsTuple(t))
        {
            foreach (var f in FSharpValue.GetTupleFields(value))
            {
                Visit(f);
            }
        }
    }

    /// <summary>The cases of <paramref name="union" /> the sample never reached.</summary>
    internal IReadOnlyList<string> Missing(Type union)
    {
        var declared = FSharpType.GetUnionCases(union, NoFlags).Select(c => c.Name);
        var reached = _seen.TryGetValue(union, out var cases) ? cases : new HashSet<string>(StringComparer.Ordinal);
        return declared.Where(n => !reached.Contains(n)).ToArray();
    }

    /// <summary>How many cases of <paramref name="union" /> the sample reached.</summary>
    internal int Reached(Type union) => _seen.TryGetValue(union, out var cases) ? cases.Count : 0;

    /// <summary>How many cases <paramref name="union" /> declares.</summary>
    internal static int Declared(Type union) => FSharpType.GetUnionCases(union, NoFlags).Length;
}
