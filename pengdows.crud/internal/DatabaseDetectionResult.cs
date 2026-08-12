using pengdows.crud.enums;

namespace pengdows.crud.@internal;

/// <summary>
/// Records the outcome of a single detection probe (schema lookup, flavor query, etc.).
/// </summary>
internal sealed record DetectionProbeAttempt(string ProbeName, bool Succeeded, string? FailureReason);

/// <summary>
/// The resolved product plus the trail of probes that were tried to get there — evidence
/// that <see cref="DatabaseDetectionService"/>'s bare-enum entry points otherwise discard.
/// </summary>
internal sealed record DatabaseDetectionResult(
    SupportedDatabase ResolvedProduct,
    IReadOnlyList<DetectionProbeAttempt> Attempts);
