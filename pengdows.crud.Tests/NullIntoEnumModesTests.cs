using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A NULL into a non-nullable enum is an error in every enum failure mode. The lenient modes (SetDefaultValue, SetNullAndLog)
/// substitute a default for a stored value that is not a member; they never substitute one for NULL.
/// </summary>
public sealed class NullIntoEnumModesTests
{
    public enum Shade { Light, Dark }

    [Table("null_enum_modes")]
    public class ShadeEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("v", DbType.String)]
        public Shade V { get; set; }
    }

    public class ShadePoco
    {
        [Column("v", DbType.String)]
        public Shade V { get; set; }
    }

    private static DatabaseContext Context(out fakeDbConnection exec)
    {
        var product = SupportedDatabase.PostgreSql;
        var factory = new fakeDbFactory(product);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = product });
        exec = new fakeDbConnection { EmulatedProduct = product };
        factory.Connections.Add(exec);
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Server=x;Database=y;EmulatedProduct={product}",
            DbMode = DbMode.Standard
        }, factory);
    }

    public static IEnumerable<object[]> Modes()
    {
        yield return new object[] { EnumParseFailureMode.Throw };
        yield return new object[] { EnumParseFailureMode.SetDefaultValue };
        yield return new object[] { EnumParseFailureMode.SetNullAndLog };
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task AGatewayRefusesANullIntoANonNullableEnumInEveryMode(EnumParseFailureMode mode)
    {
        await using var ctx = Context(out var exec);
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["v"] = DBNull.Value }
        }));
        var gateway = new TableGateway<ShadeEntity, int>(ctx, enumParseBehavior: mode);

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));

        Assert.Contains("'v'", ex.Message);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task ADataReaderMapperRefusesANullIntoANonNullableEnumInEveryMode(EnumParseFailureMode mode)
    {
        await using var ctx = Context(out var exec);
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["v"] = DBNull.Value }
        }));
        await using var reader = await ctx.CreateSqlContainer("SELECT v FROM null_enum_modes").ExecuteReaderAsync();

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () =>
            await DataReaderMapper.LoadAsync<ShadePoco>(reader, MapperOptions.Default with { EnumMode = mode }));

        Assert.Contains("'v'", ex.Message);
    }
}
