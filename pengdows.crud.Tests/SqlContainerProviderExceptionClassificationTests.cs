#region

using System;
using System.Reflection;
using System.Threading.Tasks;
using AdoNetCore.AseClient;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// SqlContainer.LooksLikeProviderException decides whether an exception surfacing from command
/// execution gets routed through IDbExceptionTranslator.Translate (rethrown as a typed
/// DatabaseException) or propagated as-is. It has to recognize non-DbException provider
/// exceptions (AdoNetCore.AseClient's AseException for Sybase is the one documented case — see
/// SybaseExceptionTranslator.cs), but must not misclassify an unrelated application exception
/// that merely happens to expose a similarly-named property (Number/SqlState/NativeError) as a
/// database error — doing so would silently discard the real exception type and mislead any
/// caller doing catch (DatabaseException)-based retry/handling.
/// </summary>
public class SqlContainerProviderExceptionClassificationTests
{
    /// <summary>
    /// A plausible unrelated application exception: something like a request-validation error
    /// that happens to expose a "SqlState" property for its own, entirely unrelated reasons. This
    /// is not a real provider exception and must never be reclassified as one.
    /// </summary>
    private sealed class CoincidentalApplicationException : Exception
    {
        public CoincidentalApplicationException(string message) : base(message)
        {
        }

        public string SqlState => "VALID"; // unrelated to any real SQLSTATE
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_ApplicationExceptionWithCoincidentalSqlStateProperty_PropagatesAsIs()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        using var sc = ctx.CreateSqlContainer("UPDATE \"t\" SET \"x\" = 1");

        var conn = new fakeDbConnection();
        conn.SetNonQueryExecuteException(
            new CoincidentalApplicationException("request validation failed"));
        factory.Connections.Add(conn);

        var ex = await Record.ExceptionAsync(() => sc.ExecuteNonQueryAsync().AsTask());

        // The real defect this guards: today, LooksLikeProviderException's bare property-name
        // duck-typing sees the "SqlState" property and wrongly treats this as a provider error,
        // rethrowing it wrapped as a DatabaseException instead of propagating the original type.
        Assert.IsType<CoincidentalApplicationException>(ex);
    }

    // AseError's MessageNumber/Message setters are non-public (real driver code populates them
    // internally when parsing a TDS error token); mirrors SybaseTranslatorTests.cs's helper.
    private static AseException Ase(int messageNumber, string message)
    {
        var error = new AseError();
        SetProperty(error, nameof(AseError.MessageNumber), messageNumber);
        SetProperty(error, nameof(AseError.Message), message);
        return new AseException(new[] { error });
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        var setter = property!.GetSetMethod(nonPublic: true);
        setter!.Invoke(target, new[] { value });
    }

    /// <summary>
    /// The one legitimate non-DbException provider case LooksLikeProviderException exists for
    /// (AdoNetCore.AseClient's AseException, used for Sybase ASE) must still be recognized and
    /// routed through translation end-to-end via SqlContainer, not just at the translator's own
    /// unit-test level (SybaseTranslatorTests.cs calls the translator directly and never
    /// exercises this classification gate at all).
    /// </summary>
    [Fact]
    public async Task ExecuteNonQueryAsync_RealAseException_IsTranslatedNotPropagatedRaw()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SybaseASE);
        await using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SybaseASE", factory);
        using var sc = ctx.CreateSqlContainer("UPDATE \"t\" SET \"x\" = 1");

        var conn = new fakeDbConnection();
        conn.SetNonQueryExecuteException(Ase(2601, "Attempt to insert duplicate key row"));
        factory.Connections.Add(conn);

        var ex = await Record.ExceptionAsync(() => sc.ExecuteNonQueryAsync().AsTask());

        Assert.IsType<UniqueConstraintViolationException>(ex);
    }
}
