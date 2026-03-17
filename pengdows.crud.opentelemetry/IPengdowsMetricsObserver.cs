namespace pengdows.crud.opentelemetry;

/// <summary>
/// Defines the public contract for the Pengdows metrics observer.
/// </summary>
public interface IPengdowsMetricsObserver : IDisposable
{
    /// <summary>
    /// Starts tracking metrics for the provided context.
    /// </summary>
    /// <param name="context">The context to track.</param>
    void Track(IDatabaseContext context);
}
