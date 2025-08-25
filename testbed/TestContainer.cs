#region

using System.Data.Common;
using AdoNetCore.AseClient;
using DotNet.Testcontainers.Containers;
using FirebirdSql.Data.FirebirdClient;
using Oracle.ManagedDataAccess.Client;
using pengdows.crud;

#endregion

namespace testbed;

public abstract class TestContainer : ITestContainer
{
    private int _disposed;

    public async Task RunTestWithContainerAsync<TTestProvider>(
        IServiceProvider services,
        Func<IDatabaseContext, IServiceProvider, TTestProvider> testProviderFactory)
        where TTestProvider : TestProvider
    {
        await StartAsync();
        var dbContext = await GetDatabaseContextAsync(services);
        var testProvider = testProviderFactory(dbContext, services);

        Console.WriteLine($"Running test with container: {GetType().Name}");
        await testProvider.RunTest();
        Console.WriteLine(
            $"Test finished: MaxConnections={dbContext.MaxNumberOfConnections} CurrentOpenConnection={dbContext.NumberOfOpenConnections}");
    }

    public abstract Task StartAsync();
    public abstract Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services);

    protected async Task WaitForDbToStart(DbProviderFactory instance, string connectionString, IContainer container,
        int numberOfSecondsToWait = 60)
    {
        var csb = instance.CreateConnectionStringBuilder();
        csb.ConnectionString = connectionString;

        var timeout = TimeSpan.FromSeconds(numberOfSecondsToWait);
        var startTime = DateTime.UtcNow;

        var lastError = String.Empty;
        while (DateTime.UtcNow - startTime < timeout)
        {
            await using var connection = instance.CreateConnection();
            try
            {
                connection.ConnectionString = csb.ConnectionString;

                await connection.OpenAsync();
                await connection.CloseAsync();
                return;
            }
            catch (OracleException ex)
            {
                Console.WriteLine(ex);
                await Task.Delay(1000);
            }
            catch (FbException ex) when (ex.Message.Contains("I/O error"))
            {
                try
                {
                    if (csb is not FbConnectionStringBuilder orig)
                    {
                        throw new InvalidOperationException("Connection string builder is not valid.");
                    }

                    var db = orig.Database;
                    if (string.IsNullOrWhiteSpace(db))
                    {
                        throw new InvalidOperationException("Database path is not specified.");
                    }

                    var csbTemp = new FbConnectionStringBuilder
                    {
                        DataSource = orig.DataSource,
                        Port = orig.Port,
                        UserID = orig.UserID,
                        Password = orig.Password,
                        Charset = orig.Charset,
                        Pooling = false
                    };

                    await using var createConn = instance.CreateConnection();
                    createConn.ConnectionString = csbTemp.ConnectionString;

                    await using var createCmd = createConn.CreateCommand();
                    createCmd.CommandText = $"CREATE DATABASE '{db}';";

                    await createConn.OpenAsync();
                    await createCmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex1)
                {
                    Console.WriteLine(ex1);
                }
            }
            catch (AseException aseException)
            {
                Console.WriteLine(aseException);
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                var currentError = ex.Message;
                if (currentError != lastError)
                {
                    Console.WriteLine(currentError);
                }

                lastError = currentError;
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException($"Could not connect after {numberOfSecondsToWait}s.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask DisposeAsyncCore()
    {
        // Override this in derived test container classes if they hold disposable resources
        return ValueTask.CompletedTask;
    }
}