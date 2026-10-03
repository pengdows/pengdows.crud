// =============================================================================
// FILE: TableGateway.Core.cs
// PURPOSE: Core partial of TableGateway - the primary CRUD API for entities.
//          Contains constructor, initialization, and core infrastructure.
//
// AI SUMMARY:
// - TableGateway<TEntity, TRowID> is the main API for entity CRUD operations.
// - Derives from BaseTableGateway<TEntity>, which holds the shared fields (context,
//   dialect, tableInfo, caches), audit handling, and DataReader mapping.
// - This partial contains:
//   * Static initialization and TRowID type validation
//   * Constructor that takes IDatabaseContext and optional IAuditValueResolver
//   * Per-dialect ConditionalWeakTable caches for binders, SQL templates, and containers
//   * Create, Retrieve-by-id, and Delete operations
// - Partial class structure:
//   * Core.cs - This file
//   * Batch.cs - Batch create/update/upsert operations
//   * Retrieve.cs - SELECT operations
//   * Sql.cs - SQL generation helpers and templates
//   * Update.cs - UPDATE operations
//   * Upsert.cs - UPSERT operations
// - TRowID validation: Must be primitive integer, Guid, or string.
// - Thread-safe: Uses bounded caches and thread-safe data structures.
// - Uses TypeMapRegistry to get entity metadata (TableInfo).
// =============================================================================

#region

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.connection;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.exceptions;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Primary SQL-first CRUD gateway for table-mapped entities.
/// Provides SQL generation and CRUD operations for entities mapped to database tables.
/// </summary>
/// <typeparam name="TEntity">The entity type to operate on.</typeparam>
/// <typeparam name="TRowID">The row ID type (must be primitive integer, Guid, or string).</typeparam>
/// <remarks>
/// <para>
/// This is the primary table gateway API for entities with an <c>[Id]</c> row identifier.
/// Use <see cref="PrimaryKeyTableGateway{TEntity}"/> for entities keyed only by <c>[PrimaryKey]</c>.
/// </para>
/// </remarks>
public partial class TableGateway<TEntity, TRowID> :
    BaseTableGateway<TEntity>,
    ITableGateway<TEntity, TRowID> where TEntity : class, new()
{
    private const string EmptyIdListMessage = "List of IDs cannot be empty.";
    private const string UpsertNoKeyMessage = "Upsert requires an Id or a composite primary key.";
    private const string UpsertNoWritableKeyMessage = "Upsert requires client-assigned Id or [PrimaryKey] attributes.";

    static TableGateway()
    {
        ValidateRowIdType();
    }

    private IColumnInfo? _idColumn;

    // Scratch list the insert binder fills before AddParameters copies it; the fill and copy are
    // synchronous, so one list per thread replaces a list per BuildCreate (PERF-013).
    [ThreadStatic] private static List<DbParameter>? t_bindScratch;

    // Multitenancy background for every cache in this block: TableGateway is a singleton shared
    // across tenant contexts (see CLAUDE.md's multi-tenancy pattern: gateway.Method(entity,
    // tenantCtx)). Two tenants on different versions/capability sets of the same engine (e.g.
    // MySQL 8.0.18 vs 8.0.21) get distinct dialect instances with distinct
    // ProductInfo.ParsedVersion — a cache keyed only by the coarse SupportedDatabase enum let
    // whichever tenant's dialect built the entry first silently dictate SQL for every other
    // same-enum tenant, even when a dialect property is explicitly version-gated (e.g.
    // MySqlDialect.UpsertIncomingAlias).
    //
    // These three bake actual DbParameter construction into the cached delegate (see
    // CompiledBinderFactory — it closes over the dialect instance itself via Expression.Constant
    // and keeps calling dialect.CreateDbParameter for the lifetime of the cache entry), so they
    // stay keyed by dialect INSTANCE (ConditionalWeakTable, reclaimed once a tenant's
    // DatabaseContext/dialect is no longer referenced) rather than a version fingerprint —
    // CreateDbParameter's behavior also depends on the live DbProviderFactory instance and, for
    // Firebird, GuidStorageMode, neither of which a simple DatabaseType+version fingerprint
    // captures. See docs/FUTURE_WORK.md's fingerprint-audit entry before changing this.
    private readonly ConditionalWeakTable<ISqlDialect, CompiledBinderFactory<TEntity>.Binder> _insertBinders = new();
    private readonly ConditionalWeakTable<ISqlDialect, CompiledBinderFactory<TEntity>.Binder> _upsertBinders = new();
    private readonly ConditionalWeakTable<ISqlDialect, CompiledBinderFactory<TEntity>.UpdateBinder> _updateBinders = new();

    // Per-dialect templates are cached in _templatesByDialect

    // SQL templates cached per dialect INSTANCE to support context overrides. A
    // ConditionalWeakTable (not a ConcurrentDictionary) ties each entry's lifetime to its dialect
    // instance so the cache doesn't grow without bound as contexts are created/disposed, while
    // still keeping differently-versioned servers of the same product from sharing stale SQL.
    private readonly ConditionalWeakTable<ISqlDialect, Lazy<CachedSqlTemplates>> _templatesByDialect = new();

    // Pre-built SqlContainer cache for common operations (GetById, GetByIds, etc.)
    private readonly ConditionalWeakTable<ISqlDialect, Lazy<CachedContainerTemplates>> _containersByDialect =
        new();


    // Unified constructor accepting optional audit resolver and optional logger (by name)
    public TableGateway(IDatabaseContext databaseContext,
        IAuditValueResolver? auditValueResolver = null,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw,
        ILogger? logger = null)
        : base(databaseContext, auditValueResolver, enumParseBehavior, logger)
    {
        // Id-specific initialization: locate the [Id] column after the base constructor ran
        _idColumn = _tableInfo.Columns.Values.FirstOrDefault(itm => itm.IsId);
    }


    /// <inheritdoc/>
    public ValueTask<bool> CreateAsync(TEntity entity)
    {
        return CreateAsync(entity, _context);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        // BuildCreate (called below via every branch) mutates audit fields as a side effect of
        // building the INSERT, before anything executes. Restore them if the write never
        // actually succeeds, so a failed Create doesn't leave the entity claiming one did.
        // writeSucceeded is set the instant the database has accepted the persisting INSERT
        // (the write command completed without throwing) — some branches then do a follow-up
        // step (retrieving a generated ID) that can itself throw. Once the write has succeeded, a
        // later failure in that follow-up step
        // must NOT restore audit fields — the row already exists with the new values; restoring
        // would make the entity falsely claim a rollback that never happened. A plain local bool
        // can't be observed from the shared catch below across the ExecuteReaderInsertedIdAsync
        // branch's async call, hence the one-element array as a simple mutable cell.
        var auditSnapshot = SnapshotAuditFields(entity);
        var writeSucceeded = new bool[1];
        try
        {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var plan = dialect.GetGeneratedKeyPlan();

        // 1. Handle PREFETCH plans (InterBase)
        //
        // Only prefetch (and overwrite the entity's Id) when the column is NOT client-writable —
        // mirrors branch 2's own IsIdWritable check below. CONFIRMED live via InterBase (the first
        // dialect in this codebase where GetGeneratedKeyPlan actually returns PrefetchSequence in
        // practice — Oracle's own override resolves to Returning instead, so this branch had never
        // been exercised with a writable Id column before): without this check, an entity that
        // explicitly sets its Id (TestTable's [Id] default, Writable=true) had that value silently
        // discarded and replaced with a freshly generated one, so any caller who inserted with an
        // explicit id then looked the row up again by that same id got "not found" — the row was
        // there, just under a different id than the caller was told to expect.
        if (plan == GeneratedKeyPlan.PrefetchSequence && _idColumn != null && !_idColumn.IsIdWritable)
        {
            var seqQuery = dialect.GetSequenceNextValQuery(GetSequenceName());
            using var seqSc = ctx.CreateSqlContainer(seqQuery);
            // Advancing a sequence is a write: a read-only connection rejects it (pengdows.flatfile).
            var nextVal = await seqSc.ExecuteScalarRequiredAsync<object>(ExecutionType.Write).ConfigureAwait(false);
            var converted = TypeCoercionHelper.ConvertWithCache(nextVal, _idColumn.PropertyInfo.PropertyType);
            _idColumn.PropertyInfo.SetValue(entity, converted);

            await using var sc = BuildCreateWithPrefetchedId(entity, ctx, dialect);
            var succeeded = await sc.ExecuteNonQueryAsync().ConfigureAwait(false) == 1;
            writeSucceeded[0] = succeeded;
            return RestoreAuditFieldsIfFailed(succeeded, entity, auditSnapshot);
        }

        if (plan == GeneratedKeyPlan.PrefetchSequence && _idColumn != null && _idColumn.IsIdWritable)
        {
            await using var sc = BuildCreate(entity, ctx);
            var succeeded = await sc.ExecuteNonQueryAsync().ConfigureAwait(false) == 1;
            writeSucceeded[0] = succeeded;
            return RestoreAuditFieldsIfFailed(succeeded, entity, auditSnapshot);
        }

        // 2. Handle INLINE plans (Postgres, SQL Server, etc.)
        if ((plan == GeneratedKeyPlan.Returning || plan == GeneratedKeyPlan.OutputInserted) &&
            _idColumn != null && !_idColumn.IsIdWritable)
        {
            await using var idLease = await AcquireGeneratedIdLeaseAsync(ctx, CancellationToken.None).ConfigureAwait(false);
            await using var sc = BuildCreateWithReturning(entity, true, ctx);
            PinTo(sc, idLease);

            object? generatedId;
            if (dialect.RequiresOutputParameterForReturning())
            {
                await sc.ExecuteNonQueryAsync(ExecutionType.Write).ConfigureAwait(false);

                // The INSERT above executed without throwing — the database accepted the write
                // regardless of whether reading the OUT parameter below succeeds.
                writeSucceeded[0] = true;
                generatedId = sc.GetParameterValue(OracleReturningParameterName);
            }
            else
            {
                generatedId = await sc.ExecuteScalarOrNullAsync<object>(ExecutionType.Write).ConfigureAwait(false);

                // The statement above executed without throwing — the database accepted the
                // write regardless of whether a generated ID came back inline.
                writeSucceeded[0] = true;
            }

            if (generatedId != null && generatedId != DBNull.Value)
            {
                var targetType = _idColumn.PropertyInfo.PropertyType;
                var converted = TypeCoercionHelper.ConvertWithCache(generatedId, targetType);
                _idColumn.PropertyInfo.SetValue(entity, converted);
                return true;
            }

            // Fallback on the INSERT's own connection (CORE-016)
            await PopulateGeneratedIdAsync(entity, ctx, CancellationToken.None, idLease).ConfigureAwait(false);
            return true;
        }

        // DEC-007: the correlation token is this dialect's only way to read a generated id back.
        // Without one the INSERT would succeed and leave the id unset, so refuse before writing.
        if (plan == GeneratedKeyPlan.CorrelationToken && _tableInfo.CorrelationColumn == null &&
            _idColumn != null && !_idColumn.IsIdWritable &&
            dialect is SqlDialect { RequiresCorrelationTokenForGeneratedIds: true })
        {
            throw MissingCorrelationTokenException(dialect);
        }

        // 3. Handle CORRELATION TOKEN plan
        if (plan == GeneratedKeyPlan.CorrelationToken && _tableInfo.CorrelationColumn != null && _idColumn != null)
        {
            var token = CreateCorrelationToken();
            _tableInfo.CorrelationColumn.PropertyInfo.SetValue(entity, token);

            await using var sc = BuildCreate(entity, ctx);
            if (await sc.ExecuteNonQueryAsync().ConfigureAwait(false) != 1)
            {
                RestoreAuditFields(entity, auditSnapshot);
                return false;
            }

            writeSucceeded[0] = true;

            var lookupSql = dialect.GetCorrelationTokenLookupQuery(
                _tableInfo.Name,
                _idColumn.Name,
                _tableInfo.CorrelationColumn.Name,
                dialect.MakeParameterName("p1"));

            using var lookupSc = ctx.CreateSqlContainer(lookupSql);
            lookupSc.AddParameterWithValue("p1", _tableInfo.CorrelationColumn.DbType, token);

            var generatedId = await lookupSc.ExecuteScalarRequiredAsync<object>(ExecutionType.Read).ConfigureAwait(false);
            var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
            _idColumn.PropertyInfo.SetValue(entity, converted);
            return true;
        }

        // 4. Compound statement plan (MySQL Oracle MySql.Data, SQLite pre-3.35).
        // Appends the dialect's session-scoped ID query (e.g. "; SELECT LAST_INSERT_ID()")
        // to the INSERT and executes both as a single batch on one connection.
        // This fixes the two-lease hazard: LAST_INSERT_ID() / last_insert_rowid() are
        // session-scoped; a separate pool lease could return a stale or zero value.
        if (plan == GeneratedKeyPlan.CompoundStatement && _idColumn != null && !_idColumn.IsIdWritable)
        {
            await using var idLease = await AcquireGeneratedIdLeaseAsync(ctx, CancellationToken.None).ConfigureAwait(false);
            await using var sc = BuildCreate(entity, ctx);
            PinTo(sc, idLease);
            sc.Query.Append(dialect.GetCompoundInsertIdSuffix());

            // Scope the reader so it is closed before the fallback query, which runs on the same
            // pinned connection (CORE-016).
            object? generatedId = null;
            await using (var reader = await sc.ExecuteReaderAsync(ExecutionType.Write).ConfigureAwait(false))
            {
                // ExecuteReaderAsync above executed without throwing — the INSERT (the compound
                // statement's first result set) already ran server-side. The database accepted
                // the write regardless of whether navigating to/reading the trailing SELECT
                // result set below succeeds.
                writeSucceeded[0] = true;

                // First result set = INSERT (rows-affected, no data rows).
                // Advance to the SELECT result set to read the generated ID.
                // Use IInternalTrackedReader.InnerReader to bypass TrackedReader.NextResult() policy;
                // the policy blocks multi-result for general use, but the compound path reads all
                // result sets before disposing so the connection lifecycle is correctly managed.
                if (reader is IInternalTrackedReader internalReader)
                {
                    var inner = internalReader.InnerReader;
                    if (await inner.NextResultAsync().ConfigureAwait(false) &&
                        await inner.ReadAsync().ConfigureAwait(false))
                    {
                        generatedId = inner[0];
                    }
                }
            } // reader disposed here; the pinned connection stays open for the fallback query

            if (generatedId != null && generatedId != DBNull.Value)
            {
                var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
                _idColumn.PropertyInfo.SetValue(entity, converted);
                return true;
            }

            // Fallback for providers/fakeDb that return false from NextResult()
            // (e.g. fakeDbDataReader.NextResult always returns false).
            await PopulateGeneratedIdAsync(entity, ctx, CancellationToken.None, idLease).ConfigureAwait(false);
            return true;
        }

        // 4b. ReaderInsertedId plan (MySqlConnector): execute INSERT as a reader, read
        // LastInsertedId from the underlying MySqlCommand (populated from the OK packet).
        // No multi-statement support required — MySqlConnector deliberately omits it.
        if (plan == GeneratedKeyPlan.ReaderInsertedId && _idColumn != null && !_idColumn.IsIdWritable)
            return await ExecuteReaderInsertedIdAsync(entity, ctx, dialect, writeSucceeded).ConfigureAwait(false);

        // 5. Default path, including SessionScopedFunction (Informix, SAP HANA, Access): INSERT, then the
        // session-scoped id query. A last-id function reports the id only on the connection that ran
        // the INSERT, so both share one pinned connection (GEN-001, CORE-016).
        {
            var needsId = _idColumn != null && !_idColumn.IsIdWritable;
            await using var idLease = needsId
                ? await AcquireGeneratedIdLeaseAsync(ctx, CancellationToken.None).ConfigureAwait(false)
                : null;
            await using var sc = BuildCreate(entity, ctx);
            PinTo(sc, idLease);
            var rowsAffected = await sc.ExecuteNonQueryAsync().ConfigureAwait(false);
            var succeeded = rowsAffected == 1;
            writeSucceeded[0] = succeeded;

            if (succeeded && needsId)
            {
                await PopulateGeneratedIdAsync(entity, ctx, CancellationToken.None, idLease).ConfigureAwait(false);
            }

            return RestoreAuditFieldsIfFailed(succeeded, entity, auditSnapshot);
        }
        }
        catch
        {
            if (!writeSucceeded[0])
            {
                RestoreAuditFields(entity, auditSnapshot);
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        // See the 2-arg CreateAsync overload above for why this exists (including the
        // writeSucceeded flag and why it's a one-element array).
        var auditSnapshot = SnapshotAuditFields(entity);
        var writeSucceeded = new bool[1];
        try
        {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var plan = dialect.GetGeneratedKeyPlan();

        // 1. Handle PREFETCH plans (InterBase)
        //
        // Only prefetch (and overwrite the entity's Id) when the column is NOT client-writable —
        // see the sibling CreateAsync(TEntity, IDatabaseContext?) overload above for the full
        // rationale (CONFIRMED live via InterBase).
        if (plan == GeneratedKeyPlan.PrefetchSequence && _idColumn != null && !_idColumn.IsIdWritable)
        {
            var seqQuery = dialect.GetSequenceNextValQuery(GetSequenceName());
            using var seqSc = ctx.CreateSqlContainer(seqQuery);
            // Advancing a sequence is a write: a read-only connection rejects it (pengdows.flatfile).
            var nextVal = await seqSc.ExecuteScalarRequiredAsync<object>(ExecutionType.Write, CommandType.Text, cancellationToken).ConfigureAwait(false);
            var converted = TypeCoercionHelper.ConvertWithCache(nextVal, _idColumn.PropertyInfo.PropertyType);
            _idColumn.PropertyInfo.SetValue(entity, converted);

            await using var sc = BuildCreateWithPrefetchedId(entity, ctx, dialect);
            var succeeded = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false) == 1;
            writeSucceeded[0] = succeeded;
            return RestoreAuditFieldsIfFailed(succeeded, entity, auditSnapshot);
        }

        if (plan == GeneratedKeyPlan.PrefetchSequence && _idColumn != null && _idColumn.IsIdWritable)
        {
            await using var sc = BuildCreate(entity, ctx);
            var succeeded = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false) == 1;
            writeSucceeded[0] = succeeded;
            return RestoreAuditFieldsIfFailed(succeeded, entity, auditSnapshot);
        }

        // 2. Handle INLINE plans (Postgres, SQL Server, etc.)
        if ((plan == GeneratedKeyPlan.Returning || plan == GeneratedKeyPlan.OutputInserted) &&
            _idColumn != null && !_idColumn.IsIdWritable)
        {
            await using var idLease = await AcquireGeneratedIdLeaseAsync(ctx, cancellationToken).ConfigureAwait(false);
            await using var sc = BuildCreateWithReturning(entity, true, ctx);
            PinTo(sc, idLease);

            object? generatedId;
            if (dialect.RequiresOutputParameterForReturning())
            {
                await sc.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, cancellationToken)
                    .ConfigureAwait(false);

                // The INSERT above executed without throwing — the database accepted the write
                // regardless of whether reading the OUT parameter below succeeds.
                writeSucceeded[0] = true;
                generatedId = sc.GetParameterValue(OracleReturningParameterName);
            }
            else
            {
                generatedId = await sc
                    .ExecuteScalarOrNullAsync<object>(ExecutionType.Write, CommandType.Text, cancellationToken)
                    .ConfigureAwait(false);

                // The statement above executed without throwing — the database accepted the
                // write regardless of whether a generated ID came back inline.
                writeSucceeded[0] = true;
            }

            if (generatedId != null && generatedId != DBNull.Value)
            {
                var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
                _idColumn.PropertyInfo.SetValue(entity, converted);
                return true;
            }

            // Fallback on the INSERT's own connection (CORE-016)
            await PopulateGeneratedIdAsync(entity, ctx, cancellationToken, idLease).ConfigureAwait(false);
            return true;
        }

        // DEC-007: the correlation token is this dialect's only way to read a generated id back.
        // Without one the INSERT would succeed and leave the id unset, so refuse before writing.
        if (plan == GeneratedKeyPlan.CorrelationToken && _tableInfo.CorrelationColumn == null &&
            _idColumn != null && !_idColumn.IsIdWritable &&
            dialect is SqlDialect { RequiresCorrelationTokenForGeneratedIds: true })
        {
            throw MissingCorrelationTokenException(dialect);
        }

        // 3. Handle CORRELATION TOKEN plan
        if (plan == GeneratedKeyPlan.CorrelationToken && _tableInfo.CorrelationColumn != null && _idColumn != null)
        {
            var token = CreateCorrelationToken();
            _tableInfo.CorrelationColumn.PropertyInfo.SetValue(entity, token);

            await using var sc = BuildCreate(entity, ctx);
            if (await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false) != 1)
            {
                RestoreAuditFields(entity, auditSnapshot);
                return false;
            }

            writeSucceeded[0] = true;

            var lookupSql = dialect.GetCorrelationTokenLookupQuery(
                _tableInfo.Name,
                _idColumn.Name,
                _tableInfo.CorrelationColumn.Name,
                dialect.MakeParameterName("p1"));

            using var lookupSc = ctx.CreateSqlContainer(lookupSql);
            lookupSc.AddParameterWithValue("p1", _tableInfo.CorrelationColumn.DbType, token);

            var generatedId = await lookupSc.ExecuteScalarRequiredAsync<object>(ExecutionType.Read, CommandType.Text, cancellationToken).ConfigureAwait(false);
            var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
            _idColumn.PropertyInfo.SetValue(entity, converted);
            return true;
        }

        // 4. Compound statement plan (MySQL Oracle MySql.Data, SQLite pre-3.35).
        if (plan == GeneratedKeyPlan.CompoundStatement && _idColumn != null && !_idColumn.IsIdWritable)
        {
            await using var idLease = await AcquireGeneratedIdLeaseAsync(ctx, cancellationToken).ConfigureAwait(false);
            await using var sc = BuildCreate(entity, ctx);
            PinTo(sc, idLease);
            sc.Query.Append(dialect.GetCompoundInsertIdSuffix());

            // Scope the reader so it is closed before the fallback query on the pinned connection.
            object? generatedId = null;
            await using (var reader = await sc.ExecuteReaderAsync(ExecutionType.Write, CommandType.Text, cancellationToken).ConfigureAwait(false))
            {
                // ExecuteReaderAsync above executed without throwing — the INSERT (the compound
                // statement's first result set) already ran server-side. The database accepted
                // the write regardless of whether navigating to/reading the trailing SELECT
                // result set below succeeds.
                writeSucceeded[0] = true;

                if (reader is IInternalTrackedReader internalReader)
                {
                    var inner = internalReader.InnerReader;
                    if (await inner.NextResultAsync(cancellationToken).ConfigureAwait(false) &&
                        await inner.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        generatedId = inner[0];
                    }
                }
            } // reader disposed here; the pinned connection stays open for the fallback query

            if (generatedId != null && generatedId != DBNull.Value)
            {
                var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
                _idColumn.PropertyInfo.SetValue(entity, converted);
                return true;
            }

            await PopulateGeneratedIdAsync(entity, ctx, cancellationToken, idLease).ConfigureAwait(false);
            return true;
        }

        // 4b. ReaderInsertedId plan (MySqlConnector): see ExecuteReaderInsertedIdAsync.
        // No multi-statement support required.
        if (plan == GeneratedKeyPlan.ReaderInsertedId && _idColumn != null && !_idColumn.IsIdWritable)
            return await ExecuteReaderInsertedIdAsync(entity, ctx, dialect, writeSucceeded, cancellationToken).ConfigureAwait(false);

        // 5. Default path, including SessionScopedFunction (Informix, SAP HANA, Access): INSERT, then the
        // session-scoped id query. A last-id function reports the id only on the connection that ran
        // the INSERT, so both share one pinned connection (GEN-001, CORE-016).
        {
            var needsId = _idColumn != null && !_idColumn.IsIdWritable;
            await using var idLease = needsId
                ? await AcquireGeneratedIdLeaseAsync(ctx, cancellationToken).ConfigureAwait(false)
                : null;
            await using var sc = BuildCreate(entity, ctx);
            PinTo(sc, idLease);
            var rowsAffected = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
            var succeeded = rowsAffected == 1;
            writeSucceeded[0] = succeeded;

            if (succeeded && needsId)
            {
                await PopulateGeneratedIdAsync(entity, ctx, cancellationToken, idLease).ConfigureAwait(false);
            }

            return RestoreAuditFieldsIfFailed(succeeded, entity, auditSnapshot);
        }
        }
        catch
        {
            if (!writeSucceeded[0])
            {
                RestoreAuditFields(entity, auditSnapshot);
            }

            throw;
        }
    }

    private object CreateCorrelationToken()
    {
        var type = Nullable.GetUnderlyingType(_tableInfo.CorrelationColumn!.PropertyInfo.PropertyType)
            ?? _tableInfo.CorrelationColumn.PropertyInfo.PropertyType;
        if (type == typeof(string))
        {
            return Guid.NewGuid().ToString("N");
        }

        if (type == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        throw new InvalidOperationException(
            $"[CorrelationToken] requires a string or Guid property; '{type.Name}' is not supported.");
    }

    /// <summary>
    /// Executes an INSERT as a reader (MySqlConnector path) and retrieves the generated key
    /// from the command's LastInsertedId property (populated from the MySQL OK packet).
    /// Falls back to <see cref="PopulateGeneratedIdAsync"/> when LastInsertedId is absent (e.g. fakeDb).
    /// </summary>
    /// <param name="writeSucceeded">
    /// Set to true once the INSERT reader has executed without throwing, before the
    /// <see cref="PopulateGeneratedIdAsync"/> fallback (which can itself throw) runs — see the
    /// caller (<c>CreateAsync</c>) for why this must be observable outside this method.
    /// </param>
    private async ValueTask<bool> ExecuteReaderInsertedIdAsync(
        TEntity entity,
        IDatabaseContext ctx,
        ISqlDialect dialect,
        bool[] writeSucceeded,
        CancellationToken cancellationToken = default)
    {
        await using var idLease = await AcquireGeneratedIdLeaseAsync(ctx, cancellationToken).ConfigureAwait(false);
        await using var sc = BuildCreate(entity, ctx);
        PinTo(sc, idLease);
        object? generatedId = null;
        await using (var reader = await sc.ExecuteReaderAsync(ExecutionType.Write, CommandType.Text, cancellationToken).ConfigureAwait(false))
        {
            // ExecuteReaderAsync above executed without throwing — the database accepted the
            // write regardless of whether reading LastInsertedId from the command below succeeds.
            writeSucceeded[0] = true;

            if (reader is IInternalTrackedReader internalReader)
                generatedId = dialect.GetLastInsertedIdFromCommand(internalReader.InnerCommand);
        }

        if (generatedId is not null && generatedId != DBNull.Value)
            _idColumn!.PropertyInfo.SetValue(entity,
                TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType));
        else
            await PopulateGeneratedIdAsync(entity, ctx, cancellationToken, idLease).ConfigureAwait(false);
        return true;
    }

    // GEN-001 / CORE-016: a session-scoped id query must run on the connection that ran the INSERT.
    // Inside a caller's transaction that is the transaction's connection (no lease); otherwise one
    // connection is pinned for both commands and released once when the lease is disposed.
    private static async ValueTask<PinnedConnectionLease?> AcquireGeneratedIdLeaseAsync(IDatabaseContext ctx,
        CancellationToken cancellationToken)
    {
        if (ctx is ITransactionContext || ctx is not IInternalConnectionProvider)
        {
            return null;
        }

        return await PinnedConnectionLease.AcquireAsync(ctx, ExecutionType.Write, cancellationToken)
            .ConfigureAwait(false);
    }

    private NotSupportedException MissingCorrelationTokenException(ISqlDialect dialect) =>
        new($"{typeof(TEntity).Name}: {dialect.DatabaseType} can't return a database-generated [Id(false)] " +
            "value. Add a [CorrelationToken] column (a unique string or Guid the gateway sets and reads " +
            "the new row back by), or make the id client-provided ([Id] with a value you assign).");

    private static void PinTo(ISqlContainer container, PinnedConnectionLease? lease)
    {
        if (lease != null && container is SqlContainer sqlContainer)
        {
            sqlContainer.PinnedConnection = lease;
        }
    }

    private async Task PopulateGeneratedIdAsync(TEntity entity, IDatabaseContext context,
        CancellationToken cancellationToken = default, PinnedConnectionLease? lease = null)
    {
        if (_idColumn == null)
        {
            return;
        }

        // Get the database-specific query for retrieving the last inserted ID
        var ctx = context ?? _context;
        string lastIdQuery;
        try
        {
            lastIdQuery = ctx.GetDialect().GetLastInsertedIdQuery();
        }
        catch (NotSupportedException)
        {
            // Some databases (Oracle, Unknown) don't support generic last-insert-id queries
            // This is expected behavior - just skip ID population
            return;
        }

        if (string.IsNullOrEmpty(lastIdQuery))
        {
            return;
        }

        await using var sc = ctx.CreateSqlContainer(lastIdQuery);
        PinTo(sc, lease);
        var generatedId = await sc.ExecuteScalarOrNullAsync<object>(ExecutionType.Write, CommandType.Text, cancellationToken);

        if (generatedId != null && generatedId != DBNull.Value)
        {
            try
            {
                var targetType = _idColumn.PropertyInfo.PropertyType;
                if (targetType == typeof(Guid))
                {
                    if (generatedId is Guid g)
                    {
                        _idColumn.PropertyInfo.SetValue(entity, g);
                    }
                    else if (Guid.TryParse(generatedId.ToString(), out var parsed))
                    {
                        _idColumn.PropertyInfo.SetValue(entity, parsed);
                    }
                    else
                    {
                        throw new InvalidCastException("Unable to convert generated ID to Guid.");
                    }
                }
                else
                {
                    // Convert the ID to the appropriate type and set it on the entity
                    var convertedId = TypeCoercionHelper.ConvertWithCache(generatedId, targetType);
                    _idColumn.PropertyInfo.SetValue(entity, convertedId);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to convert generated ID '{generatedId}' to type {_idColumn.PropertyInfo.PropertyType.Name}: {ex.Message}",
                    ex);
            }
        }
    }


    // Placeholders for identity-returning clauses in INSERT statements
    private const string
        PrefixClausePlaceholder = "{prefix}"; // SQL Server: DECLARE @table for the trigger-safe OUTPUT INTO form

    private const string OutputClausePlaceholder = "{output}"; // SQL Server: OUTPUT INSERTED.id (before VALUES)

    private const string
        ReturningClausePlaceholder = "{returning}"; // PostgreSQL/SQLite/etc: RETURNING id (after VALUES)

    private const string OracleReturningParameterName = OracleDialect.ReturningParameterName;

    /// <inheritdoc/>
    public ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null)
    {
        var (sc, _) = PrepareInsertContainer(entity, context, stripPlaceholders: true);
        return sc;
    }

    /// <summary>
    /// Prepares the SqlContainer with an INSERT statement.
    /// When stripPlaceholders is true, identity clause placeholders are removed and the cached
    /// template is cloned for better performance. When false, placeholders remain for
    /// BuildCreateWithReturning to replace.
    /// </summary>
    private (ISqlContainer sc, ISqlDialect dialect) PrepareInsertContainer(TEntity entity, IDatabaseContext? context,
        bool stripPlaceholders)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        // Mutate entity first (same for both paths)
        MutateEntityForInsert(entity);

        var sqlTemplate = GetTemplatesForDialect(dialect);

        // Fast path: clone from cached InsertTemplate (skips SQL string building)
        if (stripPlaceholders)
        {
            var containerTemplates = GetContainerTemplatesForDialect(dialect, ctx);
            // Copy only the template's text (PERF-013): the binder creates every parameter, so
            // cloning the template's parameters only to discard them was wasted work.
            var sc = ((SqlContainer)containerTemplates.InsertTemplate).CloneQueryOnly(ctx);

            var binder = GetOrBuildInsertBinder(dialect, sqlTemplate);
            var parameters = t_bindScratch ??= new List<DbParameter>();
            try
            {
                binder(entity, parameters);
                sc.AddParameters(parameters);
            }
            finally
            {
                parameters.Clear();
            }

            return (sc, dialect);
        }

        // Slow path: build full SQL with placeholders for BuildCreateWithReturning
        return BuildInsertContainerDirect(entity, ctx, dialect, sqlTemplate);
    }

    /// <summary>
    /// Applies entity mutations required before INSERT: ID generation, audit fields, version init.
    /// </summary>
    private void MutateEntityForInsert(TEntity entity)
    {
        EnsureWritableIdHasValue(entity);

        if (_hasAuditColumns)
        {
            SetAuditFields(entity, false);
        }

        if (_versionColumn != null)
        {
            var current = _versionColumn.MakeParameterValueFromField(entity);
            if (current == null || Utils.IsZeroNumeric(current))
            {
                var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                             _versionColumn.PropertyInfo.PropertyType;
                if (Utils.IsZeroNumeric(TypeCoercionHelper.ConvertWithCache(0, target)))
                {
                    var one = TypeCoercionHelper.ConvertWithCache(1, target);
                    _versionColumn.PropertyInfo.SetValue(entity, one);
                }
            }
        }
    }

    /// <summary>
    /// Builds INSERT SQL container directly (no template cloning). Used during template
    /// initialization and for BuildCreateWithReturning which needs placeholder tokens.
    /// </summary>
    /// <summary>
    /// INSERT for the PrefetchSequence plan: the id was just read from the sequence, so it is sent
    /// even though the column is database-generated (<c>[Id(false)]</c>) and normally left out of
    /// the INSERT. Without it the row got NULL (CONFIRMED live on InterBase, 2026-09-28).
    /// </summary>
    private ISqlContainer BuildCreateWithPrefetchedId(TEntity entity, IDatabaseContext ctx, ISqlDialect dialect)
    {
        MutateEntityForInsert(entity);

        var template = GetTemplatesForDialect(dialect);
        var withId = new CachedSqlTemplates
        {
            InsertColumns = new List<IColumnInfo>(template.InsertColumns.Count + 1) { _idColumn! },
            InsertParameterNames = new List<string>(template.InsertParameterNames.Count + 1)
                { $"i{template.InsertParameterNames.Count}" }
        };
        withId.InsertColumns.AddRange(template.InsertColumns);
        withId.InsertParameterNames.AddRange(template.InsertParameterNames);

        var (sc, _) = BuildInsertContainerDirect(entity, ctx, dialect, withId);
        sc.Query.Replace(PrefixClausePlaceholder, string.Empty);
        sc.Query.Replace(OutputClausePlaceholder, string.Empty);
        sc.Query.Replace(ReturningClausePlaceholder, string.Empty);
        return sc;
    }

    private (ISqlContainer sc, ISqlDialect dialect) BuildInsertContainerDirect(
        TEntity entity, IDatabaseContext ctx, ISqlDialect dialect, CachedSqlTemplates sqlTemplate)
    {
        var sc = ctx.CreateSqlContainer();

        sc.Query.Append(PrefixClausePlaceholder)
            .Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (");

        for (var i = 0; i < sqlTemplate.InsertColumns.Count; i++)
        {
            var column = sqlTemplate.InsertColumns[i];
            var value = column.MakeParameterValueFromField(entity);

            var paramName = sqlTemplate.InsertParameterNames[i];
            var param = dialect.CreateDbParameter(paramName, column.DbType, value);
            dialect.MarkColumnParameter(param, column);

            sc.AddParameter(param);

            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(dialect.WrapSimpleName(column.Name));
        }

        // Insert OUTPUT placeholder (for SQL Server) between column list and VALUES
        var fromSelect = dialect.InsertsFromSelect(sqlTemplate.InsertColumns);
        sc.Query.Append(')')
            .Append(OutputClausePlaceholder)
            .Append(fromSelect ? " SELECT " : " VALUES (");

        for (var i = 0; i < sqlTemplate.InsertColumns.Count; i++)
        {
            var column = sqlTemplate.InsertColumns[i];
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            var paramName = sqlTemplate.InsertParameterNames[i];
            if (dialect.RendersColumnArgument(column))
            {
                sc.Query.Append(dialect.RenderColumnArgument(dialect.MakeParameterName(paramName), column));
            }
            else
            {
                sc.Query.Append(dialect.MakeParameterName(paramName));
            }
        }

        // Insert RETURNING placeholder (for PostgreSQL/SQLite/etc) after VALUES
        if (!fromSelect)
        {
            sc.Query.Append(')');
        }

        sc.Query.Append(ReturningClausePlaceholder);

        return (sc, dialect);
    }

    private void EnsureWritableIdHasValue(TEntity entity)
    {
        if (_idColumn == null || !_idColumn.IsIdWritable)
        {
            return;
        }

        var property = _idColumn.PropertyInfo;
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var currentValue = _idColumn.MakeParameterValueFromField(entity);

        if (underlyingType == typeof(Guid))
        {
            var currentGuid = currentValue switch
            {
                Guid g => g,
                _ => Guid.Empty
            };

            if (currentGuid == Guid.Empty)
            {
                var newGuid = Guid.NewGuid();
                property.SetValue(entity, property.PropertyType == typeof(Guid?) ? (Guid?)newGuid : newGuid);
            }
        }
        else if (underlyingType == typeof(string))
        {
            var currentString = currentValue as string;
            if (string.IsNullOrWhiteSpace(currentString))
            {
                var generated = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
                property.SetValue(entity, generated);
            }
        }
    }

    /// <summary>
    /// Builds an INSERT statement with optional RETURNING/OUTPUT clause for identity capture.
    /// Uses placeholder replacement for clean, predictable SQL generation.
    /// </summary>
    /// <param name="entity">Entity to insert</param>
    /// <param name="withReturning">Whether to include RETURNING/OUTPUT clause for identity</param>
    /// <param name="context">Database context</param>
    /// <returns>SQL container with INSERT statement</returns>
    public ISqlContainer BuildCreateWithReturning(TEntity entity, bool withReturning, IDatabaseContext? context = null)
    {
        var (sc, dialect) = PrepareInsertContainer(entity, context, stripPlaceholders: false);

        var prefixClause = string.Empty;
        var outputClause = string.Empty;
        var returningClause = string.Empty;
        var wrapsEntireStatement = false;
        string? idWrapped = null;

        if (withReturning && _idColumn != null && !_idColumn.IsIdWritable && dialect.SupportsInsertReturning)
        {
            idWrapped = dialect.WrapSimpleName(_idColumn.Name);

            if (dialect.InsertReturningClauseBeforeValues)
            {
                // The dialect owns its generated-key protocol (SQL Server captures the id INTO a
                // table variable so triggers on the target table don't break it).
                var clause = dialect.RenderInsertReturningClause(idWrapped);
                (prefixClause, outputClause, returningClause) = dialect is IInternalSqlDialect internalDialect
                    ? internalDialect.RenderOutputInsertClauses(idWrapped, clause)
                    : (string.Empty, clause, string.Empty);
            }
            else if (dialect.RequiresOutputParameterForReturning())
            {
                var clause = dialect.RenderInsertReturningClause(idWrapped);
                returningClause = clause.Replace("?", dialect.MakeParameterName(OracleReturningParameterName),
                    StringComparison.Ordinal);
                sc.AddParameterWithValue<object?>(OracleReturningParameterName, _idColumn.DbType, null,
                    ParameterDirection.Output);
            }
            else if (dialect is SqlDialect { InsertReturningWrapsEntireStatement: true })
            {
                // Db2 wraps the ENTIRE insert statement rather than appending a trailing
                // clause: SELECT "Id" FROM FINAL TABLE (INSERT INTO t (...) VALUES (...)).
                wrapsEntireStatement = true;
            }
            else
            {
                returningClause = dialect.RenderInsertReturningClause(idWrapped); // Others: RETURNING goes after VALUES
            }
        }

        // Replace placeholders with actual clauses (or empty strings)
        sc.Query.Replace(PrefixClausePlaceholder, prefixClause);
        sc.Query.Replace(OutputClausePlaceholder, outputClause);
        sc.Query.Replace(ReturningClausePlaceholder, returningClause);

        if (wrapsEntireStatement)
        {
            // ISqlQueryBuilder has no Insert-at-position method, so rebuild via ToString/Clear.
            var insertSql = sc.Query.ToString();
            sc.Query.Clear();
            sc.Query.Append($"SELECT {idWrapped} FROM FINAL TABLE (").Append(insertSql).Append(')');
        }

        return sc;
    }


    /// <inheritdoc/>
    public ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                $"row identity column for table {BuildWrappedTableName(dialect)} not found");
        }

        var containerTemplates = GetContainerTemplatesForDialect(dialect, ctx);
        var template = containerTemplates.DeleteByIdTemplate;
        if (template != null)
        {
            var sc = template.Clone(ctx);
            sc.SetParameterValue("k0", id);
            return sc;
        }

        return BuildDeleteDirect(id, ctx);
    }

    /// <summary>
    /// Builds DELETE SQL directly without using cached container templates.
    /// Used during template initialization to avoid circular dependency.
    /// </summary>
    private ISqlContainer BuildDeleteDirect(TRowID id, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();
        var dialect = GetDialect(ctx);

        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                $"row identity column for table {BuildWrappedTableName(dialect)} not found");
        }

        var counters = new ClauseCounters();
        var name = counters.NextKey();
        var param = dialect.CreateDbParameter(name, _idColumn.DbType, id);
        sc.AddParameter(param);

        var deleteCache = GetOrCreateQueryCache(dialect);
        if (!deleteCache.TryGet("DeleteById", out var sql))
        {
            sql = string.Format(GetTemplatesForDialect(dialect).DeleteSql, dialect.MakeParameterName(param));
            deleteCache.GetOrAdd("DeleteById", _ => sql);
        }

        sc.Query.Append(sql);
        return sc;
    }

    /// <inheritdoc/>
    public async ValueTask<int> DeleteAsync(TRowID id, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        await using var sc = BuildDelete(id, ctx);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = MaterializeDistinctIds(ids);
        if (list.Count == 0)
        {
            throw new ArgumentException(EmptyIdListMessage, nameof(ids));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        // Set-valued dialects: BuildRetrieve binds one scalar for a single id and one typed array
        // for several. The scalar-template fast paths below would push an array into a scalar
        // parameter, which Npgsql rejects (BP-116).
        if (dialect.SupportsSetValuedParameters)
        {
            await using var setValuedContainer = BuildRetrieve(list, ctx);
            return await LoadListAsync(setValuedContainer, cancellationToken).ConfigureAwait(false);
        }

        if (!dialect.SupportsSetValuedParameters && ctx.MaxParameterLimit > 0 && list.Count > ctx.MaxParameterLimit)
        {
            var results = new List<TEntity>(list.Count);
            var limit = ctx.MaxParameterLimit;
            for (var offset = 0; offset < list.Count; offset += limit)
            {
                var count = Math.Min(limit, list.Count - offset);
                var chunk = list.GetRange(offset, count);
                await using var sc = BuildRetrieve(chunk, ctx);
                var chunkResults = await LoadListAsync(sc, cancellationToken).ConfigureAwait(false);
                results.AddRange(chunkResults);
            }

            return results;
        }

        // Try to use cached templates for better performance, but fall back to traditional method
        // to avoid circular dependency during template building
        try
        {
            // For small lists, use cached template for better performance
            // For larger lists, fall back to BuildRetrieve to handle dynamic parameter lists correctly
            if (list.Count == 1)
            {
                // Single ID - reuse GetByIdTemplate
                var templates = GetContainerTemplatesForDialect(dialect, ctx);
                using var container = templates.GetByIdTemplate!.Clone(ctx);

                if (dialect.SupportsSetValuedParameters)
                {
                    container.SetParameterValue("p0", list.ToArray());
                }
                else
                {
                    container.SetParameterValue("p0", list[0]);
                }

                return await LoadListAsync(container, cancellationToken).ConfigureAwait(false);
            }

            if (list.Count == 2 && !dialect.SupportsSetValuedParameters)
            {
                // Two IDs - can reuse GetByIdsTemplate for non-array dialects
                var templates = GetContainerTemplatesForDialect(dialect, ctx);
                using var container = templates.GetByIdsTemplate!.Clone(ctx);

                // BuildWhere names IN-list parameters w0..wN (p0/p1 resolve to them only on
                // named-parameter dialects, not on positional ones such as Informix).
                container.SetParameterValue("w0", list[0]);
                container.SetParameterValue("w1", list[1]);

                return await LoadListAsync(container, cancellationToken).ConfigureAwait(false);
            }

            // For n>2: BuildRetrieve handles dialect-specific parameterization internally
            await using var sc = BuildRetrieve(list, ctx);
            return await LoadListAsync(sc, cancellationToken).ConfigureAwait(false);
        }
        catch (exceptions.TemplateInitializationException)
        {
            // Template build failed for this dialect; fall back to direct BuildRetrieve path
            await using var sc = BuildRetrieve(list, ctx);
            return await LoadListAsync(sc, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids,
        IDatabaseContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = MaterializeDistinctIds(ids);
        if (list.Count == 0)
        {
            // Empty ID list - return empty stream
            yield break;
        }

        var ctx = context ?? _context;

        // Get the container to use (with try-catch for error handling)
        await using var container = GetRetrieveContainer(list, ctx);

        // Stream results from the container
        await foreach (var entity in LoadStreamAsync(container, cancellationToken).ConfigureAwait(false))
        {
            yield return entity;
        }
    }

    private ISqlContainer GetRetrieveContainer(IReadOnlyList<TRowID> list, IDatabaseContext ctx)
    {
        var dialect = GetDialect(ctx);

        if (dialect.SupportsSetValuedParameters)
        {
            return BuildRetrieve(list, ctx);
        }

        // Try to use cached templates for better performance, but fall back to traditional method
        // to avoid circular dependency during template building
        try
        {
            // For small lists, use cached template for better performance
            // For larger lists, fall back to BuildRetrieve to handle dynamic parameter lists correctly
            if (list.Count == 1)
            {
                // Single ID - reuse GetByIdTemplate
                var templates = GetContainerTemplatesForDialect(dialect, ctx);
                var container = templates.GetByIdTemplate!.Clone(ctx);

                if (dialect.SupportsSetValuedParameters)
                {
                    container.SetParameterValue("p0", list.ToArray());
                }
                else
                {
                    container.SetParameterValue("p0", list[0]);
                }

                return container;
            }

            if (list.Count == 2 && !dialect.SupportsSetValuedParameters)
            {
                // Two IDs - can reuse GetByIdsTemplate for non-array dialects
                var templates = GetContainerTemplatesForDialect(dialect, ctx);
                var container = templates.GetByIdsTemplate!.Clone(ctx);

                // BuildWhere names IN-list parameters w0..wN (p0/p1 resolve to them only on
                // named-parameter dialects, not on positional ones such as Informix).
                container.SetParameterValue("w0", list[0]);
                container.SetParameterValue("w1", list[1]);

                return container;
            }

            // Fall back to dynamic BuildRetrieve for larger lists
            return BuildRetrieve(list, ctx);
        }
        catch (exceptions.TemplateInitializationException)
        {
            // Template build failed for this dialect; fall back to direct BuildRetrieve path
            return BuildRetrieve(list, ctx);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchDelete(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = MaterializeDistinctIds(ids);
        if (list.Count == 0)
        {
            throw new ArgumentException(EmptyIdListMessage, nameof(ids));
        }

        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }

        var dialect = GetDialect(ctx);
        var wrappedIdColumnName = WrapColumnReference(dialect, _idColumn.Name);

        // Chunk by max parameter limit (with 10% headroom, similar to BatchCreate)
        var chunks = ChunkList(list, 1, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = ctx.CreateSqlContainer();
            sc.Query.Append("DELETE FROM ").Append(BuildWrappedTableName(dialect));
            BuildWhere(wrappedIdColumnName, chunk, sc);
            result.Add(sc);
        }

        return result;
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public async ValueTask<int> BatchDeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        var containers = BuildBatchDelete(ids, ctx);
        var totalAffected = 0;

        foreach (var sc in containers)
        {
            await using var owned = sc;
            cancellationToken.ThrowIfCancellationRequested();
            totalAffected += await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                .ConfigureAwait(false);
        }

        return totalAffected;
    }

    /// <inheritdoc/>
    public ValueTask<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchDeleteAsync(ids, context, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> DeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchDeleteAsync(entities, context, cancellationToken);

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchDelete(IReadOnlyCollection<TEntity> entities,
        IDatabaseContext? context = null)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (entities.Count == 0)
        {
            return Array.Empty<ISqlContainer>();
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var keys = GetPrimaryKeys();

        // Chunk by Math.Floor(maxParameterLimit / numberOfPrimaryKeys) with 10% headroom
        var chunks = ChunkList(entities.ToList(), keys.Count, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = ctx.CreateSqlContainer();
            sc.Query.Append("DELETE FROM ").Append(BuildWrappedTableName(dialect));
            BuildWhereByPrimaryKey(chunk, sc, "", dialect);
            result.Add(sc);
        }

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<int> BatchDeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (entities.Count == 0)
        {
            return 0;
        }

        var ctx = context ?? _context;
        var containers = BuildBatchDelete(entities, ctx);
        var totalAffected = 0;

        foreach (var sc in containers)
        {
            await using var owned = sc;
            cancellationToken.ThrowIfCancellationRequested();
            totalAffected += await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                .ConfigureAwait(false);
        }

        return totalAffected;
    }

    // ChunkList moved to BaseTableGateway.Core.cs

    private static List<TRowID> MaterializeDistinctIds(IEnumerable<TRowID> ids)
    {
        // Optimization: Fast path for common collection types to avoid double-enumeration or 
        // unnecessary allocations for single-element lists.
        if (ids is IReadOnlyCollection<TRowID> roc)
        {
            if (roc.Count == 0)
            {
                return new List<TRowID>(0);
            }
            if (roc.Count == 1)
            {
                var id = roc is IList<TRowID> list ? list[0] : roc.First();
                return new List<TRowID>(1) { id };
            }
        }

        var result = ids is ICollection<TRowID> coll
            ? new List<TRowID>(coll.Count)
            : new List<TRowID>();

        var comparer = EqualityComparer<TRowID>.Default;
        HashSet<TRowID>? seen = null;

        foreach (var id in ids)
        {
            if (result.Count == 0)
            {
                result.Add(id);
                continue;
            }

            // Threshold-based deduplication:
            // 1. Very small lists (< 16): Use linear search on result list (faster than HashSet overhead)
            // 2. Larger lists (>= 16): Switch to HashSet for O(1) lookups
            if (seen == null)
            {
                if (result.Count < 16)
                {
                    var found = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (comparer.Equals(result[i], id))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        result.Add(id);
                    }
                    continue;
                }

                // Transition to HashSet
                seen = new HashSet<TRowID>(result, comparer);
            }

            if (seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }


    /// <inheritdoc/>
    public async ValueTask<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (objectToRetrieve == null)
        {
            throw new ArgumentNullException(nameof(objectToRetrieve));
        }

        var ctx = context ?? _context;
        var list = new List<TEntity> { objectToRetrieve };
        await using var sc = BuildRetrieve(list, string.Empty, ctx);
        return await LoadSingleAsync(sc, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }

        var dialect = GetDialect(ctx);
        var templates = GetContainerTemplatesForDialect(dialect, ctx);
        using var container = templates.GetByIdTemplate!.Clone(ctx);
        container.SetParameterValue("p0", id);

        return await LoadSingleAsync(container, cancellationToken).ConfigureAwait(false);
    }

    // LoadSingleAsync, LoadListAsync, LoadStreamAsync moved to BaseTableGateway.Core.cs

    // GetCachedInsertableColumns moved to BaseTableGateway.Core.cs

    // CheckParameterLimit moved to BaseTableGateway.Core.cs

    private CompiledBinderFactory<TEntity>.Binder GetOrBuildInsertBinder(ISqlDialect dialect, CachedSqlTemplates template)
    {
        if (_insertBinders.TryGetValue(dialect, out var cached))
        {
            return cached;
        }

        return AddInsertBinder(dialect, template);
    }

    private CompiledBinderFactory<TEntity>.Binder AddInsertBinder(ISqlDialect dialect, CachedSqlTemplates template)
    {
        return _insertBinders.GetValue(dialect, d =>
            CompiledBinderFactory<TEntity>.CreateInsertBinder(template.InsertColumns, template.InsertParameterNames, d));
    }

    private CompiledBinderFactory<TEntity>.Binder GetOrBuildUpsertBinder(ISqlDialect dialect, CachedSqlTemplates template)
    {
        if (_upsertBinders.TryGetValue(dialect, out var cached))
        {
            return cached;
        }

        return AddUpsertBinder(dialect, template);
    }

    private CompiledBinderFactory<TEntity>.Binder AddUpsertBinder(ISqlDialect dialect, CachedSqlTemplates template)
    {
        return _upsertBinders.GetValue(dialect, d =>
            CompiledBinderFactory<TEntity>.CreateInsertBinder(template.UpsertColumns, template.UpsertParameterNames, d));
    }

    private CompiledBinderFactory<TEntity>.UpdateBinder GetOrBuildUpdateBinder(ISqlDialect dialect, CachedSqlTemplates template)
    {
        if (_updateBinders.TryGetValue(dialect, out var cached))
        {
            return cached;
        }

        return AddUpdateBinder(dialect, template);
    }

    private CompiledBinderFactory<TEntity>.UpdateBinder AddUpdateBinder(ISqlDialect dialect, CachedSqlTemplates template)
    {
        return _updateBinders.GetValue(dialect, d =>
            CompiledBinderFactory<TEntity>.CreateUpdateBinder(template.UpdateColumns, template.UpdateColumnWrappedNames, d));
    }

    // CheckParameterLimit moved to BaseTableGateway.Core.cs







    /// <inheritdoc/>
    public ValueTask<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        return UpdateAsync(objectToUpdate, _versionColumn != null, ctx, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;

        // BuildUpdateAsync mutates audit fields as a side effect of building the UPDATE, before
        // anything executes. Restore them whenever the write doesn't actually succeed — including
        // the version-conflict/0-rows-affected case below — so the entity doesn't claim a write
        // that never persisted.
        var auditSnapshot = SnapshotAuditFields(objectToUpdate);
        try
        {
            await using var sc = await BuildUpdateAsync(objectToUpdate, loadOriginal, ctx, cancellationToken).ConfigureAwait(false);
            var rowsAffected = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
            if (rowsAffected == 0)
            {
                // 0 rows affected without an exception is a failed write regardless of whether
                // this entity is versioned — restore before the (conditional) throw below, not
                // just in the generic catch, since a plain "return 0" for an unversioned entity
                // never reaches that catch at all.
                RestoreAuditFields(objectToUpdate, auditSnapshot);
                if (_versionColumn != null)
                {
                    throw new ConcurrencyConflictException(
                        $"Concurrency conflict on {typeof(TEntity).Name}: version mismatch or row deleted.",
                        ctx.Product);
                }
            }
            else
            {
                // The SET clause incremented [Version] server-side; mirror it on the entity so the
                // same instance can be updated again.
                WriteBackIncrementedVersion(objectToUpdate);
            }

            return rowsAffected;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No changes detected for update."))
        {
            RestoreAuditFields(objectToUpdate, auditSnapshot);
            return 0;
        }
        catch
        {
            RestoreAuditFields(objectToUpdate, auditSnapshot);
            throw;
        }
    }

    private static bool IsDefaultId(object? value)
    {
        if (Utils.IsNullOrDbNull(value))
        {
            return true;
        }

        var type = typeof(TRowID);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
        {
            return value as string == string.Empty;
        }

        if (underlying == typeof(Guid))
        {
            return value is Guid g && g == Guid.Empty;
        }

        if (Utils.IsZeroNumeric(value!))
        {
            return true;
        }

        if (value is TRowID typed)
        {
            return EqualityComparer<TRowID>.Default.Equals(typed, default!);
        }

        // Different runtime type than TRowID and not a zero/empty equivalent
        return false;
    }


    private static bool TryParseMajorVersion(string version, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var match = Regex.Match(version, "(\\d+)");
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups[1].Value, out major);
    }


    private string GetSequenceName()
    {
        // Simple heuristic: [table_name]_seq
        return string.Concat(_tableInfo.Name, "_seq");
    }

    // BuildWrappedTableName and GetDialect moved to BaseTableGateway.Core.cs

    private static void ValidateRowIdType()
    {
        var type = typeof(TRowID);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        var isValid = underlying == typeof(string) || underlying == typeof(Guid);
        if (!isValid)
        {
            switch (Type.GetTypeCode(underlying))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    isValid = true;
                    break;
                default:
                    isValid = false;
                    break;
            }
        }

        if (!isValid)
        {
            throw new NotSupportedException(
                $"TRowID type '{type.FullName}' is not supported. Use string, Guid, or integer types.");
        }
    }
}
