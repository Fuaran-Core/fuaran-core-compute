// ============================================================================
//  The round-trip law over the dataframe half of the facade (Phase 128; its own
//  project since Phase 257): facade -> F# value -> facade is the identity, in the
//  useful form stated in tests/Fuaran.Core.CSharp.Proof/RoundTrip.cs —
//
//      ToCore( Rebuild( FromCore( x.ToCore() ) ) )  =  x.ToCore()
//
//  where `Rebuild` reads every case through `Match` and re-constructs it through
//  the public factories, touching no F# type. Its falsifier is concrete and was
//  exercised: swapping the two operands in the `Binary` rebuild, or dropping the
//  else-branch of `Case`, reddens it.
// ============================================================================

namespace Fuaran.Core.CSharp.Proof;

internal static class DataFrameRoundTrip
{
    internal const int Iterations = 600;

    internal static void Run(Check check, Coverage coverage)
    {
        var gen = new Gen(seed: 20260912);

        for (var i = 0; i < Iterations; i++)
        {
            var expr = gen.Expression(i, depth: 3);
            var core = expr.ToCore();
            coverage.Visit(core);
            check.That(
                Rebuild.Expression(Expr.FromCore(core)).ToCore().Equals(core),
                $"expression round trip failed at iteration {i}: `{core}`"
            );

            var pipeline = gen.PipelineOf(i);
            var pipelineCore = pipeline.ToCore();
            coverage.Visit(pipelineCore);
            check.That(
                Rebuild.PipelineOf(Pipeline.FromCore(pipelineCore)).ToCore().Equals(pipelineCore),
                $"pipeline round trip failed at iteration {i}: `{pipelineCore}`"
            );
        }
    }

    /// <summary>The unions the sample must reach EVERY case of for the law above to mean anything.</summary>
    internal static IReadOnlyList<Type> CoveredUnions { get; } =
        new[]
        {
            typeof(ColExpr),
            typeof(Transform),
            // Phase 125 — the two `Slot` instantiations the algebra uses, and the grain a `Now`
            // carries. Listed for the same reason every other union here is: a sample that never
            // built a `Slot.Param` would let the round-trip law report green about a case it never
            // saw, and the whole point of the slot is the param.
            typeof(Slot<string>),
            typeof(Slot<int>),
            typeof(NowGrain),
            typeof(BinOp),
            typeof(ScalarFn),
            typeof(JoinKind),
            typeof(SortDir),
            typeof(WindowFn),
        };
}
