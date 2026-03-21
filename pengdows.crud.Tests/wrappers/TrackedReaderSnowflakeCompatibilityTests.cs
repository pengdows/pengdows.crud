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
    public class TrackedReaderSnowflakeCompatibilityTests
    {
        [Fact]
        public async Task DisposeAsync_WhenSnowflakeCommandThrowsOnConnectionNull_CompletesWithoutThrowing()
        {
            var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["Value"] = 1 } });
            var command = new Snowflake.Data.Client.ThrowingSnowflakeDbCommand();
            await using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(),
                Mock.Of<IAsyncDisposable>(), false, command: command);

            Assert.True(await tracked.ReadAsync());
            Assert.False(await tracked.ReadAsync());
        }

        [Fact]
        public void Dispose_WhenSnowflakeCommandThrowsOnConnectionNull_CompletesWithoutThrowing()
        {
            // No `using` on reader — TrackedReader takes ownership and disposes it.
            var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["Value"] = 1 } });
            var command = new Snowflake.Data.Client.ThrowingSnowflakeDbCommand();
            using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(),
                Mock.Of<IAsyncDisposable>(), false, command: command);

            Assert.True(tracked.Read());
            Assert.False(tracked.Read());
        }
    }
}

// Test stub only — simulates the real Snowflake.Data.Client.SnowflakeDbCommand behavior
// where setting DbConnection to null after execution throws (VendorCode 270009).
// Placed in the vendor namespace so test intent is self-documenting.
namespace Snowflake.Data.Client
{
    public sealed class ThrowingSnowflakeDbCommand : DbCommand
    {
        private readonly DbParameterCollection _parameters = new FakeParameterCollection();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection
        {
            get => null;
            set
            {
                if (value == null)
                {
                    throw new InvalidOperationException("simulated Snowflake.Data set_DbConnection failure");
                }
            }
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }

        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object? ExecuteScalar() => throw new NotSupportedException();
        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new fakeDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
    }
}
