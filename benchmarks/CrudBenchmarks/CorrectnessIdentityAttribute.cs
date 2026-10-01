namespace CrudBenchmarks;

/// <summary>
/// Declares the (Framework, Scenario) identity a [Benchmark] method's correctness tracking is
/// recorded under, so BenchmarkCorrectnessArtifacts lookups don't depend on parsing the method
/// name's suffix. Added after the third instance of the same silent-failure class in one
/// session (2026-08-27): CorrectnessColumn's old "_Pengdows"/"_Dapper"/"_EntityFramework"
/// suffix matching requires every benchmark method to spell its identity as a name suffix, and
/// PostgreSqlConnectionGovernanceBenchmarks' methods (Dapper_Uncontrolled, Dapper_StormGate,
/// EF_Uncontrolled) don't — "StormGate" and "EF_Uncontrolled" match none of the three suffixes,
/// so the column silently rendered "-" for a class that WAS tracking correctness data, and the
/// real counts (1,950 / 2,158 failures) were only found by reading raw fragment JSON directly.
/// A class that opts into this attribute gets exact, non-inferred matching; CorrectnessColumn
/// falls back to suffix parsing only for classes not yet retrofitted, and renders "?" instead of
/// "-" when neither resolves but a correctness-fragments file for the class exists at all — so a
/// naming mismatch becomes a visible warning instead of a blank cell that reads as "no data."
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class CorrectnessIdentityAttribute : Attribute
{
    public string Framework { get; }
    public string Scenario { get; }

    public CorrectnessIdentityAttribute(string framework, string scenario)
    {
        Framework = framework;
        Scenario = scenario;
    }
}
