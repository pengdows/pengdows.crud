using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// MariaDB added INSERT ... RETURNING in 10.5. On a detected 10.5+ server the create path gets the
/// generated id inline with RETURNING; older servers read it from the INSERT's own reply.
/// </summary>
public sealed class MariaDbInsertReturningTests
{
    private static DatabaseContext CreateContext(string serverVersion)
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.MariaDb };
        connection.SetServerVersion(serverVersion);
        connection.SetScalarResultForCommand("SELECT VERSION()", serverVersion);
        factory.Connections.Add(connection);

        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=MariaDb",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        }, factory);
    }

    [Theory]
    [InlineData("10.4.34-MariaDB", 10, 4, false)]
    [InlineData("10.11.6-MariaDB", 10, 11, true)]
    public void CreateSql_UsesReturning_FromMariaDb105(string serverVersion, int major, int minor, bool expected)
    {
        using var context = CreateContext(serverVersion);
        var dialect = (SqlDialect)context.GetDialect();
        Assert.Equal(SupportedDatabase.MariaDb, context.Product);
        Assert.Equal(new Version(major, minor), dialect.ProductInfo.ParsedVersion is { } v ? new Version(v.Major, v.Minor) : null);

        var gateway = new TableGateway<AutoIdEntity, int>(context);
        using var container = gateway.BuildCreateWithReturning(new AutoIdEntity { Name = "x" }, true, context);

        Assert.Equal(expected, context.SupportsInsertReturning);
        Assert.Equal(expected, container.Query.ToString().Contains("RETURNING", StringComparison.Ordinal));
    }

    [Table("auto_id")]
    private sealed class AutoIdEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
