namespace pengdows.crud.opentelemetry;

/// <summary>
/// Defines the public contract for the Pengdows metrics observer.
/// </summary>
public interface IPengdowsMetricsObserver : IDisposable
{
    /// <summary>
    /// Starts tracking metrics for the provided context.
    /// Idempotent — tracking the same context twice has no effect.
    /// </summary>
    /// <param name="context">The context to track.</param>
    void Track(IDatabaseContext context);

    /// <summary>
    /// Stops tracking metrics for the provided context, removes it from all
    /// gauges, and unsubscribes from its <c>MetricsUpdated</c> event.
    /// Idempotent — untracking a context that was never tracked (or was already
    /// untracked) is safe and has no effect.
    /// </summary>
    /// <param name="context">The context to stop tracking.</param>
    void Untrack(IDatabaseContext context);
}
