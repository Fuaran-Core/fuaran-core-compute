// ============================================================================
//  The surface check — "no `FSharpOption` or `FSharpList` on the public surface,
//  analyzer-checked", stated as a rule a machine can apply and a decoy can falsify.
//
//  It is written here rather than imported because the available off-the-shelf
//  analyzers answer a different question (nullability, public-API diffs); this one
//  is about which ASSEMBLY a type on the surface came from, and about one
//  deliberate exemption.
//
//  THE RULE, in two parts:
//
//   A. No member of a public facade type may mention an F# type — anything from
//      `FSharp.Core` (option, list, function, unit, map, result), anything from a
//      `Fuaran.Core.*` F# assembly (the wrapped unions and records themselves), or
//      a positional `Tuple` / `ValueTuple`, at any depth of a generic argument.
//
//   B. EXCEPT on a member named exactly `ToCore` or `FromCore`. That pair is the
//      declared bridge, and a facade with no bridge is not a facade — it is a
//      re-implementation. The census of bridge members is PRINTED, so how wide the
//      exemption actually is stays visible rather than merely permitted.
//
//  Part B is why this could not just be "the assembly references nothing F#": the
//  facade must hand a `ColExpr` to Core somewhere. What the rule buys is that the
//  somewhere is exactly two member names, and every authoring and reading member is
//  clean.
//
//  The decoys below are the go-red proof. A check that has never been observed
//  failing is a description, not a check.
// ============================================================================

using System.Reflection;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Fuaran.Core.CSharp.Proof;

internal sealed class SurfaceScan
{
    internal List<string> Violations { get; } = new();

    internal List<string> Bridge { get; } = new();
}

internal static class Surface
{
    private const BindingFlags Members =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static bool IsBridge(string name) => name is "ToCore" or "FromCore";

    private static bool IsOffending(Type t)
    {
        if (t.IsByRef || t.IsPointer || t.IsArray)
        {
            return IsOffending(t.GetElementType()!);
        }

        if (t.IsGenericType)
        {
            var definition = t.GetGenericTypeDefinition();
            var name = definition.FullName ?? string.Empty;

            if (name.StartsWith("System.Tuple`", StringComparison.Ordinal) || name.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
            {
                return true;
            }

            if (Offends(definition) || t.GetGenericArguments().Any(IsOffending))
            {
                return true;
            }

            return false;
        }

        return Offends(t);
    }

    private static bool Offends(Type t)
    {
        var assembly = t.Assembly.GetName().Name ?? string.Empty;

        if (assembly == "FSharp.Core")
        {
            return true;
        }

        return assembly.StartsWith("Fuaran.Core.", StringComparison.Ordinal) && !FacadeAssemblies.Contains(assembly);
    }

    // The C# facade assemblies — the only `Fuaran.Core.*` assemblies whose types are not F#. Two since
    // Phase 257, when the dataframe half moved to its own assembly; each proof scans its own.
    private static readonly HashSet<string> FacadeAssemblies = new(StringComparer.Ordinal)
    {
        "Fuaran.Core.CSharp",
        "Fuaran.Core.DataFrame.CSharp",
    };

    private static IEnumerable<(Type Type, string Where)> SignatureTypes(MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method:
                yield return (method.ReturnType, "return");
                foreach (var p in method.GetParameters())
                {
                    yield return (p.ParameterType, $"parameter `{p.Name}`");
                }

                break;
            case ConstructorInfo ctor:
                foreach (var p in ctor.GetParameters())
                {
                    yield return (p.ParameterType, $"parameter `{p.Name}`");
                }

                break;
            case PropertyInfo property:
                yield return (property.PropertyType, "type");
                foreach (var p in property.GetIndexParameters())
                {
                    yield return (p.ParameterType, $"index `{p.Name}`");
                }

                break;
            case FieldInfo field:
                yield return (field.FieldType, "type");
                break;
            case EventInfo ev when ev.EventHandlerType is not null:
                yield return (ev.EventHandlerType, "handler");
                break;
        }
    }

    private static bool Skip(MemberInfo member) =>
        member is MethodInfo { IsSpecialName: true } m
        && (
            m.Name.StartsWith("get_", StringComparison.Ordinal)
            || m.Name.StartsWith("set_", StringComparison.Ordinal)
            || m.Name.StartsWith("add_", StringComparison.Ordinal)
            || m.Name.StartsWith("remove_", StringComparison.Ordinal)
        );

    internal static SurfaceScan Scan(IEnumerable<Type> types)
    {
        var scan = new SurfaceScan();

        foreach (var t in types)
        {
            foreach (var member in t.GetMembers(Members))
            {
                if (Skip(member) || member is Type)
                {
                    continue;
                }

                var offenders = SignatureTypes(member)
                    .Where(s => IsOffending(s.Type))
                    .Select(s => $"{s.Where} is `{s.Type.Name}`")
                    .ToArray();

                if (offenders.Length == 0)
                {
                    continue;
                }

                var where = $"{t.Name}.{member.Name}";

                if (IsBridge(member.Name))
                {
                    scan.Bridge.Add($"{where} ({string.Join(", ", offenders)})");
                }
                else
                {
                    scan.Violations.Add($"{where}: {string.Join(", ", offenders)}");
                }
            }
        }

        return scan;
    }

    // ---- the go-red proof ----

    /// <summary>A member that leaks an F# option and list, not on the bridge — the rule must flag it.</summary>
    public sealed class LeakDecoy
    {
        public FSharpOption<int> Leaked(FSharpList<string> xs) => FSharpOption<int>.None;
    }

    /// <summary>The same leak, ON the bridge — the exemption must admit it, and the census must show it.</summary>
    public sealed class BridgeDecoy
    {
        public FSharpList<string> ToCore() => ListModule.Empty<string>();
    }

    /// <summary>A positional tuple at an authoring site — flagged, because that is what the facade replaced.</summary>
    public sealed class TupleDecoy
    {
        public Tuple<string, int> Positional() => Tuple.Create("a", 1);
    }

    /// <summary>Nothing to find — the rule must NOT fire, or the other three prove nothing.</summary>
    public sealed class CleanDecoy
    {
        public IReadOnlyList<string> Fine(int n) => System.Array.Empty<string>();
    }

    internal static void ProveItGoesRed(Check check)
    {
        var leak = Scan(new[] { typeof(LeakDecoy) });
        check.That(leak.Violations.Count == 1, "surface check should flag a non-bridge F# leak, and did not");

        var bridge = Scan(new[] { typeof(BridgeDecoy) });
        check.That(bridge.Violations.Count == 0, "surface check should exempt a bridge member, and did not");
        check.That(bridge.Bridge.Count == 1, "surface check should census a bridge member, and did not");

        var tuple = Scan(new[] { typeof(TupleDecoy) });
        check.That(tuple.Violations.Count == 1, "surface check should flag a positional tuple, and did not");

        var clean = Scan(new[] { typeof(CleanDecoy) });
        check.That(
            clean.Violations.Count == 0 && clean.Bridge.Count == 0,
            "surface check fired on a clean type — it is measuring the wrong thing"
        );
    }
}
