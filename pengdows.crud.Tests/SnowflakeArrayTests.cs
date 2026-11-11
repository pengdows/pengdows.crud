using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    [Table("sf_array")]
    public class SnowflakeArrayEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // A Snowflake ARRAY column: the array is written as JSON (PARSE_JSON) and read back as JSON (TO_JSON).
        [Column("a", DbType.Object, NativeType = "ARRAY")]
        public int[]? Items { get; set; }
    }

    /// <summary>
    /// A Snowflake ARRAY is written from its JSON text and read as its JSON. Snowflake refuses a function call on a bind in
    /// VALUES, so the insert takes its source from a SELECT (live-checked: INSERT ... SELECT PARSE_JSON(:p) succeeds, and
    /// TO_JSON of the column reads back as [1,2,3]).
    /// </summary>
    public class SnowflakeArrayTests
    {
        private static DatabaseContext SnowflakeContext() =>
            new("account=x;user=y;EmulatedProduct=Snowflake", new fakeDbFactory(SupportedDatabase.Snowflake));

        [Fact]
        public void AnArrayIsWrittenThroughParseJson()
        {
            using var context = SnowflakeContext();
            using var sc = new TableGateway<SnowflakeArrayEntity, int>(context).BuildCreate(
                new SnowflakeArrayEntity { Id = 1, Items = new[] { 1, 2, 3 } });

            Assert.Contains("PARSE_JSON(", sc.Query.ToString());
            // An ARRAY column takes an array, not a vector: a VECTOR cast here is refused by the server ("expecting ARRAY but got
            // VECTOR(INT, 3)", live 2026-11-11).
            Assert.DoesNotContain("VECTOR", sc.Query.ToString());
        }

        [Fact]
        public void TheWrittenValueIsTheArrayAsJsonText()
        {
            using var context = SnowflakeContext();
            using var sc = new TableGateway<SnowflakeArrayEntity, int>(context).BuildCreate(
                new SnowflakeArrayEntity { Id = 1, Items = new[] { 1, 2, 3 } });

            Assert.Contains("[1,2,3]", Enumerable.Range(0, sc.ParameterCount).Select(i => sc.GetParameterValue("i" + i)?.ToString() ?? string.Empty));
        }

        [Fact]
        public void AnArrayIsReadAsItsJson()
        {
            using var context = SnowflakeContext();
            using var sc = new TableGateway<SnowflakeArrayEntity, int>(context).BuildBaseRetrieve("a");

            Assert.Contains("TO_JSON(", sc.Query.ToString());
        }

        [Fact]
        public async Task AnArrayReadAsJsonIsTheTypedArray()
        {
            var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake });
            var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake };
            factory.Connections.Add(exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["a"] = "[1,2,3]" }
            }));
            await using var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "account=x;user=y;EmulatedProduct=Snowflake",
                DbMode = DbMode.Standard
            }, factory);

            var row = await new TableGateway<SnowflakeArrayEntity, int>(context).RetrieveOneAsync(1);

            Assert.Equal(new[] { 1, 2, 3 }, row!.Items);
        }
    }
}
