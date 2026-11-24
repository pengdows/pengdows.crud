using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// API-009 (docs/FUTURE_WORK.md): every public type in the core assembly has a recorded decision. A type added later
/// fails the first test until someone decides whether it is public API (Keep), a 3.0 removal (Remove3_0, already
/// [Obsolete] in 2.0.6), or a base class consumers derive from (BaseClass). The second test fails for a register entry
/// that names a type that does not exist, so the register cannot silently drift from the code.
/// </summary>
public class PublicTypeAuditRegisterTests
{
    private enum Decision
    {
        /// <summary>Intended public API: a consumer uses it, is an extension point, a documented value or result, or is
        /// reachable from a shipped public signature (so it cannot leave 2.x).</summary>
        Keep,

        /// <summary>Internal plumbing shipped in 2.0.5. Deprecated ([Obsolete]) in 2.0.6; removed in 3.0.</summary>
        Remove3_0,

        /// <summary>Base class consumers derive from. Stays public.</summary>
        BaseClass,
    }

    private static readonly Dictionary<string, Decision> Register = new()
    {
        // Entry points, the gateway and context surface, and the result types they return.
        ["pengdows.crud.DatabaseContext"] = Decision.Keep,
        ["pengdows.crud.DatabaseContextColumnNameExtensions"] = Decision.Keep,
        ["pengdows.crud.TableGateway`2"] = Decision.Keep,
        ["pengdows.crud.PrimaryKeyTableGateway`1"] = Decision.Keep,
        ["pengdows.crud.BaseTableGateway`1"] = Decision.BaseClass,
        ["pengdows.crud.ContextBase"] = Decision.BaseClass,
        ["pengdows.crud.infrastructure.SafeAsyncDisposableBase"] = Decision.BaseClass,
        ["pengdows.crud.SqlContainer"] = Decision.Keep,
        ["pengdows.crud.SqlContainerExtensions"] = Decision.Keep,
        ["pengdows.crud.TransactionContext"] = Decision.Keep,
        ["pengdows.crud.AuditValues"] = Decision.Keep,
        ["pengdows.crud.DataReaderMapper"] = Decision.Keep,
        ["pengdows.crud.MapperOptions"] = Decision.Keep,
        ["pengdows.crud.TypeCoercionOptions"] = Decision.Remove3_0,
        ["pengdows.crud.Uuid7Options"] = Decision.Keep,
        ["pengdows.crud.Uuid7ClockMode"] = Decision.Keep,
        ["pengdows.crud.Uuid7Optimized"] = Decision.Keep,
        ["pengdows.crud.EphemeralSecureString"] = Decision.Remove3_0,

        // Extension points consumers implement or use.
        ["pengdows.crud.AuditValueResolver"] = Decision.Keep,
        ["pengdows.crud.tenant.TenantConfiguration"] = Decision.Keep,
        ["pengdows.crud.tenant.TenantConnectionResolver"] = Decision.Keep,
        ["pengdows.crud.tenant.TenantContextRegistry"] = Decision.Keep,
        ["pengdows.crud.tenant.MultiTenantOptions"] = Decision.Keep,
        ["pengdows.crud.tenant.TenantServiceCollectionExtensions"] = Decision.Keep,
        ["pengdows.crud.tenant.ITenantConfiguration"] = Decision.Remove3_0,

        // Configuration and metrics options.
        ["pengdows.crud.configuration.DatabaseContextConfiguration"] = Decision.Keep,
        ["pengdows.crud.configuration.DatabaseProviderConfig"] = Decision.Keep,
        ["pengdows.crud.configuration.DbProviderLoaderServiceCollectionExtensions"] = Decision.Keep,
        ["pengdows.crud.configuration.DbProviderLoader"] = Decision.Remove3_0,
        ["pengdows.crud.metrics.MetricsOptions"] = Decision.Keep,
        ["pengdows.crud.metrics.ModeContentionSnapshot"] = Decision.Keep,
        ["pengdows.crud.connection.ConnectionLocalState"] = Decision.Remove3_0,

        // Mapping attributes that work today.
        ["pengdows.crud.attributes.TableAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.ColumnAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.IdAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.PrimaryKeyAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.VersionAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.CreatedByAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.CreatedOnAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.LastUpdatedByAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.LastUpdatedOnAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.NonInsertableAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.NonUpdateableAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.CorrelationTokenAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.JsonAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.EnumColumnAttribute"] = Decision.Keep,
        ["pengdows.crud.attributes.EnumLiteralAttribute"] = Decision.Keep,

        // Inert attributes: no effect, deprecated in 2.0.6, removed in 3.0.
        ["pengdows.crud.types.attributes.AllowZeroDateAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.AsStringAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.CaseFoldOnReadAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.CaseInsensitiveAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.ComputedAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.ConcurrencyTokenAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.CurrencyAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.DbEnumAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.EnumStorage"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.JsonContractAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.MaxLengthForInlineAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.RangeTypeAttribute"] = Decision.Remove3_0,
        ["pengdows.crud.types.attributes.SpatialTypeAttribute"] = Decision.Remove3_0,

        // Value types the type matrix documents, and the coercion type a shipped public interface takes.
        ["pengdows.crud.types.valueobjects.Geography"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.Geometry"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.SpatialValue"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.SpatialFormat"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.HierarchyId"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.Range`1"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.RowVersion"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.Cidr"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.Inet"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.MacAddress"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.HStore"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.JsonValue"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.IntervalDaySecond"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.IntervalYearMonth"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.PostgreSqlInterval"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.PostgreSqlPoint"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.PostgreSqlLine"] = Decision.Keep,
        ["pengdows.crud.types.valueobjects.PostgreSqlPolygon"] = Decision.Keep,
        ["pengdows.crud.types.coercion.DbValue"] = Decision.Keep,

        // Exceptions (the documented exception hierarchy; users catch these).
        ["pengdows.crud.exceptions.PrimaryKeyOnRowIdColumn"] = Decision.Keep,
        ["pengdows.crud.exceptions.DatabaseException"] = Decision.Keep,
        ["pengdows.crud.exceptions.DatabaseOperationException"] = Decision.Keep,
        ["pengdows.crud.exceptions.ConstraintViolationException"] = Decision.Keep,
        ["pengdows.crud.exceptions.UniqueConstraintViolationException"] = Decision.Keep,
        ["pengdows.crud.exceptions.ForeignKeyViolationException"] = Decision.Keep,
        ["pengdows.crud.exceptions.NotNullViolationException"] = Decision.Keep,
        ["pengdows.crud.exceptions.CheckConstraintViolationException"] = Decision.Keep,
        ["pengdows.crud.exceptions.TransientWriteConflictException"] = Decision.Keep,
        ["pengdows.crud.exceptions.DeadlockException"] = Decision.Keep,
        ["pengdows.crud.exceptions.SerializationConflictException"] = Decision.Keep,
        ["pengdows.crud.exceptions.AmbiguousResultException"] = Decision.Keep,
        ["pengdows.crud.exceptions.ConcurrencyConflictException"] = Decision.Keep,
        ["pengdows.crud.exceptions.CommandTimeoutException"] = Decision.Keep,
        ["pengdows.crud.exceptions.ConnectionException"] = Decision.Keep,
        ["pengdows.crud.exceptions.ConnectionFailedException"] = Decision.Keep,
        ["pengdows.crud.exceptions.FileLockContentionException"] = Decision.Keep,
        ["pengdows.crud.exceptions.TooManyConnectionsException"] = Decision.Keep,
        ["pengdows.crud.exceptions.ReadOnlyViolationException"] = Decision.Keep,
        ["pengdows.crud.exceptions.ReadOnlyContextException"] = Decision.Keep,
        ["pengdows.crud.exceptions.ReadOnlyAccessException"] = Decision.Keep,
        ["pengdows.crud.exceptions.TransactionException"] = Decision.Keep,
        ["pengdows.crud.exceptions.TransactionModeNotSupportedException"] = Decision.Keep,
        ["pengdows.crud.exceptions.SqlGenerationException"] = Decision.Keep,
        ["pengdows.crud.exceptions.DataMappingException"] = Decision.Keep,
        ["pengdows.crud.exceptions.ModeContentionException"] = Decision.Keep,
        ["pengdows.crud.exceptions.PoolSaturatedException"] = Decision.Keep,
        ["pengdows.crud.exceptions.PoolForbiddenException"] = Decision.Keep,
        ["pengdows.crud.exceptions.TooManyParametersException"] = Decision.Keep,
        ["pengdows.crud.exceptions.InvalidValueException"] = Decision.Keep,
        ["pengdows.crud.exceptions.NoColumnsFoundException"] = Decision.Keep,
        ["pengdows.crud.exceptions.TooManyColumns"] = Decision.Keep,
    };

    [Fact]
    public void EveryPublicTypeInTheCoreAssembly_HasARecordedDecision()
    {
        var assembly = typeof(DatabaseContext).Assembly;
        var undecided = assembly.GetTypes()
            .Where(t => t.IsPublic && !(t.Namespace ?? string.Empty).StartsWith("pengdows.crud.@internal", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .Where(name => !Register.ContainsKey(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(undecided.Count == 0,
            $"{undecided.Count} public type(s) in pengdows.crud have no recorded decision:" + Environment.NewLine +
            string.Join(Environment.NewLine, undecided));
    }

    [Fact]
    public void EveryRegisterEntry_NamesARealPublicType()
    {
        var assembly = typeof(DatabaseContext).Assembly;
        var stale = Register.Keys
            .Where(name => assembly.GetType(name) is not { IsPublic: true })
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            $"{stale.Count} register entr(ies) name no public type:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }
}
