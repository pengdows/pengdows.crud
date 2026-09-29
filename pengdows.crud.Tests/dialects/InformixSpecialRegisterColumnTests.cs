using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// CONFIRMED live (Informix 15.0.1.0.3, DELIMIDENT): an UNQUALIFIED quoted identifier that names a
/// special register - "user", "today", "current", "sitename", "dbservername", "current_user" -
/// resolves to the register, not the column, wherever it appears in an expression. So
/// <c>DELETE FROM "t" WHERE "user" = 'informix'</c> compared the session user name and deleted every
/// row. A table-qualified reference (<c>"t"."user"</c>) resolves to the column. Column lists (INSERT
/// column list, UPDATE SET targets) are not expressions and are unaffected.
/// Every column reference the gateways emit inside an expression must therefore be qualified on
/// Informix.
/// </summary>
public class InformixSpecialRegisterColumnTests
{
    private static readonly string[] Registers = { "user", "today", "current", "sitename" };

    private static DatabaseContext CreateContext() =>
        new("Data Source=test;EmulatedProduct=Informix", new fakeDbFactory(SupportedDatabase.Informix));

    // After WHERE, every register-named identifier must be qualified ("x"."user"); anywhere, an
    // expression right-hand side ("= \"user\"") must be qualified. SET targets / column lists are fine.
    private static void AssertNoUnqualifiedRegisterReference(string sql)
    {
        foreach (var register in Registers)
        {
            var id = Regex.Escape($"\"{register}\"");
            var whereIndex = sql.IndexOf(" WHERE ", StringComparison.Ordinal);
            if (whereIndex >= 0)
            {
                var predicate = sql[whereIndex..];
                Assert.False(Regex.IsMatch(predicate, $@"(?<!\.){id}"),
                    $"Unqualified {register} in predicate: {sql}");
            }

            Assert.False(Regex.IsMatch(sql, $@"=\s*(?<!\.){id}"),
                $"Unqualified {register} on an expression right-hand side: {sql}");

            // A SELECT list reads each column; "AS \"x\"" output aliases are names, not references.
            foreach (Match select in Regex.Matches(sql, @"SELECT (.*?)\s+FROM ", RegexOptions.Singleline))
            {
                Assert.False(Regex.IsMatch(select.Groups[1].Value, $@"(?<!\.)(?<!AS ){id}"),
                    $"Unqualified {register} in SELECT list: {sql}");
            }
        }
    }

    public static IEnumerable<object[]> RowIdGatewayStatements()
    {
        var entity = new HostileRowIdEntity { User = "u1", Today = 1, Current = 1, SiteName = "s" };
        yield return new object[] { "BuildDelete", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(g.BuildDelete("u1").Query.ToString())) };
        yield return new object[] { "BuildUpdateAsync", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(async g => (await g.BuildUpdateAsync(entity, loadOriginal: false)).Query.ToString()) };
        yield return new object[] { "BuildRetrieve(ids)", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(g.BuildRetrieve(new[] { "u1", "u2" }, "a").Query.ToString())) };
        yield return new object[] { "BuildRetrieve(entities)", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(g.BuildRetrieve(new[] { entity }, "a").Query.ToString())) };
        yield return new object[] { "BuildRetrieve(one id, no alias)", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(g.BuildRetrieve(new[] { "u1" }).Query.ToString())) };
        yield return new object[] { "BuildRetrieve(ids, no alias)", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(g.BuildRetrieve(new[] { "u1", "u2" }).Query.ToString())) };
        yield return new object[] { "BuildRetrieve(entities, no alias)", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(g.BuildRetrieve(new[] { entity }, "").Query.ToString())) };
        yield return new object[] { "BuildBaseRetrieve(no alias)", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(g.BuildBaseRetrieve("").Query.ToString())) };
        yield return new object[] { "BuildBatchDelete(ids)", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(string.Join("\n", g.BuildBatchDelete(new[] { "u1", "u2" }).Select(c => c.Query.ToString())))) };
        yield return new object[] { "BuildBatchDelete(entities)", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(string.Join("\n", g.BuildBatchDelete(new[] { entity }).Select(c => c.Query.ToString())))) };
        yield return new object[] { "BuildBatchUpdate", (Func<TableGateway<HostileRowIdEntity, string>, Task<string>>)(g => Task.FromResult(string.Join("\n", g.BuildBatchUpdate(new[] { entity }).Select(c => c.Query.ToString())))) };
    }

    [Theory]
    [MemberData(nameof(RowIdGatewayStatements))]
    public async Task TableGateway_QualifiesRegisterNamedColumnsInExpressions(string statement,
        Func<TableGateway<HostileRowIdEntity, string>, Task<string>> build)
    {
        var gateway = new TableGateway<HostileRowIdEntity, string>(CreateContext());

        var sql = await build(gateway);

        Assert.False(string.IsNullOrWhiteSpace(sql), statement);
        AssertNoUnqualifiedRegisterReference(sql);
    }

    public static IEnumerable<object[]> PrimaryKeyGatewayStatements()
    {
        var entity = new HostilePkEntity { User = "u1", Today = 1, Current = 1, SiteName = "s" };
        yield return new object[] { "BuildUpdateAsync", (Func<PrimaryKeyTableGateway<HostilePkEntity>, Task<string>>)(async g => (await g.BuildUpdateAsync(entity)).Query.ToString()) };
        yield return new object[] { "BuildRetrieve", (Func<PrimaryKeyTableGateway<HostilePkEntity>, Task<string>>)(g => Task.FromResult(g.BuildRetrieve(new[] { entity }, "a").Query.ToString())) };
        yield return new object[] { "BuildRetrieve(no alias)", (Func<PrimaryKeyTableGateway<HostilePkEntity>, Task<string>>)(g => Task.FromResult(g.BuildRetrieve(new[] { entity }, "").Query.ToString())) };
        yield return new object[] { "BuildBatchDelete", (Func<PrimaryKeyTableGateway<HostilePkEntity>, Task<string>>)(g => Task.FromResult(string.Join("\n", g.BuildBatchDelete(new[] { entity }).Select(c => c.Query.ToString())))) };
        yield return new object[] { "BuildBatchUpdate", (Func<PrimaryKeyTableGateway<HostilePkEntity>, Task<string>>)(g => Task.FromResult(string.Join("\n", g.BuildBatchUpdate(new[] { entity }).Select(c => c.Query.ToString())))) };
    }

    [Theory]
    [MemberData(nameof(PrimaryKeyGatewayStatements))]
    public async Task PrimaryKeyTableGateway_QualifiesRegisterNamedColumnsInExpressions(string statement,
        Func<PrimaryKeyTableGateway<HostilePkEntity>, Task<string>> build)
    {
        var gateway = new PrimaryKeyTableGateway<HostilePkEntity>(CreateContext());

        var sql = await build(gateway);

        Assert.False(string.IsNullOrWhiteSpace(sql), statement);
        AssertNoUnqualifiedRegisterReference(sql);
    }

    [Fact]
    public async Task CountHelpers_QualifyUnqualifiedColumnArguments()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Informix", factory);
        var gateway = new TableGateway<HostileRowIdEntity, string>(context);

        await gateway.CountWhereAsync("user", "u1");
        await gateway.CountWhereNullAsync("user");
        await gateway.CountWhereEqualsAsync("user", "u1", andWhereNull: "sitename");
        await gateway.CountWhereEqualsAsync("user", "u1", andWhereNotNull: "today");

        // Scalars are served through the reader path; skip the context's own version probe.
        var texts = factory.CreatedConnections
            .SelectMany(c => c.ExecutedReaderTexts.Concat(c.ExecutedScalarTexts))
            .Where(t => t.Contains("COUNT(*)", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(4, texts.Count);
        Assert.All(texts, AssertNoUnqualifiedRegisterReference);
    }

    [Table("hostile_rowid")]
    public class HostileRowIdEntity
    {
        [Id(true)]
        [Column("user", DbType.String)]
        public string User { get; set; } = string.Empty;

        [PrimaryKey(1)]
        [Column("today", DbType.Int32)]
        public int Today { get; set; }

        [Version]
        [Column("current", DbType.Int32)]
        public int Current { get; set; }

        [Column("sitename", DbType.String)]
        public string SiteName { get; set; } = string.Empty;
    }

    [Table("hostile_pk")]
    public class HostilePkEntity
    {
        [PrimaryKey(1)]
        [Column("user", DbType.String)]
        public string User { get; set; } = string.Empty;

        [PrimaryKey(2)]
        [Column("today", DbType.Int32)]
        public int Today { get; set; }

        [Version]
        [Column("current", DbType.Int32)]
        public int Current { get; set; }

        [Column("sitename", DbType.String)]
        public string SiteName { get; set; } = string.Empty;
    }
}
