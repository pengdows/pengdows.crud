// =============================================================================
// FILE: IMetricsCollectorAccessor.cs
// PURPOSE: Interface for accessing internal MetricsCollector instance.
//
// AI SUMMARY:
// - Internal interface for components needing metrics collection access.
// - Single property: MetricsCollector? (nullable, may not be configured).
// - Implemented by DatabaseContext to expose metrics to internal components.
// - Allows SqlContainer, connection strategies to record metrics.
// - Nullable pattern: metrics collection is optional, may be disabled.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.@internal;

internal interface IMetricsCollectorAccessor
{
    MetricsCollector? MetricsCollector { get; }
    MetricsCollector? GetMetricsCollector(ExecutionType executionType);
    MetricsCollector? ReadMetricsCollector { get; }
    MetricsCollector? WriteMetricsCollector { get; }
}
