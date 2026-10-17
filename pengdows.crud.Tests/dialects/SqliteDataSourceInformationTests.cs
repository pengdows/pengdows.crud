#region

using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

// Microsoft.Data.Sqlite releases up to 10.0.x do not implement GetSchema; later ones do. The dialect asks the
// provider first and, when the provider has nothing, describes the database it is actually connected to.
public class SqliteDataSourceInformationTests
{
    private static SqliteDialect NewDialect() =>
        new(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger.Instance);

    // A connection whose provider has no GetSchema, answering the version query like SQLite does.
    private static ITrackedConnection ProviderWithoutSchema(object? version)
    {
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        inner.ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite";
        inner.Open();
        if (version != null)
        {
            inner.SetScalarResultForCommand("SELECT sqlite_version()", version);
        }
        else
        {
            inner.SetScalarExecuteException(new InvalidOperationException("no version"));
        }

        inner.SchemaException = new NotSupportedException();
        return new TrackedConnection(inner);
    }

    private static DataRow Row(DataTable table)
    {
        Assert.Single(table.Rows);
        return table.Rows[0];
    }

    [Fact]
    public void ProviderSchema_WhenAvailable_IsReturnedUnchanged()
    {
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        var provided = DataSourceInformation.BuildEmptySchema("SQLite", "9.9.9", "@", "@{0}", 128, ".", ".", true);
        inner.SchemaTable = provided;
        var tracked = new TrackedConnection(inner);

        var table = NewDialect().GetDataSourceInformationSchema(tracked);

        Assert.Equal("9.9.9", Row(table)["DataSourceProductVersion"]);
    }

    [Fact]
    public void NoProviderSchema_ReportsTheLiveSqliteVersion_NotAFixedOne()
    {
        var table = NewDialect().GetDataSourceInformationSchema(ProviderWithoutSchema("3.38.5"));

        var row = Row(table);
        Assert.Equal("SQLite", row["DataSourceProductName"]);
        Assert.Equal("3.38.5", row["DataSourceProductVersion"]);
        Assert.Equal("03.038.0005.0000", row["DataSourceProductVersionNormalized"]);
    }

    [Theory]
    [InlineData("3.38.5", SupportedJoinOperators.Inner | SupportedJoinOperators.LeftOuter)]
    [InlineData("3.39.0", SupportedJoinOperators.Inner | SupportedJoinOperators.LeftOuter | SupportedJoinOperators.RightOuter | SupportedJoinOperators.FullOuter)]
    [InlineData("3.45.1", SupportedJoinOperators.Inner | SupportedJoinOperators.LeftOuter | SupportedJoinOperators.RightOuter | SupportedJoinOperators.FullOuter)]
    public void NoProviderSchema_JoinOperatorsFollowTheVersion(string version, SupportedJoinOperators expected)
    {
        var table = NewDialect().GetDataSourceInformationSchema(ProviderWithoutSchema(version));

        Assert.Equal((int)expected, Convert.ToInt32(Row(table)["SupportedJoinOperators"]));
    }

    [Fact]
    public void NoProviderSchema_DescribesSqliteParameterAndQuotingRules()
    {
        var row = Row(NewDialect().GetDataSourceInformationSchema(ProviderWithoutSchema("3.45.1")));

        Assert.Equal("@{0}", row["ParameterMarkerFormat"]);
        Assert.Equal(";", row["StatementSeparatorPattern"]);
        Assert.Equal("\\.", row["CompositeIdentifierSeparatorPattern"]);
        Assert.Matches((string)row["ParameterMarkerPattern"], "@name");
        Assert.Matches((string)row["ParameterMarkerPattern"], ":name");
        Assert.Matches((string)row["ParameterMarkerPattern"], "?1");
        Assert.Matches((string)row["IdentifierPattern"], "überschrift");
    }

    [Fact]
    public void NoProviderSchema_AndNoVersionAnswer_StillDescribesSqliteWithoutThrowing()
    {
        var row = Row(NewDialect().GetDataSourceInformationSchema(ProviderWithoutSchema(null)));

        Assert.Equal("SQLite", row["DataSourceProductName"]);
        Assert.Equal(string.Empty, row["DataSourceProductVersion"]);
        // Unknown version: claim only the joins every SQLite has.
        Assert.Equal((int)(SupportedJoinOperators.Inner | SupportedJoinOperators.LeftOuter), Convert.ToInt32(row["SupportedJoinOperators"]));
    }

    // What the loaded SQLite library really has, read once from its own catalogs when the context initializes,
    // beats guessing from the version number: a custom build can raise MAX_VARIABLE_NUMBER or compile JSON out.
    // Both probes are single scalar queries, so they never consume reader results a fakeDb test queued.
    private const string MaxVariableNumberSql =
        "SELECT compile_options FROM pragma_compile_options WHERE compile_options LIKE 'MAX_VARIABLE_NUMBER=%'";

    private const string FunctionsSql =
        "SELECT 256 + (SELECT count(*) > 0 FROM pragma_function_list)" +
        " + 2 * (SELECT count(*) > 0 FROM pragma_function_list WHERE name = 'jsonb')" +
        " + 4 * (SELECT count(*) > 0 FROM pragma_function_list WHERE name = 'row_number')";

    private static ITrackedConnection LiveLibrary(string? maxVariableNumber, long? functionMask)
    {
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        inner.ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite";
        inner.Open();
        inner.SetScalarResultForCommand("PRAGMA journal_mode", "memory");
        inner.SetScalarResultForCommand(MaxVariableNumberSql, maxVariableNumber);
        inner.SetScalarResultForCommand(FunctionsSql, functionMask);

        return new TrackedConnection(inner);
    }

    [Fact]
    public void Init_ReadsMaxVariableNumberFromCompileOptions()
    {
        var dialect = NewDialect();
        Assert.Equal(999, dialect.MaxParameterLimit); // uninitialized: the version rule has nothing to go on

        dialect.ObserveDatabaseInitialization(LiveLibrary("MAX_VARIABLE_NUMBER=250000", 256 + 1));

        Assert.Equal(250000, dialect.MaxParameterLimit);
    }

    [Fact]
    public void Init_JsonSupportFollowsTheFunctionListNotTheVersion()
    {
        var with = NewDialect();
        with.ObserveDatabaseInitialization(LiveLibrary(null, 256 + 1 + 2 + 4));
        Assert.True(with.SupportsJsonTypes);
        Assert.True(with.SupportsWindowFunctions);

        var without = NewDialect();
        without.ObserveDatabaseInitialization(LiveLibrary(null, 256 + 1));
        Assert.False(without.SupportsJsonTypes);
        Assert.False(without.SupportsWindowFunctions);
    }

    [Fact]
    public void Init_WhenTheCatalogsCannotBeRead_FallsBackToTheVersionRules()
    {
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        inner.ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite";
        inner.Open();
        inner.SetScalarResultForCommand("PRAGMA journal_mode", "memory");
        inner.SetScalarExecuteException(null);
        inner.SetCustomCommandBehavior(() => throw new NotSupportedException("no pragma table functions"));
        var tracked = new TrackedConnection(inner);
        var dialect = NewDialect();

        var ex = Record.Exception(() => dialect.ObserveDatabaseInitialization(tracked));

        Assert.Null(ex);
        Assert.Equal(999, dialect.MaxParameterLimit);
        Assert.False(dialect.SupportsJsonTypes);
    }

    private sealed class CollectingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public System.Collections.Generic.List<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    private static ITrackedConnection ProviderFailingWith(Exception failure)
    {
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        inner.ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite";
        inner.Open();
        inner.SetScalarResultForCommand("SELECT sqlite_version()", "3.50.4");
        inner.SchemaException = failure;
        return new TrackedConnection(inner);
    }

    [Theory]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(ArgumentException))] // Microsoft.Data.Sqlite: "The requested collection is not defined"
    public void ProviderWithoutTheCollection_FallsBackWithoutAWarning(Type failure)
    {
        var logger = new CollectingLogger();
        var dialect = new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), logger);

        var table = dialect.GetDataSourceInformationSchema(
            ProviderFailingWith((Exception)Activator.CreateInstance(failure)!));

        Assert.Equal("3.50.4", Row(table)["DataSourceProductVersion"]);
        Assert.DoesNotContain(Microsoft.Extensions.Logging.LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public void ProviderFailingForAnyOtherReason_StillFallsBackButWarns()
    {
        var logger = new CollectingLogger();
        var dialect = new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), logger);

        var table = dialect.GetDataSourceInformationSchema(
            ProviderFailingWith(new InvalidOperationException("database is locked")));

        Assert.Equal("3.50.4", Row(table)["DataSourceProductVersion"]);
        Assert.Contains(Microsoft.Extensions.Logging.LogLevel.Warning, logger.Levels);
    }
}
