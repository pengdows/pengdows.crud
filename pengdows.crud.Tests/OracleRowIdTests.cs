using System;
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
    [Table("rowid_doc")]
    public class RowIdEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // A ROWID is the row's physical address: the database assigns it, so it is never written. It is read as its text.
        [Column("rid", DbType.String, NativeType = "ROWID")]
        [NonInsertable]
        [NonUpdateable]
        public string? Rid { get; set; }
    }

    /// <summary>
    /// An Oracle ROWID is supported by the database: it reads as text (ROWIDTOCHAR), and it is never written, because the
    /// database assigns it. The column is NonInsertable and NonUpdateable, so an insert or update leaves it out.
    /// </summary>
    public class OracleRowIdTests
    {
        private static DatabaseContext OracleContext() =>
            new("Data Source=test;EmulatedProduct=Oracle", new fakeDbFactory(SupportedDatabase.Oracle));

        [Fact]
        public void ARowIdIsReadAsItsText()
        {
            using var context = OracleContext();
            var gateway = new TableGateway<RowIdEntity, int>(context);

            using var sc = gateway.BuildBaseRetrieve("a");

            Assert.Contains("ROWIDTOCHAR(", sc.Query.ToString());
        }

        [Fact]
        public void ARowIdIsNeverWrittenByAnInsert()
        {
            using var context = OracleContext();
            var gateway = new TableGateway<RowIdEntity, int>(context);

            using var sc = gateway.BuildCreate(new RowIdEntity { Id = 1, Rid = "AAAR3xAAEAAAACDAAA" });

            Assert.DoesNotContain("\"rid\"", sc.Query.ToString());
        }

        [Fact]
        public async Task ARowIdReadAsTextMapsToTheProperty()
        {
            var factory = new fakeDbFactory(SupportedDatabase.Oracle);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle });
            var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle };
            factory.Connections.Add(exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["rid"] = "AAAR3xAAEAAAACDAAA" }
            }));
            await using var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "Server=x;Database=y;EmulatedProduct=Oracle",
                DbMode = DbMode.Standard
            }, factory);
            var gateway = new TableGateway<RowIdEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.Equal("AAAR3xAAEAAAACDAAA", row!.Rid);
        }
    }
}
