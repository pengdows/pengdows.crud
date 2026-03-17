namespace pengdows.crud.exceptions;

/// <summary>
/// Thrown when pre-built SQL container template initialization fails for a dialect.
/// Caught by <see cref="TableGateway{TEntity,TRowID}"/> retrieve paths to trigger
/// a fallback to the direct <c>BuildRetrieve</c> path.
/// </summary>
internal sealed class TemplateInitializationException : Exception
{
    public TemplateInitializationException(string message) : base(message)
    {
    }

    public TemplateInitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
