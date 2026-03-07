using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Text.Json;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class CoveragePush_TableGatewayAuditUpsertTests
{
    [Table("coverage_push_audit")]
    private sealed class AuditEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }
    }

    [Table("coverage_push_no_key")]
    private sealed class NoKeyEntity
    {
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("coverage_push_upsert_writable_id")]
    private sealed class WritableIdEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("coverage_push_upsert_pk_only")]
    private sealed class PrimaryKeyOnlyEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("code", DbType.String)]
        public string Code { get; set; } = string.Empty;
    }

    [Table("coverage_push_upsert_nonwritable_no_pk")]
    private sealed class NonWritableNoPkEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }

    private static readonly MethodInfo CoerceMethod =
        typeof(TableGateway<AuditEntity, int>).GetMethod("Coerce", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo IsDefaultTimestampMethod =
        typeof(TableGateway<AuditEntity, int>).GetMethod("IsDefaultTimestamp",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo SetAuditFieldsMethod =
        typeof(TableGateway<AuditEntity, int>).GetMethod("SetAuditFields",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(AuditEntity), typeof(bool) },
            null)!;

    private static readonly MethodInfo SetAuditFieldsWithValuesMethod =
        typeof(TableGateway<AuditEntity, int>).GetMethod("SetAuditFields",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(AuditEntity), typeof(bool), typeof(IAuditValues) },
            null)!;

    private static readonly MethodInfo GetFirebirdDataTypeMethod =
        typeof(TableGateway<AuditEntity, int>).GetMethod("GetFirebirdDataType",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo FormatFirebirdValueExpressionMethod =
        typeof(TableGateway<AuditEntity, int>).GetMethod("FormatFirebirdValueExpression",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void AuditHelpers_CoerceAndTimestampBranches_AreCovered()
    {
        var guid = Guid.NewGuid();
        var parsed = (Guid)CoerceMethod.Invoke(null, new object?[] { guid.ToString("D"), typeof(Guid) })!;
        Assert.Equal(guid, parsed);

        var converted = (int)CoerceMethod.Invoke(null, new object?[] { "42", typeof(int) })!;
        Assert.Equal(42, converted);

        var nullResult = CoerceMethod.Invoke(null, new object?[] { null, typeof(Guid) });
        Assert.Null(nullResult);

        var badGuid = Assert.Throws<TargetInvocationException>(() =>
            CoerceMethod.Invoke(null, new object?[] { "not-a-guid", typeof(Guid) }));
        Assert.IsType<InvalidOperationException>(badGuid.InnerException);

        var nonTimestamp = (bool)IsDefaultTimestampMethod.Invoke(null, new object?[] { new object() })!;
        Assert.False(nonTimestamp);
    }

    [Fact]
    public void SetAuditFields_NullEntity_ReturnsEarly()
    {
        using var context = CreateContext(SupportedDatabase.Sqlite);
        var gateway = new TableGateway<AuditEntity, int>(context);

        SetAuditFieldsMethod.Invoke(gateway, new object?[] { null, false });
        SetAuditFieldsWithValuesMethod.Invoke(gateway, new object?[] { null, false, null });
    }

    [Fact]
    public void BuildUpsert_WithoutIdOrPrimaryKey_ThrowsNotSupported()
    {
        using var context = CreateContext(SupportedDatabase.Sqlite);
        var gateway = new TableGateway<WritableIdEntity, int>(context);
        var idField = typeof(TableGateway<WritableIdEntity, int>).GetField("_idColumn",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(idField);
        idField!.SetValue(gateway, null);
        var entity = new WritableIdEntity { Id = 1, Name = "x" };

        var ex = Assert.Throws<NotSupportedException>(() => gateway.BuildUpsert(entity));
        Assert.Contains("Upsert requires", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveUpsertKey_CoversWritableId_PrimaryKey_AndThrowBranches()
    {
        using var context = CreateContext(SupportedDatabase.Sqlite);

        var writableGateway = new TableGateway<WritableIdEntity, int>(context);
        var writableMethod = typeof(TableGateway<WritableIdEntity, int>).GetMethod(
            "ResolveUpsertKey",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var writableKeys = Assert.IsAssignableFrom<IReadOnlyList<IColumnInfo>>(writableMethod.Invoke(writableGateway, null));
        Assert.Single(writableKeys);
        Assert.True(writableKeys[0].IsId);

        var primaryGateway = new TableGateway<PrimaryKeyOnlyEntity, int>(context);
        var primaryMethod = typeof(TableGateway<PrimaryKeyOnlyEntity, int>).GetMethod(
            "ResolveUpsertKey",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var primaryKeys = Assert.IsAssignableFrom<IReadOnlyList<IColumnInfo>>(primaryMethod.Invoke(primaryGateway, null));
        Assert.Single(primaryKeys);
        Assert.True(primaryKeys[0].IsPrimaryKey);

        var nonWritableGateway = new TableGateway<NonWritableNoPkEntity, int>(context);
        var nonWritableMethod = typeof(TableGateway<NonWritableNoPkEntity, int>).GetMethod(
            "ResolveUpsertKey",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var ex = Assert.Throws<TargetInvocationException>(() => nonWritableMethod.Invoke(nonWritableGateway, null));
        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Theory]
    [InlineData(DbType.Boolean, "SMALLINT")]
    [InlineData(DbType.Byte, "SMALLINT")]
    [InlineData(DbType.SByte, "SMALLINT")]
    [InlineData(DbType.Int16, "SMALLINT")]
    [InlineData(DbType.UInt16, "SMALLINT")]
    [InlineData(DbType.Int32, "INTEGER")]
    [InlineData(DbType.UInt32, "BIGINT")]
    [InlineData(DbType.Int64, "BIGINT")]
    [InlineData(DbType.UInt64, "BIGINT")]
    [InlineData(DbType.Decimal, "DECIMAL(18,2)")]
    [InlineData(DbType.Double, "DOUBLE PRECISION")]
    [InlineData(DbType.Single, "DOUBLE PRECISION")]
    [InlineData(DbType.Date, "DATE")]
    [InlineData(DbType.DateTime, "TIMESTAMP")]
    [InlineData(DbType.AnsiStringFixedLength, "VARCHAR(255)")]
    [InlineData(DbType.AnsiString, "VARCHAR(255)")]
    [InlineData(DbType.String, "VARCHAR(255)")]
    [InlineData(DbType.StringFixedLength, "VARCHAR(255)")]
    [InlineData(DbType.Guid, "CHAR(36)")]
    [InlineData(DbType.Binary, "BLOB")]
    [InlineData(DbType.Object, "VARCHAR(255)")]
    public void FirebirdDataTypeMapping_CoversSwitchCases(DbType dbType, string expected)
    {
        var column = new StubColumnInfo { DbType = dbType };

        var resolved = (string)GetFirebirdDataTypeMethod.Invoke(null, new object[] { column })!;

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void FormatFirebirdValueExpression_CastsNullAndParameter()
    {
        var column = new StubColumnInfo { DbType = DbType.Boolean };

        var nullExpression = (string)FormatFirebirdValueExpressionMethod.Invoke(
            null,
            new object[] { "NULL", column })!;
        Assert.Equal("CAST(NULL AS SMALLINT)", nullExpression);

        var parameterExpression = (string)FormatFirebirdValueExpressionMethod.Invoke(
            null,
            new object[] { "@p0", column })!;
        Assert.Equal("CAST(@p0 AS SMALLINT)", parameterExpression);
    }

    private static DatabaseContext CreateContext(SupportedDatabase database)
    {
        return new DatabaseContext(
            $"Data Source=coverage-push;EmulatedProduct={database}",
            new fakeDbFactory(database));
    }

    private sealed class FirebirdHolder
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class StubColumnInfo : IColumnInfo
    {
        private static readonly PropertyInfo ValueProperty =
            typeof(FirebirdHolder).GetProperty(nameof(FirebirdHolder.Value))!;

        public string Name { get; init; } = "value";
        public PropertyInfo PropertyInfo { get; init; } = ValueProperty;
        public bool IsId { get; init; }
        public DbType DbType { get; set; }
        public bool IsNonUpdateable { get; set; }
        public bool IsNonInsertable { get; set; }
        public bool IsEnum { get; set; }
        public Type? EnumType { get; set; }
        public Type? EnumUnderlyingType { get; set; }
        public bool EnumAsString { get; set; }
        public bool IsJsonType { get; set; }
        public JsonSerializerOptions JsonSerializerOptions { get; set; } = new();
        public bool IsIdWritable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsCorrelationToken { get; set; }
        public int PkOrder { get; set; }
        public bool IsVersion { get; set; }
        public bool IsCreatedBy { get; set; }
        public bool IsCreatedOn { get; set; }
        public bool IsLastUpdatedBy { get; set; }
        public bool IsLastUpdatedOn { get; set; }
        public int Ordinal { get; set; }

        public object? MakeParameterValueFromField<T>(T objectToCreate)
        {
            return ValueProperty.GetValue(objectToCreate);
        }
    }
}
