using System;
using System.Reflection;
using AdoNetCore.AseClient;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class SybaseTranslatorTests
{
    private readonly SybaseExceptionTranslator _translator = new();

    // AseError's MessageNumber/Message setters are non-public (real driver code populates them
    // internally when parsing a TDS error token), so tests fill them via reflection to build a
    // realistic AseException without needing a live ASE connection.
    private static AseException Ase(int messageNumber, string message)
    {
        var error = new AseError();
        SetProperty(error, nameof(AseError.MessageNumber), messageNumber);
        SetProperty(error, nameof(AseError.Message), message);
        return new AseException(new[] { error });
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        var setter = property!.GetSetMethod(nonPublic: true);
        setter!.Invoke(target, new[] { value });
    }

    [Fact]
    public void AseError2601_MapsTo_UniqueConstraintViolationException()
    {
        var raw = Ase(2601, "Attempt to insert duplicate key row in object 'parent_t' with unique index 'ux'");

        var result = _translator.Translate(SupportedDatabase.Sybase, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Sybase, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void AseError546_MapsTo_ForeignKeyViolationException()
    {
        var raw = Ase(546, "Foreign key constraint violation occurred, dbname = 'testdb', table name = 'child_t', constraint name = 'fk1'.");

        var result = _translator.Translate(SupportedDatabase.Sybase, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void AseError548_MapsTo_CheckConstraintViolationException()
    {
        var raw = Ase(548, "Check constraint violation occurred, dbname = 'testdb', table name = 'parent_t', constraint name = 'ck1'.");

        var result = _translator.Translate(SupportedDatabase.Sybase, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void AseError233_MapsTo_NotNullViolationException()
    {
        var raw = Ase(233, "The column name in table dbo.parent_t does not allow null values.");

        var result = _translator.Translate(SupportedDatabase.Sybase, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void AseError1205_MapsTo_DeadlockException()
    {
        var raw = Ase(1205, "Your server command encountered a deadlock situation. Please re-run your command.");

        var result = _translator.Translate(SupportedDatabase.Sybase, raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
    }

    [Fact]
    public void UnknownAseError_MapsTo_DatabaseOperationException()
    {
        var raw = Ase(99999, "some unrecognized ASE failure");

        var result = _translator.Translate(SupportedDatabase.Sybase, raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Fact]
    public void Timeout_ByLooksLikeTimeout_Maps_CommandTimeoutException()
    {
        var raw = new TimeoutException("wait for lock expired");

        var result = _translator.Translate(SupportedDatabase.Sybase, raw, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public void Registry_Routes_Sybase_To_SybaseExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<SybaseExceptionTranslator>(registry.Get(SupportedDatabase.Sybase));
    }
}
