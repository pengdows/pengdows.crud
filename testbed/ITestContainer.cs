#region

using pengdows.crud;

#endregion

namespace testbed;

public interface ITestContainer : IAsyncDisposable
{
    /// <summary>
    /// Starts the container and waits for it to be ready for connections.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Builds a database context configured to connect to this container.
    /// </summary>
    /// <param name="services">Service provider for resolving dependencies.</param>
    /// <returns>A configured database context.</returns>
    Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services);

    /// <summary>
    /// Runs a test provider using a context created from this container.
    /// </summary>
    /// <typeparam name="TTestProvider">Test provider type.</typeparam>
    /// <param name="services">Service provider for resolving dependencies.</param>
    /// <param name="testProviderFactory">Factory used to build the test provider.</param>
    Task RunTestWithContainerAsync<TTestProvider>(
        IServiceProvider services,
        Func<IDatabaseContext, IServiceProvider, TTestProvider> testProviderFactory)
        where TTestProvider : TestProvider;
}
