using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using Xunit;

namespace pengdows.crud.Tests.exceptions;

/// <summary>
/// Verifies that TypeMapRegistry throws SqlGenerationException (not InvalidOperationException)
/// for all entity metadata validation errors that represent programmer mistakes.
/// </summary>
public class SqlGenerationExceptionWiringTests
{
    private static SqlGenerationException RegisterAndExpect<T>()
    {
        var reg = new TypeMapRegistry();
        return Assert.Throws<SqlGenerationException>(() => reg.Register<T>());
    }

    // -------------------------------------------------------------------------
    // Missing / empty [Table]
    // -------------------------------------------------------------------------

    private class NoTableAttr
    {
        [Id(false)]
        [Column("id", DbType.Int32)] public int Id { get; set; }
    }

    [Fact]
    public void MissingTableAttribute_Throws_SqlGenerationException()
    {
        var ex = RegisterAndExpect<NoTableAttr>();
        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
        Assert.Contains("NoTableAttr", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Table("")]
    private class EmptyTableName
    {
        [Id(false)]
        [Column("id", DbType.Int32)] public int Id { get; set; }
    }

    [Fact]
    public void EmptyTableName_Throws_SqlGenerationException()
    {
        var ex = RegisterAndExpect<EmptyTableName>();
        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
        Assert.Contains("TableAttribute.Name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Empty [Column] name
    // -------------------------------------------------------------------------

    [Table("t")]
    private class EmptyColumnName
    {
        [Id(false)]
        [Column("", DbType.Int32)] public int Id { get; set; }
    }

    [Fact]
    public void EmptyColumnName_Throws_SqlGenerationException()
    {
        var ex = RegisterAndExpect<EmptyColumnName>();
        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
        Assert.Contains("ColumnAttribute.Name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Enum with invalid DbType
    // -------------------------------------------------------------------------

    private enum SampleEnum { A, B }

    [Table("t")]
    private class EnumBadDbType
    {
        [Id(false)]
        [Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("e", DbType.DateTime)]
        public SampleEnum Value { get; set; }
    }

    [Fact]
    public void EnumWithInvalidDbType_Throws_SqlGenerationException()
    {
        var ex = RegisterAndExpect<EnumBadDbType>();
        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
        Assert.Contains("must use string or numeric DbType", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Duplicate [Column] name
    // -------------------------------------------------------------------------

    [Table("t")]
    private class DuplicateColumnName
    {
        [Id(false)]
        [Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String)] public string Name1 { get; set; } = "";
        [Column("name", DbType.String)] public string Name2 { get; set; } = "";
    }

    [Fact]
    public void DuplicateColumnName_Throws_SqlGenerationException()
    {
        var ex = RegisterAndExpect<DuplicateColumnName>();
        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
        Assert.Contains("Duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // No [Id] and no [PrimaryKey]
    // -------------------------------------------------------------------------

    [Table("t")]
    private class NoIdNoPrimaryKey
    {
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    [Fact]
    public void NoIdAndNoPrimaryKey_Throws_SqlGenerationException()
    {
        var ex = RegisterAndExpect<NoIdNoPrimaryKey>();
        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
        Assert.Contains("must define either", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
