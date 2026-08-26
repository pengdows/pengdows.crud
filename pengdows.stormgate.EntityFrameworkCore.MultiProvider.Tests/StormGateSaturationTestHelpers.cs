namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

internal static class StormGateSaturationTestHelpers
{
    public static TimeoutException? FindStormGateSaturationTimeout(Exception? exception)
    {
        while (exception != null)
        {
            if (exception is TimeoutException { Message: var message } timeout
                && message.Contains("storm gate", StringComparison.OrdinalIgnoreCase))
            {
                return timeout;
            }

            exception = exception.InnerException;
        }

        return null;
    }
}
