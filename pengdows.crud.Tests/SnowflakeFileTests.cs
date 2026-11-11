using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    [Table("sf_file")]
    public class SnowflakeFileEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // A Snowflake FILE column holds a staged file: the value is a stage path, and the server resolves it with TO_FILE.
        [Column("f", DbType.String, NativeType = "FILE")]
        public string? F { get; set; }
    }

    /// <summary>
    /// A Snowflake FILE is written from its stage path through TO_FILE (live-checked 2026-11-11: INSERT ... SELECT TO_FILE('@STAGE/file.bin')
    /// succeeds, and a plain SELECT returns the file as a JSON document with RELATIVE_PATH and STAGE).
    /// </summary>
    public class SnowflakeFileTests
    {
        private static DatabaseContext SnowflakeContext() =>
            new("account=x;user=y;EmulatedProduct=Snowflake", new fakeDbFactory(SupportedDatabase.Snowflake));

        [Fact]
        public void AFileIsWrittenThroughToFile()
        {
            using var context = SnowflakeContext();
            using var sc = new TableGateway<SnowflakeFileEntity, int>(context).BuildCreate(
                new SnowflakeFileEntity { Id = 1, F = "@FIDELITY_STAGE/file.bin" });

            Assert.Contains("TO_FILE(", sc.Query.ToString());
            Assert.DoesNotContain("PARSE_JSON", sc.Query.ToString());
        }

        [Fact]
        public void TheWrittenValueIsTheStagePathNotJson()
        {
            using var context = SnowflakeContext();
            using var sc = new TableGateway<SnowflakeFileEntity, int>(context).BuildCreate(
                new SnowflakeFileEntity { Id = 1, F = "@FIDELITY_STAGE/file.bin" });

            Assert.Contains("@FIDELITY_STAGE/file.bin",
                Enumerable.Range(0, sc.ParameterCount).Select(i => sc.GetParameterValue("i" + i)?.ToString() ?? string.Empty));
        }
    }
}
