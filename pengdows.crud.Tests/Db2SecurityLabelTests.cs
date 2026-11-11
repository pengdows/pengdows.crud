using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    [Table("db2_sec")]
    public class Db2SecurityLabelEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // A Db2 LBAC column: its value is a label's text, written with SECLABEL and read with SECLABEL_TO_CHAR, both of the
        // policy named in the native type (POL).
        [Column("sl", DbType.String, NativeType = "DB2SECURITYLABEL(POL)")]
        public string? Label { get; set; }
    }

    /// <summary>
    /// A Db2 security label is written as its label text through SECLABEL('POL', ...) and read as SECLABEL_TO_CHAR('POL', ...).
    /// Live 2026-11-11: SECLABEL('POL', 'LOW') inserts, and SECLABEL_TO_CHAR('POL', sl) reads back LOW.
    /// </summary>
    public class Db2SecurityLabelTests
    {
        private static DatabaseContext Db2Context() =>
            new("Server=x;Database=y;UID=z;EmulatedProduct=Db2", new fakeDbFactory(SupportedDatabase.Db2));

        [Fact]
        public void ALabelIsWrittenThroughSeclabelOfItsPolicy()
        {
            using var context = Db2Context();
            using var sc = new TableGateway<Db2SecurityLabelEntity, int>(context).BuildCreate(
                new Db2SecurityLabelEntity { Id = 1, Label = "LOW" });

            Assert.Contains("SECLABEL('POL', ", sc.Query.ToString());
        }

        [Fact]
        public void ALabelIsReadAsItsTextThroughSeclabelToChar()
        {
            using var context = Db2Context();
            using var sc = new TableGateway<Db2SecurityLabelEntity, int>(context).BuildBaseRetrieve("l");

            Assert.Contains("SECLABEL_TO_CHAR('POL', ", sc.Query.ToString());
        }
    }
}
