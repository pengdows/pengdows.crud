namespace testbed;

/// <summary>
/// Shared helpers so ITestContainer implementations can (a) opt into Testcontainers.NET's
/// experimental WithReuse feature, letting a second `dotnet test` process (e.g. net10.0, when
/// net8.0 already spun one up for the same multi-targeted run) attach to an already-running
/// container instead of pulling/booting its own, and (b) do that safely by never operating on a
/// fixed, shared database/table namespace: every container instance gets its own randomly named
/// database, created at startup and dropped at teardown, so two processes sharing one physical
/// container never see each other's tables even if they run concurrently.
/// </summary>
internal static class TestContainerReuse
{
    /// <summary>
    /// Opt-in flag for actually enabling WithReuse on container builders. Off by default: reuse
    /// is an experimental Testcontainers feature (doesn't account for every builder option when
    /// hashing) and disables Testcontainers' own Ryuk-based cleanup, so it's only worth the risk
    /// when a multi-targeted test run is deliberately trying to share containers across processes.
    /// </summary>
    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("TESTBED_REUSE_CONTAINERS"), "true",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A short, SQL-identifier-safe, per-instance random suffix. Applied unconditionally (not
    /// just when <see cref="Enabled"/>) so every test run — reused container or not — gets its
    /// own database and never collides with a fixed literal name like "testdb".
    /// </summary>
    public static string NewSuffix() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>
    /// Serializes the "does a matching reusable container already exist, else create one" check
    /// across processes. Confirmed live: a multi-targeted `dotnet test` invocation launches its
    /// net8.0 and net10.0 testhost processes concurrently (both start within the same second),
    /// so without this, two processes that both find no existing container race to create their
    /// own — WithReuse's hash-matching has no cross-process locking of its own, it only recognizes
    /// a container that's already running by the time it looks. Whichever process acquires this
    /// lock first calls the real <c>IContainer.StartAsync()</c> (which does the reuse lookup and,
    /// finding nothing, creates the container); by the time the second process's turn comes, that
    /// container is already up, so its own lookup finds and attaches to it instead.
    /// <paramref name="key"/> must be the same across processes for containers meant to share
    /// (e.g. provider name + image tag) so unrelated container types don't serialize behind each
    /// other for no reason.
    /// </summary>
    public static async Task<IDisposable> AcquireStartupLockAsync(string key)
    {
        var safeName = string.Concat(key.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var path = Path.Combine(Path.GetTempPath(), $"pengdows-testbed-reuse-{safeName}.lock");

        while (true)
        {
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new FileLockHandle(stream);
            }
            catch (IOException)
            {
                await Task.Delay(200);
            }
        }
    }

    private sealed class FileLockHandle : IDisposable
    {
        private readonly FileStream _stream;
        public FileLockHandle(FileStream stream) => _stream = stream;
        public void Dispose() => _stream.Dispose();
    }
}
