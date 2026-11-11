using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    [Table("sf_variant")]
    public class SnowflakeVariantEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // A Snowflake VARIANT: a scalar is written as its JSON (a string is a quoted JSON string) through PARSE_JSON.
        [Column("v", DbType.Object, NativeType = "VARIANT")]
        public object? V { get; set; }
    }

    [Table("sf_object")]
    public class SnowflakeObjectEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // A Snowflake OBJECT: a dictionary is written as its JSON object through PARSE_JSON, and read back from its JSON text.
        [Column("m", DbType.Object, NativeType = "OBJECT")]
        public Dictionary<string, object>? M { get; set; }
    }

    /// <summary>
    /// Snowflake's VARIANT and OBJECT columns take their value as JSON text through PARSE_JSON (live-checked 2026-11-11:
    /// PARSE_JSON('"variant text"') inserts, and the driver returns the variant as "variant text"; PARSE_JSON of a JSON object
    /// inserts into OBJECT, and the driver returns it as JSON object text).
    /// </summary>
    public class SnowflakeSemiStructuredTests
    {
        private static DatabaseContext SnowflakeContext() =>
            new("account=x;user=y;EmulatedProduct=Snowflake", new fakeDbFactory(SupportedDatabase.Snowflake));

        private static string BoundValue(ISqlContainer sc) =>
            string.Join("|", Enumerable.Range(0, sc.ParameterCount).Select(i => sc.GetParameterValue("i" + i)?.ToString() ?? string.Empty));

        [Fact]
        public void AVariantIsWrittenAsItsJsonThroughParseJson()
        {
            using var context = SnowflakeContext();
            using var sc = new TableGateway<SnowflakeVariantEntity, int>(context).BuildCreate(
                new SnowflakeVariantEntity { Id = 1, V = "variant text" });

            Assert.Contains("PARSE_JSON(", sc.Query.ToString());
            Assert.Contains("\"variant text\"", BoundValue(sc));
        }

        [Fact]
        public void AnObjectIsWrittenAsItsJsonObjectThroughParseJson()
        {
            using var context = SnowflakeContext();
            using var sc = new TableGateway<SnowflakeObjectEntity, int>(context).BuildCreate(
                new SnowflakeObjectEntity { Id = 1, M = new Dictionary<string, object> { ["a"] = "1" } });

            Assert.Contains("PARSE_JSON(", sc.Query.ToString());
            Assert.Contains("{\"a\":\"1\"}", BoundValue(sc));
        }

        [Fact]
        public async Task AnObjectReadAsJsonTextIsTheDictionary()
        {
            var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake });
            var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake };
            factory.Connections.Add(exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["m"] = "{\"a\":\"1\",\"b\":\"2\"}" }
            }));
            await using var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "account=x;user=y;EmulatedProduct=Snowflake",
                DbMode = DbMode.Standard
            }, factory);

            var row = await new TableGateway<SnowflakeObjectEntity, int>(context).RetrieveOneAsync(1);

            Assert.Equal("1", row!.M!["a"]);
            Assert.Equal("2", row.M["b"]);
        }

        [Fact]
        public void MarkingAVariantTwiceEncodesItOnce()
        {
            // The library can mark a parameter more than once; a string must not be quoted again on the second mark (live: the
            // fidelity read came back as the text with its quotes, stored twice-encoded).
            var dialect = new SnowflakeDialect(new fakeDbFactory(SupportedDatabase.Snowflake), Microsoft.Extensions.Logging.Abstractions.NullLogger<SnowflakeDialect>.Instance);
            var column = new TypeMapRegistry().GetTableInfo<SnowflakeVariantEntity>().Columns.Values.First(c => c.Name == "v");
            var parameter = new fakeDbParameter { ParameterName = "v", DbType = DbType.Object, Value = "variant text" };

            dialect.MarkColumnParameter(parameter, column);
            dialect.MarkColumnParameter(parameter, column);

            Assert.Equal("\"variant text\"", parameter.Value);
        }

        [Fact]
        public void AVariantIsReadAsItsTextWhenItIsAStringAndAsJsonOtherwise()
        {
            // The driver returns a VARIANT as its JSON text, so a string variant comes back quoted ("\"variant text\""). A
            // string is read as its text (TO_VARCHAR), anything else as its JSON (live 2026-11-11: the stored VARCHAR was right).
            using var context = SnowflakeContext();
            using var sc = new TableGateway<SnowflakeVariantEntity, int>(context).BuildBaseRetrieve("a");

            var sql = sc.Query.ToString();
            Assert.Contains("TYPEOF(", sql);
            Assert.Contains("TO_VARCHAR(", sql);
            Assert.Contains("TO_JSON(", sql);
        }
    }
}
