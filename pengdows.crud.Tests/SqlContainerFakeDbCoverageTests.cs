using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;
using pengdows.crud;

namespace pengdows.crud.Tests;

public class SqlContainerFakeDbCoverageTests : IAsyncLifetime
{
    private IDatabaseContext _context = null!;
    private fakeDbFactory _factory = null!;
    private TypeMapRegistry _typeMap = null!;

    public Task InitializeAsync()
    {
        _typeMap = new TypeMapRegistry();
        _factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", _factory, _typeMap);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_context is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (_context is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static SqlContainer CreateContainer(IDatabaseContext context, string? sql = null)
    {
        return (SqlContainer)context.CreateSqlContainer(sql ?? "SELECT 1");
    }

    [Fact]
    public void CreateForDialect_UsesProvidedDialect()
    {
        var stubDialect = new StubDialect(_factory, NullLogger.Instance);
        var container = SqlContainer.CreateForDialect(_context, stubDialect, "SELECT 1", NullLogger<ISqlContainer>.Instance);

        Assert.Equal("<", container.QuotePrefix);
        Assert.Equal(">", container.QuoteSuffix);
        Assert.Equal("$", container.CompositeIdentifierSeparator);
        var parameter = container.CreateDbParameter("custom", DbType.Int32, 1);
        var formatted = container.MakeParameterName(parameter);
        Assert.Equal("#custom", formatted);
    }

    [Fact]
    public void RenderParams_ReplacesNamedPlaceholdersAndRecordsSequence()
    {
        var container = CreateContainer(_context);
        container.Query.Clear();
        container.Query.Append("SELECT {P}id, {P}name");

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.Contains("@id", rendered);
        Assert.Contains("@name", rendered);
        Assert.Equal(new[] { "id", "name" }, container.ParamSequence.ToArray());
    }

    [Fact]
    public void CreateDbParameter_WithoutName_GeneratesParameterName()
    {
        var container = CreateContainer(_context);
        var parameter = container.CreateDbParameter(DbType.Int32, 10);

        Assert.False(string.IsNullOrWhiteSpace(parameter.ParameterName));
    }

    [Fact]
    public void MakeParameterName_WithDbParameter_UsesDialectMarker()
    {
        var container = CreateContainer(_context);
        var parameter = container.CreateDbParameter("custom", DbType.Int32, 5);

        var formatted = container.MakeParameterName(parameter);

        Assert.Equal("@custom", formatted);
    }

    [Fact]
    public void AddParameterWithValue_OutputDirection_RespectsMaxOutput()
    {
        var container = CreateContainer(_context);
        var info = (DataSourceInformation)_context.DataSourceInfo;
        info.MaxOutputParameters = 1;

        var parameter = container.AddParameterWithValue("out", DbType.Int32, 42, ParameterDirection.Output);

        Assert.Equal(ParameterDirection.Output, parameter.Direction);
        Assert.Equal(1, container.ParameterCount);
    }

    [Fact]
    public void AddParameterWithValue_DefaultOverload_IncrementsCount()
    {
        var container = CreateContainer(_context);

        var parameter = container.AddParameterWithValue(DbType.Int32, 77);

        Assert.NotNull(parameter);
        Assert.Equal(1, container.ParameterCount);
    }

    [Fact]
    public void GetParameterValue_WithAlternateName_FallsBackToStoredValue()
    {
        var container = CreateContainer(_context);
        container.AddParameterWithValue("p1", DbType.Int32, 321);

        var value = container.GetParameterValue("w1");

        Assert.Equal(321, value);
    }

    private sealed class StubDialect : SqlDialect
    {
        public StubDialect(DbProviderFactory factory, ILogger logger)
            : base(factory, logger)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.SqlServer;

        public override string QuotePrefix => "<";

        public override string QuoteSuffix => ">";

        public override string CompositeIdentifierSeparator => "$";

        public override string ParameterMarker => "#";
    }
}
