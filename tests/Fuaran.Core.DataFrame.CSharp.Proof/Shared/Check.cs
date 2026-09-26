// ============================================================================
//  The proof's assertion collector. Every check runs; the run reports every
//  failure it found rather than stopping at the first, because "which cases does
//  the facade get wrong" is the useful answer and "the first one" is not.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal sealed class Check
{
    private readonly List<string> _failures = new();
    private int _passed;

    internal int Passed => _passed;

    internal IReadOnlyList<string> Failures => _failures;

    internal void That(bool condition, string what)
    {
        if (condition)
        {
            _passed++;
        }
        else
        {
            _failures.Add(what);
        }
    }

    internal void Equal<T>(T expected, T actual, string what)
        where T : class => That(expected.Equals(actual), $"{what}: expected `{expected}`, got `{actual}`");

    internal void Fail(string what) => _failures.Add(what);
}
