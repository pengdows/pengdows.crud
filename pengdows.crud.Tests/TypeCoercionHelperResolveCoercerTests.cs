using System;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

public class TypeCoercionHelperResolveCoercerTests
{
    private sealed class FieldTypeTarget
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class FieldTypeHolder
    {
        public FieldTypeTarget Payload { get; set; } = new();
    }

    private sealed class FieldTypeObserverCoercion : DbCoercion<FieldTypeTarget>
    {
        public Type? LastObservedDbType { get; private set; }

        public override bool TryRead(in DbValue src, out FieldTypeTarget? value)
        {
            LastObservedDbType = src.DbType;
            if (src.RawValue is string text)
            {
                value = new FieldTypeTarget { Value = text };
                return true;
            }

            value = null;
            return false;
        }

        public override bool TryWrite(FieldTypeTarget? value, DbParameter parameter) => false;
    }

    private sealed class StubColumnInfo : IColumnInfo
    {
        private static readonly PropertyInfo PayloadProperty =
            typeof(FieldTypeHolder).GetProperty(nameof(FieldTypeHolder.Payload))!;

        public string Name { get; init; } = "payload";
        public PropertyInfo PropertyInfo { get; init; } = PayloadProperty;
        public bool IsId { get; init; }
        public DbType DbType { get; set; }
        public bool IsNonUpdateable { get; set; }
        public bool IsNonInsertable { get; set; }
        public bool IsEnum { get; set; }
        public Type? EnumType { get; set; }
        public Type? EnumUnderlyingType { get; set; }
        public bool IsJsonType { get; set; }
        public JsonSerializerOptions JsonSerializerOptions { get; set; } = new();
        public bool IsIdIsWritable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public int PkOrder { get; set; }
        public bool IsVersion { get; set; }
        public bool IsCreatedBy { get; set; }
        public bool IsCreatedOn { get; set; }
        public bool IsLastUpdatedBy { get; set; }
        public bool IsLastUpdatedOn { get; set; }
        public int Ordinal { get; set; }
        public object? MakeParameterValueFromField<T>(T objectToCreate) => PayloadProperty.GetValue(objectToCreate);
    }

    [Fact]
    public void ResolveCoercer_UsesProvidedFieldTypeWhenObservingDbType()
    {
        var coercion = new FieldTypeObserverCoercion();
        CoercionRegistry.Shared.Register(coercion);

        var resolved = TypeCoercionHelper.ResolveCoercer(
            new StubColumnInfo(),
            SupportedDatabase.Sqlite,
            EnumParseFailureMode.Throw,
            TypeCoercionOptions.Default,
            fieldType: typeof(string));

        var result = resolved("payload");

        var typed = Assert.IsType<FieldTypeTarget>(result!);
        Assert.Equal("payload", typed.Value);
        Assert.Equal(typeof(string), coercion.LastObservedDbType);
    }
}
