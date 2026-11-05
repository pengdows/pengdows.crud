using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    [Table("native_text")]
    public class NativeTextEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // A text column of a declared length: the value is written as it is, so the database enforces the length.
        [Column("doc", DbType.String, NativeType = "varchar(10)")]
        public string Doc { get; set; } = string.Empty;
    }

    /// <summary>
    /// A text-typed native column takes its text unchanged. A CAST to varchar(n) would cut a long value silently; the
    /// unchanged text makes the database refuse it instead.
    /// </summary>
    public class NativeTextTypeTests
    {
        [Theory]
        [InlineData(SupportedDatabase.PostgreSql)]
        [InlineData(SupportedDatabase.Firebird)]
        public void ATextNativeTypeIsNotCastOnTheWrite(SupportedDatabase product)
        {
            using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
            var gateway = new TableGateway<NativeTextEntity, int>(context);

            using var sc = gateway.BuildCreate(new NativeTextEntity { Id = 1, Doc = "abc" });

            var sql = sc.Query.ToString();
            Assert.DoesNotContain("CAST(", sql);
            Assert.DoesNotContain("varchar(10)", sql.ToLowerInvariant());
        }
    }
}
