#nullable enable
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

namespace pengdows.crud.Tests
{
    public enum NullMood
    {
        Happy,
        Sad,
    }

    public class NullIntoStringPoco
    {
        [Column("v", DbType.String)]
        public string V { get; set; } = "";
    }

    public class NullIntoNullableStringPoco
    {
        [Column("v", DbType.String)]
        public string? V { get; set; }
    }

    public class NullIntoEnumPoco
    {
        [Column("v", DbType.String)]
        public NullMood V { get; set; }
    }

    [Table("null_into_string")]
    public class NullIntoStringEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("v", DbType.String)]
        public string V { get; set; } = "";
    }

    /// <summary>
    /// A NULL stored value into a non-nullable property is an error on every path and in every mode: it never becomes the
    /// type's default, an empty string or a silent null. A nullable property takes NULL as null.
    /// </summary>
    public class NullIntoNonNullableTests
    {
        private static DatabaseContext Context(SupportedDatabase product, out fakeDbConnection exec)
        {
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

        private static async Task<pengdows.crud.wrappers.ITrackedReader> ReaderOfNull(DatabaseContext ctx, fakeDbConnection exec)
        {
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["v"] = DBNull.Value }
            }));
            var sc = ctx.CreateSqlContainer("SELECT v FROM null_into_string");
            return await sc.ExecuteReaderAsync();
        }

        [Fact]
        public async Task ADataReaderMapperRefusesANullIntoANonNullableString()
        {
            await using var ctx = Context(SupportedDatabase.PostgreSql, out var exec);
            await using var reader = await ReaderOfNull(ctx, exec);

            var ex = await Assert.ThrowsAsync<DataMappingException>(async () =>
                await DataReaderMapper.LoadAsync<NullIntoStringPoco>(reader, MapperOptions.Default));

            Assert.Contains("'v'", ex.Message);
        }

        [Fact]
        public async Task ADataReaderMapperRefusesANullIntoANonNullableStringInNonStrictMode()
        {
            await using var ctx = Context(SupportedDatabase.PostgreSql, out var exec);
            await using var reader = await ReaderOfNull(ctx, exec);
            var options = MapperOptions.Default with { Strict = false };

            await Assert.ThrowsAsync<DataMappingException>(async () =>
                await DataReaderMapper.LoadAsync<NullIntoStringPoco>(reader, options));
        }

        [Fact]
        public async Task ADataReaderMapperRefusesANullIntoANonNullableEnum()
        {
            await using var ctx = Context(SupportedDatabase.PostgreSql, out var exec);
            await using var reader = await ReaderOfNull(ctx, exec);

            await Assert.ThrowsAsync<DataMappingException>(async () =>
                await DataReaderMapper.LoadAsync<NullIntoEnumPoco>(reader, MapperOptions.Default));
        }

        [Fact]
        public async Task ADataReaderMapperReadsANullIntoANullableStringAsNull()
        {
            await using var ctx = Context(SupportedDatabase.PostgreSql, out var exec);
            await using var reader = await ReaderOfNull(ctx, exec);

            var rows = await DataReaderMapper.LoadAsync<NullIntoNullableStringPoco>(reader, MapperOptions.Default);

            Assert.Null(rows[0].V);
        }

        [Fact]
        public async Task AGatewayRefusesANullIntoANonNullableString()
        {
            await using var ctx = Context(SupportedDatabase.PostgreSql, out var exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["v"] = DBNull.Value }
            }));
            var gateway = new TableGateway<NullIntoStringEntity, int>(ctx);

            var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));

            Assert.Contains("'v'", ex.Message);
        }
    }
}
