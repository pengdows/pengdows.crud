using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using pengdows.crud.dialects;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    private class CachedSqlTemplates
    {
        public string InsertSql = null!;
        public List<IColumnInfo> InsertColumns = null!;
        public List<string> InsertParameterNames = null!;
        public string DeleteSql = null!;
        public string UpdateSql = null!;
        public List<IColumnInfo> UpdateColumns = null!;
    }

    /// <summary>
    /// Neutral Token Replacement Strategy for SQL Template Caching
    /// 
    /// This optimization allows EntityHelper to cache SQL templates in a database-agnostic format,
    /// then replace neutral tokens with dialect-specific tokens at runtime. This approach provides
    /// significant performance benefits by avoiding repeated SQL string construction.
    /// 
    /// Token Definitions:
    /// - {Q} = Quote prefix (e.g., "[" for SQL Server, "`" for MySQL, "\"" for PostgreSQL)
    /// - {q} = Quote suffix (e.g., "]" for SQL Server, "`" for MySQL, "\"" for PostgreSQL)  
    /// - {S} = Composite identifier separator (typically "." for schema.table notation)
    /// - {P}paramname = Parameter placeholder (replaced with dialect-specific parameter markers)
    /// 
    /// Example transformation:
    /// Neutral: "SELECT {Q}Name{q} FROM {Q}Users{q} WHERE {Q}Id{q} = {P}id"
    /// SQL Server: "SELECT [Name] FROM [Users] WHERE [Id] = @id"
    /// PostgreSQL: "SELECT \"Name\" FROM \"Users\" WHERE \"Id\" = $1"
    /// MySQL: "SELECT `Name` FROM `Users` WHERE `Id` = ?"
    /// 
    /// This strategy enables:
    /// 1. Single cached template per operation type (instead of per dialect)
    /// 2. Fast string replacement instead of complex SQL building
    /// 3. Thread-safe caching without dialect-specific lock contention
    /// 4. Reduced memory usage across multi-database applications
    /// </summary>
    
    // Neutral tokens used in cached SQL; replaced with dialect-specific tokens on retrieval
    private const string NeutralQuotePrefix = "{Q}";
    private const string NeutralQuoteSuffix = "{q}";
    private const string NeutralSeparator = "{S}";

    private static string WrapNeutral(string name)
    {
        return string.IsNullOrEmpty(name) ? string.Empty : NeutralQuotePrefix + name + NeutralQuoteSuffix;
    }

    private static string ReplaceNeutralTokens(string sql, ISqlDialect dialect)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return sql;
        }
        return sql.Replace(NeutralQuotePrefix, dialect.QuotePrefix)
                  .Replace(NeutralQuoteSuffix, dialect.QuoteSuffix)
                  .Replace(NeutralSeparator, dialect.CompositeIdentifierSeparator);
    }

    private CachedSqlTemplates BuildCachedSqlTemplatesNeutral()
    {
        var idCol = _tableInfo.Columns.Values.FirstOrDefault(c => c.IsId)
                     ?? throw new InvalidOperationException($"No ID column defined for {typeof(TEntity).Name}");

        var insertColumns = _tableInfo.Columns.Values
            .Where(c => !c.IsNonInsertable && (!c.IsId || c.IsIdIsWritable))
            .Where(c => _auditValueResolver != null || (!c.IsCreatedBy && !c.IsLastUpdatedBy))
            .Cast<IColumnInfo>()
            .ToList();

        var wrappedCols = new List<string>();
        for (var i = 0; i < insertColumns.Count; i++)
        {
            wrappedCols.Add(WrapNeutral(insertColumns[i].Name));
        }

        var paramNames = new List<string>();
        var valuePlaceholders = new List<string>();
        for (var i = 0; i < insertColumns.Count; i++)
        {
            var name = $"i{i}";
            paramNames.Add(name);
            valuePlaceholders.Add("{P}" + name);
        }

        var insertSql =
            $"INSERT INTO {BuildWrappedTableNameNeutral()} ({string.Join(", ", wrappedCols)}) VALUES ({string.Join(", ", valuePlaceholders)})";
        var deleteSql =
            $"DELETE FROM {BuildWrappedTableNameNeutral()} WHERE {WrapNeutral(idCol.Name)} = {{0}}";

        var updateColumns = _tableInfo.Columns.Values
            .Where(c => !c.IsId && !c.IsVersion && !c.IsNonUpdateable && !c.IsCreatedBy && !c.IsCreatedOn)
            .Cast<IColumnInfo>()
            .ToList();

        var updateSql =
            $"UPDATE {BuildWrappedTableNameNeutral()} SET {{0}} WHERE {WrapNeutral(idCol.Name)} = {{1}}";

        return new CachedSqlTemplates
        {
            InsertSql = insertSql,
            InsertColumns = insertColumns,
            InsertParameterNames = paramNames,
            DeleteSql = deleteSql,
            UpdateSql = updateSql,
            UpdateColumns = updateColumns
        };
    }

    private CachedSqlTemplates GetTemplatesForDialect(ISqlDialect dialect)
    {
        return _templatesByDialect
            .GetOrAdd(dialect.DatabaseType, _ => new Lazy<CachedSqlTemplates>(() =>
            {
                var neutral = _cachedSqlTemplates.Value;
                string RenderParams(string sql)
                {
                    // Replace neutral param tokens with dialect-appropriate placeholders.
                    // Named: {P}name -> @name or :name
                    // Positional: {P}name -> ?
                    return Regex.Replace(sql, "\\{P\\}([A-Za-z_][A-Za-z0-9_]*)",
                        m => dialect.SupportsNamedParameters
                            ? string.Concat(dialect.ParameterMarker, m.Groups[1].Value)
                            : dialect.ParameterMarker);
                }

                return new CachedSqlTemplates
                {
                    InsertSql = RenderParams(ReplaceNeutralTokens(neutral.InsertSql, dialect)),
                    InsertColumns = neutral.InsertColumns,
                    InsertParameterNames = neutral.InsertParameterNames,
                    DeleteSql = ReplaceNeutralTokens(neutral.DeleteSql, dialect),
                    UpdateSql = ReplaceNeutralTokens(neutral.UpdateSql, dialect),
                    UpdateColumns = neutral.UpdateColumns
                };
            }))
            .Value;
    }

    private string BuildWrappedTableNameNeutral()
    {
        if (string.IsNullOrWhiteSpace(_tableInfo.Schema))
        {
            return WrapNeutral(_tableInfo.Name);
        }

        var sb = new StringBuilder();
        sb.Append(WrapNeutral(_tableInfo.Schema));
        sb.Append(NeutralSeparator);
        sb.Append(WrapNeutral(_tableInfo.Name));
        return sb.ToString();
    }
}
