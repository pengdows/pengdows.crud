namespace pengdows.crud.fakeDb;

public sealed partial class fakeDbFactory
{
    public void SetGlobalFailureMode(ConnectionFailureMode mode, int? failAfterCount = null,
        Exception? customException = null)
    {
        _failureMode = mode;
        _customException = customException;
        _failAfterCount = failAfterCount;

        // Ensure new connections reflect updated failure mode
        _connections.Clear();

        // When switching to FailAfterCount reset shared counters and skip first open if appropriate
        if (mode == ConnectionFailureMode.FailAfterCount)
        {
            _sharedOpenCount = 0;
            _skipFirstOpen = true;
        }
        else if (mode == ConnectionFailureMode.FailOnOpen || mode == ConnectionFailureMode.Broken)
        {
            _skipFirstOpen = true;
        }
        else
        {
            _skipFirstOpen = false;
        }
    }
}