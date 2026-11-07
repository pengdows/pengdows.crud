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
    public class NullIntoIntPoco
    {
        [Column("v", DbType.Int32)]
        public int V { get; set; }
    }

    public class NullIntoNullableIntPoco
    {
        [Column("v", DbType.Int32)]
        public int? V { get; set; }
    }

    [Table("null_into_int")]
    public class NullIntoIntEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("v", DbType.Int32)]
        public int V { get; set; }
    }

    /// <summary>
    /// A NULL stored value into a non-nullable value type must fail loudly, naming the column. It must never become the
    /// type's default (0, false, the empty Guid): that is silent data loss. A nullable property takes NULL as null.
    /// </summary>
    public class NullIntoValueTypeTests
    {
        private static (DatabaseContext Context, fakeDbConnection Exec) Context(SupportedDatabase product)
        {
            var factory = new fakeDbFactory(product);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = product });
            var exec = new fakeDbConnection { EmulatedProduct = product };
            factory.Connections.Add(exec);
            var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = $"Server=x;Database=y;EmulatedProduct={product}",
                DbMode = DbMode.Standard
            }, factory);
            return (context, exec);
        }

        [Fact]
        public async Task ADataReaderMapperRefusesANullIntoANonNullableValueType()
        {
            var (context, exec) = Context(SupportedDatabase.PostgreSql);
            await using var ctx = context;
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["v"] = DBNull.Value }
            }));
            var sc = ctx.CreateSqlContainer("SELECT v FROM null_into_int");
            await using var reader = await sc.ExecuteReaderAsync();

            var ex = await Assert.ThrowsAsync<DataMappingException>(async () =>
                await DataReaderMapper.LoadAsync<NullIntoIntPoco>(reader, MapperOptions.Default));

            Assert.Contains("'v'", ex.Message);
        }

        [Fact]
        public async Task ADataReaderMapperReadsANullIntoANullablePropertyAsNull()
        {
            var (context, exec) = Context(SupportedDatabase.PostgreSql);
            await using var ctx = context;
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["v"] = DBNull.Value }
            }));
            var sc = ctx.CreateSqlContainer("SELECT v FROM null_into_int");
            await using var reader = await sc.ExecuteReaderAsync();

            var rows = await DataReaderMapper.LoadAsync<NullIntoNullableIntPoco>(reader, MapperOptions.Default);

            Assert.Null(rows[0].V);
        }

        [Fact]
        public async Task AGatewayRefusesANullIntoANonNullableValueType()
        {
            var (context, exec) = Context(SupportedDatabase.PostgreSql);
            await using var ctx = context;
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["v"] = DBNull.Value }
            }));
            var gateway = new TableGateway<NullIntoIntEntity, int>(ctx);

            var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));

            Assert.Contains("'v'", ex.Message);
        }
    }
}
