using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.coercion;

public static class CoercionInfrastructureTests
{
    [Fact]
    public static void ProviderParameterFactory_ConfiguresNpgsqlSpecifics()
    {
        var parameter = new NpgsqlParameter();
        var guid = Guid.NewGuid();

        var configured = ProviderParameterFactory.TryConfigureParameter(
            parameter,
            typeof(Guid),
            guid,
            SupportedDatabase.PostgreSql);

        Assert.True(configured);
        Assert.Equal(guid, parameter.Value);
        Assert.Equal(27, parameter.NpgsqlDbType); // UUID constant used in implementation
    }

    [Fact]
    public static void CoercionRegistry_UsesProviderSpecificCoercionWhenRegistered()
    {
        var registry = new CoercionRegistry();
        var generic = new RecordingCoercion();
        var providerSpecific = new RecordingCoercion();
        registry.Register(generic);
        registry.Register(SupportedDatabase.PostgreSql, providerSpecific);

        var parameter = new FakeDbParameter();
        registry.TryWrite("ignored", parameter, SupportedDatabase.PostgreSql);

        Assert.True(providerSpecific.WasCalled);
        Assert.False(generic.WasCalled);
    }

    [Fact]
    public static void ParameterBindingRules_NormalizesBooleanForMySql()
    {
        var parameter = new FakeDbParameter();
        var applied = ParameterBindingRules.ApplyBindingRules(parameter, typeof(bool), true, SupportedDatabase.MySql);

        Assert.True(applied);
        Assert.Equal(DbType.Byte, parameter.DbType);
        Assert.Equal((byte)1, parameter.Value);
    }

    [Fact]
    public static void ParameterBindingRules_SerializesArraysToJsonForOtherProviders()
    {
        var parameter = new FakeDbParameter();
        var applied = ParameterBindingRules.ApplyBindingRules(parameter, typeof(int[]), new[] { 1, 2, 3 }, SupportedDatabase.SqlServer);

        Assert.True(applied);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("[1,2,3]", parameter.Value);
    }

    [Fact]
    public static void ParameterBindingRules_SetsSizeForLargeStrings()
    {
        var parameter = new FakeDbParameter();
        var large = new string('x', 9000);
        var applied = ParameterBindingRules.ApplyBindingRules(parameter, typeof(string), large, SupportedDatabase.SqlServer);

        Assert.True(applied);
        Assert.Equal(-1, parameter.Size);
    }

    [Fact]
    public static void TypeCoercionHelper_ConvertsEnumsAndJson()
    {
        var column = new ColumnInfo
        {
            Name = "Status",
            PropertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Status))!,
            EnumType = typeof(TestStatus),
            IsEnum = true
        };

        var coercedEnum = TypeCoercionHelper.Coerce("Active", typeof(string), column);
        Assert.Equal(TestStatus.Active, coercedEnum);

        var jsonPayload = JsonSerializer.Serialize(new { Value = 5 });
        var jsonColumn = new ColumnInfo
        {
            Name = nameof(TestEntity.JsonPayload),
            PropertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.JsonPayload))!,
            IsEnum = false,
            EnumType = null,
            IsJsonType = true,
            JsonSerializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        };
        var coercedJson = TypeCoercionHelper.Coerce(jsonPayload, typeof(string), jsonColumn) as JsonElement?;

        Assert.True(coercedJson.HasValue);
        Assert.Equal(5, coercedJson.Value.GetProperty("Value").GetInt32());
    }

    [Fact]
    public static void TypeCoercionHelper_FallsBackToCharArrayConversion()
    {
        var result = TypeCoercionHelper.Coerce("hello".ToCharArray(), typeof(char[]), typeof(string));
        Assert.Equal("hello", result);
    }

    [Fact]
    public static void CoercionRegistry_TryReadUsesRegisteredCoercion()
    {
        var registry = new CoercionRegistry();
        registry.Register(new GuidCoercion()); // ensure registration

        var guid = Guid.NewGuid();
        var dbValue = new DbValue(guid, typeof(Guid));

        Assert.True(registry.TryRead(dbValue, typeof(Guid), out var value));
        Assert.Equal(guid, value);
    }

    private sealed class TestEntity
    {
        public TestStatus Status { get; set; }
        public JsonElement JsonPayload { get; set; }
    }

    private enum TestStatus
    {
        Active,
        Disabled
    }

    private sealed class RecordingCoercion : IDbCoercion<string>
    {
        public bool WasCalled { get; private set; }

        public bool TryRead(in DbValue src, out string? value)
        {
            WasCalled = true;
            value = src.RawValue?.ToString();
            return true;
        }

        public bool TryWrite(string? value, DbParameter parameter)
        {
            WasCalled = true;
            parameter.Value = value ?? (object)DBNull.Value;
            return true;
        }

        public bool TryRead(in DbValue src, Type targetType, out object? value)
        {
            var success = TryRead(src, out var typed);
            value = typed;
            return success;
        }

        public bool TryWrite(object? value, DbParameter parameter)
        {
            WasCalled = true;
            parameter.Value = value ?? DBNull.Value;
            return true;
        }

        public Type TargetType => typeof(string);
    }

    private class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        private string? parameterName;
        private string? sourceColumn;

        [AllowNull]
        public override string ParameterName
        {
            get => parameterName ?? string.Empty;
            set => parameterName = value;
        }

        [AllowNull]
        public override string SourceColumn
        {
            get => sourceColumn ?? string.Empty;
            set => sourceColumn = value;
        }
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class NpgsqlParameter : FakeDbParameter
    {
        public int NpgsqlDbType { get; set; }
    }
}
