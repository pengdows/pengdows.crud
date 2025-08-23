#region

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.FakeDb;

public class FakeDbCommand : DbCommand
{
    public FakeDbCommand(DbConnection connection)
    {
        Connection = connection;
    }

    public FakeDbCommand()
    {
    }

    [AllowNull]
    public override string CommandText { get; set; }
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }

    public new DbConnection Connection { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    public new DbTransaction Transaction { get; set; }

    protected override DbParameterCollection DbParameterCollection
        => new FakeParameterCollection();

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override void Prepare()
    {
    }

    private FakeDbConnection? FakeConnection => Connection as FakeDbConnection;

    public override int ExecuteNonQuery()
    {
        var conn = FakeConnection;
        if (conn != null && conn.NonQueryResults.Count > 0)
            return conn.NonQueryResults.Dequeue();
        return 1;
    }

    public override object? ExecuteScalar()
    {
        var conn = FakeConnection;
        if (conn != null)
        {
            // Check for command-text-based result first
            if (!string.IsNullOrEmpty(CommandText) && conn.ScalarResultsByCommand.TryGetValue(CommandText, out var commandResult))
                return commandResult;
            
            // Handle version queries automatically based on emulated product
            if (!string.IsNullOrEmpty(CommandText))
            {
                var versionResult = GetVersionQueryResult(CommandText, conn.EmulatedProduct);
                if (versionResult != null)
                    return versionResult;
            }
            
            // Fall back to queued results
            if (conn.ScalarResults.Count > 0)
                return conn.ScalarResults.Dequeue();
        }
        return 42;
    }

    private string? GetVersionQueryResult(string commandText, SupportedDatabase emulatedProduct)
    {
        var normalizedCommand = commandText.Trim().ToUpperInvariant();

        return emulatedProduct switch
        {
            SupportedDatabase.SqlServer when normalizedCommand == "SELECT @@VERSION" 
                => "Microsoft SQL Server 2019 (RTM) - 15.0.2000.5",
            
            SupportedDatabase.PostgreSql when normalizedCommand == "SELECT VERSION()" 
                => "PostgreSQL 15.0 on x86_64-pc-linux-gnu",
            
            SupportedDatabase.MySql when normalizedCommand == "SELECT VERSION()" 
                => "8.0.33",
            
            SupportedDatabase.MariaDb when normalizedCommand == "SELECT VERSION()" 
                => "10.11.0-MariaDB",
            
            SupportedDatabase.Sqlite when normalizedCommand == "SELECT SQLITE_VERSION()" 
                => "3.42.0",
            
            SupportedDatabase.Oracle when normalizedCommand.Contains("SELECT * FROM V$VERSION") 
                => "Oracle Database 19c Enterprise Edition Release 19.0.0.0.0",
            
            SupportedDatabase.Firebird when normalizedCommand.Contains("RDB$GET_CONTEXT('SYSTEM', 'ENGINE_VERSION')") 
                => "4.0.0",
            
            SupportedDatabase.CockroachDb when normalizedCommand == "SELECT VERSION()" 
                => "CockroachDB CCL v23.1.0",
            
            _ => null
        };
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior _)
    {
        var conn = FakeConnection;
        if (conn != null && conn.ReaderResults.Count > 0)
            return new FakeDbDataReader(conn.ReaderResults.Dequeue());
        return new FakeDbDataReader();
    }

    // **Async overrides**  
    public override Task<int> ExecuteNonQueryAsync(CancellationToken ct)
    {
        return Task.FromResult(ExecuteNonQuery());
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken ct)
    {
        return Task.FromResult(ExecuteScalar());
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken ct)
    {
        return Task.FromResult<DbDataReader>(ExecuteDbDataReader(behavior));
    }

    public override Task PrepareAsync(CancellationToken ct)
    {
        Prepare();
        return Task.CompletedTask;
    }

    protected override DbParameter CreateDbParameter()
    {
        return new FakeDbParameter();
    }
}