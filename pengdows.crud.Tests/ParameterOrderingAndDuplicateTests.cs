using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Data.Common;
using pengdows.crud.collections;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ParameterOrderingAndDuplicateTests
{
    /// <summary>
    /// Proves PropertyCache returns properties in deterministic MetadataToken order,
    /// not arbitrary reflection order.
    /// </summary>
    [Fact]
    public void FromObject_ReturnsPropertiesInMetadataTokenOrder()
    {
        var obj = new TestEntity { Alpha = 1, Beta = "two", Gamma = 3.0 };
        var dict = OrderedDictionaryExtensions.FromObject(obj);

        var keys = dict.Keys.ToList();

        // MetadataToken order matches source declaration order
        var expectedOrder = typeof(TestEntity)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(p => p.MetadataToken)
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(expectedOrder, keys);
    }

    /// <summary>
    /// Proves that FromObject is stable across repeated calls (cached).
    /// </summary>
    [Fact]
    public void FromObject_StableOrderAcrossCalls()
    {
        var obj = new TestEntity { Alpha = 1, Beta = "x", Gamma = 9.9 };

        var keys1 = OrderedDictionaryExtensions.FromObject(obj).Keys.ToList();
        var keys2 = OrderedDictionaryExtensions.FromObject(obj).Keys.ToList();

        Assert.Equal(keys1, keys2);
    }

    /// <summary>
    /// Proves that SupportsRepeatedNamedParameters defaults to SupportsNamedParameters.
    /// </summary>
    [Fact]
    public async Task SupportsRepeatedNamedParameters_DefaultsToNamedParameterSupport()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        Assert.True(((IDatabaseContext)context).SupportsNamedParameters);
        Assert.True(((IDatabaseContext)context).SupportsRepeatedNamedParameters);
    }

    /// <summary>
    /// Proves that named parameter providers add each parameter once even when
    /// the same placeholder appears multiple times in SQL.
    /// </summary>
    [Fact]
    public async Task NamedParameters_DuplicatePlaceholders_SingleParameterAdded()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        // SQL references {P}val twice
        await using var sc = context.CreateSqlContainer(
            "UPDATE t SET col = {P}val WHERE col2 = {P}val");
        sc.AddParameterWithValue("val", DbType.Int32, 42);

        // For named providers, only one parameter should exist
        Assert.Equal(1, sc.ParameterCount);
    }

    /// <summary>
    /// Oracle treats repeated named placeholders as distinct bind positions.
    /// Verify that repeated {P} names are rewritten to unique placeholders and
    /// that parameters are duplicated with metadata preserved.
    /// </summary>
    [Fact]
    public async Task Oracle_DuplicateNamedParameters_RewrittenAndDuplicated()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Oracle",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        await using var sc = context.CreateSqlContainer(
            "UPDATE test_table SET col = {P}p WHERE other = {P}p");
        var param = sc.AddParameterWithValue("p", DbType.Int32, 7);
        param.Direction = ParameterDirection.InputOutput;
        param.Size = 12;
        param.Precision = 5;
        param.Scale = 2;
        param.SourceColumn = "col";
        param.SourceVersion = DataRowVersion.Original;
        param.IsNullable = true;

        await sc.ExecuteNonQueryAsync();

        var commandText = factory.LastCommandText ?? string.Empty;
        Assert.Contains(":p", commandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(":p_2", commandText, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(2, factory.Parameters.Count);
        var first = factory.Parameters[0];
        var second = factory.Parameters[1];

        Assert.Equal("p", first.Name);
        Assert.Equal("p_2", second.Name);
        Assert.Equal(first.Type, second.Type);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first.Direction, second.Direction);
        Assert.Equal(first.Size, second.Size);
        Assert.Equal(first.Precision, second.Precision);
        Assert.Equal(first.Scale, second.Scale);
        Assert.Equal(first.SourceColumn, second.SourceColumn);
        Assert.Equal(first.SourceVersion, second.SourceVersion);
        Assert.Equal(first.IsNullable, second.IsNullable);
    }

    private class TestEntity
    {
        public int Alpha { get; set; }
        public string Beta { get; set; } = "";
        public double Gamma { get; set; }
    }

    private sealed class RecordingFactory : DbProviderFactory
    {
        public string? LastCommandText { get; private set; }
        public List<RecordedParameter> Parameters { get; } = new();

        public override DbConnection CreateConnection()
        {
            return new RecordingConnection(this);
        }

        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new fakeDbParameter();

        public void RecordCommand(string? commandText, IReadOnlyList<RecordedParameter> parameters)
        {
            LastCommandText = commandText;
            Parameters.Clear();
            Parameters.AddRange(parameters);
        }
    }

    private sealed class RecordingConnection : fakeDbConnection
    {
        private readonly RecordingFactory _factory;

        public RecordingConnection(RecordingFactory factory)
        {
            _factory = factory;
        }

        protected override DbCommand CreateDbCommand()
        {
            return new RecordingCommand(this, _factory);
        }
    }

    private sealed class RecordingCommand : fakeDbCommand
    {
        private readonly RecordingFactory _factory;

        public RecordingCommand(fakeDbConnection connection, RecordingFactory factory) : base(connection)
        {
            _factory = factory;
        }

        public override int ExecuteNonQuery()
        {
            var recorded = new List<RecordedParameter>();
            foreach (DbParameter param in Parameters)
            {
                recorded.Add(new RecordedParameter(
                    param.ParameterName,
                    param.DbType,
                    param.Value,
                    param.Direction,
                    param.Size,
                    param.Precision,
                    param.Scale,
                    param.SourceColumn,
                    param.SourceVersion,
                    param.IsNullable));
            }

            _factory.RecordCommand(CommandText, recorded);
            return base.ExecuteNonQuery();
        }
    }

    private sealed record RecordedParameter(
        string Name,
        DbType Type,
        object? Value,
        ParameterDirection Direction,
        int Size,
        byte Precision,
        byte Scale,
        string SourceColumn,
        DataRowVersion SourceVersion,
        bool IsNullable);

    // ── Provider-specific property preservation tests ────────────────────

    /// <summary>
    /// Simulates an Oracle-like parameter with a provider-specific enum property.
    /// </summary>
    private enum FakeOracleDbType
    {
        Default = 0,
        Clob = 105,
        Blob = 106,
        IntervalYearToMonth = 107
    }

    /// <summary>
    /// A fake DbParameter that exposes <see cref="OracleDbType"/>,
    /// mimicking ODP.NET's OracleParameter.
    /// </summary>
    private sealed class FakeOracleParameter : fakeDbParameter
    {
        public FakeOracleDbType OracleDbType { get; set; } = FakeOracleDbType.Default;
    }

    private sealed class FakeOracleFactory : DbProviderFactory
    {
        public string? LastCommandText { get; private set; }
        public List<FakeOracleParameter> CapturedParameters { get; } = new();

        public override DbConnection CreateConnection()
        {
            return new FakeOracleConnection(this);
        }

        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new FakeOracleParameter();

        public void RecordExecution(string? commandText, DbParameterCollection parameters)
        {
            LastCommandText = commandText;
            CapturedParameters.Clear();
            foreach (DbParameter p in parameters)
            {
                if (p is FakeOracleParameter fp)
                {
                    CapturedParameters.Add(fp);
                }
            }
        }
    }

    private sealed class FakeOracleConnection : fakeDbConnection
    {
        private readonly FakeOracleFactory _factory;

        public FakeOracleConnection(FakeOracleFactory factory)
        {
            _factory = factory;
        }

        protected override DbCommand CreateDbCommand()
        {
            return new FakeOracleCommand(this, _factory);
        }
    }

    private sealed class FakeOracleCommand : fakeDbCommand
    {
        private readonly FakeOracleFactory _factory;

        public FakeOracleCommand(fakeDbConnection connection, FakeOracleFactory factory) : base(connection)
        {
            _factory = factory;
        }

        public override int ExecuteNonQuery()
        {
            _factory.RecordExecution(CommandText, Parameters);
            return base.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Proves that provider-specific properties (OracleDbType) are preserved
    /// when duplicate placeholders cause parameter cloning on the Oracle path
    /// (!SupportsRepeatedNamedParameters).
    /// </summary>
    [Fact]
    public async Task Oracle_DuplicatePlaceholders_PreservesProviderSpecificProperties()
    {
        var factory = new FakeOracleFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Oracle",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        await using var sc = context.CreateSqlContainer(
            "UPDATE t SET col = {P}p WHERE other = {P}p");
        var param = sc.AddParameterWithValue("p", DbType.Object, "clob-data");

        // Simulate user explicitly setting provider-specific property after creation
        if (param is FakeOracleParameter oracleParam)
        {
            oracleParam.OracleDbType = FakeOracleDbType.Clob;
        }
        else
        {
            // If the factory didn't produce our type, the test infrastructure is wrong
            Assert.Fail("Expected FakeOracleParameter but got " + param.GetType().Name);
        }

        await sc.ExecuteNonQueryAsync();

        // Oracle doesn't support repeated named params, so duplicate {P}p becomes :p and :p_2
        Assert.Equal(2, factory.CapturedParameters.Count);

        var first = factory.CapturedParameters[0];
        var second = factory.CapturedParameters[1];

        // Both parameters should have the provider-specific property preserved
        Assert.Equal(FakeOracleDbType.Clob, first.OracleDbType);
        Assert.Equal(FakeOracleDbType.Clob, second.OracleDbType);
    }
}