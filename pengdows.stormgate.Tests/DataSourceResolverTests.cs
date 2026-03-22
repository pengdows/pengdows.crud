using System.Data.Common;
using System.Reflection;

using Microsoft.Extensions.Logging;

namespace pengdows.stormgate.Tests;

public class DataSourceResolverTests
{
    private readonly Mock<ILogger> _mockLogger = new();
    private readonly DataSourceResolver _resolver;

    public DataSourceResolverTests()
    {
        _resolver = new DataSourceResolver(_mockLogger.Object);
    }

    [Fact]
    public void SanitizeConnectionString_LogsNormalization()
    {
        // Arrange
        var factory = new MockFactory();
        var cs = "KEY=Value"; // Standard builder will lowercase the key

        // Act
        var ds = _resolver.CreateDataSource(factory, cs);

        // Assert
        Assert.Equal("key=Value", ds.ConnectionString);
        _mockLogger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("normalized")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
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
        _mockLogger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed probing")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
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

    private class FactoryWithNoBuilder : MockFactory
    {
        public override DbConnectionStringBuilder CreateConnectionStringBuilder() => null!;
    }

    private class FactoryReturningNullFromNative : MockFactory
    {
        public new DbDataSource? CreateDataSource(string connectionString) => null;
        public DbDataSource? CreateDataSource(DbConnectionStringBuilder builder) => null;
    }

    private class FactoryWithNoOverride : MockFactory
    {
    }

    private class FactoryThatThrowsGeneric : MockFactory
    {
        public new DbDataSource CreateDataSource(string connectionString) => throw new Exception("FAIL");
    }

    [Fact]
    public void CreateDataSource_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => _resolver.CreateDataSource(null!, "Key=Value"));
        Assert.Throws<ArgumentException>(() => _resolver.CreateDataSource(new MockFactory(), null!));
        Assert.Throws<ArgumentException>(() => _resolver.CreateDataSource(new MockFactory(), " "));
    }

    [Fact]
    public void CreateDataSource_ReturnsGenericDataSourceIfNoNativeSupport()
    {
        // Arrange
        var factory = new MockFactory();

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
        Assert.IsType<MockDataSource>(dataSource);
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
        Assert.IsType<MockDataSource>(dataSource);
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

        // Must warn and include the removed key name
        _mockLogger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) =>
                v.ToString()!.Contains("Unknown") &&
                v.ToString()!.Contains("removed")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private class FactoryWithKeyFilteringBuilder : MockFactory
    {
        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
            => new KeyFilteringBuilder();

        // Accepts only "server"; any other keyword is silently dropped on assignment.
        private sealed class KeyFilteringBuilder : DbConnectionStringBuilder
        {
            private static readonly HashSet<string> AllowedKeys =
                new(StringComparer.OrdinalIgnoreCase) { "server" };

            public override object this[string keyword]
            {
                get => base[keyword];
                set
                {
                    if (AllowedKeys.Contains(keyword))
                        base[keyword] = value;
                    // else: silently dropped — this simulates MySQL builder ignoring unknown keys
                }
            }
        }
    }

    private class MockFactory : DbProviderFactory
    {
        public override DbConnection CreateConnection() => new Mock<DbConnection>().Object;
        public override DbConnectionStringBuilder CreateConnectionStringBuilder() => new DbConnectionStringBuilder();
    }

    private class FactoryWithDataSourceString : MockFactory
    {
        public new DbDataSource CreateDataSource(string connectionString) => new MockDataSource(connectionString);
    }

    private class FactoryWithDataSourceBuilder : MockFactory
    {
        public DbDataSource CreateDataSource(DbConnectionStringBuilder builder) => new MockDataSource(builder.ConnectionString);
    }

    private class FactoryThatThrowsNotSupported : MockFactory
    {
        public new DbDataSource CreateDataSource(string connectionString) => throw new TargetInvocationException(new NotSupportedException());
    }

    private class MockDataSource : DbDataSource
    {
        public MockDataSource(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public override string ConnectionString { get; }
        protected override DbConnection CreateDbConnection() => throw new NotImplementedException();
    }
}
