// namespace pengdows.crud;
//
// using System;
// using System.Data;
// using System.Data.Common;
// using System.Text;
// using pengdows.crud.enums;
//
// /// <summary>
// /// Provides database-dialect-specific helpers for building SQL.
// /// </summary>
// internal class SqlDialect
// {
//     private readonly IDataSourceInformation _info;
//     private readonly DbProviderFactory _factory;
//     private readonly Func<int, int, string> _nameGenerator;
//
//     internal SqlDialect(IDataSourceInformation info, DbProviderFactory factory, Func<int, int, string> nameGenerator)
//     {
//         _info = info;
//         _factory = factory;
//         _nameGenerator = nameGenerator;
//     }
//
//     internal string QuotePrefix => _info.QuotePrefix;
//     internal string QuoteSuffix => _info.QuoteSuffix;
//     internal string CompositeIdentifierSeparator => _info.CompositeIdentifierSeparator;
//     
//     internal int ParameterNameMaxLength => _info.ParameterNameMaxLength;
//     internal SqlStandardLevel StandardCompliance => _info.StandardCompliance;
//
//     internal bool SupportsIntegrityConstraints => StandardCompliance >= SqlStandardLevel.Sql89;
//     internal bool SupportsJoins => StandardCompliance >= SqlStandardLevel.Sql92;
//     internal bool SupportsOuterJoins => StandardCompliance >= SqlStandardLevel.Sql92;
//     internal bool SupportsSubqueries => StandardCompliance >= SqlStandardLevel.Sql92;
//     internal bool SupportsUnion => StandardCompliance >= SqlStandardLevel.Sql92;
//     internal bool SupportsUserDefinedTypes => StandardCompliance >= SqlStandardLevel.Sql99;
//     internal bool SupportsArrayTypes => StandardCompliance >= SqlStandardLevel.Sql99;
//     internal bool SupportsRegularExpressions => StandardCompliance >= SqlStandardLevel.Sql99;
//     internal bool SupportsMerge => StandardCompliance >= SqlStandardLevel.Sql2003;
//     internal bool SupportsXmlTypes => StandardCompliance >= SqlStandardLevel.Sql2003;
//     internal bool SupportsWindowFunctions => StandardCompliance >= SqlStandardLevel.Sql2003;
//     internal bool SupportsCommonTableExpressions => StandardCompliance >= SqlStandardLevel.Sql2003;
//     internal bool SupportsInsteadOfTriggers => StandardCompliance >= SqlStandardLevel.Sql2008;
//     internal bool SupportsTruncateTable => StandardCompliance >= SqlStandardLevel.Sql2008;
//     internal bool SupportsTemporalData => StandardCompliance >= SqlStandardLevel.Sql2011;
//     internal bool SupportsEnhancedWindowFunctions => StandardCompliance >= SqlStandardLevel.Sql2011;
//     internal bool SupportsJsonTypes => StandardCompliance >= SqlStandardLevel.Sql2016;
//     internal bool SupportsRowPatternMatching => StandardCompliance >= SqlStandardLevel.Sql2016;
//     internal bool SupportsMultidimensionalArrays => StandardCompliance >= SqlStandardLevel.Sql2019;
//     internal bool SupportsPropertyGraphQueries => StandardCompliance >= SqlStandardLevel.Sql2023;
//     internal virtual bool SupportsInsertOnConflict => false;
//     internal virtual bool RequiresStoredProcParameterNameMatch => false;
//     internal virtual bool SupportsNamespaces => true;
//
//     internal string WrapObjectName(string name)
//     {
//         var qp = QuotePrefix;
//         var qs = QuoteSuffix;
//         var tmp = name?.Replace(qp, string.Empty)?.Replace(qs, string.Empty);
//         if (string.IsNullOrEmpty(tmp))
//         {
//             return string.Empty;
//         }
//
//         var parts = tmp.Split(CompositeIdentifierSeparator);
//         var sb = new StringBuilder();
//         for (var i = 0; i < parts.Length; i++)
//         {
//             if (i > 0)
//             {
//                 sb.Append(CompositeIdentifierSeparator);
//             }
//
//             sb.Append(qp);
//             sb.Append(parts[i]);
//             sb.Append(qs);
//         }
//
//         return sb.ToString();
//     }
//
//     internal string MakeParameterName(DbParameter dbParameter)
//     {
//         return MakeParameterName(dbParameter.ParameterName);
//     }
//
//     internal string MakeParameterName(string parameterName)
//     {
//         if (!_info.SupportsNamedParameters)
//         {
//             return "?";
//         }
//
//         return string.Concat(_info.ParameterMarker, parameterName);
//     }
//
//     internal DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
//     {
//         var p = _factory.CreateParameter() ?? throw new InvalidOperationException("Failed to create parameter.");
//         if (string.IsNullOrWhiteSpace(name))
//         {
//             name = _nameGenerator(5, ParameterNameMaxLength);
//         }
//
//         var valueIsNull = Utils.IsNullOrDbNull(value);
//         p.ParameterName = name;
//         p.DbType = type;
//         p.Value = valueIsNull ? DBNull.Value : value;
//         if (!valueIsNull && p.DbType == DbType.String && value is string s)
//         {
//             p.Size = Math.Max(s.Length, 1);
//         }
//
//         return p;
//     }
//
//     internal DbParameter CreateDbParameter<T>(DbType type, T value)
//     {
//         return CreateDbParameter(null, type, value);
//     }
// }
