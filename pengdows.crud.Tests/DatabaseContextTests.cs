#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using Moq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DatabaseContextTests
{
    public static IEnumerable<object[]> AllSupportedProviders()
    {
        return Enum.GetValues<SupportedDatabase>()
            .Where(p => p != SupportedDatabase.Unknown)
            .Select(p => new object[] { p });
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void CanInitializeContext_ForEachSupportedProvider(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var config = new DatabaseContextConfiguration
        {
            DbMode = DbMode.SingleWriter,
            ProviderName = product.ToString(),
            ConnectionString = $"Data Source=test;EmulatedProduct={product}"
        };
        var context = new DatabaseContext(config, factory);

        var conn = context.GetConnection(ExecutionType.Read);
        Assert.NotNull(conn);
        Assert.Equal(ConnectionState.Closed, conn.State);

        var schema = conn.GetSchema();
        Assert.NotNull(schema);
        Assert.True(schema.Rows.Count > 0);
    }

    // [Fact]
    // public void Constructor_WithNullFactory_Throws()
    // {
    //     Assert.Throws<NullReferenceException>(() =>
    //         new DatabaseContext("fake", (string)null!));
    // }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void WrapObjectName_SplitsAndWrapsCorrectly(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var wrapped = context.WrapObjectName("schema.table");
        Assert.Contains(".", wrapped);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void GenerateRandomName_ValidatesFirstChar(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var name = context.GenerateRandomName(10);
        Assert.True(char.IsLetter(name[0]));
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void CreateDbParameter_SetsPropertiesCorrectly(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var result = context.CreateDbParameter("p1", DbType.Int32, 123, ParameterDirection.Output);

        Assert.Equal("p1", result.ParameterName);
        Assert.Equal(DbType.Int32, result.DbType);
        Assert.Equal(123, result.Value);
        Assert.Equal(ParameterDirection.Output, result.Direction);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void CreateDbParameter_DefaultsDirectionToInput(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var result = context.CreateDbParameter("p1", DbType.Int32, 123);

        Assert.Equal(ParameterDirection.Input, result.Direction);
    }

    [Theory]
    [InlineData("@foo", "foo")]
    [InlineData(":bar", "bar")]
    [InlineData("?baz", "baz")]
    public void CreateDbParameter_RemovesPrefixesFromName(string input, string expected)
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var result = context.CreateDbParameter(input, DbType.String, "v");

        Assert.Equal(expected, result.ParameterName);
    }

    [Theory]
    [InlineData("@foo", "foo")]
    [InlineData(":bar", "bar")]
    [InlineData("?baz", "baz")]
    public void CreateDbParameter_RemovesPrefixesFromName(string input, string expected)
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var result = context.CreateDbParameter(input, DbType.String, "v");

        Assert.Equal(expected, result.ParameterName);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public async Task CloseAndDisposeConnectionAsync_WithAsyncDisposable_DisposesCorrectly(SupportedDatabase product)
    {
        var mockTracked = new Mock<ITrackedConnection>();
        mockTracked.As<IAsyncDisposable>().Setup(d => d.DisposeAsync())
            .Returns(ValueTask.CompletedTask).Verifiable();

        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        await context.CloseAndDisposeConnectionAsync(mockTracked.Object);

        mockTracked.As<IAsyncDisposable>().Verify(d => d.DisposeAsync(), Times.Once);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void AssertIsWriteConnection_WhenFalse_Throws(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory,
            readWriteMode: ReadWriteMode.ReadOnly);
        Assert.Throws<InvalidOperationException>(() => context.AssertIsWriteConnection());
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void AssertIsReadConnection_WhenFalse_Throws(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory,
            readWriteMode: ReadWriteMode.WriteOnly);
        Assert.Throws<InvalidOperationException>(() => context.AssertIsReadConnection());
    }

    public static IEnumerable<object[]> ProvidersWithSettings()
    {
        return new List<object[]>
        {
            new object[] { SupportedDatabase.SqlServer, false },
            new object[] { SupportedDatabase.MySql, true },
            new object[] { SupportedDatabase.MariaDb, true },
            new object[] { SupportedDatabase.PostgreSql, true },
            new object[] { SupportedDatabase.CockroachDb, true },
            new object[] { SupportedDatabase.Oracle, true },
            new object[] { SupportedDatabase.Sqlite, true },
            new object[] { SupportedDatabase.Firebird, false }
        };
    }

    [Theory]
    [MemberData(nameof(ProvidersWithSettings))]
    public void SessionSettingsPreamble_CorrectPerProvider(SupportedDatabase product, bool expectSettings)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var preamble = context.SessionSettingsPreamble;
        if (expectSettings)
            Assert.False(string.IsNullOrWhiteSpace(preamble));
        else
            Assert.True(string.IsNullOrWhiteSpace(preamble));
    }

    [Fact]
    public void CloseAndDisposeConnection_StandardMode_ClosesConnection()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new FakeDbFactory(product);
        var context =
            new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory, mode: DbMode.Standard);
        Assert.Equal(DbMode.Standard, context.ConnectionMode);
        var conn = context.GetConnection(ExecutionType.Read);
        conn.Open();
        Assert.Equal(1, context.NumberOfOpenConnections);
        context.CloseAndDisposeConnection(conn);
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void CloseAndDisposeConnection_SingleConnectionMode_KeepsOpen()
    {
        var product = SupportedDatabase.Sqlite;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.SingleConnection
        };
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext(config, factory);
        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        var conn = context.GetConnection(ExecutionType.Read);
        Assert.Equal(ConnectionState.Open, conn.State);
        var before = context.NumberOfOpenConnections;
        context.CloseAndDisposeConnection(conn);
        Assert.Equal(before, context.NumberOfOpenConnections);
        Assert.Equal(ConnectionState.Open, conn.State);
        context.Dispose();
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void MaxNumberOfConnections_TracksPeakUsage()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var c1 = context.GetConnection(ExecutionType.Read);
        var c2 = context.GetConnection(ExecutionType.Read);
        c1.Open();
        c2.Open();
        Assert.Equal(2, context.MaxNumberOfConnections);
        context.CloseAndDisposeConnection(c1);
        context.CloseAndDisposeConnection(c2);
        Assert.Equal(2, context.MaxNumberOfConnections);
    }

    [Fact]
    public void RCSIEnabled_DefaultIsFalse()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}", factory);
        Assert.False(context.RCSIEnabled);
    }

    [Fact]
    public void MakeParameterName_UsesDatabaseMarker()
    {
        var product = SupportedDatabase.PostgreSql;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var name = context.MakeParameterName("foo");
        var expected = context.DataSourceInfo.ParameterMarker + "foo";
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("@foo")]
    [InlineData(":foo")]
    [InlineData("?foo")]
    [InlineData("@:foo?")]
    public void MakeParameterName_StripsExistingPrefixes(string input)
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var name = context.MakeParameterName(input);
        var expected = context.DataSourceInfo.ParameterMarker + "foo";
        Assert.Equal(expected, name);
    }

    [Fact]
    public void MakeParameterName_NoNamedParameters_ReturnsQuestionMark()
    {
        var product = SupportedDatabase.Unknown;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        Assert.Equal("?", context.MakeParameterName("foo"));
        Assert.Equal("?", context.MakeParameterName("@foo"));
        Assert.Equal("?", context.MakeParameterName(":foo"));
    }

    [Fact]
    public void MakeParameterName_DbParameter_StripsPrefixes()
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var param = new FakeDbParameter { ParameterName = ":foo", DbType = DbType.String, Value = "x" };

        var name = context.MakeParameterName(param);

        Assert.Equal(context.DataSourceInfo.ParameterMarker + "foo", name);
    }

    [Fact]
    public void MakeParameterName_DbParameter_NoNamedParameters_ReturnsQuestionMark()
    {
        var product = SupportedDatabase.Unknown;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var param = new FakeDbParameter { ParameterName = "@foo", DbType = DbType.String, Value = "x" };

        Assert.Equal("?", context.MakeParameterName(param));
    }

    [Fact]
    public void MaxOutputParameters_ExposedViaContext()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);

        Assert.Equal(context.DataSourceInfo.MaxOutputParameters, context.MaxOutputParameters);
 
    }

    [Fact]
    public void Product_WhenInitialized_ReturnsProvidedProduct()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);

        Assert.Equal(product, context.Product);
    }

    [Fact]
    public void Product_WithoutDataSourceInfo_ReturnsUnknown()
    {
        var context = (DatabaseContext)FormatterServices.GetUninitializedObject(typeof(DatabaseContext));

        Assert.Equal(SupportedDatabase.Unknown, context.Product);
 
    }
}