using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// The documented System.Data.Common behavior fakeDb owes its callers, as the ADO.NET conformance suite
/// (pengdows.crud.fakeDb.Conformance.Tests) found it missing: the defaults, connection-state checks, open-reader
/// guards, transaction completion and schema collections a real provider has.
/// </summary>
public class FakeDbAdoNetContractTests
{
    private static fakeDbFactory PersistentFactory()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { EnableDataPersistence = true };
        factory.DataStore.StrictMode = true;
        return factory;
    }

    private static DbConnection OpenConnection(fakeDbFactory factory)
    {
        var connection = factory.CreateConnection();
        connection.ConnectionString = "Data Source=contract;EmulatedProduct=Sqlite";
        connection.Open();
        return connection;
    }

    [Fact]
    public void New_parameter_is_an_Input_with_a_null_Value()
    {
        var parameter = new fakeDbFactory(SupportedDatabase.Sqlite).CreateParameter();

        Assert.Equal(ParameterDirection.Input, parameter.Direction);
        Assert.Null(parameter.Value);
    }

    [Fact]
    public void New_command_is_Text()
    {
        Assert.Equal(CommandType.Text, new fakeDbCommand().CommandType);
    }

    [Fact]
    public async Task Execute_and_Prepare_without_a_connection_throw_InvalidOperationException()
    {
        using var command = new fakeDbCommand { CommandText = "SELECT 1" };

        Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery());
        Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
        Assert.Throws<InvalidOperationException>(() => command.ExecuteReader());
        Assert.Throws<InvalidOperationException>(() => command.Prepare());
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteNonQueryAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteScalarAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteReaderAsync());
    }

    [Fact]
    public void Execute_and_Prepare_on_a_closed_connection_throw_InvalidOperationException()
    {
        using var connection = new fakeDbFactory(SupportedDatabase.Sqlite).CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery());
        Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
        Assert.Throws<InvalidOperationException>(() => command.ExecuteReader());
        Assert.Throws<InvalidOperationException>(() => command.Prepare());
    }

    [Fact]
    public void An_open_reader_locks_its_command()
    {
        using var connection = OpenConnection(PersistentFactory());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        using var reader = command.ExecuteReader();

        Assert.Throws<InvalidOperationException>(() => command.CommandText = "SELECT 2");
        Assert.Throws<InvalidOperationException>(() => command.Connection = null);
        Assert.Throws<InvalidOperationException>(() => command.ExecuteReader());

        reader.Close();
        command.CommandText = "SELECT 2";
        Assert.Equal("SELECT 2", command.CommandText);
    }

    [Fact]
    public void A_transaction_from_another_connection_is_refused()
    {
        var factory = PersistentFactory();
        using var connection = OpenConnection(factory);
        using var other = OpenConnection(factory);
        using var otherTransaction = other.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.Transaction = otherTransaction;

        Assert.Throws<InvalidOperationException>(() => command.ExecuteReader());
    }

    [Fact]
    public void CloseConnection_closes_the_connection_with_the_reader()
    {
        using var connection = OpenConnection(PersistentFactory());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 0";

        using (command.ExecuteReader(CommandBehavior.CloseConnection))
        {
        }

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void A_canceled_token_returns_a_canceled_task()
    {
        using var connection = OpenConnection(PersistentFactory());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var canceled = new CancellationToken(true);

        Assert.True(command.ExecuteReaderAsync(canceled).IsCanceled);
        Assert.True(command.ExecuteScalarAsync(canceled).IsCanceled);
        Assert.True(command.ExecuteNonQueryAsync(canceled).IsCanceled);
    }

    [Fact]
    public void ConnectionString_cannot_change_while_open()
    {
        using var connection = OpenConnection(PersistentFactory());

        Assert.Throws<InvalidOperationException>(() => connection.ConnectionString = "Data Source=other;EmulatedProduct=Sqlite");
    }

    [Fact]
    public void A_connection_reports_the_factory_that_created_it()
    {
        var factory = PersistentFactory();
        using var connection = factory.CreateConnection();

        Assert.Same(factory, DbProviderFactories.GetFactory(connection));
    }

    [Fact]
    public void GetSchema_lists_MetaDataCollections_and_refuses_an_unknown_collection()
    {
        using var connection = OpenConnection(PersistentFactory());

        foreach (var table in new[] { connection.GetSchema(), connection.GetSchema(DbMetaDataCollectionNames.MetaDataCollections) })
        {
            Assert.Equal(DbMetaDataCollectionNames.MetaDataCollections, table.TableName);
            Assert.Equal(typeof(string), table.Columns[DbMetaDataColumnNames.CollectionName]!.DataType);
            Assert.Equal(typeof(int), table.Columns[DbMetaDataColumnNames.NumberOfRestrictions]!.DataType);
            Assert.Equal(typeof(int), table.Columns[DbMetaDataColumnNames.NumberOfIdentifierParts]!.DataType);
            Assert.Contains(table.Rows.Cast<DataRow>(),
                row => (string)row[DbMetaDataColumnNames.CollectionName] == DbMetaDataCollectionNames.DataSourceInformation);
        }

        Assert.Equal("SQLite",
            connection.GetSchema(DbMetaDataCollectionNames.DataSourceInformation).Rows[0]["DataSourceProductName"]);
        Assert.Throws<ArgumentException>(() => connection.GetSchema("NoSuchCollection"));
    }

    [Fact]
    public void A_completed_transaction_drops_its_connection_and_refuses_another_completion()
    {
        using var connection = OpenConnection(PersistentFactory());

        var committed = connection.BeginTransaction();
        committed.Commit();
        Assert.Null(committed.Connection);
        Assert.Throws<InvalidOperationException>(() => committed.Commit());
        Assert.Throws<InvalidOperationException>(() => committed.Rollback());

        var rolledBack = connection.BeginTransaction();
        rolledBack.Rollback();
        Assert.Null(rolledBack.Connection);
        Assert.Throws<InvalidOperationException>(() => rolledBack.Rollback());

        var disposed = connection.BeginTransaction();
        disposed.Dispose();
        Assert.Null(((IDbTransaction)disposed).Connection);
    }

    [Fact]
    public async Task Savepoint_methods_throw_NotSupportedException_when_savepoints_are_unsupported()
    {
        using var connection = OpenConnection(PersistentFactory());
        using var transaction = connection.BeginTransaction();

        Assert.False(transaction.SupportsSavepoints);
        Assert.Throws<NotSupportedException>(() => transaction.Save("sp"));
        Assert.Throws<NotSupportedException>(() => transaction.Rollback("sp"));
        Assert.Throws<NotSupportedException>(() => transaction.Release("sp"));
        await Assert.ThrowsAsync<NotSupportedException>(() => transaction.ReleaseAsync("sp"));
    }

    [Theory]
    [InlineData("SELECT 7;", 7)]
    [InlineData("SELECT 7; SELECT 8;", 7)]
    [InlineData("-- leading\nSELECT 7;", 7)]
    [InlineData("SELECT 7; -- trailing", 7)]
    [InlineData("SELECT /* inline */ 7;", 7)]
    [InlineData("SELECT 8 AS v UNION SELECT 7 AS v ORDER BY 1;", 7)]
    public void A_constant_SELECT_returns_its_value(string sql, int expected)
    {
        using var connection = OpenConnection(PersistentFactory());
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        Assert.Equal(expected, Convert.ToInt32(command.ExecuteScalar()));
    }

    [Fact]
    public void A_constant_SELECT_returns_text_reals_nulls_and_parameters()
    {
        using var connection = OpenConnection(PersistentFactory());
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT 'it''s';";
        Assert.Equal("it's", command.ExecuteScalar());
        command.CommandText = "SELECT 3.14;";
        Assert.Equal(3.14m, Convert.ToDecimal(command.ExecuteScalar()));
        command.CommandText = "SELECT NULL;";
        Assert.Equal(DBNull.Value, command.ExecuteScalar());

        command.CommandText = "SELECT @p;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "p";
        parameter.Value = "bound";
        command.Parameters.Add(parameter);
        Assert.Equal("bound", command.ExecuteScalar());
        parameter.Value = DBNull.Value;
        Assert.Equal(DBNull.Value, command.ExecuteScalar());
    }

    [Fact]
    public void A_parameter_value_with_no_database_type_is_refused()
    {
        using var connection = OpenConnection(PersistentFactory());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT @p;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "p";
        parameter.Value = new object();
        command.Parameters.Add(parameter);

        Assert.Throws<NotSupportedException>(() => command.ExecuteScalar());
    }

    [Fact]
    public void A_constant_SELECT_batch_reads_one_result_set_per_statement()
    {
        using var connection = OpenConnection(PersistentFactory());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1; SELECT 'two';";

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.False(reader.Read());
        Assert.True(reader.NextResult());
        Assert.True(reader.Read());
        Assert.Equal("two", reader.GetString(0));
        Assert.False(reader.NextResult());
    }

    [Fact]
    public void ExecuteNonQuery_of_a_SELECT_returns_negative_one()
    {
        using var connection = OpenConnection(PersistentFactory());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";

        Assert.Equal(-1, command.ExecuteNonQuery());
    }

    [Fact]
    public async Task StrictMode_raises_a_provider_syntax_error_for_text_that_is_not_SQL()
    {
        using var connection = OpenConnection(PersistentFactory());
        using var command = connection.CreateCommand();
        command.CommandText = "INVALID;";

        Assert.ThrowsAny<DbException>(() => command.ExecuteReader());
        Assert.ThrowsAny<DbException>(() => command.ExecuteScalar());
        Assert.ThrowsAny<DbException>(() => command.ExecuteNonQuery());
        await Assert.ThrowsAnyAsync<DbException>(() => command.ExecuteScalarAsync());
    }
}
