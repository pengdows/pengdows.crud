#region

using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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

        var tracked = new Mock<ITrackedConnection>();
        tracked.Setup(c => c.GetSchema(It.IsAny<string>())).Throws(new NotSupportedException());
        tracked.Setup(c => c.CreateCommand()).Returns(() => inner.CreateCommand());
        return tracked.Object;
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
        var tracked = new Mock<ITrackedConnection>();
        tracked.Setup(c => c.GetSchema(It.IsAny<string>())).Returns(() => inner.GetSchema("DataSourceInformation"));

        var table = NewDialect().GetDataSourceInformationSchema(tracked.Object);

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
    private static ITrackedConnection LiveLibrary(string[] compileOptions, string[]? functions)
    {
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        inner.ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite";
        inner.Open();
        inner.SetScalarResultForCommand("PRAGMA journal_mode", "memory");
        inner.EnqueueReaderResult(Rows(compileOptions));
        if (functions != null)
        {
            inner.EnqueueReaderResult(Rows(functions));
        }

        var tracked = new Mock<ITrackedConnection>();
        tracked.Setup(c => c.CreateCommand()).Returns(() => inner.CreateCommand());
        return tracked.Object;
    }

    private static System.Collections.Generic.IEnumerable<System.Collections.Generic.Dictionary<string, object?>> Rows(
        string[] names)
    {
        foreach (var n in names)
        {
            yield return new System.Collections.Generic.Dictionary<string, object?> { ["name"] = n };
        }
    }

    [Fact]
    public void Init_ReadsMaxVariableNumberFromCompileOptions()
    {
        var dialect = NewDialect();
        Assert.Equal(999, dialect.MaxParameterLimit); // uninitialized: the version rule has nothing to go on

        dialect.ObserveDatabaseInitialization(LiveLibrary(["ENABLE_FTS5", "MAX_VARIABLE_NUMBER=250000"], ["abs"]));

        Assert.Equal(250000, dialect.MaxParameterLimit);
    }

    [Fact]
    public void Init_JsonSupportFollowsTheFunctionListNotTheVersion()
    {
        var with = NewDialect();
        with.ObserveDatabaseInitialization(LiveLibrary(["THREADSAFE=1"], ["abs", "json", "jsonb", "row_number"]));
        Assert.True(with.SupportsJsonTypes);
        Assert.True(with.SupportsWindowFunctions);

        var without = NewDialect();
        without.ObserveDatabaseInitialization(LiveLibrary(["OMIT_JSON"], ["abs", "count"]));
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
        var tracked = new Mock<ITrackedConnection>();
        tracked.Setup(c => c.CreateCommand()).Throws(new NotSupportedException("no pragma table functions"));
        var dialect = NewDialect();

        var ex = Record.Exception(() => dialect.ObserveDatabaseInitialization(tracked.Object));

        Assert.Null(ex);
        Assert.Equal(999, dialect.MaxParameterLimit);
        Assert.False(dialect.SupportsJsonTypes);
    }
}
