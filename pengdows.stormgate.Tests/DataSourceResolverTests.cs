using System.Data.Common;
using System.Reflection;

using Microsoft.Extensions.Logging;

namespace pengdows.stormgate.Tests;

public class DataSourceResolverTests
{
    private readonly RecordingLogger _logger = new();
    private readonly DataSourceResolver _resolver;

    public DataSourceResolverTests()
    {
        _resolver = new DataSourceResolver(_logger);
    }

    [Fact]
    public void SanitizeConnectionString_LogsNormalization()
    {
        // Arrange
        var factory = new StubFactory();
        var cs = "KEY=Value"; // Standard builder will lowercase the key

        // Act
        var ds = _resolver.CreateDataSource(factory, cs);

        // Assert
        Assert.Equal("key=Value", ds.ConnectionString);
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("normalized"));
    }

    [Fact]
    public void CreateDataSource_LogsGenericException()
    {
        // Arrange
        var factory = new FactoryThatThrowsGeneric();

        // Act
        var ds = _resolver.CreateDataSource(factory, "Key=Value");

        // Assert
        Assert.IsType<GenericDbDataSource>(ds);
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("Failed probing"));
    }

    [Fact]
    public void CreateDataSource_ReturnsGenericIfNoOverride()
    {
        // Arrange
        var factory = new FactoryWithNoOverride();

        // Act
        var ds = _resolver.CreateDataSource(factory, "Key=Value");

        // Assert
        Assert.IsType<GenericDbDataSource>(ds);
    }

    [Fact]
    public void SanitizeConnectionString_HandlesNullBuilder()
    {
        // Arrange
        var factory = new FactoryWithNoBuilder();
        var cs = "key=Value";

        // Act
        var ds = _resolver.CreateDataSource(factory, cs);

        // Assert
        Assert.Equal("key=Value", ds.ConnectionString);
    }

    [Fact]
    public void TryCreateProviderDataSource_HandlesNullFromNative()
    {
        // Arrange
        var factory = new FactoryReturningNullFromNative();

        // Act
        var ds = _resolver.CreateDataSource(factory, "Key=Value");

        // Assert
        Assert.IsType<GenericDbDataSource>(ds);
    }

    private class FactoryWithNoBuilder : StubFactory
    {
        public override DbConnectionStringBuilder CreateConnectionStringBuilder() => null!;
    }

    private class FactoryReturningNullFromNative : StubFactory
    {
        public new DbDataSource? CreateDataSource(string connectionString) => null;
        public DbDataSource? CreateDataSource(DbConnectionStringBuilder builder) => null;
    }

    private class FactoryWithNoOverride : StubFactory
    {
    }

    private class FactoryThatThrowsGeneric : StubFactory
    {
        public new DbDataSource CreateDataSource(string connectionString) => throw new Exception("FAIL");
    }

    [Fact]
    public void CreateDataSource_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => _resolver.CreateDataSource(null!, "Key=Value"));
        Assert.Throws<ArgumentException>(() => _resolver.CreateDataSource(new StubFactory(), null!));
        Assert.Throws<ArgumentException>(() => _resolver.CreateDataSource(new StubFactory(), " "));
    }

    [Fact]
    public void CreateDataSource_ReturnsGenericDataSourceIfNoNativeSupport()
    {
        // Arrange
        var factory = new StubFactory();

        // Act
        var dataSource = _resolver.CreateDataSource(factory, "Key=Value");

        // Assert
        Assert.IsType<GenericDbDataSource>(dataSource);
        Assert.Equal("key=Value", dataSource.ConnectionString);
    }

    [Fact]
    public void CreateDataSource_ReturnsNativeDataSourceIfStringOverloadExists()
    {
        // Arrange
        var factory = new FactoryWithDataSourceString();

        // Act
        var dataSource = _resolver.CreateDataSource(factory, "Key=Value");

        // Assert
        Assert.IsType<StubDataSource>(dataSource);
        Assert.Equal("key=Value", dataSource.ConnectionString);
    }

    [Fact]
    public void CreateDataSource_ReturnsNativeDataSourceIfBuilderOverloadExists()
    {
        // Arrange
        var factory = new FactoryWithDataSourceBuilder();

        // Act
        var dataSource = _resolver.CreateDataSource(factory, "Key=Value");

        // Assert
        Assert.IsType<StubDataSource>(dataSource);
        Assert.Equal("key=Value", dataSource.ConnectionString);
    }

    [Fact]
    public void CreateDataSource_FallsBackToGenericIfNativeThrowsNotSupported()
    {
        // Arrange
        var factory = new FactoryThatThrowsNotSupported();

        // Act
        var dataSource = _resolver.CreateDataSource(factory, "Key=Value");

        // Assert
        Assert.IsType<GenericDbDataSource>(dataSource);
    }

    // P1: SanitizeConnectionString must log a Warning (not just Debug) and include the names of
    // dropped keys when the provider's builder silently removes unknown keywords.
    // A stripped Encrypt=True or SslMode=Required would be a silent security regression.
    [Fact]
    public void SanitizeConnectionString_LogsWarningWithRemovedKeyNames_WhenBuilderDropsKeys()
    {
        var factory = new FactoryWithKeyFilteringBuilder();

        // "Unknown=secret" is stripped by the filtering builder; "Server=localhost" is kept.
        var ds = _resolver.CreateDataSource(factory, "Server=localhost;Unknown=secret");

        // Must warn and include the removed key name.
        // DbConnectionStringBuilder normalizes keys to lowercase, so the warning message
        // will contain "unknown" (not "Unknown") regardless of the original casing.
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning &&
            e.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase) && e.Message.Contains("removed"));
    }

    // Full-codebase review finding: the "silently dropped key" detector compares raw-parsed keys
    // against the provider-typed builder's canonicalized keys, which cannot distinguish a
    // renamed alias (e.g. Npgsql's Server -> Host) from an actually-removed key — a real key
    // rename would be flagged identically to a genuine Encrypt/SslMode drop, training operators
    // to ignore the warning and masking the next real one.
    [Fact]
    public void SanitizeConnectionString_DoesNotWarn_WhenKeyIsOnlyRenamedToAnAlias_NotGenuinelyDropped()
    {
        var factory = new FactoryWithAliasingBuilder();

        _resolver.CreateDataSource(factory, "Server=localhost");

        // "Server" is renamed to "host" by the provider's builder with its value preserved —
        // this must not be reported as a dropped key.
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    private class FactoryWithAliasingBuilder : StubFactory
    {
        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
            => new AliasingBuilder();

        // Mirrors Npgsql's real alias normalization: "server" is canonicalized to "host", value
        // preserved — not dropped, just renamed.
        private sealed class AliasingBuilder : DbConnectionStringBuilder
        {
            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override object this[string keyword]
            {
                get => base[keyword];
                set
                {
                    var canonical = string.Equals(keyword, "server", StringComparison.OrdinalIgnoreCase)
                        ? "host"
                        : keyword;
                    base[canonical] = value;
                }
            }
        }
    }

    private class FactoryWithKeyFilteringBuilder : StubFactory
    {
        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
            => new KeyFilteringBuilder();

        // Accepts only "server"; any other keyword is silently dropped on assignment.
        // The virtual indexer IS called by DbConnectionStringBuilder.ConnectionString's setter,
        // so overriding this[keyword] is sufficient to filter keys at parse time.
        private sealed class KeyFilteringBuilder : DbConnectionStringBuilder
        {
            private static readonly HashSet<string> AllowedKeys =
                new(StringComparer.OrdinalIgnoreCase) { "server" };

            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override object this[string keyword]
            {
                get => base[keyword];
                set
                {
                    if (AllowedKeys.Contains(keyword))
                    {
                        base[keyword] = value;
                    }
                }
            }
        }
    }

    private class StubFactory : DbProviderFactory
    {
        public override DbConnection CreateConnection() => new fakeDbConnection();
        public override DbConnectionStringBuilder CreateConnectionStringBuilder() => new DbConnectionStringBuilder();
    }

    private class FactoryWithDataSourceString : StubFactory
    {
        public new DbDataSource CreateDataSource(string connectionString) => new StubDataSource(connectionString);
    }

    private class FactoryWithDataSourceBuilder : StubFactory
    {
        public DbDataSource CreateDataSource(DbConnectionStringBuilder builder) => new StubDataSource(builder.ConnectionString);
    }

    private class FactoryThatThrowsNotSupported : StubFactory
    {
        public new DbDataSource CreateDataSource(string connectionString) => throw new TargetInvocationException(new NotSupportedException());
    }

    private class StubDataSource : DbDataSource
    {
        public StubDataSource(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public override string ConnectionString { get; }
        protected override DbConnection CreateDbConnection() => throw new NotImplementedException();
    }
}
