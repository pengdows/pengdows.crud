// =============================================================================
// FILE: BaseTableGateway.Count.cs
// PURPOSE: Parameterized COUNT helpers available on every gateway variant.
//
// AI SUMMARY:
// - CountAllAsync() — SELECT COUNT(*) FROM table
// - CountWhereAsync(column, value, isLike) — COUNT with equality or LIKE
// - CountWhereNullAsync(column) — COUNT WHERE col IS NULL
// - CountWhereEqualsAsync(column, value, andWhereNull?, andWhereNotNull?) — compound COUNT
// All identifiers go through WrapObjectName; all values are parameterized.
// =============================================================================

using System.Data;

namespace pengdows.crud;

public abstract partial class BaseTableGateway<TEntity>
    where TEntity : class, new()
{
    /// <summary>Returns a COUNT(*) of all rows in this table.</summary>
    public async ValueTask<long> CountAllAsync(IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        using var sc = ctx.CreateSqlContainer();
        sc.Query.Append("SELECT COUNT(*) FROM ").Append(WrappedTableName);
        return await sc.ExecuteScalarOrNullAsync<long?>() ?? 0;
    }

    /// <summary>
    /// Returns a COUNT(*) filtered by a single column equality (or LIKE) check.
    /// </summary>
    public async ValueTask<long> CountWhereAsync(
        string column,
        string value,
        bool isLike = false,
        IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        using var sc = ctx.CreateSqlContainer();
        sc.Query.Append("SELECT COUNT(*) FROM ").Append(WrappedTableName)
            .Append(" WHERE ").Append(sc.WrapObjectName(column))
            .Append(isLike ? " LIKE " : " = ");
        var p = sc.AddParameterWithValue("v", DbType.String, value);
        sc.Query.Append(sc.MakeParameterName(p));
        return await sc.ExecuteScalarOrNullAsync<long?>() ?? 0;
    }

    /// <summary>Returns a COUNT(*) of rows where <paramref name="column"/> IS NULL.</summary>
    public async ValueTask<long> CountWhereNullAsync(
        string column,
        IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        using var sc = ctx.CreateSqlContainer();
        sc.Query.Append("SELECT COUNT(*) FROM ").Append(WrappedTableName)
            .Append(" WHERE ").Append(sc.WrapObjectName(column)).Append(" IS NULL");
        return await sc.ExecuteScalarOrNullAsync<long?>() ?? 0;
    }

    /// <summary>
    /// Returns a COUNT(*) where <paramref name="column"/> equals <paramref name="value"/>,
    /// optionally combined with an IS NULL or IS NOT NULL check on a second column.
    /// </summary>
    public async ValueTask<long> CountWhereEqualsAsync(
        string column,
        string value,
        string? andWhereNull = null,
        string? andWhereNotNull = null,
        IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        using var sc = ctx.CreateSqlContainer();
        sc.Query.Append("SELECT COUNT(*) FROM ").Append(WrappedTableName)
            .Append(" WHERE ").Append(sc.WrapObjectName(column)).Append(" = ");
        var p = sc.AddParameterWithValue("v", DbType.String, value);
        sc.Query.Append(sc.MakeParameterName(p));

        if (andWhereNull != null)
            sc.Query.Append(" AND ").Append(sc.WrapObjectName(andWhereNull)).Append(" IS NULL");
        else if (andWhereNotNull != null)
            sc.Query.Append(" AND ").Append(sc.WrapObjectName(andWhereNotNull)).Append(" IS NOT NULL");

        return await sc.ExecuteScalarOrNullAsync<long?>() ?? 0;
    }
}
