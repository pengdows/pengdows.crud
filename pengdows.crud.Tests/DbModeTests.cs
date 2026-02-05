#region

using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class DbModeTests
{
    private static async Task BuildUsersTableAsync(IDatabaseContext context)
    {
        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        var sql = string.Format(@"CREATE TABLE IF NOT EXISTS
{0}Users{1} (
    {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
    {0}Email{1} TEXT UNIQUE,
    {0}Version{1} INTEGER,
    {0}Name{1} TEXT
)", qp, qs);

        var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SingleConnection_SerializesMultithreadedOperations_InMemory()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<User>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(cfg, SqliteFactory.Instance, NullLoggerFactory.Instance, typeMap);
        await BuildUsersTableAsync(context);

        var helper = new TableGateway<User, int>(context, null);
        var users = Enumerable.Range(1, 20)
            .Select(i => new User { Email = $"test{i}@example.com", Name = $"Test{i}", Version = 1 })
            .ToList();

        var tasks = users.Select(u => Task.Run(async () =>
        {
            var sc = helper.BuildCreate(u);
            await sc.ExecuteNonQueryAsync();
        })).ToArray();

        await Task.WhenAll(tasks);

        var scSelect = context.CreateSqlContainer("SELECT COUNT(*) FROM Users");
        var count = await scSelect.ExecuteScalarAsync<int>();
        Assert.Equal(20, count);
    }

    [Fact]
    public async Task SingleWriter_SerializesMultithreadedWrites_FileBased()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<User>();

        var dbFile = Path.Combine(Path.GetTempPath(), $"crud_{Guid.NewGuid():N}.db");
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={dbFile}",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(cfg, SqliteFactory.Instance, NullLoggerFactory.Instance, typeMap);
        await BuildUsersTableAsync(context);

        var helper = new TableGateway<User, int>(context, null);
        var users = Enumerable.Range(1, 20)
            .Select(i => new User { Email = $"test{i}@example.com", Name = $"Test{i}", Version = 1 })
            .ToList();

        var tasks = users.Select(u => Task.Run(async () =>
        {
            var sc = helper.BuildCreate(u);
            await sc.ExecuteNonQueryAsync();
        })).ToArray();

        await Task.WhenAll(tasks);

        var scRead = context.CreateSqlContainer("SELECT COUNT(*) FROM Users");
        var count = await scRead.ExecuteScalarAsync<int>();
        Assert.Equal(20, count);

        // Cleanup the temp file
        try
        {
            File.Delete(dbFile);
        }
        catch
        {
        }
    }

    // Minimal entity definition to exercise helper
    [Table("Users")]
    private class User
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey]
        [Column("Email", DbType.String)]
        public string Email { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}