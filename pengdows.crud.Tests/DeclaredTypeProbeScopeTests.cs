using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The declared database type of a column is learned per column, not per table name. Two row types can share a table name
/// (the fidelity cells all write one table): a column learned for the first must not stop the second's columns being probed,
/// or the second's LOB and string parameters are bound with no declared type (staging then fails on Informix).
/// </summary>
public class DeclaredTypeProbeScopeTests
{
    [Table("scope_t")]
    public sealed class FirstRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("payload", DbType.Binary)] public byte[] Payload { get; set; } = [];
    }

    [Table("scope_t")]
    public sealed class SecondRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("notes", DbType.String)] public string Notes { get; set; } = "";
    }

    [Fact]
    public void AColumnLearnedForOneRowTypeDoesNotStopAnotherRowTypeOnTheSameTableBeingProbed()
    {
        var dialect = new InformixDialect(new fakeDbFactory(SupportedDatabase.Informix), NullLogger<InformixDialect>.Instance);
        var first = new TypeMapRegistry().GetTableInfo<FirstRow>().Columns["payload"];
        var second = new TypeMapRegistry().GetTableInfo<SecondRow>().Columns["notes"];
        dialect.RecordDeclaredTypes(new[] { (first, "BLOB") });

        Assert.True(dialect.HasProbedDeclaredTypes(new[] { first }));
        Assert.False(dialect.HasProbedDeclaredTypes(new[] { second }));
    }

    [Fact]
    public void AColumnWithNoNeededDeclaredTypeNeedsNoProbe()
    {
        var dialect = new InformixDialect(new fakeDbFactory(SupportedDatabase.Informix), NullLogger<InformixDialect>.Instance);

        Assert.True(dialect.HasProbedDeclaredTypes(Enumerable.Empty<IColumnInfo>()));
    }

    // A column the probe asked about and the metadata did not name is still probed: marking it probed means it is not asked again,
    // so a denied or empty metadata query runs once per dialect, not once per gateway.
    [Fact]
    public void AColumnMarkedProbedWithNoDeclaredTypeIsNotProbedAgain()
    {
        var dialect = new InformixDialect(new fakeDbFactory(SupportedDatabase.Informix), NullLogger<InformixDialect>.Instance);
        var second = new TypeMapRegistry().GetTableInfo<SecondRow>().Columns["notes"];

        dialect.MarkDeclaredTypesProbed(new[] { second });

        Assert.True(dialect.HasProbedDeclaredTypes(new[] { second }));
        Assert.Null(dialect.DeclaredTypeOf(second));
    }

    // The declared-type cache is shared by every gateway on a dialect. Concurrent learners and readers must neither throw nor lose
    // a learned type.
    [Fact]
    public void ConcurrentLearnersAndReadersOfTheDeclaredTypeCacheAgree()
    {
        var dialect = new InformixDialect(new fakeDbFactory(SupportedDatabase.Informix), NullLogger<InformixDialect>.Instance);
        var first = new TypeMapRegistry().GetTableInfo<FirstRow>().Columns["payload"];
        var second = new TypeMapRegistry().GetTableInfo<SecondRow>().Columns["notes"];
        var columns = new[] { first, second };

        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < 1000; i++)
            {
                foreach (var column in columns)
                {
                    dialect.RecordDeclaredTypes(new[] { (column, "BLOB") });
                    dialect.MarkDeclaredTypesProbed(new[] { column });
                    Assert.True(dialect.HasProbedDeclaredTypes(new[] { column }));
                }
            }
        });

        Assert.All(columns, column => Assert.Equal("BLOB", dialect.DeclaredTypeOf(column)));
    }
}
