using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextAsyncCreationTests
{
    [Fact]
    public async Task CreateAsync_WithConfiguration_ReturnsInitializedContextAndDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetScalarResult(42);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnableMetrics = true
        };

        await using var context = await DatabaseContext.CreateAsync(config, factory);

        Assert.NotNull(context);
        Assert.Equal("PostgreSQL", context.Name);
        Assert.NotNull(context.Dialect);
        Assert.Equal(SupportedDatabase.PostgreSql, context.Dialect.DatabaseType);
        Assert.Equal(DbMode.Standard, context.ConnectionMode);
        Assert.NotNull(context.Metrics);

        using var sc = context.CreateSqlContainer("SELECT 42");
        var result = await sc.ExecuteScalarRequiredAsync<int>();
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task CreateAsync_WithDataSource_ReturnsInitializedContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SupportsNativeDataSource = true;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        var dataSource = (FakeDbDataSource)factory.CreateDataSource(config.ConnectionString);

        await using var context = await DatabaseContext.CreateAsync(config, dataSource, factory);

        Assert.NotNull(context);
        Assert.Equal("PostgreSQL", context.Name);
        Assert.Equal(SupportedDatabase.PostgreSql, context.Dialect.DatabaseType);
    }

    [Fact]
    public async Task CreateAsync_WithConnectionStringAndFactory_ReturnsInitializedContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connStr = "Data Source=test.db;EmulatedProduct=Sqlite";

        await using var context = await DatabaseContext.CreateAsync(connStr, factory);

        Assert.NotNull(context);
        Assert.Equal("SQLite", context.Name);
        Assert.Equal(SupportedDatabase.Sqlite, context.Dialect.DatabaseType);
    }

    [Fact]
    public async Task CreateAsync_WithPreCanceledToken_CancelsBeforeOpeningConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql"
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, factory, cancellationToken: cts.Token);
        });

        Assert.Empty(factory.CreatedConnections);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidConfiguration_ThrowsArgumentException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await DatabaseContext.CreateAsync((IDatabaseContextConfiguration)null!, factory);
        });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            var config = new DatabaseContextConfiguration { ConnectionString = "" };
            await DatabaseContext.CreateAsync(config, factory);
        });
    }

    // Code-review finding: InitializeInternalsAsync's `catch (Exception ex)` around the init
    // connection's OpenAsync wrapped OperationCanceledException into ConnectionFailedException
    // instead of letting it propagate unwrapped, violating this project's documented invariant
    // that cancellation is never wrapped. This must throw OperationCanceledException, not
    // ConnectionFailedException.
    [Fact]
    public async Task CreateAsync_CancelledDuringInitConnectOpen_PropagatesOperationCanceledExceptionUnwrapped()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=cancel_init_connect_test;EmulatedProduct=PostgreSql"
        };

        // Probe construction (no injected gate) to learn the exact ConnectionString the init
        // connection ends up carrying, without guessing at any normalization/decoration.
        var probingFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using (await DatabaseContext.CreateAsync(config, probingFactory))
        {
        }

        var probedInitConnectionString = probingFactory.CreatedConnections
            .Select(c => c.ConnectionString)
            .First(cs => cs != null && cs.Contains("cancel_init_connect_test", StringComparison.Ordinal))!;

        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetOpenGateForConnectionString(probedInitConnectionString); // never completed

        using var cts = new CancellationTokenSource();
        var createTask = DatabaseContext.CreateAsync(config, factory, cancellationToken: cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await createTask);
    }

    // Code-review finding: TestConnectAsync's `catch (Exception ex)` around the read-only
    // validation connection's OpenAsync likewise wrapped OperationCanceledException into
    // ConnectionFailedException{Phase="ReadOnlyValidation"} — the same bug class as InitConnect,
    // a second site.
    [Fact]
    public async Task CreateAsync_CancelledDuringReadOnlyValidationOpen_PropagatesOperationCanceledExceptionUnwrapped()
    {
        var writeConnectionString = "Host=localhost;Database=cancel_ro_validation_write;EmulatedProduct=PostgreSql";
        var readConnectionString = "Host=localhost;Database=cancel_ro_validation_read;EmulatedProduct=PostgreSql";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = writeConnectionString,
            ReadOnlyConnectionString = readConnectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        // Probe construction (no injected gate) to learn the exact ConnectionString the
        // read-only-validation connection ends up carrying.
        var probingFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using (await DatabaseContext.CreateAsync(config, probingFactory))
        {
        }

        var probedReadConnectionString = probingFactory.CreatedConnections
            .Select(c => c.ConnectionString)
            .First(cs => cs != null && cs.Contains("cancel_ro_validation_read", StringComparison.Ordinal))!;

        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetOpenGateForConnectionString(probedReadConnectionString); // never completed

        using var cts = new CancellationTokenSource();
        var createTask = DatabaseContext.CreateAsync(config, factory, cancellationToken: cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await createTask);
    }

    [Fact]
    public async Task CreateAsync_WhenConnectionFails_ThrowsConnectionFailedException()
    {
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.PostgreSql,
            ConnectionFailureMode.FailOnOpen,
            new InvalidOperationException("Connection open failed."));

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql"
        };

        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, factory);
        });

        Assert.Equal("InitConnect", ex.Phase);
    }

    [Fact]
    public async Task CreateAsync_WhenClaimUniqueFails_UnwindsInternallyCreatedDataSource()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql) { SupportsNativeDataSource = true };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test_ds_unwind;EmulatedProduct=PostgreSql",
            EnforceUniqueConnectionString = true
        };

        await using var ctx1 = await DatabaseContext.CreateAsync(config, factory);

        var dataSourcesBefore = factory.CreatedDataSources.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, factory);
        });

        Assert.True(factory.CreatedDataSources.Count > dataSourcesBefore);
        Assert.True(factory.CreatedDataSources[^1].WasDisposed);
    }

    [Fact]
    public async Task CreateAsync_WithEnforceUniqueConnectionString_ThrowsOnDuplicateContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=unique_async_test;EmulatedProduct=PostgreSql",
            EnforceUniqueConnectionString = true
        };

        await using var ctx1 = await DatabaseContext.CreateAsync(config, factory);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, factory);
        });
    }

    [Fact]
    public async Task CreateAsync_ProvesAsyncPathUsed_WithBlockedSynchronousExecution()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=localhost;Database=test;EmulatedProduct=MySql",
            DbMode = DbMode.Standard
        };

        var customFactory = new BlockingAsyncDbProviderFactory(SupportedDatabase.MySql);
        customFactory.ScalarResult = "3.04.0.1";

        await using var context = await DatabaseContext.CreateAsync(config, customFactory);

        Assert.Equal(SupportedDatabase.AuroraMySql, context.Dialect.DatabaseType);
        Assert.Equal("MySQL", context.Name);
    }

    [Fact]
    public async Task CreateAsync_SingleConnectionMode_InitializesPersistentConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };

        await using var context = await DatabaseContext.CreateAsync(config, factory);

        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        Assert.NotNull(context.PersistentConnection);
    }

    [Fact]
    public async Task CreateAsync_PreventDatabaseUnloadMode_SetsUpSentinels()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload
        };

        await using var context = await DatabaseContext.CreateAsync(config, factory);

        Assert.Equal(DbMode.PreventDatabaseUnload, context.ConnectionMode);
        Assert.True(context.GetSentinelSnapshot().Count > 0);
    }

    [Fact]
    public async Task DefaultDatabaseContextFactory_CreateAsync_CreatesAndInitializesContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql"
        };

        IDatabaseContextFactory contextFactory = new DefaultDatabaseContextFactory();
        var context = await contextFactory.CreateAsync(config, factory, NullLoggerFactory.Instance);

        Assert.NotNull(context);
        Assert.Equal("PostgreSQL", context.Name);

        if (context is IAsyncDisposable ad)
        {
            await ad.DisposeAsync();
        }
        else
        {
            context.Dispose();
        }
    }

    // Code-review finding: a dozen identity-defining fields (_factory, _dialect, _loggerFactory,
    // _logger, TypeMapRegistry, Name, metrics collectors, etc.) went from `readonly` to mutable to
    // support the new two-phase (parameterless-ctor + async Initialize) construction, with no
    // runtime guard replacing the compiler's single-assignment guarantee. DatabaseContext is
    // documented as a long-lived singleton — nothing should be able to re-run initialization on an
    // already-initialized, in-use instance and silently overwrite its identity.
    [Fact]
    public async Task InitializeAsync_CalledTwiceOnSameInstance_ThrowsInsteadOfSilentlyReinitializing()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=reentrancy_test;EmulatedProduct=PostgreSql"
        };

        await using var context = await DatabaseContext.CreateAsync(config, factory);
        var originalName = context.Name;

        var initializeAsyncMethod = typeof(DatabaseContext).GetMethod(
            "InitializeAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(initializeAsyncMethod);

        var secondFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var secondConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=reentrancy_test2;EmulatedProduct=Sqlite"
        };

        var invokeResult = initializeAsyncMethod!.Invoke(context, new object?[]
        {
            secondConfig, secondFactory, NullLoggerFactory.Instance, new TypeMapRegistry(), null, CancellationToken.None
        });
        var secondInitTask = (Task)invokeResult!;

        var ex = await Record.ExceptionAsync(async () => await secondInitTask);
        Assert.IsType<InvalidOperationException>(ex);

        // The original context must be completely unaffected by the rejected re-initialization
        // attempt — proving this isn't just throwing after already overwriting fields.
        Assert.Equal(originalName, context.Name);
        Assert.Equal(SupportedDatabase.PostgreSql, context.Dialect.DatabaseType);
    }

    // Coverage: the DbDataSource-based CreateAsync overload's own null-guard (separate from the
    // connection-string-based overload below) — never exercised anywhere in the existing suite.
    [Fact]
    public async Task CreateAsync_WithConfigurationAndNullDataSource_ThrowsArgumentNullException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql"
        };

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, (DbDataSource)null!, factory);
        });

        Assert.Equal("dataSource", ex.ParamName);
    }

    // Coverage: the (connectionString, providerFactory-name) CreateAsync overload's two guard
    // clauses — neither the connectionString nor the providerFactory-name null check was
    // previously exercised.
    [Fact]
    public async Task CreateAsync_WithNullConnectionString_ThrowsArgumentNullException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await DatabaseContext.CreateAsync((string)null!, "System.Data.SqlClient");
        });

        Assert.Equal("connectionString", ex.ParamName);
    }

    [Fact]
    public async Task CreateAsync_WithNullProviderFactoryName_ThrowsArgumentNullException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await DatabaseContext.CreateAsync("Host=localhost;Database=test", (string)null!);
        });

        Assert.Equal("providerFactory", ex.ParamName);
    }

    // Coverage: TestConnectAsync's generic `catch (Exception ex)` path for the read-only
    // validation connection — only the OperationCanceledException-propagation sibling
    // (CreateAsync_CancelledDuringReadOnlyValidationOpen_...) existed before this.
    [Fact]
    public async Task CreateAsync_ReadOnlyValidationOpenFails_ThrowsConnectionFailedExceptionWithReadOnlyPhase()
    {
        var writeConnectionString = "Host=localhost;Database=ro_validation_fail_write;EmulatedProduct=PostgreSql";
        var readConnectionString = "Host=localhost;Database=ro_validation_fail_read;EmulatedProduct=PostgreSql";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = writeConnectionString,
            ReadOnlyConnectionString = readConnectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        // Probe construction (no injected failure) to learn the exact ConnectionString the
        // read-only-validation connection ends up carrying — same technique as the cancellation
        // sibling test above.
        var probingFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using (await DatabaseContext.CreateAsync(config, probingFactory))
        {
        }

        var probedReadConnectionString = probingFactory.CreatedConnections
            .Select(c => c.ConnectionString)
            .First(cs => cs != null && cs.Contains("ro_validation_fail_read", StringComparison.Ordinal))!;

        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetFailOnOpenForConnectionString(
            probedReadConnectionString,
            new InvalidOperationException("Simulated read-only validation open failure."));

        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, factory);
        });

        Assert.Equal("ReadOnlyValidation", ex.Phase);
        Assert.Equal("ReadOnly", ex.Role);
    }

    private sealed class BlockingAsyncDbProviderFactory : DbProviderFactory
    {
        private readonly fakeDbFactory _inner;
        public object? ScalarResult { get; set; }

        public BlockingAsyncDbProviderFactory(SupportedDatabase product)
        {
            _inner = new fakeDbFactory(product);
        }

        public override DbConnection CreateConnection()
        {
            var conn = (fakeDbConnection)_inner.CreateConnection();
            conn.BlockSynchronousCommandExecution = true;
            if (ScalarResult != null)
            {
                conn.SetScalarResultForCommand("SELECT @@aurora_version", ScalarResult);
            }
            return conn;
        }

        public override DbCommand CreateCommand() => _inner.CreateCommand();
        public override DbParameter CreateParameter() => _inner.CreateParameter();
        public override DbConnectionStringBuilder? CreateConnectionStringBuilder() => _inner.CreateConnectionStringBuilder();
    }
}
