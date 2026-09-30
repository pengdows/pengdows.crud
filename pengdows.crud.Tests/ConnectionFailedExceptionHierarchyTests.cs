using System;
using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DEC-001 (maintainer decision 2026-09-30: reparent): ConnectionFailedException, thrown when a
/// DatabaseContext's first connect or read-only validation fails, derived from Exception, so
/// catch (ConnectionException) / catch (DatabaseException) missed a failure at construction that the
/// same code catches at runtime. It is now a ConnectionException; its constructors, Phase and Role
/// are unchanged, and it carries the underlying failure's SQLSTATE, error code and transience.
/// </summary>
public sealed class ConnectionFailedExceptionHierarchyTests
{
    [Fact]
    public void IsAConnectionExceptionAndADatabaseException()
    {
        var ex = new ConnectionFailedException("Failed to open database connection.");

        Assert.IsAssignableFrom<ConnectionException>(ex);
        Assert.IsAssignableFrom<DatabaseException>(ex);
        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
        Assert.Null(ex.SqlState);
        Assert.Null(ex.IsTransient);
    }

    [Fact]
    public void CarriesATranslatedInnerFailuresDetails()
    {
        var inner = new TooManyConnectionsException("too many clients", SupportedDatabase.PostgreSql,
            sqlState: "53300", errorCode: 53300);

        var ex = new ConnectionFailedException("Failed to open database connection.", inner);

        Assert.Same(inner, ex.InnerException);
        Assert.Equal(SupportedDatabase.PostgreSql, ex.Database);
        Assert.Equal("53300", ex.SqlState);
        Assert.Equal(53300, ex.ErrorCode);
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public void CarriesAProviderExceptionsSqlStateAndTransience()
    {
        var ex = new ConnectionFailedException("Failed to open database connection.",
            new ProviderException("08001", transient: true));

        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
        Assert.Equal("08001", ex.SqlState);
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public void ContextConstructionFailure_IsCaughtAsConnectionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql, ConnectionFailureMode.FailOnOpen,
            new ProviderException("08001", transient: true));

        ConnectionException? caught = null;
        try
        {
            using var _ = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "Host=unreachable;Database=app;EmulatedProduct=PostgreSql",
                DbMode = DbMode.Standard
            }, factory);
        }
        catch (ConnectionException ex)
        {
            caught = ex;
        }

        var failed = Assert.IsType<ConnectionFailedException>(caught);
        Assert.Equal("InitConnect", failed.Phase);
        Assert.Equal("08001", failed.SqlState);
    }

    private sealed class ProviderException : DbException
    {
        private readonly bool _transient;

        public ProviderException(string sqlState, bool transient) : base("connection refused")
        {
            SqlState = sqlState;
            _transient = transient;
        }

        public override string SqlState { get; }
        public override bool IsTransient => _transient;
    }
}
