using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// fakeDb is conformant by default; <see cref="fakeDbDriverQuirks"/> reproduces the departures real drivers have, each
/// one as the ADO.NET conformance suite recorded it against that driver (pengdows.AdoNetApiTest.conformance results,
/// 2026-10-23), so code that must survive a driver's defect can be tested against it.
/// </summary>
public class FakeDbDriverQuirksTests
{
    private static fakeDbFactory Factory(fakeDbDriverQuirks quirks)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { EnableDataPersistence = true, DriverQuirks = quirks };
        factory.DataStore.StrictMode = true;
        return factory;
    }

    private static DbConnection Open(fakeDbFactory factory)
    {
        var connection = factory.CreateConnection();
        connection.ConnectionString = "Data Source=quirks;EmulatedProduct=Sqlite";
        connection.Open();
        return connection;
    }

    private static DbCommand Command(DbConnection connection, string sql = "SELECT 1;")
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    [Fact]
    public void The_defaults_are_conformant()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var quirks = factory.DriverQuirks;

        Assert.False(quirks.PrepareWithoutOpenConnection);
        Assert.False(quirks.CompletedTransactionKeepsConnection);
        Assert.Null(quirks.NonQueryResultForSelect);
        Assert.Same(quirks, ((fakeDbConnection)factory.CreateConnection()).DriverQuirks);
        Assert.Equal(CommandType.Text, factory.CreateCommand().CommandType);
        Assert.Equal(string.Empty, factory.CreateParameter().ParameterName);
    }

    [Fact]
    public void PrepareWithoutOpenConnection_lets_Prepare_return()
    {
        var factory = Factory(new fakeDbDriverQuirks { PrepareWithoutOpenConnection = true });
        using var closed = factory.CreateConnection();
        using var command = Command(closed);

        command.Prepare();
        using var detached = factory.CreateCommand();
        detached.Prepare();
    }

    [Fact]
    public async Task NullReferenceWithoutConnection_throws_NullReferenceException_from_every_execute()
    {
        var factory = Factory(new fakeDbDriverQuirks { NullReferenceWithoutConnection = true });
        using var command = factory.CreateCommand();
        command.CommandText = "SELECT 1;";

        Assert.Throws<NullReferenceException>(() => command.ExecuteNonQuery());
        Assert.Throws<NullReferenceException>(() => command.ExecuteScalar());
        Assert.Throws<NullReferenceException>(() => command.ExecuteReader());
        await Assert.ThrowsAsync<NullReferenceException>(() => command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task AsyncNullReferenceWithoutConnection_affects_only_the_async_reader_and_scalar()
    {
        var factory = Factory(new fakeDbDriverQuirks { AsyncNullReferenceWithoutConnection = true });
        using var command = factory.CreateCommand();
        command.CommandText = "SELECT 1;";

        await Assert.ThrowsAsync<NullReferenceException>(() => command.ExecuteReaderAsync());
        await Assert.ThrowsAsync<NullReferenceException>(() => command.ExecuteScalarAsync());
        Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ScalarNullReferenceOnClosedConnection_throws_NullReferenceException_from_the_scalar_only()
    {
        var factory = Factory(new fakeDbDriverQuirks { ScalarNullReferenceOnClosedConnection = true });
        using var closed = factory.CreateConnection();
        using var command = Command(closed);

        Assert.Throws<NullReferenceException>(() => command.ExecuteScalar());
        await Assert.ThrowsAsync<NullReferenceException>(() => command.ExecuteScalarAsync());
        Assert.Throws<InvalidOperationException>(() => command.ExecuteReader());
    }

    [Fact]
    public void An_open_reader_can_be_left_unguarded_or_refused_with_the_drivers_exception()
    {
        var lax = Factory(new fakeDbDriverQuirks
        {
            CommandTextChangeableWithOpenReader = true,
            ConnectionChangeableWithOpenReader = true,
            ReexecuteWithOpenReaderAllowed = true
        });
        using (var connection = Open(lax))
        using (var command = Command(connection))
        using (command.ExecuteReader())
        {
            command.CommandText = "SELECT 2;";
            using (command.ExecuteReader())
            {
            }

            command.Connection = connection;
        }

        var providerError = new InvalidTimeZoneException("already in progress");
        var refusing = Factory(new fakeDbDriverQuirks { ReexecuteWithOpenReaderException = () => providerError });
        using (var connection = Open(refusing))
        using (var command = Command(connection))
        using (command.ExecuteReader())
        {
            Assert.Same(providerError, Assert.Throws<InvalidTimeZoneException>(() => command.ExecuteReader()));
        }
    }

    [Fact]
    public void ForeignTransactionAccepted_runs_under_another_connections_transaction()
    {
        var factory = Factory(new fakeDbDriverQuirks { ForeignTransactionAccepted = true });
        using var connection = Open(factory);
        using var other = Open(factory);
        using var otherTransaction = other.BeginTransaction();
        using var command = Command(connection);
        command.Transaction = otherTransaction;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
    }

    [Fact]
    public void CloseConnectionIgnored_leaves_the_connection_open()
    {
        var factory = Factory(new fakeDbDriverQuirks { CloseConnectionIgnored = true });
        using var connection = Open(factory);
        using var command = Command(connection);

        using (command.ExecuteReader(CommandBehavior.CloseConnection))
        {
        }

        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Fact]
    public void NonQueryResultForSelect_is_what_ExecuteNonQuery_of_a_SELECT_returns()
    {
        var factory = Factory(new fakeDbDriverQuirks { NonQueryResultForSelect = 0 });
        using var connection = Open(factory);
        using var command = Command(connection);

        Assert.Equal(0, command.ExecuteNonQuery());
    }

    [Fact]
    public void CommandTypeDefaultsToZero_and_ParameterDirectionDefaultsToZero_and_ParameterNameDefaultsToNull()
    {
        var factory = Factory(new fakeDbDriverQuirks
        {
            CommandTypeDefaultsToZero = true,
            ParameterDirectionDefaultsToZero = true,
            ParameterNameDefaultsToNull = true
        });
        using var connection = Open(factory);
        using var command = connection.CreateCommand();

        Assert.Equal((CommandType)0, factory.CreateCommand().CommandType);
        Assert.Equal((CommandType)0, command.CommandType);
        Assert.Equal((ParameterDirection)0, factory.CreateParameter().Direction);
        Assert.Null(factory.CreateParameter().ParameterName);
        Assert.Null(command.CreateParameter().ParameterName);
    }

    [Fact]
    public void A_parameter_left_unnamed_by_ParameterNameDefaultsToNull_binds_nothing_and_does_not_throw()
    {
        var factory = Factory(new fakeDbDriverQuirks { ParameterNameDefaultsToNull = true });
        using var connection = Open(factory);
        using var command = Command(connection, "SELECT @p;");
        var parameter = command.CreateParameter();
        parameter.Value = 1;
        command.Parameters.Add(parameter);

        Assert.Equal(-1, command.ExecuteNonQuery());
    }

    [Fact]
    public async Task CanceledExecuteReaderAsyncRuns_ignores_a_canceled_token()
    {
        var factory = Factory(new fakeDbDriverQuirks { CanceledExecuteReaderAsyncRuns = true });
        using var connection = Open(factory);
        using var command = Command(connection);

        using var reader = await command.ExecuteReaderAsync(new CancellationToken(true));
        Assert.True(reader.Read());
    }

    [Fact]
    public void UnmappableParameterValueException_replaces_NotSupportedException()
    {
        var factory = Factory(new fakeDbDriverQuirks
        {
            UnmappableParameterValueException = value => new ArgumentException($"No mapping for {value.GetType().Name}")
        });
        using var connection = Open(factory);
        using var command = Command(connection, "SELECT @p;");
        var parameter = command.CreateParameter();
        parameter.ParameterName = "p";
        parameter.Value = new object();
        command.Parameters.Add(parameter);

        Assert.Equal("No mapping for Object", Assert.Throws<ArgumentException>(() => command.ExecuteScalar()).Message);
    }

    [Fact]
    public void The_connection_string_of_an_open_connection_can_be_settable_or_refused_with_the_drivers_exception()
    {
        using (var settable = Open(Factory(new fakeDbDriverQuirks { ConnectionStringSettableWhileOpen = true })))
        {
            settable.ConnectionString = "Data Source=changed;EmulatedProduct=Sqlite";
            Assert.Contains("changed", settable.ConnectionString);
        }

        var providerError = new InvalidTimeZoneException("state=Open");
        using var refusing = Open(Factory(new fakeDbDriverQuirks { ConnectionStringWhileOpenException = () => providerError }));
        Assert.Same(providerError,
            Assert.Throws<InvalidTimeZoneException>(() => refusing.ConnectionString = "Data Source=x;EmulatedProduct=Sqlite"));
    }

    [Fact]
    public void A_completed_or_disposed_transaction_can_keep_its_connection()
    {
        using var connection = Open(Factory(new fakeDbDriverQuirks
        {
            CompletedTransactionKeepsConnection = true,
            DisposedTransactionKeepsConnection = true
        }));

        var committed = connection.BeginTransaction();
        committed.Commit();
        Assert.Same(connection, committed.Connection);

        var disposed = connection.BeginTransaction();
        disposed.Dispose();
        Assert.Same(connection, disposed.Connection);
    }

    [Fact]
    public void DisposedTransactionThrowsObjectDisposed_on_reading_its_connection()
    {
        using var connection = Open(Factory(new fakeDbDriverQuirks { DisposedTransactionThrowsObjectDisposed = true }));
        var transaction = connection.BeginTransaction();
        transaction.Dispose();

        Assert.Throws<ObjectDisposedException>(() => transaction.Connection);
    }

    [Fact]
    public void TransactionCompletesAgain_lets_a_second_Commit_or_Rollback_return()
    {
        using var connection = Open(Factory(new fakeDbDriverQuirks { TransactionCompletesAgain = true }));
        using var transaction = connection.BeginTransaction();

        transaction.Commit();
        transaction.Commit();
        transaction.Rollback();
    }

    [Fact]
    public async Task SavepointReleaseIsNoOp_lets_only_Release_return()
    {
        using var connection = Open(Factory(new fakeDbDriverQuirks { SavepointReleaseIsNoOp = true }));
        using var transaction = connection.BeginTransaction();

        Assert.False(transaction.SupportsSavepoints);
        Assert.Throws<NotSupportedException>(() => transaction.Save("sp"));
        Assert.Throws<NotSupportedException>(() => transaction.Rollback("sp"));
        transaction.Release("sp");
        await transaction.ReleaseAsync("sp");
    }

    [Fact]
    public async Task SavepointsWorkButUnreported_runs_every_savepoint_method_although_SupportsSavepoints_is_false()
    {
        using var connection = Open(Factory(new fakeDbDriverQuirks { SavepointsWorkButUnreported = true }));
        using var transaction = (fakeDbTransaction)connection.BeginTransaction();

        Assert.False(transaction.SupportsSavepoints);
        transaction.Save("sp");
        transaction.Rollback("sp");
        transaction.Release("sp");
        await transaction.SaveAsync("sp2");

        Assert.Equal(new[] { "Save(sp)", "Rollback(sp)", "Release(sp)", "Save(sp2)" }, transaction.Operations);
    }

    [Fact]
    public void A_connection_can_have_its_own_quirks()
    {
        var factory = Factory(new fakeDbDriverQuirks());
        using var connection = (fakeDbConnection)factory.CreateConnection();
        connection.DriverQuirks = new fakeDbDriverQuirks { PrepareWithoutOpenConnection = true };
        using var command = Command(connection);

        command.Prepare();
        Assert.False(factory.DriverQuirks.PrepareWithoutOpenConnection);
    }

    public static TheoryData<string, Func<fakeDbDriverQuirks>> Presets => new()
    {
        { "SqlClient", fakeDbDriverQuirks.SqlClient },
        { "Npgsql", fakeDbDriverQuirks.Npgsql },
        { "MySqlConnector", fakeDbDriverQuirks.MySqlConnector },
        { "MySqlData", fakeDbDriverQuirks.MySqlData },
        { "DuckDbNet", fakeDbDriverQuirks.DuckDbNet },
        { "OracleManagedDataAccess", fakeDbDriverQuirks.OracleManagedDataAccess },
        { "SystemDataSqlite", fakeDbDriverQuirks.SystemDataSqlite },
        { "AseClient", fakeDbDriverQuirks.AseClient },
        { "Db2", fakeDbDriverQuirks.Db2 }
    };

    [Theory]
    [MemberData(nameof(Presets))]
    public void Every_preset_departs_from_the_contract_somewhere_and_returns_a_new_instance(string name,
        Func<fakeDbDriverQuirks> preset)
    {
        var quirks = preset();

        Assert.NotSame(quirks, preset());
        Assert.False(new fakeDbDriverQuirks().HasAnyQuirk, name);
        Assert.True(quirks.HasAnyQuirk, name);
    }

    [Fact]
    public void The_presets_reproduce_their_drivers()
    {
        var sqlClient = fakeDbDriverQuirks.SqlClient();
        Assert.True(sqlClient.PrepareWithoutOpenConnection && sqlClient.SavepointsWorkButUnreported &&
                    sqlClient.AsyncNullReferenceWithoutConnection);
        Assert.StartsWith("No mapping exists from object type System.Uri",
            sqlClient.UnmappableParameterValueException!(new Uri("http://x"))!.Message);
        Assert.Null(sqlClient.UnmappableParameterValueException(new object()));

        var npgsql = fakeDbDriverQuirks.Npgsql();
        Assert.True(npgsql.CompletedTransactionKeepsConnection && npgsql.DisposedTransactionThrowsObjectDisposed &&
                    npgsql.ForeignTransactionAccepted);
        Assert.IsAssignableFrom<DbException>(npgsql.ReexecuteWithOpenReaderException!());

        var mySqlData = fakeDbDriverQuirks.MySqlData();
        Assert.True(mySqlData.ParameterNameDefaultsToNull && mySqlData.ScalarNullReferenceOnClosedConnection &&
                    mySqlData.SavepointReleaseIsNoOp);
        Assert.IsAssignableFrom<DbException>(mySqlData.ConnectionStringWhileOpenException!());

        var duck = fakeDbDriverQuirks.DuckDbNet();
        Assert.Equal(0, duck.NonQueryResultForSelect);
        Assert.True(duck.CommandTypeDefaultsToZero && duck.ParameterDirectionDefaultsToZero &&
                    duck.ConnectionStringSettableWhileOpen);

        var ase = fakeDbDriverQuirks.AseClient();
        Assert.Equal(1, ase.NonQueryResultForSelect);
        Assert.True(ase.NullReferenceWithoutConnection && ase.CloseConnectionIgnored && ase.TransactionCompletesAgain);

        Assert.True(fakeDbDriverQuirks.MySqlConnector().SavepointsWorkButUnreported);
        Assert.True(fakeDbDriverQuirks.OracleManagedDataAccess().ReexecuteWithOpenReaderAllowed);
        Assert.True(fakeDbDriverQuirks.SystemDataSqlite().ParameterNameDefaultsToNull);
        Assert.True(fakeDbDriverQuirks.Db2().SavepointReleaseIsNoOp);
    }
}
