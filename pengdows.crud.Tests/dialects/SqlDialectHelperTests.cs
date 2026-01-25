using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class SqlDialectHelperTests
{
    [Fact]
    public void BuildSessionSettingsScript_IncludesOnlyDifferingEntries()
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANSI_NULLS"] = "ON",
            ["QUOTED_IDENTIFIER"] = "ON"
        };

        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANSI_NULLS"] = "ON",
            ["QUOTED_IDENTIFIER"] = "OFF"
        };

        var result = InvokeBuildSessionSettingsScript(
            expected,
            current,
            (name, value) => $"SET {name} {value};");

        Assert.Equal("SET QUOTED_IDENTIFIER ON;", result.Trim());
    }

    [Fact]
    public void GetSqlServerSessionSettings_UsesFallbackWhenReaderThrows()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLoggerFactory.Instance.CreateLogger(nameof(SqlServerDialect)));
        var connection = new ThrowingSessionConnection();
        var (settings, usedFallback, _) = CallSessionSettingsResult(dialect, "GetSqlServerSessionSettings", connection);

        Assert.True(usedFallback);
        Assert.Contains("SET ANSI_NULLS ON", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSqlServerSessionSettings_GeneratesExpectedScript()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLoggerFactory.Instance.CreateLogger(nameof(SqlServerDialect)));
        var rows = new[]
        {
            new SessionSettingRow(new[] { "variable_name", "value" }, new[] { "NUMERIC_ROUNDABORT", "SET" }),
            new SessionSettingRow(new[] { "variable_name", "value" }, new[] { "QUOTED_IDENTIFIER", "OFF" })
        };

        var connection = new SessionSettingsConnection(rows);
        var (settings, usedFallback, snapshot) = CallSessionSettingsResult(dialect, "GetSqlServerSessionSettings", connection);

        Assert.False(usedFallback);
        Assert.Contains("SET NUMERIC_ROUNDABORT OFF", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET QUOTED_IDENTIFIER ON", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ON", snapshot["NUMERIC_ROUNDABORT"]);
        Assert.Equal("OFF", snapshot["QUOTED_IDENTIFIER"]);
    }

    [Fact]
    public void GetPostgreSqlSessionSettings_BuildsScriptFromReader()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLoggerFactory.Instance.CreateLogger(nameof(PostgreSqlDialect)));
        var rows = new[]
        {
            new SessionSettingRow(new[] { "name", "setting" }, new[] { "standard_conforming_strings", "off" }),
            new SessionSettingRow(new[] { "name", "setting" }, new[] { "client_min_messages", "panic" })
        };

        var connection = new SessionSettingsConnection(rows);
        var (settings, usedFallback, snapshot) = CallSessionSettingsResult(dialect, "GetPostgreSqlSessionSettings", connection);

        Assert.False(usedFallback);
        Assert.Contains("SET standard_conforming_strings = on", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET client_min_messages = warning", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("off", snapshot["standard_conforming_strings"]);
        Assert.Equal("panic", snapshot["client_min_messages"]);
    }

    [Fact]
    public void PostgreSqlDialect_TryMarkJsonParameter_StampsStringType()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLoggerFactory.Instance.CreateLogger(nameof(PostgreSqlDialect)));
        var parameter = new fakeDbParameter();
        var column = new StubColumnInfo { Name = "json_payload", DbType = DbType.String, IsJsonType = true };

        dialect.TryMarkJsonParameter(parameter, column);

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(0, parameter.Size);
    }

    [Fact]
    public void IsVersionAtLeast_ReturnsFalseWhenUndetected()
    {
        var dialect = CreateTestDialect();
        Assert.False(dialect.TestIsVersionAtLeast(1));
    }

    [Fact]
    public void IsVersionAtLeast_UsesParsedVersion()
    {
        var dialect = CreateTestDialect();
        SetParsedVersion(dialect, new Version(3, 7, 1));

        Assert.True(dialect.TestIsVersionAtLeast(3, 6));
        Assert.False(dialect.TestIsVersionAtLeast(4, 0));
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_ThrowsForEmptyKeys()
    {
        var dialect = CreateNaturalKeyDialect(SupportedDatabase.PostgreSql, true);
        Assert.Throws<InvalidOperationException>(() => dialect.GetNaturalKeyLookupQuery("table", "id", Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_ThrowsForMismatchedCounts()
    {
        var dialect = CreateNaturalKeyDialect(SupportedDatabase.PostgreSql, true);
        Assert.Throws<ArgumentException>(() => dialect.GetNaturalKeyLookupQuery("table", "id", new[] { "name" }, Array.Empty<string>()));
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_IncludesSqlServerTopClause()
    {
        var dialect = CreateNaturalKeyDialect(SupportedDatabase.SqlServer, supportsIdentityColumns: true);
        var sql = dialect.GetNaturalKeyLookupQuery("orders", "id", new[] { "name" }, new[] { ":name" });

        Assert.Contains("SELECT TOP 1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY \"id\" DESC", sql);
        Assert.DoesNotContain("LIMIT 1", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_AppendsLimitForNonSqlServer()
    {
        var dialect = CreateNaturalKeyDialect(SupportedDatabase.PostgreSql, supportsIdentityColumns: true);
        var sql = dialect.GetNaturalKeyLookupQuery("customers", "id", new[] { "email" }, new[] { ":email" });

        Assert.Contains("LIMIT 1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY \"id\" DESC", sql);
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_AddsRowNumForOracle()
    {
        var dialect = CreateNaturalKeyDialect(SupportedDatabase.Oracle, supportsIdentityColumns: true);
        var sql = dialect.GetNaturalKeyLookupQuery("items", "id", new[] { "sku" }, new[] { ":sku" });

        Assert.Contains("AND ROWNUM = 1", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string InvokeBuildSessionSettingsScript(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> current,
        Func<string, string, string> formatter)
    {
        var method = typeof(SqlDialect).GetMethod("BuildSessionSettingsScript", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new InvalidOperationException("Missing BuildSessionSettingsScript");
        return (string)method.Invoke(null, new object[] { expected, current, formatter })!;
    }

    private static (string Settings, bool UsedFallback, IReadOnlyDictionary<string, string> Snapshot) CallSessionSettingsResult(SqlDialect dialect, string methodName, IDbConnection connection)
    {
        var method = dialect.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException($"Missing {methodName}");
        var result = method.Invoke(dialect, new object[] { connection })!;
        var settings = (string)result.GetType().GetProperty("Settings")!.GetValue(result)!;
        var usedFallback = (bool)result.GetType().GetProperty("UsedFallback")!.GetValue(result)!;
        var snapshot = (IReadOnlyDictionary<string, string>)result.GetType().GetProperty("Snapshot")!.GetValue(result)!;
        return (settings, usedFallback, snapshot);
    }

    private static TestDialect CreateTestDialect()
    {
        return new TestDialect(new fakeDbFactory(SupportedDatabase.Unknown), NullLoggerFactory.Instance.CreateLogger(nameof(TestDialect)));
    }

    private static void SetParsedVersion(SqlDialect dialect, Version version)
    {
        var info = new DatabaseProductInfo
        {
            ParsedVersion = version,
            DatabaseType = SupportedDatabase.Unknown,
            ProductName = "Test",
            ProductVersion = version.ToString()
        };

        var field = typeof(SqlDialect).GetField("_productInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                   ?? throw new InvalidOperationException("Missing _productInfo field");
        field.SetValue(dialect, info);
    }

    private static SqlDialect CreateNaturalKeyDialect(SupportedDatabase product, bool supportsIdentityColumns)
    {
        return new NaturalKeyDialect(new fakeDbFactory(SupportedDatabase.Unknown), NullLoggerFactory.Instance.CreateLogger(nameof(NaturalKeyDialect)), product, supportsIdentityColumns);
    }

    private sealed class TestDialect : SqlDialect
    {
        public TestDialect(DbProviderFactory factory, Microsoft.Extensions.Logging.ILogger logger) : base(factory, logger)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public override string ParameterMarker => ":";
        public override int MaxParameterLimit => 256;
        public override int ParameterNameMaxLength => 64;
        public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;
        public bool TestIsVersionAtLeast(int major, int minor = 0, int build = 0) => IsVersionAtLeast(major, minor, build);
    }

    private sealed class NaturalKeyDialect : SqlDialect
    {
        private readonly SupportedDatabase _product;
        private readonly bool _supportsIdentity;

        public NaturalKeyDialect(DbProviderFactory factory, Microsoft.Extensions.Logging.ILogger logger, SupportedDatabase product, bool supportsIdentityColumns)
            : base(factory, logger)
        {
            _product = product;
            _supportsIdentity = supportsIdentityColumns;
        }

        public override SupportedDatabase DatabaseType => _product;
        public override string ParameterMarker => ":";
        public override int MaxParameterLimit => 256;
        public override int ParameterNameMaxLength => 64;
        public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;
        public override bool SupportsIdentityColumns => _supportsIdentity;
    }

    private sealed class SessionSettingsConnection : IDbConnection
    {
        private readonly IEnumerable<SessionSettingRow> _rows;

        public SessionSettingsConnection(IEnumerable<SessionSettingRow> rows)
        {
            _rows = rows;
        }

        private string _connectionString = string.Empty;
        [AllowNull]
        public string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }
        public int ConnectionTimeout => 30;
        public string Database => "Test";
        public ConnectionState State => ConnectionState.Open;
        public string DataSource => "SessionSettings";
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) { }
        public void Close() { }
        public IDbCommand CreateCommand() => new SessionSettingsCommand(_rows);
        public void Open() { }
        public void Dispose() { }
    }

    private sealed class ThrowingSessionConnection : IDbConnection
    {
        private string _connectionString = string.Empty;
        [AllowNull]
        public string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }
        public int ConnectionTimeout => 0;
        public string Database => "Throwing";
        public ConnectionState State => ConnectionState.Closed;
        public string DataSource => "Throwing";
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) { }
        public void Close() { }
        public IDbCommand CreateCommand() => new ThrowingSessionCommand();
        public void Open() { }
        public void Dispose() { }
    }

    private sealed class SessionSettingsCommand : IDbCommand
    {
        private readonly IEnumerable<SessionSettingRow> _rows;
        public SessionSettingsCommand(IEnumerable<SessionSettingRow> rows)
        {
            _rows = rows;
        }

        private string _commandText = string.Empty;
        [AllowNull]
        public string CommandText
        {
            get => _commandText;
            set => _commandText = value ?? string.Empty;
        }
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; } = CommandType.Text;
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters => throw new NotSupportedException();
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }
        public void Cancel() { }
        public IDbDataParameter CreateParameter() => throw new NotSupportedException();
        public void Dispose() { }
        public int ExecuteNonQuery() => throw new NotSupportedException();
        public IDataReader ExecuteReader()
        {
            var clones = _rows.Select(r => r.Clone()).ToList();
            return new SessionSettingsReader(clones);
        }

        public IDataReader ExecuteReader(CommandBehavior behavior) => ExecuteReader();
        public object ExecuteScalar() => throw new NotSupportedException();
        public void Prepare() { }
    }

    private sealed class ThrowingSessionCommand : IDbCommand
    {
        private string _commandText = string.Empty;
        [AllowNull]
        public string CommandText
        {
            get => _commandText;
            set => _commandText = value ?? string.Empty;
        }
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; }
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters => throw new NotSupportedException();
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }
        public void Cancel() { }
        public IDbDataParameter CreateParameter() => throw new NotSupportedException();
        public void Dispose() { }
        public int ExecuteNonQuery() => throw new NotSupportedException();
        public IDataReader ExecuteReader() => throw new InvalidOperationException("boom");
        public IDataReader ExecuteReader(CommandBehavior behavior) => ExecuteReader();
        public object ExecuteScalar() => throw new NotSupportedException();
        public void Prepare() { }
    }

    private sealed class SessionSettingsReader : IDataReader
    {
        private readonly List<SessionSettingRow> _rows;
        private int _index = -1;

        public SessionSettingsReader(List<SessionSettingRow> rows)
        {
            _rows = rows;
        }

        public object this[int i] => GetValue(i);
        public object this[string name] => throw new NotSupportedException();
        public int Depth => 0;
        public bool IsClosed => false;
        public int RecordsAffected => 0;
        public int FieldCount => _index >= 0 ? _rows[_index].ColumnNames.Length : (_rows.FirstOrDefault()?.ColumnNames.Length ?? 0);
        public void Close() { }
        public void Dispose() { }
        public bool GetBoolean(int i) => bool.Parse(GetString(i));
        public byte GetByte(int i) => byte.Parse(GetString(i));
        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
        public char GetChar(int i) => char.Parse(GetString(i));
        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
        public IDataReader GetData(int i) => throw new NotSupportedException();
        public string GetDataTypeName(int i) => "string";
        public DateTime GetDateTime(int i) => DateTime.Parse(GetString(i));
        public decimal GetDecimal(int i) => decimal.Parse(GetString(i));
        public double GetDouble(int i) => double.Parse(GetString(i));
        public Type GetFieldType(int i) => typeof(string);
        public float GetFloat(int i) => float.Parse(GetString(i));
        public Guid GetGuid(int i) => Guid.Parse(GetString(i));
        public short GetInt16(int i) => short.Parse(GetString(i));
        public int GetInt32(int i) => int.Parse(GetString(i));
        public long GetInt64(int i) => long.Parse(GetString(i));
        public string GetName(int i) => _rows[_index].ColumnNames.ElementAtOrDefault(i) ?? string.Empty;
        public int GetOrdinal(string name) => Array.IndexOf(_rows.FirstOrDefault()?.ColumnNames ?? Array.Empty<string>(), name);
        public DataTable GetSchemaTable() => throw new NotSupportedException();
        public string GetString(int i) => _rows[_index].Values[i];
        public object GetValue(int i) => GetString(i);
        public int GetValues(object[] values)
        {
            var current = _rows[_index];
            for (var i = 0; i < current.Values.Length && i < values.Length; i++)
            {
                values[i] = current.Values[i];
            }
            return Math.Min(current.Values.Length, values.Length);
        }
        public bool IsDBNull(int i) => false;
        public bool NextResult() => false;
        public bool Read()
        {
            if (_index + 1 >= _rows.Count)
            {
                return false;
            }
            _index++;
            return true;
        }
    }

    private sealed class SessionSettingRow
    {
        public SessionSettingRow(string[] columnNames, string[] values)
        {
            ColumnNames = columnNames;
            Values = values;
        }

        public string[] ColumnNames { get; }
        public string[] Values { get; }

        public SessionSettingRow Clone() => new(ColumnNames.ToArray(), Values.ToArray());
    }

    private sealed class StubColumnInfo : IColumnInfo
    {
        public string Name { get; init; } = string.Empty;
        public System.Reflection.PropertyInfo PropertyInfo { get; init; } = typeof(object).GetProperty(nameof(object.ToString))!;
        public bool IsId { get; init; }
        public DbType DbType { get; set; }
        public bool IsNonUpdateable { get; set; }
        public bool IsNonInsertable { get; set; }
        public bool IsEnum { get; set; }
        public Type? EnumType { get; set; }
        public Type? EnumUnderlyingType { get; set; }
        public bool IsJsonType { get; set; }
        public System.Text.Json.JsonSerializerOptions JsonSerializerOptions { get; set; } = new();
        public bool IsIdIsWritable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public int PkOrder { get; set; }
        public bool IsVersion { get; set; }
        public bool IsCreatedBy { get; set; }
        public bool IsCreatedOn { get; set; }
        public bool IsLastUpdatedBy { get; set; }
        public bool IsLastUpdatedOn { get; set; }
        public int Ordinal { get; set; }
        public object? MakeParameterValueFromField<T>(T objectToCreate) => objectToCreate;
    }
}
