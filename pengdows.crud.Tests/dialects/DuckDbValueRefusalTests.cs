using System;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.configuration;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// DuckDB.NET 2.0 refuses a TimeSpan outside a time of day at bind time, with a plain InvalidCastException
/// ("Cannot cast from TimeSpan to TimeOnly for parameter ... the value is out of range"). That is a refusal of the
/// value, like the DuckDB insert-time conversion error 1.5.6.1 raised for the same value. pengdows must surface it as a
/// typed DatabaseException with the provider error preserved, not let the raw framework exception escape.
/// </summary>
public sealed class DuckDbValueRefusalTests
{
    private const string Refusal =
        "Cannot cast from TimeSpan to TimeOnly for parameter \"i1\" (DbType.Time): the value is out of range";

    [Fact]
    public async Task OutOfRangeTimeParameter_SurfacesAsTypedDatabaseException()
    {
        const string sql = "INSERT INTO t (id, span) VALUES (1, 'x')";
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        factory.SetCommandFailure(sql, new InvalidCastException(Refusal));
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard
        }, factory);

        await using var sc = context.CreateSqlContainer(sql);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => sc.ExecuteNonQueryAsync().AsTask());

        var typed = Assert.IsAssignableFrom<DatabaseException>(ex);
        Assert.Contains("out of range", typed.InnerException?.Message ?? typed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutOfRangeTimeParameter_IsTheSameTypeAsTheInsertTimeRefusal()
    {
        const string sql = "INSERT INTO t (id, span) VALUES (2, 'y')";
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        factory.SetCommandFailure(sql, new InvalidCastException(Refusal));
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard
        }, factory);

        await using var sc = context.CreateSqlContainer(sql);
        await Assert.ThrowsAsync<DatabaseOperationException>(() => sc.ExecuteNonQueryAsync().AsTask());
    }

    [Fact]
    public async Task OtherInvalidCastFailures_StillEscapeUnchanged()
    {
        const string sql = "SELECT 1";
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        factory.SetCommandFailure(sql, new InvalidCastException("Unable to cast object of type 'X' to type 'Y'."));
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard
        }, factory);

        await using var sc = context.CreateSqlContainer(sql);
        await Assert.ThrowsAsync<InvalidCastException>(() => sc.ExecuteNonQueryAsync().AsTask());
    }
}
