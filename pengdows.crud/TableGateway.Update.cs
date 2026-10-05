// =============================================================================
// FILE: TableGateway.Update.cs
// PURPOSE: UPDATE statement building and execution.
//
// AI SUMMARY:
// - BuildUpdateAsync() - Creates UPDATE statement with parameters.
// - UpdateAsync() - Executes UPDATE and returns rows affected.
// - Handles:
//   * Optimistic concurrency via [Version] column
//   * Audit field updates (LastUpdatedBy/On)
//   * Non-updateable columns (excluded from SET)
//   * Original value loading for change detection
// - loadOriginal parameter: If true, loads the current row (throws if missing) and only
//   columns whose values differ are written to SET. The overload without loadOriginal
//   loads the original only for entities with a [Version] column.
// - Version column behavior:
//   * SET version = version + 1
//   * WHERE version = @currentVersion
//   * Returns 0 if version mismatch (concurrent modification)
// - Requires [Id] column for WHERE clause.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Globalization;
using pengdows.crud.dialects;
using pengdows.crud.exceptions;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: UPDATE statement building and execution.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
    /// <inheritdoc/>
    public ValueTask<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        // Optimization: for version-less entities without original load, building is fully synchronous
        // (once the table's declared column types are applied, TYPE-020).
        if (_versionColumn == null)
        {
            return DeclaredTypesPending(ctx)
                ? BuildUpdateAfterDeclaredTypesAsync(objectToUpdate, ctx, cancellationToken)
                : ValueTask.FromResult(BuildUpdate(objectToUpdate, ctx));
        }

        return BuildUpdateAsync(objectToUpdate, true, ctx, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context = null, CancellationToken cancellationToken = default) =>
        await TryBuildUpdateAsync(objectToUpdate, loadOriginal, context, cancellationToken).ConfigureAwait(false)
        ?? throw NoChanges();

    private static InvalidOperationException NoChanges() => new("No changes detected for update.");

    private async ValueTask<ISqlContainer> BuildUpdateAfterDeclaredTypesAsync(TEntity objectToUpdate,
        IDatabaseContext ctx, CancellationToken cancellationToken)
    {
        await EnsureDeclaredTypesAsync(ctx, cancellationToken).ConfigureAwait(false);
        return BuildUpdate(objectToUpdate, ctx);
    }

    // The UPDATE, or null when no column changed: UpdateAsync returns 0 for that without an
    // exception (COR-012); BuildUpdateAsync throws, as documented.
    private async ValueTask<ISqlContainer?> TryBuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context, CancellationToken cancellationToken)
    {
        if (objectToUpdate == null)
        {
            throw new ArgumentNullException(nameof(objectToUpdate));
        }

        var ctx = context ?? _context;
        await EnsureDeclaredTypesAsync(ctx, cancellationToken).ConfigureAwait(false); // TYPE-020
        if (_idColumn == null)
        {
            throw new NotSupportedException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }

        TEntity? original = null;
        if (loadOriginal)
        {
            original = await LoadOriginalAsync(objectToUpdate, ctx, cancellationToken).ConfigureAwait(false);
            if (original == null)
            {
                throw new ConcurrencyConflictException(
                    "The record was deleted or changed before it could be updated.",
                    GetDialect(ctx).DatabaseType);
            }
        }

        return TryBuildUpdateInternal(objectToUpdate, original, ctx);
    }

    /// <summary>
    /// Synchronous version of BuildUpdate for internal framework use where original record 
    /// loading is not required or already handled.
    /// </summary>
    internal ISqlContainer BuildUpdate(TEntity entity, IDatabaseContext? context = null)
    {
        return BuildUpdateInternal(entity, null, context ?? _context);
    }

    private ISqlContainer BuildUpdateInternal(TEntity objectToUpdate, TEntity? original, IDatabaseContext ctx) =>
        TryBuildUpdateInternal(objectToUpdate, original, ctx) ?? throw NoChanges();

    private ISqlContainer? TryBuildUpdateInternal(TEntity objectToUpdate, TEntity? original, IDatabaseContext ctx)
    {
        var sc = ctx.CreateSqlContainer();
        var dialect = GetDialect(ctx);
        var template = GetTemplatesForDialect(dialect);

        if (_hasAuditColumns)
        {
            SetAuditFields(objectToUpdate, true);
        }

        var counters = new ClauseCounters();
        sc.Query.Append(template.UpdateSqlPrefix);
        var (columnsAdded, parameters) = BuildSetClause(objectToUpdate, original, dialect, ref counters, sc.Query);
        if (columnsAdded == 0)
        {
            sc.Dispose();
            return null;
        }

        // Append version increment directly from cached clause (no string alloc)
        if (template.VersionIncrementClause != null)
        {
            sc.Query.Append(template.VersionIncrementClause);
        }

        var idName = counters.NextKey();
        var pId = dialect.CreateDbParameter(idName, _idColumn!.DbType,
            _idColumn.MakeParameterValueFromField(objectToUpdate));
        parameters.Add(pId);

        sc.Query.Append(template.UpdateSqlSuffix);
        if (dialect.SupportsNamedParameters)
        {
            sc.Query.Append(dialect.ParameterMarker);
            sc.Query.Append(idName);
        }
        else
        {
            sc.Query.Append('?');
        }

        if (_versionColumn != null)
        {
            var versionValue = _versionColumn.MakeParameterValueFromField(objectToUpdate);
            var versionParam = AppendVersionCondition(sc, versionValue, dialect, ref counters);
            if (versionParam != null)
            {
                parameters.Add(versionParam);
            }
        }

        sc.AddParameters(parameters);
        return sc;
    }

    private ValueTask<TEntity?> LoadOriginalAsync(TEntity objectToUpdate, IDatabaseContext? context = null)
    {
        return LoadOriginalAsync(objectToUpdate, context, CancellationToken.None);
    }

    private async ValueTask<TEntity?> LoadOriginalAsync(TEntity objectToUpdate, IDatabaseContext? context,
        CancellationToken cancellationToken)
    {
        var ctx = context ?? _context;
        var idValue = _idColumn!.MakeParameterValueFromField(objectToUpdate);
        if (IsDefaultId(idValue))
        {
            return null;
        }

        try
        {
            if (idValue is TRowID typedId)
            {
                return await RetrieveOneAsync(typedId, ctx, cancellationToken).ConfigureAwait(false);
            }

            var targetType = typeof(TRowID);
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            var converted = underlying switch
            {
                _ when underlying == typeof(Guid) => ConvertToGuid(idValue),
                _ => TypeCoercionHelper.ConvertWithCache(idValue!, underlying)
            };

            if (converted == null)
            {
                return null;
            }

            return await RetrieveOneAsync((TRowID)converted, ctx, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException)
        {
            throw new InvalidOperationException(
                $"Cannot convert ID value '{idValue}' of type {idValue!.GetType().Name} to {typeof(TRowID).Name}: {ex.Message}",
                ex);
        }
    }

    private static Guid ConvertToGuid(object? value)
    {
        return value switch
        {
            Guid guid => guid,
            string text => Guid.Parse(text),
            _ => Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
        };
    }

    // Writes SET clause items directly into queryTarget (sc.Query) to avoid the
    // intermediate SbLite.ToString() + string.Format allocations.
    // Returns the count of columns written (0 = no changes detected).
    private (int columnsAdded, List<DbParameter> parameters) BuildSetClause(TEntity updated, TEntity? original,
        ISqlDialect dialect, ref ClauseCounters counters, ISqlQueryBuilder queryTarget)
    {
        var template = GetTemplatesForDialect(dialect);

        // Hoist dialect properties outside loop — constant per dialect, avoid N virtual calls
        var supportsNamed = dialect.SupportsNamedParameters;
        var paramMarker = dialect.ParameterMarker;

        // Pre-size parameters list based on updatable column count
        var parameters = new List<DbParameter>(template.UpdateColumns.Count);
        var columnsAdded = 0;

        for (var i = 0; i < template.UpdateColumns.Count; i++)
        {
            var column = template.UpdateColumns[i];
            var newValue = column.MakeParameterValueFromField(updated);
            var originalValue = original != null ? column.MakeParameterValueFromField(original) : null;

            if (original != null && ValuesAreEqual(newValue, originalValue, column.DbType))
            {
                continue;
            }

            if (columnsAdded > 0)
            {
                queryTarget.Append(SqlFragments.Comma);
            }

            columnsAdded++;

            if (Utils.IsNullOrDbNull(newValue))
            {
                queryTarget.Append(template.UpdateColumnWrappedNames[i]);
                queryTarget.Append(" = NULL");
            }
            else
            {
                var name = counters.NextSet();
                var param = dialect.CreateDbParameter(name, column.DbType, newValue);
                dialect.MarkColumnParameter(param, column);

                parameters.Add(param);

                queryTarget.Append(template.UpdateColumnWrappedNames[i]);
                queryTarget.Append(SqlFragments.EqualsOp);
                if (dialect.RendersColumnArgument(column))
                {
                    // JSON columns need the full marker string for wrapping — rare path
                    queryTarget.Append(dialect.RenderColumnArgument(dialect.MakeParameterName(name), column));
                }
                else if (supportsNamed)
                {
                    // Direct append — eliminates MakeParameterName's Replace+Concat per column
                    queryTarget.Append(paramMarker);
                    queryTarget.Append(name);
                }
                else
                {
                    queryTarget.Append('?');
                }
            }
        }

        return (columnsAdded, parameters);
    }

    internal static bool ValuesAreEqual(object? newValue, object? originalValue, DbType dbType)
    {
        if (newValue == null && originalValue == null)
        {
            return true;
        }

        if (newValue == null || originalValue == null)
        {
            return false;
        }

        if (newValue is byte[] a && originalValue is byte[] b)
        {
            return a.SequenceEqual(b);
        }

        switch (dbType)
        {
            case DbType.Decimal:
            case DbType.Currency:
            case DbType.VarNumeric:
                return decimal.Compare(Convert.ToDecimal(newValue, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(originalValue, CultureInfo.InvariantCulture)) == 0;
            case DbType.DateTime:
            case DbType.DateTime2:
                return TypeCoercionHelper.NormalizeDateTime(Convert.ToDateTime(newValue, CultureInfo.InvariantCulture)) ==
                       TypeCoercionHelper.NormalizeDateTime(Convert.ToDateTime(originalValue, CultureInfo.InvariantCulture));
            case DbType.DateTimeOffset:
                return NormalizeDateTimeOffset(newValue).UtcDateTime ==
                       NormalizeDateTimeOffset(originalValue).UtcDateTime;
            default:
                return Equals(newValue, originalValue);
        }
    }

    // As every read path normalizes them (DRY-010): a DateTime keeps a Local offset and is otherwise UTC;
    // timestamp text reads through TypeCoercionHelper.TryParseTimestampText.
    internal static DateTimeOffset NormalizeDateTimeOffset(object value)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                return dto;
            case DateTime dt:
                return TypeCoercionHelper.DateTimeOffsetFromDateTime(dt, TypeCoercionOptions.Default);
            case string s:
                return TypeCoercionHelper.TryParseTimestampText(s.Trim(), out var parsed)
                    ? parsed
                    : throw new FormatException("The value is not a timestamp.");
            default:
                var converted = Convert.ToDateTime(value, CultureInfo.InvariantCulture);
                return new DateTimeOffset(TypeCoercionHelper.NormalizeDateTime(converted), TimeSpan.Zero);
        }
    }
}
