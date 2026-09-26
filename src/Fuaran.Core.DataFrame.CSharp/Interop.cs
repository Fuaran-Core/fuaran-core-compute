// ============================================================================
//  Fuaran.Core.DataFrame.CSharp — the internal F# interop helpers (Phase 128;
//  this assembly's own copy since Phase 257, DECISIONS.md D68).
//
//  Everything in this file is INTERNAL on purpose. The facade's whole promise is
//  that a caller never names an F# option, list, function or union case, so the
//  places that do are confined here and to the `ToCore` / `FromCore` bridge
//  members of the public types — which is exactly the invariant the proof
//  project's surface check asserts over the built assembly.
// ============================================================================

using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Fuaran.Core.CSharp;

internal static class Interop
{
    /// <summary>A C# sequence as an F# list, in order.</summary>
    internal static FSharpList<T> List<T>(IEnumerable<T> xs) => ListModule.OfSeq(xs);

    /// <summary>An F# list as a C# read-only list, in order.</summary>
    internal static IReadOnlyList<T> Read<T>(FSharpList<T> xs) => xs.ToArray();

    /// <summary>A nullable reference as an F# option — <c>null</c> is <c>None</c>.</summary>
    internal static FSharpOption<T> Some<T>(T? v)
        where T : class => v is null ? FSharpOption<T>.None : FSharpOption<T>.Some(v);

    /// <summary>An F# option as a nullable reference — <c>None</c> is <c>null</c>.</summary>
    internal static T? Opt<T>(FSharpOption<T> o)
        where T : class => o is null ? null : o.Value;

    /// <summary>Argument guard — the facade refuses a null where the F# value cannot carry one.</summary>
    internal static T NotNull<T>(T? v, string name)
        where T : class => v ?? throw new ArgumentNullException(name);

    /// <summary>Argument guard for a sequence, materialised once so a lazy caller cannot be enumerated twice.</summary>
    internal static IReadOnlyList<T> Items<T>(IEnumerable<T>? xs, string name)
        where T : class
    {
        if (xs is null)
        {
            throw new ArgumentNullException(name);
        }

        var arr = xs.ToArray();
        for (var i = 0; i < arr.Length; i++)
        {
            if (arr[i] is null)
            {
                throw new ArgumentException($"{name}[{i}] is null", name);
            }
        }

        return arr;
    }

    /// <summary>The refusal a total <c>Match</c> raises when the wrapped union grew a case the facade predates.</summary>
    internal static InvalidOperationException UnknownCase(string union, int tag) =>
        new(
            $"Fuaran.Core.DataFrame.CSharp does not model case tag {tag} of {union}. "
                + "The wrapped F# union has grown a case this facade version predates — upgrade the package."
        );
}
