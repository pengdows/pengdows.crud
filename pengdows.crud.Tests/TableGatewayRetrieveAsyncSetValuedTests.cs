#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayRetrieveAsyncSetValuedTests
{
    private readonly TypeMapRegistry _typeMap;

    [Table("Ret")]
    private class RetEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    public TableGatewayRetrieveAsyncSetValuedTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<RetEntity>();
    }

    [Fact]
    public async Task RetrieveAsync_Postgres_UsesArrayBinding_PathAndReturnsRows()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        // Seed one init connection (unused) and one configured execution connection
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        // Queue rows for the select execution
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "a" },
            new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "b" }
        });
        factory.Connections.Add(execConn);

        using var ctx = new DatabaseContext("Data Source=pg;EmulatedProduct=PostgreSql", factory, _typeMap);
        var helper = new TableGateway<RetEntity, int>(ctx);

        var result = await helper.RetrieveAsync(new[] { 1, 2, 3 });
        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0].Name);
        Assert.Equal("b", result[1].Name);
    }

    // -------------------------------------------------------------------------
    // RetrieveAsync with single-element list on PostgreSQL (SupportsSetValuedParameters=true)
    // → hits the list.Count==1 fast path → SetParameterValue("p0", list.ToArray())
    // → covers TableGateway.Core.cs line 724
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RetrieveAsync_Postgres_SingleElement_UsesSetValuedFastPath()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 7, ["Name"] = "single" }
        });
        factory.Connections.Add(execConn);

        var typeMap = new TypeMapRegistry();
        typeMap.Register<RetEntity>();
        using var ctx = new DatabaseContext("Data Source=pg;EmulatedProduct=PostgreSql", factory, typeMap);
        var helper = new TableGateway<RetEntity, int>(ctx);

        // Single element → list.Count==1 → SupportsSetValuedParameters=true → array binding path
        var result = await helper.RetrieveAsync(new[] { 7 });
        Assert.Single(result);
        Assert.Equal("single", result[0].Name);
    }

    // BP-116: the single-id fast path cloned the scalar GetByIdTemplate and then stuffed an
    // array into its parameter. Live on Npgsql 9 (PostgreSQL, CockroachDB, YugabyteDB) that
    // fails: "Writing values of 'System.Int64[]' is not supported for parameters having
    // DataTypeName 'bigint'". A single id must bind a scalar.
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public async Task RetrieveAsync_PostgresFamily_SingleElement_BindsScalarNotArray(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = db });
        var execConn = new fakeDbConnection { EmulatedProduct = db };
        execConn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Id"] = 7, ["Name"] = "single" } });
        factory.Connections.Add(execConn);

        var typeMap = new TypeMapRegistry();
        typeMap.Register<RetEntity>();
        using var ctx = new DatabaseContext($"Data Source=pg;EmulatedProduct={db}", factory, typeMap);
        var helper = new TableGateway<RetEntity, int>(ctx);

        var result = await helper.RetrieveAsync(new[] { 7 });

        Assert.Single(result);
        var command = Assert.Single(execConn.ExecutedReaderCommands);
        Assert.DoesNotContain(command.Parameters, p => p.Value is Array);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public async Task RetrieveStreamAsync_PostgresFamily_SingleElement_BindsScalarNotArray(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = db });
        var execConn = new fakeDbConnection { EmulatedProduct = db };
        execConn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Id"] = 7, ["Name"] = "single" } });
        factory.Connections.Add(execConn);

        var typeMap = new TypeMapRegistry();
        typeMap.Register<RetEntity>();
        using var ctx = new DatabaseContext($"Data Source=pg;EmulatedProduct={db}", factory, typeMap);
        var helper = new TableGateway<RetEntity, int>(ctx);

        var count = 0;
        await foreach (var _ in helper.RetrieveStreamAsync(new[] { 7 }))
        {
            count++;
        }

        Assert.Equal(1, count);
        var command = Assert.Single(execConn.ExecutedReaderCommands);
        Assert.DoesNotContain(command.Parameters, p => p.Value is Array);
    }

    // BP-116: when a cached set-valued parameter receives a new array value, Npgsql metadata
    // (NpgsqlDbType / DataTypeName) must describe the array, not the stale scalar element type.
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void SetParameterValue_Array_RetypesNpgsqlParameterAsTypedArray(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        using var ctx = new DatabaseContext($"Data Source=pg;EmulatedProduct={db}", factory);
        using var sc = ctx.CreateSqlContainer("SELECT 1");
        var parameter = new FakeNpgsqlArrayParameter
        {
            ParameterName = "w0",
            DbType = DbType.Int64,
            NpgsqlDbType = FakeNpgsqlArrayDbType.Bigint,
            DataTypeName = "bigint",
            Value = 1L
        };
        sc.AddParameter(parameter);

        sc.SetParameterValue("w0", new long[] { 1, 2 });

        Assert.Equal(FakeNpgsqlArrayDbType.Bigint | FakeNpgsqlArrayDbType.Array, parameter.NpgsqlDbType);
        Assert.Equal("bigint[]", parameter.DataTypeName);
    }

    [Flags]
    public enum FakeNpgsqlArrayDbType
    {
        Bigint = 1,
        Smallint = 2,
        Text = 4,
        Uuid = 8,
        Boolean = 16,
        Integer = 32,
        Array = 64
    }

    private sealed class FakeNpgsqlArrayParameter : DbParameter
    {
        public FakeNpgsqlArrayDbType NpgsqlDbType { get; set; }
        public string DataTypeName { get; set; } = string.Empty;

        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = string.Empty;
        [AllowNull] public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; } = DBNull.Value;
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    [Fact]
    public async Task RetrieveAsync_Sqlite_UsesExpandedParameters_PathAndReturnsRows()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        // Seed init connection and configured execution connection
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "x" },
            new Dictionary<string, object?> { ["Id"] = 3, ["Name"] = "z" }
        });
        factory.Connections.Add(execConn);

        using var ctx = new DatabaseContext("Data Source=sqlite;EmulatedProduct=Sqlite", factory, _typeMap);
        var helper = new TableGateway<RetEntity, int>(ctx);

        var result = await helper.RetrieveAsync(new[] { 1, 2, 3 });
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(3, result[1].Id);
    }
}