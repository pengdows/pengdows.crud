namespace testbed;

public interface IAsyncTestProvider
{
    /// <summary>
    /// Runs the test logic for this provider.
    /// </summary>
    Task RunTest();
}
