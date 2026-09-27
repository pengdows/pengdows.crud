using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED live (ASE 16.0, AdoNetCore.AseClient 0.19.2): a NULL parameter typed DbType.Boolean is
/// stored as 0, so a nullable bool written as NULL read back as false (HARN-005 TypeHydrationTests).
/// A NULL bool must reach the driver typed Byte, and a non-null bool typed Boolean, whichever way
/// the gateway binds it (fresh parameters or a cached template whose values are replaced).
/// </summary>
public class SybaseNullableBoolParameterTests
{
    [Table("nullable_flags")]
    public class NullableFlagRow
    {
        [Id(true)][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("flag", DbType.Boolean)] public bool? Flag { get; set; }
    }

    private static (DatabaseContext Context, fakeDbConnection Connection) CreateContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SybaseASE);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SybaseASE",
            DbMode = DbMode.SingleConnection
        }, factory);
        return (context, connection);
    }

    // fakeDb captures each executed command's parameters with their DbType. The flag is the
    // second bound value (id, flag).
    private static CapturedParameter FlagParameter(fakeDbConnection connection, int commandIndexFromEnd)
    {
        var command = connection.ExecutedNonQueryCommands
            .Where(c => c.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            .Reverse().ElementAt(commandIndexFromEnd);
        return command.Parameters[1];
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, null)]
    [InlineData(null, null)]
    public async Task CreateAsync_TypesEachFlagByItsOwnValue(bool? first, bool? second)
    {
        var (context, connection) = CreateContext();
        await using (context)
        {
            var gateway = new TableGateway<NullableFlagRow, long>(context);
            await gateway.CreateAsync(new NullableFlagRow { Id = 1, Flag = first }, context);
            await gateway.CreateAsync(new NullableFlagRow { Id = 2, Flag = second }, context);

            AssertTyped(FlagParameter(connection, 1), first);
            AssertTyped(FlagParameter(connection, 0), second);
        }
    }

    private static void AssertTyped(CapturedParameter parameter, bool? expected)
    {
        if (expected is null)
        {
            Assert.Equal(DbType.Byte, parameter.DbType);
            Assert.Equal(DBNull.Value, parameter.Value);
        }
        else
        {
            Assert.Equal(DbType.Boolean, parameter.DbType);
            Assert.Equal(expected.Value, parameter.Value);
        }
    }
}
