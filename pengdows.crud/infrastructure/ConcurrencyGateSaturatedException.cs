namespace pengdows.crud.infrastructure;

internal sealed class ConcurrencyGateSaturatedException : Exception
{
    internal ConcurrencyGateSaturatedException(int maxQueueDepth)
        : base($"The concurrency gate queue is full at {maxQueueDepth} waiters.")
    {
    }
}
