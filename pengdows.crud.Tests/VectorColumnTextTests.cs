using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    [Table("vec_doc")]
    public class VectorEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // A VECTOR(3) column: the element values are floats.
        [Column("embedding", DbType.Object, NativeType = "VECTOR(3)")]
        public float[]? Embedding { get; set; }
    }

    /// <summary>
    /// MySQL 9 and MariaDB 11.7 store a VECTOR that rejects a plain text literal ('[1,2,3]' is ERROR 6136 on MySQL 9.4), so the
    /// library writes it through the server's conversion (STRING_TO_VECTOR; VEC_FromText on MariaDB) and reads it through the
    /// server's text form (VECTOR_TO_STRING; VEC_ToText), which the JSON array path deserializes. TiDB and SingleStore bind their
    /// vectors as text already and keep that path.
    /// </summary>
    public class VectorColumnTextTests
    {
        private static DatabaseContext Context(SupportedDatabase db, string product)
        {
            return new DatabaseContext("Data Source=test;EmulatedProduct=" + product, new fakeDbFactory(db));
        }

        [Fact]
        public void MySqlWritesAVectorThroughItsConversionFunction()
        {
            using var context = Context(SupportedDatabase.MySql, "MySql");
            var gateway = new TableGateway<VectorEntity, int>(context);

            using var sc = gateway.BuildCreate(new VectorEntity { Id = 1, Embedding = new[] { 1f, 2f, 3f } });

            Assert.Contains("STRING_TO_VECTOR(", sc.Query.ToString());
        }

        [Fact]
        public void MySqlReadsAVectorThroughItsTextForm()
        {
            using var context = Context(SupportedDatabase.MySql, "MySql");
            var gateway = new TableGateway<VectorEntity, int>(context);

            using var sc = gateway.BuildBaseRetrieve("a");

            Assert.Contains("VECTOR_TO_STRING(", sc.Query.ToString());
        }

        [Fact]
        public void MariaDbUsesItsOwnConversionFunctions()
        {
            using var context = Context(SupportedDatabase.MariaDb, "MariaDb");
            var gateway = new TableGateway<VectorEntity, int>(context);

            using var write = gateway.BuildCreate(new VectorEntity { Id = 1, Embedding = new[] { 1f, 2f, 3f } });
            using var read = gateway.BuildBaseRetrieve("a");

            Assert.Contains("VEC_FromText(", write.Query.ToString());
            Assert.Contains("VEC_ToText(", read.Query.ToString());
            Assert.DoesNotContain("STRING_TO_VECTOR", write.Query.ToString());
        }

        [Fact]
        public void TheWrittenVectorIsItsBracketedText()
        {
            using var context = Context(SupportedDatabase.MySql, "MySql");
            var gateway = new TableGateway<VectorEntity, int>(context);

            using var sc = gateway.BuildCreate(new VectorEntity { Id = 1, Embedding = new[] { 1f, 2f, 3f } });

            Assert.Contains("[1,2,3]", Enumerable.Range(0, sc.ParameterCount).Select(i => sc.GetParameterValue("i" + i)?.ToString() ?? string.Empty));
        }

        [Fact]
        public void TiDbKeepsItsOwnTextBindingAndIsNotWrappedInAFunction()
        {
            using var context = Context(SupportedDatabase.TiDb, "TiDb");
            var gateway = new TableGateway<VectorEntity, int>(context);

            using var sc = gateway.BuildCreate(new VectorEntity { Id = 1, Embedding = new[] { 1f, 2f, 3f } });

            Assert.DoesNotContain("STRING_TO_VECTOR", sc.Query.ToString());
            Assert.DoesNotContain("VEC_FromText", sc.Query.ToString());
        }

        private static async System.Threading.Tasks.Task<DatabaseContext> ReadingContext(SupportedDatabase db, string product, object value)
        {
            var factory = new fakeDbFactory(db);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = db });
            var exec = new fakeDbConnection { EmulatedProduct = db };
            factory.Connections.Add(exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["embedding"] = value }
            }));
            return new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "Server=x;Database=y;EmulatedProduct=" + product,
                DbMode = DbMode.Standard
            }, factory);
        }

        [Fact]
        public async System.Threading.Tasks.Task AMySqlVectorReadAsItsTextIsTheFloatArray()
        {
            await using var context = await ReadingContext(SupportedDatabase.MySql, "MySql", "[1.00000e+00,2.00000e+00,3.00000e+00]");
            var gateway = new TableGateway<VectorEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.Equal(new[] { 1f, 2f, 3f }, row!.Embedding);
        }

        [Fact]
        public async System.Threading.Tasks.Task AMariaDbVectorReadAsItsTextIsTheFloatArray()
        {
            await using var context = await ReadingContext(SupportedDatabase.MariaDb, "MariaDb", "[1,2,3]");
            var gateway = new TableGateway<VectorEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.Equal(new[] { 1f, 2f, 3f }, row!.Embedding);
        }
    }
}
