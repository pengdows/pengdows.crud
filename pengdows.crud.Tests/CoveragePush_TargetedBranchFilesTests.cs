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
            false
        }));
        var enabledSnapshot = enabled.GetSnapshot();
        Assert.False(enabledSnapshot.Disabled);
        Assert.False(enabledSnapshot.Forbidden);
        Assert.Equal(2, enabledSnapshot.MaxSlots);
    }

    [Fact]
    public void DatabaseContext_ConnectionMapBuilder_InvalidConnectionString_ReturnsFalse()
    {
        var method = typeof(DatabaseContext).GetMethod(
            "TryBuildNormalizedConnectionMap",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var args = new object?[]
        {
            "bad-connection-string-without-equals",
            null,
            null,
            null,
            "-ro",
            null
        };

        var ok = (bool)method!.Invoke(null, args)!;
        Assert.False(ok);
        Assert.IsType<Dictionary<string, string>>(args[^1]);
        Assert.Empty((Dictionary<string, string>)args[^1]!);
    }

    [Fact]
    public void DatabaseContext_InitializePoolGovernors_WhenDialectNull_DisablesGovernors()
    {
        var context = (DatabaseContext)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(DatabaseContext));
        SetField(context, "_dialect", null);

        var method = typeof(DatabaseContext).GetMethod("InitializePoolGovernors", AnyInstance);
        Assert.NotNull(method);
        method!.Invoke(context, null);

        Assert.False((bool)GetField(context, "_effectivePoolGovernorEnabled")!);
        Assert.Null(GetField(context, "_readerGovernor"));
        Assert.Null(GetField(context, "_writerGovernor"));
    }

    [Fact]
    public void DatabaseContext_InitializePoolGovernors_SingleConnectionDisablesGovernors()
    {
        var context = (DatabaseContext)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(DatabaseContext));
        var dialect = new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger<SqlDialect>.Instance);

        SetField(context, "_dialect", dialect);
        SetField(context, "_connectionString", "Data Source=test;EmulatedProduct=Sqlite");
        SetField(context, "_readerConnectionString", "Data Source=test;EmulatedProduct=Sqlite");
        SetField(context, "_isWriteConnection", true);
        SetProperty(context, "ConnectionMode", DbMode.SingleConnection);

        var method = typeof(DatabaseContext).GetMethod("InitializePoolGovernors", AnyInstance);
        Assert.NotNull(method);
        method!.Invoke(context, null);

        Assert.False((bool)GetField(context, "_effectivePoolGovernorEnabled")!);
        Assert.Null(GetField(context, "_readerGovernor"));
        Assert.Null(GetField(context, "_writerGovernor"));
    }

    [Fact]
    public void SqlContainer_WrapForStoredProc_IncludeParametersFalse_OmitsArguments()
    {
        using var ctx = CreateContext(SupportedDatabase.SqlServer);
        var container = ctx.CreateSqlContainer("proc_name");
        container.AddParameterWithValue("p0", DbType.Int32, 1);
        container.AddParameterWithValue("p1", DbType.String, "x");

        var sql = container.WrapForStoredProc(ExecutionType.Write, includeParameters: false);
        Assert.DoesNotContain("@p0", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@p1", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlContainer_RenderParamsDeduplicating_RewritesDuplicateNamesForOracle()
    {
        using var ctx = CreateContext(SupportedDatabase.Oracle);
        var container = ctx.CreateSqlContainer();
        container.AddParameterWithValue("id", DbType.Int32, 7);

        var method = typeof(SqlContainer).GetMethod("RenderParamsDeduplicating", AnyInstance);
        Assert.NotNull(method);

        var rendered = Assert.IsType<string>(method!.Invoke(container, new object[]
        {
            "SELECT * FROM t WHERE id={P}id OR backup_id={P}id"
        }));

        Assert.Contains(":id", rendered);
        Assert.Contains(":id_2", rendered);
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
        Assert.IsType<int[]>(p0);
    }

    [Fact]
    public void TableGateway_BuildDeleteDirect_WithoutIdColumn_Throws()
    {
        using var ctx = CreateContext(SupportedDatabase.Sqlite);
        var gateway = new TableGateway<PrimaryKeyOnlyEntity, int>(ctx);

        var method = typeof(TableGateway<PrimaryKeyOnlyEntity, int>)
            .GetMethod("BuildDeleteDirect", AnyInstance);
        Assert.NotNull(method);

        var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(gateway, new object?[] { 42, ctx }));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void TableGateway_BuildBatchCreate_FirebirdFallsBackToSingleRowContainers()
    {
        using var ctx = CreateContext(SupportedDatabase.Firebird);
        var gateway = new TableGateway<TestEntitySimple, int>(ctx);

        var containers = gateway.BuildBatchCreate(new List<TestEntitySimple>
        {
            new() { Name = "a" },
            new() { Name = "b" }
        });

        Assert.Equal(2, containers.Count);
    }

    [Fact]
    public void TableGateway_BuildBatchUpsert_MariaDb_DoesNotAppendIncomingAlias()
    {
        using var ctx = CreateContext(SupportedDatabase.MariaDb);
        var gateway = new TableGateway<CompositeUpsertEntity, int>(ctx);

        var containers = gateway.BuildBatchUpsert(new List<CompositeUpsertEntity>
        {
            new() { TenantId = 1, ExternalId = 10, Value = "v1" },
            new() { TenantId = 1, ExternalId = 11, Value = "v2" }
        });

        var sql = containers[0].Query.ToString();
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql);
        Assert.DoesNotContain(" AS incoming", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TypeCoercion_DateTimeOffsetHelpers_CoverFallbackAndErrorBranches()
    {
        var fromDateTime = TypeCoercionHelper.CoerceDateTimeOffsetFromString("2026-02-07 03:04:05");
        Assert.NotEqual(default, fromDateTime);

        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.CoerceDateTimeOffsetFromString("not-a-date"));

        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce(12345, typeof(int), typeof(DateTimeOffset)));
    }

    [Fact]
    public void TypeCoercion_ResolveCoercer_ForDateTypes_UsesDedicatedPaths()
    {
        var resolve = typeof(TypeCoercionHelper).GetMethod(
            "ResolveCoercer",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(Type), typeof(Type), typeof(EnumParseFailureMode) },
            null);

        Assert.NotNull(resolve);

        var toDateTimeOffset = Assert.IsType<Func<object?, object?>>(resolve!.Invoke(null,
            new object[] { typeof(string), typeof(DateTimeOffset), EnumParseFailureMode.Throw })!);
        var dto = Assert.IsType<DateTimeOffset>(toDateTimeOffset("2026-01-01T00:00:00Z")!);
        Assert.Equal(TimeSpan.Zero, dto.Offset);

        var toDateTime = Assert.IsType<Func<object?, object?>>(resolve.Invoke(null,
            new object[] { typeof(DateTimeOffset), typeof(DateTime), EnumParseFailureMode.Throw })!);
        var dt = Assert.IsType<DateTime>(toDateTime(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.FromHours(6)))!);
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
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
