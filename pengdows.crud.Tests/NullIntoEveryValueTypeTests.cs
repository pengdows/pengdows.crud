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
    /// <summary>
    /// A NULL into any non-nullable value type is a DataMappingException naming the column, on both the mapper and the gateway,
    /// whatever the type's read path (typed getter, GetValue plus conversion, or a converter).
    /// </summary>
    public class NullIntoEveryValueTypeTests
    {
        public class ValuePoco<T>
        {
            [Column("v", DbType.Object)]
            public T V { get; set; } = default!;
        }

        [Table("null_value_types")]
        public class ValueEntity<T>
        {
            [Id]
            [Column("id", DbType.Int32)]
            public int Id { get; set; }

            [Column("v", DbType.Object)]
            public T V { get; set; } = default!;
        }

        public enum Colour { Red, Blue }

        public static IEnumerable<object[]> Types()
        {
            yield return new object[] { typeof(int) };
            yield return new object[] { typeof(long) };
            yield return new object[] { typeof(short) };
            yield return new object[] { typeof(byte) };
            yield return new object[] { typeof(bool) };
            yield return new object[] { typeof(double) };
            yield return new object[] { typeof(float) };
            yield return new object[] { typeof(decimal) };
            yield return new object[] { typeof(Guid) };
            yield return new object[] { typeof(DateTime) };
            yield return new object[] { typeof(DateTimeOffset) };
            yield return new object[] { typeof(TimeSpan) };
            yield return new object[] { typeof(DateOnly) };
            yield return new object[] { typeof(TimeOnly) };
        }

        [Table("null_value_types")]
        public class ColourEntity
        {
            [Id]
            [Column("id", DbType.Int32)]
            public int Id { get; set; }

            [Column("v", DbType.String)]
            public Colour V { get; set; }
        }

        [Fact]
        public async Task AGatewayRefusesANullIntoANonNullableEnum()
        {
            await using var ctx = Context(out var exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["v"] = DBNull.Value }
            }));
            var gateway = new TableGateway<ColourEntity, int>(ctx);

            var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));

            Assert.Contains("'v'", ex.Message);
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

        private static Task<pengdows.crud.wrappers.ITrackedReader> NullReader(DatabaseContext ctx, fakeDbConnection exec)
        {
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["v"] = DBNull.Value }
            }));
            return ctx.CreateSqlContainer("SELECT v FROM null_value_types").ExecuteReaderAsync().AsTask();
        }

        [Theory]
        [MemberData(nameof(Types))]
        public async Task ADataReaderMapperRefusesANullIntoEveryNonNullableValueType(Type type)
        {
            await using var ctx = Context(out var exec);
            await using var reader = await NullReader(ctx, exec);

            var ex = await Assert.ThrowsAsync<DataMappingException>(() => MapAsync(type, reader));

            Assert.Contains("'v'", ex.Message);
        }

        [Theory]
        [MemberData(nameof(Types))]
        public async Task AGatewayRefusesANullIntoEveryNonNullableValueType(Type type)
        {
            await using var ctx = Context(out var exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["v"] = DBNull.Value }
            }));

            var ex = await Assert.ThrowsAsync<DataMappingException>(() => GatewayRetrieveAsync(type, ctx));

            Assert.Contains("'v'", ex.Message);
        }

        private static Task MapAsync(Type type, pengdows.crud.wrappers.ITrackedReader reader)
        {
            // LoadAsync<T> over the wrapped reader; the generic is the test's value type.
            var generic = typeof(NullIntoEveryValueTypeTests).GetMethod(nameof(MapGeneric),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(
                typeof(ValuePoco<>).MakeGenericType(type));
            return (Task)generic.Invoke(null, new object[] { reader })!;
        }

        private static async Task MapGeneric<TPoco>(pengdows.crud.wrappers.ITrackedReader reader) where TPoco : class, new()
        {
            await DataReaderMapper.LoadAsync<TPoco>(reader, MapperOptions.Default);
        }

        private static Task GatewayRetrieveAsync(Type type, DatabaseContext ctx)
        {
            var generic = typeof(NullIntoEveryValueTypeTests).GetMethod(nameof(GatewayGeneric),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(type);
            return (Task)generic.Invoke(null, new object[] { ctx })!;
        }

        private static async Task GatewayGeneric<T>(DatabaseContext ctx)
        {
            var gateway = new TableGateway<ValueEntity<T>, int>(ctx);
            await gateway.RetrieveOneAsync(1);
        }
    }
}
