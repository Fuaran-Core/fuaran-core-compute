// ============================================================================
//  Fuaran.Core.DataFrame.CSharp.Proof (Phase 257) — the dataframe half of the C#
//  facade proves its own coverage, over its own assembly, with the same four legs
//  as tests/Fuaran.Core.CSharp.Proof:
//
//   1. AUTHORING  — a C# consumer builds and reads a pipeline by hand, and what it
//                   builds goes through Core's own codec and comes back.
//   2. ROUND TRIP — read-then-rebuild is the identity over a generated sample.
//   3. COVERAGE   — that sample reached every case of every dataframe union,
//                   read off the F# TYPE rather than from a written-down number.
//   4. SURFACE    — no public member of Fuaran.Core.DataFrame.CSharp mentions an
//                   F# type except the two declared bridge names, with decoys
//                   proving the check can go red.
//
//  Exit 0 on green, 1 on any failure.
// ============================================================================

using Fuaran.Core.CSharp.Proof;

var check = new Check();
var coverage = new Coverage();

Console.WriteLine("Fuaran.Core.DataFrame.CSharp — facade conformance report");
Console.WriteLine(new string('-', 62));

DataFrameAuthoring.Run(check);
Console.WriteLine($"  authoring      : a C# consumer builds, reads and round-trips a pipeline");

DataFrameRoundTrip.Run(check, coverage);
Console.WriteLine($"  round trip     : {DataFrameRoundTrip.Iterations} iterations x 2 families");

var shortfalls = new List<string>();

foreach (var union in DataFrameRoundTrip.CoveredUnions)
{
    var missing = coverage.Missing(union);

    if (missing.Count > 0)
    {
        shortfalls.Add($"{union.Name}: {string.Join(", ", missing)}");
    }
}

check.That(
    shortfalls.Count == 0,
    "the sample never reached these cases, so the round-trip law says nothing about them: "
        + string.Join(" | ", shortfalls)
);

var reached = DataFrameRoundTrip.CoveredUnions.Sum(coverage.Reached);
var declared = DataFrameRoundTrip.CoveredUnions.Sum(Coverage.Declared);
Console.WriteLine(
    $"  coverage       : {reached}/{declared} cases across {DataFrameRoundTrip.CoveredUnions.Count} unions"
);

Surface.ProveItGoesRed(check);
var scan = Surface.Scan(typeof(Fuaran.Core.CSharp.Expr).Assembly.GetExportedTypes());

foreach (var violation in scan.Violations)
{
    check.Fail($"F# type on a non-bridge public member — {violation}");
}

check.That(scan.Bridge.Count > 0, "the facade declares no bridge at all, so the surface rule is vacuous");
Console.WriteLine($"  surface        : 0 leaks, {scan.Bridge.Count} declared bridge members");

foreach (var bridge in scan.Bridge.OrderBy(b => b, StringComparer.Ordinal))
{
    Console.WriteLine($"                   bridge: {bridge}");
}

Console.WriteLine(new string('-', 62));

if (check.Failures.Count == 0)
{
    Console.WriteLine($"PASS — {check.Passed} checks green");
    return 0;
}

foreach (var failure in check.Failures)
{
    Console.WriteLine($"FAIL — {failure}");
}

Console.WriteLine($"FAILED — {check.Failures.Count} of {check.Failures.Count + check.Passed} checks red");
return 1;
