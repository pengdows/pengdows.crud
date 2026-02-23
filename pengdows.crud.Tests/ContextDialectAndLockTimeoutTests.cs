using System;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TDD tests for:
///   1. IDatabaseContext.Dialect — the dialect must be accessible via the interface,
///      not only through an internal ISqlDialectProvider cast.
///   2. IDatabaseContext.ModeLockTimeout — completion/mode lock timeout must be
///      configured through context rather than a hardcoded constant.
///   3. TransactionContext.CompletionLockTimeoutSeconds — the hardcoded const must
///      be removed; TransactionContext must delegate to its parent context's
///      ModeLockTimeout instead.
/// </summary>
public class ContextDialectAndLockTimeoutTests
{
    private static DatabaseContext MakeContext(TimeSpan? modeLockTimeout = null)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            ModeLockTimeout = modeLockTimeout
        };
        return new DatabaseContext(config, factory);
    }

    // -------------------------------------------------------------------------
    // IDatabaseContext must expose Dialect
    // -------------------------------------------------------------------------

    [Fact]
    public void IDatabaseContext_HasDialectProperty()
    {
        var prop = typeof(IDatabaseContext).GetProperty(
            nameof(IDatabaseContext.Dialect),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(prop);
        Assert.True(prop!.PropertyType.IsAssignableTo(typeof(ISqlDialect)),
            "Dialect property must be of type ISqlDialect");
    }

    [Fact]
    public void DatabaseContext_Dialect_AccessibleViaInterface()
    {
        using IDatabaseContext ctx = MakeContext();
        Assert.NotNull(ctx.Dialect);
    }

    [Fact]
    public void TransactionContext_Dialect_MatchesParent()
    {
        using var ctx = MakeContext();
        using var tx = ctx.BeginTransaction();
        IDatabaseContext txCtx = tx;

        Assert.Same(ctx.Dialect, txCtx.Dialect);
    }

    // -------------------------------------------------------------------------
    // IDatabaseContext must expose ModeLockTimeout
    // -------------------------------------------------------------------------

    [Fact]
    public void IDatabaseContext_HasModeLockTimeoutProperty()
    {
        var prop = typeof(IDatabaseContext).GetProperty(
            nameof(IDatabaseContext.ModeLockTimeout),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(prop);
        Assert.Equal(typeof(TimeSpan?), prop!.PropertyType);
    }

    [Fact]
    public void DatabaseContext_ModeLockTimeout_Default_IsConfigDefault()
    {
        // Use a plain DatabaseContextConfiguration (not via MakeContext which always sets
        // ModeLockTimeout = null) so the config default applies.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite"
            // ModeLockTimeout intentionally not set → uses DefaultModeLockSeconds
        };
        using IDatabaseContext ctx = new DatabaseContext(config, factory);
        Assert.Equal(
            TimeSpan.FromSeconds(DatabaseContextConfiguration.DefaultModeLockSeconds),
            ctx.ModeLockTimeout);
    }

    [Fact]
    public void DatabaseContext_ModeLockTimeout_ReflectsConfiguredValue()
    {
        var configured = TimeSpan.FromSeconds(60);
        using IDatabaseContext ctx = MakeContext(modeLockTimeout: configured);
        Assert.Equal(configured, ctx.ModeLockTimeout);
    }

    [Fact]
    public void DatabaseContext_ModeLockTimeout_NullIsRespected()
    {
        // null means wait indefinitely
        using IDatabaseContext ctx = MakeContext(modeLockTimeout: null);

        // When null is passed the context stores null (not the default)
        Assert.Null(ctx.ModeLockTimeout);
    }

    [Fact]
    public void TransactionContext_ModeLockTimeout_ForwardsToParentContext()
    {
        var configured = TimeSpan.FromSeconds(45);
        using var ctx = MakeContext(modeLockTimeout: configured);
        using var tx = ctx.BeginTransaction();
        IDatabaseContext txCtx = tx;

        Assert.Equal(configured, txCtx.ModeLockTimeout);
    }

    [Fact]
    public void TransactionContext_ModeLockTimeout_NullForwardsToParent()
    {
        using var ctx = MakeContext(modeLockTimeout: null);
        using var tx = ctx.BeginTransaction();
        IDatabaseContext txCtx = tx;

        Assert.Null(txCtx.ModeLockTimeout);
    }

    // -------------------------------------------------------------------------
    // TransactionContext must NOT have a CompletionLockTimeoutSeconds constant
    // -------------------------------------------------------------------------

    [Fact]
    public void TransactionContext_DoesNotContain_CompletionLockTimeoutSecondsConst()
    {
        // The hardcoded const must be removed; timeout comes from parent context.
        var field = typeof(TransactionContext).GetField(
            "CompletionLockTimeoutSeconds",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.Null(field);
    }

    // -------------------------------------------------------------------------
    // ISqlDialect.TryEnterReadOnlyTransactionAsync must return ValueTask
    // -------------------------------------------------------------------------

    [Fact]
    public void ISqlDialect_TryEnterReadOnlyTransactionAsync_ReturnsValueTask()
    {
        var method = typeof(ISqlDialect).GetMethod(
            "TryEnterReadOnlyTransactionAsync",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(ValueTask), method!.ReturnType);
    }

    // -------------------------------------------------------------------------
    // ISqlDialect.WrapSimpleName — fast path for attribute-sourced identifiers
    // -------------------------------------------------------------------------

    [Fact]
    public void ISqlDialect_HasWrapSimpleName_Method()
    {
        var method = typeof(ISqlDialect).GetMethod(
            "WrapSimpleName",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(string), method!.ReturnType);
    }

    [Fact]
    public void ISqlDialect_WrapSimpleName_ReturnsQuotePrefixNameQuoteSuffix()
    {
        using var ctx = MakeContext();
        var dialect = ctx.Dialect!;
        var expected = dialect.QuotePrefix + "my_col" + dialect.QuoteSuffix;
        Assert.Equal(expected, dialect.WrapSimpleName("my_col"));
    }

    [Fact]
    public void ISqlDialect_WrapSimpleName_MatchesWrapObjectNameForSimpleIdentifiers()
    {
        using var ctx = MakeContext();
        var dialect = ctx.Dialect!;
        // For simple single-part names, WrapSimpleName must produce the same result as WrapObjectName.
        Assert.Equal(dialect.WrapObjectName("my_col"), dialect.WrapSimpleName("my_col"));
    }

    // -------------------------------------------------------------------------
    // TableGateway accesses Dialect via context.Dialect, not internal cast
    // -------------------------------------------------------------------------

    [Table("DialectAccessEntity")]
    private class DialectAccessEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = "";
    }

    [Fact]
    public void TableGateway_Build_UsesDialectFromContextDirectly()
    {
        // If TableGateway used the internal ISqlDialectProvider cast, this would
        // break if IDatabaseContext doesn't implement that interface. By using
        // context.Dialect instead, any IDatabaseContext implementation works.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<DialectAccessEntity>();
        using var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite),
            typeMap);

        // BuildCreate exercises the dialect path in TableGateway
        var gw = new TableGateway<DialectAccessEntity, int>(ctx);
        var entity = new DialectAccessEntity { Name = "test" };
        var sc = gw.BuildCreate(entity);

        Assert.NotNull(sc);
        Assert.Contains("INSERT", sc.Query.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}