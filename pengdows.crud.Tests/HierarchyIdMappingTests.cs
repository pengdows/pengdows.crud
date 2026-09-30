using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-016: a SQL Server hierarchyid column reads into <see cref="HierarchyId"/> (or its text into a
/// string) without Microsoft.SqlServer.Types, and a HierarchyId is written as its text form, which
/// SQL Server converts implicitly. Confirmed live on SQL Server 2025 with SqlClient 6.0.2: without
/// that assembly GetFieldType returns null, GetValue throws FileNotFoundException, GetDataTypeName
/// is "master.sys.hierarchyid" and GetBytes returns the stored encoding.
/// </summary>
public sealed class HierarchyIdMappingTests
{
    private static readonly byte[] Path12 = { 0x5B, 0x40 }; // /1/2/

    [Fact]
    public void Coerce_ReadsTextBytesAndSqlHierarchyId()
    {
        var expected = HierarchyId.Parse("/1/2/");

        Assert.Equal(expected, TypeCoercionHelper.Coerce("/1/2/", typeof(string), typeof(HierarchyId)));
        Assert.Equal(expected, TypeCoercionHelper.Coerce(Path12, typeof(byte[]), typeof(HierarchyId?)));
        Assert.Equal(expected, TypeCoercionHelper.Coerce(new SqlHierarchyId("/1/2/"), typeof(SqlHierarchyId), typeof(HierarchyId)));
        Assert.Equal("/1/2/", TypeCoercionHelper.Coerce(expected, typeof(HierarchyId), typeof(string)));
    }

    [Fact]
    public void FakeDbReader_EmulatesSqlClientOnAClrTypeWhoseAssemblyIsNotLoaded()
    {
        var reader = PathReader();

        Assert.True(reader.Read());
        Assert.Null(reader.GetFieldType(1));
        Assert.Equal("master.sys.hierarchyid", reader.GetDataTypeName(1));
        var missing = Assert.Throws<FileNotFoundException>(() => reader.GetValue(1));
        Assert.StartsWith("Microsoft.SqlServer.Types", missing.FileName);
        Assert.False(reader.IsDBNull(1));
        var buffer = new byte[16];
        Assert.Equal(2, reader.GetBytes(1, 0, buffer, 0, buffer.Length));
        Assert.Equal(Path12, buffer[..2]);
    }

    [Fact]
    public async Task RetrieveOneAsync_HierarchyIdColumn_HydratesHierarchyIdAndStringProperties()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(PathReader());
        var gateway = new TableGateway<PathRow, int>(context);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.Equal(HierarchyId.Parse("/1/2/"), row!.Path);
        Assert.Equal("/1/2/", row.PathText);
    }

    [Fact]
    public async Task RetrieveOneAsync_NullHierarchyIdColumn_HydratesNull()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(PathReader(DBNull.Value));
        var gateway = new TableGateway<PathRow, int>(context);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.Null(row!.Path);
        Assert.Null(row.PathText);
    }

    [Fact]
    public async Task TrackedReader_ReportsAndReadsTheColumnAsHierarchyId()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(PathReader());
        await using var sc = context.CreateSqlContainer("SELECT id, path FROM t");
        await using var reader = await sc.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(typeof(HierarchyId), reader.GetFieldType(1));
        Assert.Equal(HierarchyId.Parse("/1/2/"), reader.GetValue(1));
    }

    [Fact]
    public async Task StrictDataReaderMapper_HierarchyIdColumn_Maps()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(PathReader());
        await using var sc = context.CreateSqlContainer("SELECT id, path FROM t");
        await using var reader = await sc.ExecuteReaderAsync();

        var rows = await DataReaderMapper.LoadAsync<PathRow>(reader, new MapperOptions(Strict: true));

        Assert.Equal(HierarchyId.Parse("/1/2/"), Assert.Single(rows).Path);
    }

    [Fact]
    public async Task RetrieveOneAsync_OtherClrTypeWithoutItsAssembly_StillThrowsDataMappingException()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["path"] = new byte[] { 0xE6, 0x10 } }
        })
        {
            UnloadableUdtColumns = new Dictionary<string, string> { ["path"] = "master.sys.geometry" }
        });
        var gateway = new TableGateway<PathRow, int>(context);

        await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));
    }

    [Fact]
    public async Task CreateAsync_HierarchyIdProperty_BindsItsTextForm()
    {
        var (context, _) = Context();
        await using var __ = context;
        var gateway = new TableGateway<PathRow, int>(context);

        await using var sc = gateway.BuildCreate(new PathRow { Id = 1, Path = HierarchyId.Parse("/3/4.5/") });

        Assert.Equal("/3/4.5/", sc.GetParameterValue("i1"));
    }

    private static fakeDbDataReader PathReader(object? value = null) =>
        new(new[] { new Dictionary<string, object> { ["id"] = 1, ["path"] = value ?? Path12, ["path_text"] = value ?? Path12 } })
        {
            UnloadableUdtColumns = new Dictionary<string, string>
            {
                ["path"] = "master.sys.hierarchyid",
                ["path_text"] = "master.sys.hierarchyid"
            }
        };

    private static (DatabaseContext Context, fakeDbConnection Exec) Context()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer };
        factory.Connections.Add(exec);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;Database=y;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard
        }, factory);
        return (context, exec);
    }

    // Stands in for Microsoft.SqlServer.Types.SqlHierarchyId, which SqlClient returns when that
    // assembly is loaded; recognized by name, like the other provider types.
    private sealed class SqlHierarchyId
    {
        private readonly string _text;
        public SqlHierarchyId(string text) => _text = text;
        public override string ToString() => _text;
    }

    [Table("t")]
    private sealed class PathRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("path", DbType.String)] public HierarchyId? Path { get; set; }
        [Column("path_text", DbType.String)] public string? PathText { get; set; }
    }
}
