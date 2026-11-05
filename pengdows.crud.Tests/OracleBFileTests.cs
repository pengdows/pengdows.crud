using System.Data;
using pengdows.crud.attributes;
using System.Collections.Generic;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

// Stands in for Oracle.ManagedDataAccess.Types.OracleBFile, which the library reads by its type name.
namespace Oracle.ManagedDataAccess.Types
{
    public sealed class OracleBFile
    {
        public OracleBFile(string directoryName, string fileName)
        {
            DirectoryName = directoryName;
            FileName = fileName;
        }

        public string DirectoryName { get; }

        public string FileName { get; }
    }
}

namespace pengdows.crud.Tests
{
    [Table("bfile_doc")]
    public class BFileEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // "DIRECTORY/file": the directory object is fixed by the application, the file name is the value.
        [Column("doc", DbType.String, NativeType = "BFILE")]
        public string Doc { get; set; } = string.Empty;
    }

    /// <summary>
    /// An Oracle BFILE is a locator: a directory object and a file name, with the file kept on the server. A string of
    /// "DIRECTORY/file" is written as BFILENAME(directory, file), which takes the two parts as separate arguments, and the
    /// value is bound once. A read locator is its directory and file as the same text.
    /// </summary>
    public class OracleBFileTests
    {
        [Fact]
        public void AStringIsWrittenAsBfilenameOfItsDirectoryAndFile()
        {
            using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", new fakeDbFactory(SupportedDatabase.Oracle));
            var gateway = new TableGateway<BFileEntity, int>(context);

            using var sc = gateway.BuildCreate(new BFileEntity { Id = 1, Doc = "DATA_DIR/hello.txt" });

            var sql = sc.Query.ToString();
            Assert.Contains("BFILENAME(", sql);
            Assert.Contains("INSTR(", sql);
            // The value is bound once: a single-row subquery holds it, and the statement splits it there.
            Assert.Contains("FROM DUAL", sql);
            // A value with no directory fails the statement (ORA-01476, divide by zero) rather than binding a NULL directory;
            // a NULL value stays NULL.
            Assert.Contains("ELSE TO_CHAR(1/0) END", sql);
            Assert.Contains("WHEN v.x IS NULL THEN NULL", sql);
        }

        [Fact]
        public async System.Threading.Tasks.Task AReadBfileColumnIsReadAsItsLocatorAndNotOpenedAsText()
        {
            // A text read of a BFILE opens the file on the server (ORA-22288 when it is absent). The column is read as its
            // locator, which names the directory and file without opening anything.
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
                new Dictionary<string, object> { ["id"] = 1, ["doc"] = new Oracle.ManagedDataAccess.Types.OracleBFile("DATA_DIR", "hello.txt") }
            }));
            var gateway = new TableGateway<BFileEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.NotNull(row);
            Assert.Equal("DATA_DIR/hello.txt", row!.Doc);
        }

        [Fact]
        public void AReadBfileLocatorIsItsDirectoryAndFileAsText()
        {
            var locator = new Oracle.ManagedDataAccess.Types.OracleBFile("DATA_DIR", "hello.txt");

            var text = TypeCoercionHelper.Coerce(locator, locator.GetType(), typeof(string), null);

            Assert.Equal("DATA_DIR/hello.txt", text);
        }
    }
}

namespace pengdows.crud.Tests
{
    public class FakeDbBFileTests
    {
        [Fact]
        public void AStoredBFileReadsAsItsContentByGetValueAndAsItsLocatorByProviderSpecificValue()
        {
            // The driver's split, confirmed live: GetValue is the file's bytes, GetProviderSpecificValue the locator.
            var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle };
            var reader = new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["doc"] = new fakeDbBFile("DATA_DIR", "hello.txt", new byte[] { 0x78, 0x0A }) }
            });

            Assert.True(reader.Read());
            Assert.Equal(new byte[] { 0x78, 0x0A }, Assert.IsType<byte[]>(reader.GetValue(0)));
            var locator = Assert.IsType<fakeDbBFile>(reader.GetProviderSpecificValue(0));
            Assert.Equal("DATA_DIR", locator.DirectoryName);
            Assert.Equal("hello.txt", locator.FileName);
            Assert.NotNull(connection);
        }

        [Fact]
        public async System.Threading.Tasks.Task AMappedBFileColumnIsReadAsItsLocatorNotItsContent()
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
                new Dictionary<string, object> { ["id"] = 1, ["doc"] = new fakeDbBFile("DATA_DIR", "hello.txt", new byte[] { 0x78, 0x0A }) }
            }));
            var gateway = new TableGateway<BFileEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.NotNull(row);
            Assert.Equal("DATA_DIR/hello.txt", row!.Doc);
        }
    }
}
