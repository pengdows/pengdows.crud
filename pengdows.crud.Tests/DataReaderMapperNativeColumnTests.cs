using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>A plain object (not a gateway entity) whose BFILE column is declared with its native type.</summary>
    public class BFilePoco
    {
        [Column("doc", DbType.String, NativeType = "BFILE")]
        public string Doc { get; set; } = string.Empty;
    }

    /// <summary>
    /// DataReaderMapper maps plain objects, and must read a column's native type the same way a gateway does: a BFILE is its
    /// locator, not its content.
    /// </summary>
    public class DataReaderMapperNativeColumnTests
    {
        [Fact]
        public async Task ADataReaderMapperReadsABfileColumnAsItsLocator()
        {
            var factory = new fakeDbFactory(SupportedDatabase.Oracle);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle });
            var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle };
            factory.Connections.Add(exec);
            await using var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "Server=x;Database=y;EmulatedProduct=Oracle",
                DbMode = DbMode.Standard
            }, factory);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["doc"] = new fakeDbBFile("DATA_DIR", "hello.txt", new byte[] { 0x78, 0x0A }) }
            }));
            var sc = context.CreateSqlContainer("SELECT doc FROM bfile_doc");
            await using var reader = await sc.ExecuteReaderAsync();

            var rows = await DataReaderMapper.LoadAsync<BFilePoco>(reader, MapperOptions.Default);

            Assert.Single(rows);
            Assert.Equal("DATA_DIR/hello.txt", rows[0].Doc);
        }
    }
}
