using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// ProviderParameterFactory sets NpgsqlDbType on PostgreSQL parameters. It used hard-coded
/// numbers that don't match Npgsql's enum (e.g. JSONB as 14, which is not Jsonb), and the unit
/// tests' int-typed stub asserted the same numbers. Checked here against the real NpgsqlParameter.
/// No database needed.
/// </summary>
public class ProviderParameterFactoryNpgsqlTests
{
    private static NpgsqlParameter Configure(Type type, object value)
    {
        var parameter = new NpgsqlParameter { ParameterName = "p" };
        ProviderParameterFactory.TryConfigureParameter(parameter, type, value, SupportedDatabase.PostgreSql);
        return parameter;
    }

    [Fact]
    public void Guid_IsUuid()
    {
        var parameter = Configure(typeof(Guid), Guid.NewGuid());
        Assert.Equal(NpgsqlDbType.Uuid, parameter.NpgsqlDbType);
    }

    [Fact]
    public void JsonElement_IsJsonb()
    {
        using var document = JsonDocument.Parse("{\"a\":1}");
        var parameter = Configure(typeof(JsonElement), document.RootElement.Clone());
        Assert.Equal(NpgsqlDbType.Jsonb, parameter.NpgsqlDbType);
    }

    [Fact]
    public void StringArray_IsTextArray()
    {
        var parameter = Configure(typeof(string[]), new[] { "a", "b" });
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Text, parameter.NpgsqlDbType);
    }

    [Fact]
    public void IntArray_IsIntegerArray()
    {
        var parameter = Configure(typeof(int[]), new[] { 1, 2 });
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Integer, parameter.NpgsqlDbType);
    }

    [Fact]
    public void IntRange_IsIntegerRange()
    {
        var parameter = Configure(typeof(Range<int>), new Range<int>(1, 5));
        Assert.Equal(NpgsqlDbType.IntegerRange, parameter.NpgsqlDbType);
    }
}
