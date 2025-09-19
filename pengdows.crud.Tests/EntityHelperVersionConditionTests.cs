#region

using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperVersionConditionTests : SqlLiteContextTestBase
{
    [Table("VerNull")] private sealed class VerNullEntity
    {
        [Id(writable: false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;
        [Version] [Column("Version", DbType.Int32)] public int? Version { get; set; }
    }

    [Table("VerInt")] private sealed class VerIntEntity
    {
        [Id(writable: false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;
        [Version] [Column("Version", DbType.Int32)] public int Version { get; set; }
    }

    public EntityHelperVersionConditionTests()
    {
        TypeMap.Register<VerNullEntity>();
        TypeMap.Register<VerIntEntity>();

        // Create tables
        var qp = Context.QuotePrefix; var qs = Context.QuoteSuffix;
        var createNull = $@"CREATE TABLE IF NOT EXISTS {qp}VerNull{qs}(
            {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}Version{qs} INTEGER NULL
        )";
        Context.CreateSqlContainer(createNull).ExecuteNonQueryAsync().GetAwaiter().GetResult();

        var createInt = $@"CREATE TABLE IF NOT EXISTS {qp}VerInt{qs}(
            {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}Version{qs} INTEGER NOT NULL DEFAULT 0
        )";
        Context.CreateSqlContainer(createInt).ExecuteNonQueryAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task BuildUpdate_WithNullableVersionNull_AppendsIsNullCondition()
    {
        var helper = new EntityHelper<VerNullEntity, int>(Context, AuditValueResolver);
        var e = new VerNullEntity { Id = 1, Name = "n", Version = null };
        var sc = await helper.BuildUpdateAsync(e, loadOriginal: false);
        var sql = sc.Query.ToString();

        Assert.Contains("WHERE", sql);
        Assert.Contains("\"Version\" IS NULL", sql);
    }

    [Fact]
    public async Task BuildUpdate_WithVersionValue_AppendsParamAndIncrements()
    {
        var helper = new EntityHelper<VerIntEntity, int>(Context, AuditValueResolver);
        var e = new VerIntEntity { Id = 1, Name = "n", Version = 5 };
        var sc = await helper.BuildUpdateAsync(e, loadOriginal: false);
        var sql = sc.Query.ToString();

        // WHERE Id = @param... AND Version = @param...
        Assert.Matches(@"WHERE.*AND.*\""Version""\s*=\s*@\w+", sql);
        // SET Version = Version + 1
        Assert.Contains("\"Version\" = \"Version\" + 1", sql);
    }
}
