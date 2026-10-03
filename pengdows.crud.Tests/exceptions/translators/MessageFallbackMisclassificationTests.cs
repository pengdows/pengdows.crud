using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

/// <summary>
/// REV-045: ClassifyException ends with message heuristics ("deadlock", "serializ"). Since the
/// translators route every provider error through it, an ordinary error naming a column or table
/// that contains those words (identified by the provider's own SQLSTATE or error number) was thrown
/// as a transient Deadlock/SerializationConflict exception, so retry loops spun on a permanent error.
/// 2.0.5 threw DatabaseOperationException. The heuristics only apply when the provider gave no code.
/// </summary>
public sealed class MessageFallbackMisclassificationTests
{
    public static TheoryData<SupportedDatabase, Func<DbException>> IdentifiedErrors() => new()
    {
        { SupportedDatabase.PostgreSql, () => new SqlStateDbException("42703", "42703: column \"serialized_payload\" does not exist") },
        { SupportedDatabase.PostgreSql, () => new SqlStateDbException("42P01", "42P01: relation \"deadlocks\" does not exist") },
        { SupportedDatabase.SqlServer, () => new NumberedDbException(207, "Invalid column name 'serialized'.") },
        { SupportedDatabase.MySql, () => new NumberedDbException(1054, "Unknown column 'deadlock_at' in 'field list'") },
        { SupportedDatabase.Oracle, () => new NumberedDbException(904, "ORA-00904: \"SERIALIZED\": invalid identifier") }
    };

    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database), NullLogger.Instance);

    [Theory]
    [MemberData(nameof(IdentifiedErrors))]
    public void ProviderIdentifiedError_IsNotGuessedFromItsMessage(SupportedDatabase database, Func<DbException> error)
    {
        var category = Dialect(database).ClassifyException(error());

        Assert.NotEqual(DbErrorCategory.Deadlock, category);
        Assert.NotEqual(DbErrorCategory.SerializationFailure, category);
    }

    [Theory]
    [MemberData(nameof(IdentifiedErrors))]
    public async Task ProviderIdentifiedError_IsNotThrownAsTransient(SupportedDatabase database, Func<DbException> error)
    {
        var factory = new fakeDbFactory(database);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=x;EmulatedProduct={database}",
            DbMode = DbMode.Standard
        }, factory);
        foreach (var connection in factory.Connections)
        {
            connection.SetCommandFailure("UPDATE t SET x = 1", error());
        }

        var next = new fakeDbConnection { EmulatedProduct = database };
        next.SetCommandFailure("UPDATE t SET x = 1", error());
        factory.Connections.Add(next);
        await using var sc = context.CreateSqlContainer("UPDATE t SET x = 1");

        var thrown = await Assert.ThrowsAnyAsync<DatabaseException>(async () => await sc.ExecuteNonQueryAsync());

        Assert.IsNotType<DeadlockException>(thrown);
        Assert.IsNotType<SerializationConflictException>(thrown);
        Assert.NotEqual(true, thrown.IsTransient);
    }

    // Without a provider code the message is all there is, so the fallback still applies.
    [Fact]
    public void UnidentifiedError_StillUsesTheMessage()
    {
        var category = Dialect(SupportedDatabase.Sqlite).ClassifyException(new SqliteMessageDbException("deadlock detected"));

        Assert.Equal(DbErrorCategory.Deadlock, category);
    }
}
