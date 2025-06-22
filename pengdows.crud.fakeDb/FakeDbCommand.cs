#region

using System.Data;
using System.Data.Common;

#endregion

namespace pengdows.crud.FakeDb;

public sealed class FakeDbCommand : DbCommand
{
    public FakeDbCommand(DbConnection connection)
    {
        Connection = connection;
    }

    public FakeDbCommand()
    {
    }

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

    public override int ExecuteNonQuery()
    {
        return 1;
    }

    public override object ExecuteScalar()
    {
        return 42;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior _)
    {
        return new FakeDbDataReader();
    }

    // **Async overrides**  
    public override Task<int> ExecuteNonQueryAsync(CancellationToken ct)
    {
        return Task.FromResult(ExecuteNonQuery());
    }

    public override Task<object> ExecuteScalarAsync(CancellationToken ct)
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