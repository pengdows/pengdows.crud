#region

using pengdows.crud;

#endregion

namespace testbed;

public interface ITestContainer : IAsyncDisposable
{
    Task StartAsync();
    Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services);

    Task RunTestWithContainerAsync<TTestProvider>(
        IServiceProvider services,
        Func<IDatabaseContext, IServiceProvider, TTestProvider> testProviderFactory)
        where TTestProvider : TestProvider;
}