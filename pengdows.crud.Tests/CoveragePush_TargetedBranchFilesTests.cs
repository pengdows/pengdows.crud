using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class CoveragePush_TargetedBranchFilesTests
{
    private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void DatabaseContext_CreateGovernor_CoversDisabledForbiddenAndEnabled()
    {
        var context = (DatabaseContext)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(DatabaseContext));
        SetField(context, "_poolAcquireTimeout", TimeSpan.FromMilliseconds(100));

        var createGovernor = typeof(DatabaseContext).GetMethod("CreateGovernor", AnyInstance);
        Assert.NotNull(createGovernor);

        var disabled = Assert.IsType<PoolGovernor>(createGovernor!.Invoke(context, new object?[]
        {
            PoolLabel.Writer,
            "writer",
            3,
            null,
            true,
            false,
            null,
            false,
            false,
            null,
            null,
            false
        }));
        var disabledSnapshot = disabled.GetSnapshot();
        Assert.True(disabledSnapshot.Disabled);
        Assert.False(disabledSnapshot.Forbidden);

        var forbidden = Assert.IsType<PoolGovernor>(createGovernor.Invoke(context, new object?[]
        {
            PoolLabel.Reader,
            "reader",
            0,
            null,
            false,
            false,
            null,
            false,
            false,
            null,
            null,
            false
        }));
        var forbiddenSnapshot = forbidden.GetSnapshot();
        Assert.False(forbiddenSnapshot.Disabled);
        Assert.True(forbiddenSnapshot.Forbidden);

        var enabled = Assert.IsType<PoolGovernor>(createGovernor.Invoke(context, new object?[]
        {
            PoolLabel.Writer,
            "writer2",
            2,
            null,
            false,
            true,
            null,
            false,
            false,
            null,
            null,
            false
        }));
        var enabledSnapshot = enabled.GetSnapshot();
        Assert.False(enabledSnapshot.Disabled);
        Assert.False(enabledSnapshot.Forbidden);
        Assert.Equal(2, enabledSnapshot.MaxSlots);
    }

    [Fact]
    public void TableGateway_GetRetrieveContainer_CoversSingleAndDoubleIdBranches()
    {
        using var sqlite = CreateContext(SupportedDatabase.Sqlite);
        using var pg = CreateContext(SupportedDatabase.PostgreSql);
        var sqliteGateway = new TableGateway<TestEntitySimple, int>(sqlite);
        var pgGateway = new TableGateway<TestEntitySimple, int>(pg);

        var methodSqlite = typeof(TableGateway<TestEntitySimple, int>)
            .GetMethod("GetRetrieveContainer", AnyInstance);
        Assert.NotNull(methodSqlite);

        var twoContainer = Assert.IsAssignableFrom<ISqlContainer>(methodSqlite!.Invoke(sqliteGateway,
            new object[] { new List<int> { 10, 20 }, sqlite })!);
        twoContainer.SetParameterValue("p0", 10);
        twoContainer.SetParameterValue("p1", 20);

        var oneContainer = Assert.IsAssignableFrom<ISqlContainer>(methodSqlite.Invoke(pgGateway,
            new object[] { new List<int> { 11 }, pg })!);
        var p0 = oneContainer.GetParameterValue("p0");
        // A single id binds a scalar "column = @p0" even on set-valued dialects: pushing an
        // array into the scalar template failed live on Npgsql 9 (BP-116, matches 3.0).
        Assert.IsType<int>(p0);
    }

    private static DatabaseContext CreateContext(SupportedDatabase db)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            DbMode = DbMode.SingleConnection
        };
        return new DatabaseContext(config, new fakeDbFactory(db), NullLoggerFactory.Instance);
    }

    private static void SetField(object target, string fieldName, object? value)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, AnyInstance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static object? GetField(object target, string fieldName)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, AnyInstance);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        var property = typeof(DatabaseContext).GetProperty(propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    [Table("composite_upsert")]
    private sealed class CompositeUpsertEntity
    {
        [PrimaryKey(1)]
        [Column("tenant_id", DbType.Int32)]
        public int TenantId { get; set; }

        [PrimaryKey(2)]
        [Column("external_id", DbType.Int32)]
        public int ExternalId { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }

    [Table("pk_only")]
    private sealed class PrimaryKeyOnlyEntity
    {
        [PrimaryKey(1)]
        [Column("key_id", DbType.Int32)]
        public int KeyId { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
