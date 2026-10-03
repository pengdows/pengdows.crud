using System.Data;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using pengdows.crud.dialects;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// The PostgreSQL dialect sets NpgsqlDbType on the parameters it creates (REV-021 found hard-coded
/// numbers that didn't match Npgsql's enum). Checked here against the real NpgsqlParameter through
/// the live path, <see cref="SqlDialect.CreateDbParameter{T}"/>; this test first covered the
/// unreachable ProviderParameterFactory, removed in REV-039. No database needed.
/// </summary>
public class PostgreSqlParameterNpgsqlTypeTests
{
    private static NpgsqlParameter Configure<T>(T value, DbType dbType = DbType.Object)
    {
        var dialect = new PostgreSqlDialect(NpgsqlFactory.Instance, NullLogger.Instance);
        return (NpgsqlParameter)dialect.CreateDbParameter("p", dbType, value);
    }

    [Fact]
    public void Guid_IsUuid()
    {
        var parameter = Configure(Guid.NewGuid(), DbType.Guid);
        Assert.Equal(NpgsqlDbType.Uuid, parameter.NpgsqlDbType);
    }

    // A [Json] column's parameter is marked jsonb by the dialect (the gateway path). A bare
    // JsonElement passed to CreateDbParameter is left to Npgsql's own inference.
    [Fact]
    public void JsonColumn_IsJsonb()
    {
        var dialect = new PostgreSqlDialect(NpgsqlFactory.Instance, NullLogger.Instance);
        var column = new TypeMapRegistry().GetTableInfo<JsonRow>().Columns["doc"];
        // The gateway binds a [Json] value as its JSON text, then marks the parameter.
        var parameter = (NpgsqlParameter)dialect.CreateDbParameter("p", column.DbType, "{\"a\":1}");

        dialect.MarkColumnParameter(parameter, column);

        Assert.Equal(NpgsqlDbType.Jsonb, parameter.NpgsqlDbType);
    }

    [pengdows.crud.attributes.Table("json_row")]
    private sealed class JsonRow
    {
        [pengdows.crud.attributes.Id] [pengdows.crud.attributes.Column("id", DbType.Int32)] public int Id { get; set; }
        [pengdows.crud.attributes.Json] [pengdows.crud.attributes.Column("doc", DbType.String)] public JsonElement Doc { get; set; }
    }

    [Fact]
    public void StringArray_IsTextArray()
    {
        var parameter = Configure(new[] { "a", "b" });
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Text, parameter.NpgsqlDbType);
    }

    [Fact]
    public void IntArray_IsIntegerArray()
    {
        var parameter = Configure(new[] { 1, 2 });
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Integer, parameter.NpgsqlDbType);
    }

    [Fact]
    public void IntRange_IsIntegerRange()
    {
        var parameter = Configure(new Range<int>(1, 5));
        Assert.Equal(NpgsqlDbType.IntegerRange, parameter.NpgsqlDbType);
    }
}
