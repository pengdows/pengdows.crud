using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED live (Informix, HARN-005): a provider error raised while fetching a row (Informix
/// -244 "Could not do a physical-order read to fetch next row", a row locked by another
/// transaction) escaped ITrackedReader.ReadAsync as the raw IfxException, although every database
/// error is documented to surface as a typed DatabaseException with the provider exception as
/// InnerException. Errors raised while executing the command were already translated; errors
/// raised by Read/ReadAsync were not.
/// </summary>
public class TrackedReaderReadFailureTranslationTests
{
    private sealed class ProviderException : DbException
    {
        public ProviderException(string message, string sqlState) : base(message)
        {
            SqlState = sqlState;
        }

        public override string SqlState { get; }
    }

    private static (DatabaseContext Context, ProviderException Failure) CreateFailingReaderContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection();
        // 40001 (serialization_failure) is classified the same way by every PostgreSQL path.
        var failure = new ProviderException("could not serialize access", "40001");
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["id"] = 1 },
            new Dictionary<string, object?> { ["id"] = 2 }
        }, 1, failure);
        factory.Connections.Add(connection);

        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;EmulatedProduct=PostgreSql",
            DbMode = DbMode.SingleConnection
        }, factory);
        return (context, failure);
    }

    [Fact]
    public async Task ReadAsync_ProviderFailureMidStream_ThrowsTranslatedDatabaseException()
    {
        var (context, failure) = CreateFailingReaderContext();
        await using (context)
        {
            await using var sc = context.CreateSqlContainer("SELECT id FROM t");
            await using var reader = await sc.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            var ex = await Assert.ThrowsAsync<SerializationConflictException>(async () => await reader.ReadAsync());
            Assert.Same(failure, ex.InnerException);
        }
    }

    [Fact]
    public async Task Read_ProviderFailureMidStream_ThrowsTranslatedDatabaseException()
    {
        var (context, failure) = CreateFailingReaderContext();
        await using (context)
        {
            await using var sc = context.CreateSqlContainer("SELECT id FROM t");
            await using var reader = await sc.ExecuteReaderAsync();

            Assert.True(reader.Read());
            var ex = Assert.Throws<SerializationConflictException>(() => reader.Read());
            Assert.Same(failure, ex.InnerException);
        }
    }

    [Fact]
    public async Task ReadAsync_NonProviderFailure_PropagatesUnchanged()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection();
        var failure = new InvalidOperationException("application bug");
        connection.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } }, 0, failure);
        factory.Connections.Add(connection);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;EmulatedProduct=PostgreSql",
            DbMode = DbMode.SingleConnection
        }, factory);

        await using var sc = context.CreateSqlContainer("SELECT id FROM t");
        await using var reader = await sc.ExecuteReaderAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync());
        Assert.Same(failure, ex);
    }
}
