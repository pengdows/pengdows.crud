using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class InterBaseTranslatorTests
{
    private readonly InterBaseExceptionTranslator _translator = new();

    [Fact]
    public void ErrorCode335544665_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new NumberedDbException(335544665, "violation of PRIMARY or UNIQUE KEY constraint");

        var result = _translator.Translate(SupportedDatabase.InterBase, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.InterBase, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void ErrorCode335544466_MapsTo_ForeignKeyViolationException_OnInsert()
    {
        var raw = new NumberedDbException(335544466, "violation of FOREIGN KEY constraint, missing parent");

        var result = _translator.Translate(SupportedDatabase.InterBase, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void ErrorCode335544466_MapsTo_ForeignKeyViolationException_OnDelete()
    {
        var raw = new NumberedDbException(335544466, "violation of FOREIGN KEY constraint, referencing child rows");

        var result = _translator.Translate(SupportedDatabase.InterBase, raw, DbOperationKind.Delete);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void ErrorCode335544347_MapsTo_NotNullViolationException()
    {
        var raw = new NumberedDbException(335544347, "validation error for column NAME, value \"*** null ***\"");

        var result = _translator.Translate(SupportedDatabase.InterBase, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void ErrorCode335544558_MapsTo_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(335544558, "operation violates CHECK constraint");

        var result = _translator.Translate(SupportedDatabase.InterBase, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    // Unlike 3.0 (which delegates unmatched exceptions to dialect.ClassifyException's generic
    // message-keyword fallback), this branch's translators independently re-derive every
    // classification from explicit numeric/SQLSTATE codes only - see every other translator in
    // this project (Db2/Informix/Hana/SqlServer/Oracle/etc.), none of which do message-text
    // heuristics either. A message containing "deadlock" with no matching error code correctly
    // falls through to the generic DatabaseOperationException fallback here, consistent with
    // that pattern - not a gap, a deliberate branch-wide convention.
    [Fact]
    public void UnknownError_MapsTo_GenericDatabaseOperationException()
    {
        var raw = new NumberedDbException(99999, "some unrecognized InterBase failure");

        var result = _translator.Translate(SupportedDatabase.InterBase, raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Fact]
    public void Registry_Routes_InterBase_To_InterBaseExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<InterBaseExceptionTranslator>(registry.Get(SupportedDatabase.InterBase));
    }
}
