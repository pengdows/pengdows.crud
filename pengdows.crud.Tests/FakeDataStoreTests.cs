#region

using System.Data;
using Microsoft.Data.Sqlite;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class FakeDataStoreTests
{
    [Fact]
    public void FakeDataStore_ShouldCreateEmptyStore()
    {
        var store = new FakeDataStore();
        Assert.NotNull(store);
    }

    [Fact]
    public void ExecuteNonQuery_CreateTable_ShouldReturnOne()
    {
        var store = new FakeDataStore();
        var result = store.ExecuteNonQuery("CREATE TABLE TestTable (Id INTEGER PRIMARY KEY, Name TEXT)");
        Assert.Equal(1, result);
    }

    [Fact]
    public void ExecuteNonQuery_Insert_ShouldReturnOne()
    {
        var store = new FakeDataStore();

        // Create table first
        store.ExecuteNonQuery("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");

        // Insert data
        var result = store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('John')");
        Assert.Equal(1, result);
    }

    [Fact]
    public void ExecuteNonQuery_InsertWithParameters_ShouldReturnOne()
    {
        var store = new FakeDataStore();
        store.ExecuteNonQuery("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");

        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var command = factory.CreateCommand();
        command.CommandText = "INSERT INTO Users (Name) VALUES (@name)";
        var param = command.CreateParameter();
        param.ParameterName = "@name";
        param.Value = "Jane";
        param.DbType = DbType.String;
        command.Parameters.Add(param);

        var result = store.ExecuteNonQuery(command.CommandText, command.Parameters);
        Assert.Equal(1, result);
    }

    [Fact]
    public void ExecuteReader_SelectAfterInsert_ShouldReturnData()
    {
        var store = new FakeDataStore();
        store.ExecuteNonQuery("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('John')");

        var reader = store.ExecuteReader("SELECT * FROM Users");
        Assert.NotNull(reader);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var row in reader)
        {
            rows.Add(row);
        }

        Assert.Single(rows);
        Assert.True(rows[0].ContainsKey("Name"));
        Assert.Equal("John", rows[0]["Name"]);
    }

    [Fact]
    public void ExecuteReader_SelectWithWhere_ShouldFilterResults()
    {
        var store = new FakeDataStore();
        store.ExecuteNonQuery("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('John')");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('Jane')");

        var reader = store.ExecuteReader("SELECT * FROM Users WHERE Name = 'John'");
        var rows = reader.ToList();

        Assert.Single(rows);
        Assert.Equal("John", rows[0]["Name"]);
    }

    [Fact]
    public void ExecuteScalar_Count_ShouldReturnCorrectValue()
    {
        var store = new FakeDataStore();
        store.ExecuteNonQuery("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('John')");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('Jane')");

        var count = store.ExecuteScalar("SELECT COUNT(*) FROM Users");
        Assert.Equal(2L, count);
    }

    [Fact]
    public void ExecuteNonQuery_Update_ShouldModifyData()
    {
        var store = new FakeDataStore();
        store.ExecuteNonQuery("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('John')");

        var updateResult = store.ExecuteNonQuery("UPDATE Users SET Name = 'Johnny' WHERE Name = 'John'");
        Assert.Equal(1, updateResult);

        var reader = store.ExecuteReader("SELECT * FROM Users");
        var rows = reader.ToList();
        Assert.Single(rows);
        Assert.Equal("Johnny", rows[0]["Name"]);
    }

    [Fact]
    public void ExecuteNonQuery_Delete_ShouldRemoveData()
    {
        var store = new FakeDataStore();
        store.ExecuteNonQuery("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('John')");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('Jane')");

        var deleteResult = store.ExecuteNonQuery("DELETE FROM Users WHERE Name = 'John'");
        Assert.Equal(1, deleteResult);

        var reader = store.ExecuteReader("SELECT * FROM Users");
        var rows = reader.ToList();
        Assert.Single(rows);
        Assert.Equal("Jane", rows[0]["Name"]);
    }

    [Fact]
    public void Clear_ShouldRemoveAllData()
    {
        var store = new FakeDataStore();
        store.ExecuteNonQuery("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('John')");

        store.Clear();

        var reader = store.ExecuteReader("SELECT * FROM Users");
        var rows = reader.ToList();
        Assert.Empty(rows);
    }

    [Fact]
    public void ExecuteNonQuery_InsertAutoIncrement_ShouldGenerateIds()
    {
        var store = new FakeDataStore();
        store.ExecuteNonQuery("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");

        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('John')");
        store.ExecuteNonQuery("INSERT INTO Users (Name) VALUES ('Jane')");

        var reader = store.ExecuteReader("SELECT Id, Name FROM Users ORDER BY Id");
        var rows = reader.ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0]["Id"]);
        Assert.Equal("John", rows[0]["Name"]);
        Assert.Equal(2, rows[1]["Id"]);
        Assert.Equal("Jane", rows[1]["Name"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ExecuteNonQuery_EmptyOrNullCommand_ShouldReturnZero(string? commandText)
    {
        var store = new FakeDataStore();
        var result = store.ExecuteNonQuery(commandText!);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ExecuteReader_NoTable_ShouldReturnEmptyResults()
    {
        var store = new FakeDataStore();
        var reader = store.ExecuteReader("SELECT * FROM NonExistentTable");
        var rows = reader.ToList();
        Assert.Empty(rows);
    }

    [Fact]
    public void ExecuteScalar_NoData_ShouldReturnNull()
    {
        var store = new FakeDataStore();
        var result = store.ExecuteScalar("SELECT Name FROM NonExistentTable");
        Assert.Null(result);
    }
}