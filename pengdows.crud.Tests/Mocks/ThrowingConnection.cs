using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;

namespace pengdows.crud.Tests.Mocks;

public sealed class ThrowingConnection : fakeDbConnection
{
    protected override DbCommand CreateDbCommand() => new ThrowingCommand(this);

    private sealed class ThrowingCommand : DbCommand
    {
        private readonly DbConnection _connection;

        public ThrowingCommand(DbConnection connection) => _connection = connection;

        public override string? CommandText { get; set; }
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection DbConnection { get => _connection; set { } }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new InvalidOperationException();
        public override object? ExecuteScalar() => throw new InvalidOperationException();
        public override void Prepare() { }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new InvalidOperationException();
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => throw new InvalidOperationException();
        protected override DbParameter CreateDbParameter() => new fakeDbParameter();
    }
}
