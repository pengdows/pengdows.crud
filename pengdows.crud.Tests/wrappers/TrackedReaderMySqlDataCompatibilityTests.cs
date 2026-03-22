using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.wrappers
{
    public class TrackedReaderMySqlDataCompatibilityTests
    {
        [Fact]
        public async Task ReadAsync_WhenMySqlDataReaderDisposeAsyncHitsKnownNullReference_ReturnsFalse()
        {
            var reader = new MySql.Data.MySqlClient.ThrowingMySqlDataReader();
            await using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(),
                Mock.Of<IAsyncDisposable>(), false);

            Assert.True(await tracked.ReadAsync());
            Assert.False(await tracked.ReadAsync());
        }

        [Fact]
        public async Task ReadAsync_WhenMySqlDataCommandDisposeHitsKnownNullReference_ReturnsFalse()
        {
            var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["Value"] = 1 } });
            var command = new MySql.Data.MySqlClient.ThrowingMySqlDataCommand();
            await using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(),
                Mock.Of<IAsyncDisposable>(), false, command: command);

            Assert.True(await tracked.ReadAsync());
            Assert.False(await tracked.ReadAsync());
        }

        [Fact]
        public void Read_WhenMySqlDataCommandDisposeHitsKnownNullReference_ReturnsFalse()
        {
            using var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["Value"] = 1 } });
            var command = new MySql.Data.MySqlClient.ThrowingMySqlDataCommand();
            using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(),
                Mock.Of<IAsyncDisposable>(), false, command: command);

            Assert.True(tracked.Read());
            Assert.False(tracked.Read());
        }
    }
}

namespace MySql.Data.MySqlClient
{
    public sealed class ThrowingMySqlDataReader : fakeDbDataReader
    {
        public ThrowingMySqlDataReader()
            : base(new[] { new Dictionary<string, object> { ["Value"] = 1 } })
        {
        }

        public override ValueTask DisposeAsync()
        {
            throw new NullReferenceException("simulated MySql.Data dispose failure");
        }
    }

    public sealed class ThrowingMySqlDataCommand : DbCommand
    {
        private readonly DbParameterCollection _parameters = new FakeParameterCollection();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            throw new NotSupportedException();
        }

        public override object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return new fakeDbParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                throw new NullReferenceException("simulated MySql.Data command dispose failure");
            }

            base.Dispose(disposing);
        }
    }
}
