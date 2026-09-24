// =============================================================================
// FILE: IMetricsCollectorAccessor.cs
// PURPOSE: Interface for accessing internal MetricsCollector instance.
//
// AI SUMMARY:
// - Internal interface for components needing metrics collection access.
// - Members: MetricsCollector, ReadMetricsCollector, WriteMetricsCollector,
//   GetMetricsCollector(ExecutionType) — all nullable (may not be configured).
// - Implemented by DatabaseContext and TransactionContext to expose metrics to internal components.
// - Allows SqlContainer, connection strategies to record metrics.
// - Nullable pattern: metrics collection is optional, may be disabled.
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.@internal;

internal interface IMetricsCollectorAccessor
{
    MetricsCollector? MetricsCollector { get; }
    MetricsCollector? GetMetricsCollector(ExecutionType executionType);
    MetricsCollector? ReadMetricsCollector { get; }
    MetricsCollector? WriteMetricsCollector { get; }
}