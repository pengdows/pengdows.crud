using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Found live 2026-09-29 (GEN-001 verification): Informix.Net.Core's IfxDataReader.GetInt64 throws
/// InvalidCastException for a BIGSERIAL column, although GetFieldType reports Int64 and GetValue
/// returns an Int64 (SERIAL8, INT8 and BIGINT are fine). The compiled mapper calls GetInt64 for an
/// Int64 field, so reading any entity keyed by BIGSERIAL (Informix's idiomatic id type) failed. The
/// Informix dialect declares the quirk; the tracked reader then reads Int64 through GetValue.
/// </summary>
public sealed class InformixBigSerialReadTests
{
    [Fact]
    public void FakeDbReader_CanEmulateAProviderThatRejectsGetInt64ForAColumn()
    {
        var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["id"] = 42L } })
        {
            GetInt64RejectedColumns = new HashSet<string> { "id" }
        };
        Assert.True(reader.Read());

        Assert.Throws<InvalidCastException>(() => reader.GetInt64(0));
        Assert.Equal(42L, reader.GetValue(0));
        Assert.Equal(typeof(long), reader.GetFieldType(0));
    }

    [Theory]
    [MemberData(nameof(AllDatabases))]
    public void OnlyInformix_ReadsInt64ThroughGetValue(SupportedDatabase database)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

        Assert.Equal(database == SupportedDatabase.Informix, ((SqlDialect)dialect).ReadsInt64ThroughGetValue);
    }

    public static IEnumerable<object[]> AllDatabases()
    {
        foreach (var value in Enum.GetValues<SupportedDatabase>())
        {
            if (value != SupportedDatabase.Unknown)
            {
                yield return new object[] { value };
            }
        }
    }

    [Fact]
    public async Task RetrieveOneAsync_OnInformix_ReadsABigSerialId()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix });
        // The first gateway call learns the table's declared column types (TYPE-020) on its own connection.
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix };
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 42L, ["name"] = "w" }
        })
        {
            GetInt64RejectedColumns = new HashSet<string> { "id" }
        });
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Database=test;Server=ifx;EmulatedProduct=Informix",
            DbMode = DbMode.Standard
        }, factory);
        var gateway = new TableGateway<Widget, long>(context);

        var widget = await gateway.RetrieveOneAsync(42L);

        Assert.NotNull(widget);
        Assert.Equal(42L, widget!.Id);
        Assert.Equal("w", widget.Name);
    }

    [Table("widgets")]
    private sealed class Widget
    {
        [Id(false)] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}
