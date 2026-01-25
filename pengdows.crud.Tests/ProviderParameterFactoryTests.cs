using System;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

public class ProviderParameterFactoryTests
{
    private sealed class NpgsqlParameterStub : fakeDbParameter
    {
        public int NpgsqlDbType { get; set; }
    }

    private sealed class OracleParameterStub : fakeDbParameter
    {
    }

    private sealed class DecimalCoercion : DbCoercion<decimal>
    {
        public override bool TryRead(in DbValue src, out decimal value)
        {
            if (src.IsNull)
            {
                value = default;
                return false;
            }

            if (src.RawValue is decimal dec)
            {
                value = dec;
                return true;
            }

            value = default;
            return false;
        }

        public override bool TryWrite(decimal value, DbParameter parameter)
        {
            parameter.Value = value;
            parameter.DbType = DbType.Decimal;
            return true;
        }
    }

    private sealed class BoolCoercion : DbCoercion<bool>
    {
        public override bool TryRead(in DbValue src, out bool value)
        {
            value = false;
            return false;
        }

        public override bool TryWrite(bool value, DbParameter parameter)
        {
            parameter.Value = value;
            parameter.DbType = DbType.Boolean;
            return true;
        }
    }

    private sealed class IntArrayCoercion : DbCoercion<int[]>
    {
        public override bool TryRead(in DbValue src, out int[]? value)
        {
            value = null;
            return false;
        }

        public override bool TryWrite(int[]? value, DbParameter parameter)
        {
            parameter.Value = value ?? Array.Empty<int>();
            parameter.DbType = DbType.Object;
            return true;
        }
    }

    private sealed class JsonElementCoercion : DbCoercion<JsonElement>
    {
        public override bool TryRead(in DbValue src, out JsonElement value)
        {
            value = default;
            return false;
        }

        public override bool TryWrite(JsonElement value, DbParameter parameter)
        {
            parameter.Value = value.GetRawText();
            parameter.DbType = DbType.String;
            return true;
        }
    }

    private sealed class GuidCoercion : DbCoercion<Guid>
    {
        public override bool TryRead(in DbValue src, out Guid value)
        {
            value = Guid.Empty;
            return false;
        }

        public override bool TryWrite(Guid value, DbParameter parameter)
        {
            parameter.Value = value;
            parameter.DbType = DbType.Guid;
            return true;
        }
    }

    [Fact]
    public void TryConfigureParameter_ConfiguresNpgsqlGuid()
    {
        var parameter = new NpgsqlParameterStub();
        var value = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var registry = new CoercionRegistry();
        var configured = ProviderParameterFactory.TryConfigureParameter(parameter, typeof(Guid), value,
            SupportedDatabase.PostgreSql, registry);

        Assert.True(configured);
        Assert.Equal(value, parameter.Value);
        Assert.Equal(DbType.Guid, parameter.DbType);
        Assert.Equal(27, parameter.NpgsqlDbType); // UUID
    }

    [Fact]
    public void TryConfigureParameter_ConfiguresPostgresStringArray()
    {
        var parameter = new NpgsqlParameterStub();
        var value = new[] { "alpha", "beta" };

        var registry = new CoercionRegistry();
        var configured = ProviderParameterFactory.TryConfigureParameter(parameter, value.GetType(), value,
            SupportedDatabase.PostgreSql, registry);

        Assert.True(configured);
        Assert.Equal((1 << 30) | 16, parameter.NpgsqlDbType); // Array | Text
    }

    [Fact]
    public void TryConfigureParameter_ConfiguresSqlServerRowVersion()
    {
        var parameter = new fakeDbParameter { Size = 8 };
        var value = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var registry = new CoercionRegistry();
        var configured = ProviderParameterFactory.TryConfigureParameter(parameter, typeof(byte[]), value,
            SupportedDatabase.SqlServer, registry);

        Assert.True(configured);
        Assert.Equal(DbType.Binary, parameter.DbType);
        Assert.Equal(8, parameter.Size);
    }

    [Fact]
    public void TryConfigureParameter_ConfiguresOracleDecimal()
    {
        var parameter = new OracleParameterStub();

        var registry = new CoercionRegistry();
        registry.Register(new DecimalCoercion());

        var configured = ProviderParameterFactory.TryConfigureParameter(parameter, typeof(decimal), 12.34m,
            SupportedDatabase.Oracle, registry);

        Assert.True(configured);
        Assert.Equal(DbType.Decimal, parameter.DbType);
        Assert.Equal((byte)38, parameter.Precision);
        Assert.Equal((byte)10, parameter.Scale);
    }

    [Fact]
    public void TryConfigureParameter_ConfiguresDuckDbArray()
    {
        var parameter = new fakeDbParameter();
        var value = new[] { 1, 2, 3 };

        var registry = new CoercionRegistry();
        var configured = ProviderParameterFactory.TryConfigureParameter(parameter, value.GetType(), value,
            SupportedDatabase.DuckDB, registry);

        Assert.True(configured);
        Assert.Same(value, parameter.Value);
    }

    [Fact]
    public void TryConfigureParameter_ConfiguresPostgresIntArray()
    {
        var parameter = new NpgsqlParameterStub();
        var value = new[] { 1, 2 };
        var registry = new CoercionRegistry();
        registry.Register(new IntArrayCoercion());

        var configured = ProviderParameterFactory.TryConfigureParameter(parameter, value.GetType(), value,
            SupportedDatabase.PostgreSql, registry);

        Assert.True(configured);
        Assert.Same(value, parameter.Value);
        Assert.Equal((1 << 30) | 1, parameter.NpgsqlDbType);
    }

    [Fact]
    public void TryConfigureParameter_ConfiguresPostgresJsonElement()
    {
        using var doc = JsonDocument.Parse("{\"name\":\"value\"}");
        var element = doc.RootElement.Clone();
        var parameter = new NpgsqlParameterStub();
        var registry = new CoercionRegistry();
        registry.Register(new JsonElementCoercion());

        var configured = ProviderParameterFactory.TryConfigureParameter(parameter, typeof(JsonElement), element,
            SupportedDatabase.PostgreSql, registry);

        Assert.True(configured);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(14, parameter.NpgsqlDbType);
    }

    [Fact]
    public void TryConfigureParameter_ConfiguresMySqlBoolean()
    {
        var parameter = new fakeDbParameter();
        var registry = new CoercionRegistry();
        registry.Register(new BoolCoercion());

        var configured =
            ProviderParameterFactory.TryConfigureParameter(parameter, typeof(bool), true, SupportedDatabase.MySql,
                registry);

        Assert.True(configured);
        Assert.Equal(DbType.Byte, parameter.DbType);
        Assert.Equal(true, parameter.Value);
    }

    [Fact]
    public void TryConfigureParameter_ConfiguresSqliteGuid()
    {
        var parameter = new fakeDbParameter();
        var guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var registry = new CoercionRegistry();
        registry.Register(new GuidCoercion());

        var configured =
            ProviderParameterFactory.TryConfigureParameter(parameter, typeof(Guid), guid, SupportedDatabase.Sqlite,
                registry);

        Assert.True(configured);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(guid, parameter.Value);
    }
}