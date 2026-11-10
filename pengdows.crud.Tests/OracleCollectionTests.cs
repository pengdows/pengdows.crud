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
    [Table("va_doc")]
    public class VarrayIntEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        // A VARRAY(10) OF NUMBER(10) declared in the database (CREATE TYPE probe_varray).
        [Column("items", DbType.Object, NativeType = "probe_varray")]
        public int[]? Items { get; set; }
    }

    [Table("va_names")]
    public class VarrayTextEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("names", DbType.Object, NativeType = "name_varray")]
        public string[]? Names { get; set; }
    }

    /// <summary>
    /// An Oracle VARRAY is a collection type. ODP.NET refuses to bind an array to it (ORA-50071 without a custom type mapping),
    /// so the library binds the elements as JSON text and builds the collection in SQL: JSON_TABLE reads the elements,
    /// COLLECT gathers them, and CAST makes the declared VARRAY. A read selects the elements back as JSON. A NULL stays NULL,
    /// and an empty array stays an empty collection, not NULL.
    /// </summary>
    public class OracleCollectionTests
    {
        private static DatabaseContext OracleContext()
        {
            return new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", new fakeDbFactory(SupportedDatabase.Oracle));
        }

        [Fact]
        public void AnArrayWrittenToAVarrayColumnIsBuiltInSqlFromItsJsonText()
        {
            using var context = OracleContext();
            var gateway = new TableGateway<VarrayIntEntity, int>(context);

            using var sc = gateway.BuildCreate(new VarrayIntEntity { Id = 1, Items = new[] { 1, 2, 3 } });

            var sql = sc.Query.ToString();
            Assert.Contains("JSON_TABLE(", sql);
            Assert.Contains("COLLECT(", sql);
            Assert.Contains("CAST(", sql);
            Assert.Contains("AS probe_varray)", sql);
            Assert.Contains("NUMBER PATH", sql);
        }

        [Fact]
        public void TheWrittenValueIsTheArrayAsJsonText()
        {
            using var context = OracleContext();
            var gateway = new TableGateway<VarrayIntEntity, int>(context);

            using var sc = gateway.BuildCreate(new VarrayIntEntity { Id = 1, Items = new[] { 1, 2, 3 } });

            Assert.Contains("[1,2,3]", Enumerable.Range(0, sc.ParameterCount).Select(i => sc.GetParameterValue("i" + i)?.ToString() ?? string.Empty));
        }

        [Fact]
        public void ANullArrayStaysNullAndIsNotAnEmptyCollection()
        {
            using var context = OracleContext();
            var gateway = new TableGateway<VarrayIntEntity, int>(context);

            using var sc = gateway.BuildCreate(new VarrayIntEntity { Id = 1, Items = null });

            Assert.Contains("CASE WHEN", sc.Query.ToString());
            Assert.Contains("IS NULL THEN NULL", sc.Query.ToString());
        }

        [Fact]
        public void AVarrayOfTextUsesATextElement()
        {
            using var context = OracleContext();
            var gateway = new TableGateway<VarrayTextEntity, int>(context);

            using var sc = gateway.BuildCreate(new VarrayTextEntity { Id = 1, Names = new[] { "a", "b" } });

            Assert.Contains("VARCHAR2(4000) PATH", sc.Query.ToString());
        }

        [Fact]
        public void AVarrayIsReadAsItsElementsAsJsonAndAnEmptyOneIsNotNull()
        {
            using var context = OracleContext();
            var gateway = new TableGateway<VarrayIntEntity, int>(context);

            using var sc = gateway.BuildBaseRetrieve("a");

            var sql = sc.Query.ToString();
            Assert.Contains("JSON_ARRAYAGG(COLUMN_VALUE RETURNING CLOB)", sql);
            Assert.Contains("TABLE(", sql);
            Assert.Contains("TO_CLOB('[]')", sql);
        }


        private static async System.Threading.Tasks.Task<DatabaseContext> ReadingContext(string? itemsJson)
        {
            var factory = new fakeDbFactory(SupportedDatabase.Oracle);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle });
            var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle };
            factory.Connections.Add(exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["items"] = (object?)itemsJson ?? DBNull.Value }
            }));
            return new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "Server=x;Database=y;EmulatedProduct=Oracle",
                DbMode = DbMode.Standard
            }, factory);
        }

        [Fact]
        public async System.Threading.Tasks.Task AVarrayReadAsJsonTextIsTheTypedArray()
        {
            await using var context = await ReadingContext("[1,2,3]");
            var gateway = new TableGateway<VarrayIntEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.NotNull(row);
            Assert.Equal(new[] { 1, 2, 3 }, row!.Items);
        }

        [Fact]
        public async System.Threading.Tasks.Task AnEmptyVarrayReadIsAnEmptyArrayNotNull()
        {
            await using var context = await ReadingContext("[]");
            var gateway = new TableGateway<VarrayIntEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.NotNull(row!.Items);
            Assert.Empty(row.Items!);
        }

        [Fact]
        public async System.Threading.Tasks.Task ANullVarrayReadIsNull()
        {
            await using var context = await ReadingContext(null);
            var gateway = new TableGateway<VarrayIntEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.NotNull(row);
            Assert.Null(row!.Items);
        }


        [Fact]
        public async System.Threading.Tasks.Task AVarrayOfTextReadAsJsonTextIsTheTypedTextArray()
        {
            var factory = new fakeDbFactory(SupportedDatabase.Oracle);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle });
            var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle };
            factory.Connections.Add(exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["names"] = "[\"a\",\"b, c\"]" }
            }));
            await using var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "Server=x;Database=y;EmulatedProduct=Oracle",
                DbMode = DbMode.Standard
            }, factory);
            var gateway = new TableGateway<VarrayTextEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.Equal(new[] { "a", "b, c" }, row!.Names);
        }


        [Fact]
        public void ALongCollectionIsAggregatedAsAClobSoItIsNotCutAt4000Bytes()
        {
            // JSON_ARRAYAGG returns VARCHAR2 by default and the server cuts it at 4000 bytes. RETURNING CLOB keeps the whole array.
            using var context = OracleContext();
            var gateway = new TableGateway<VarrayIntEntity, int>(context);

            using var sc = gateway.BuildBaseRetrieve("a");

            Assert.Contains("JSON_ARRAYAGG(COLUMN_VALUE RETURNING CLOB)", sc.Query.ToString());
        }
    }
}
