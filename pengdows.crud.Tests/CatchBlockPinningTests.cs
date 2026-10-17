using System;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The catch blocks that swallow a failure on purpose (CATCH-001), each pinned so that removing one turns a test red.
/// </summary>
[Collection("TypeRegistry")]
public sealed class CatchBlockPinningTests
{
    private enum Color
    {
        Red = 1
    }

    private sealed class Payload
    {
        public int Id { get; set; }
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("the logging sink is broken");
    }

    private static T WithThrowingLogger<T>(Func<T> action)
    {
        var original = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = new ThrowingLogger();
            return action();
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    [Fact]
    public void EnumFailureWarning_BrokenLoggingSink_DoesNotBreakTheRead()
    {
        WithThrowingLogger(() =>
        {
            TypeCoercionHelper.LogEnumFailure(typeof(Color));
            return 0;
        });
    }

    [Fact]
    public void BadJsonPayload_BrokenLoggingSink_StillThrowsTheJsonException()
    {
        var method = typeof(TypeCoercionHelper).GetMethod("CoerceJsonValue", BindingFlags.NonPublic | BindingFlags.Static)!;

        var ex = Assert.Throws<TargetInvocationException>(() => WithThrowingLogger(() =>
            method.Invoke(null, new object?[] { "{not json", typeof(Payload), TypeCoercionOptions.Default })));

        Assert.IsType<JsonException>(ex.InnerException);
    }

    private const string Malformed = "=nokey;Data Source=a";

    [Fact]
    public void NonKeyValueConnectionString_IsItsOwnDataSource()
    {
        var method = typeof(DatabaseContext).GetMethod("TryGetDataSourcePath", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(Malformed, method.Invoke(null, new object?[] { Malformed }));
    }

    [Fact]
    public void MalformedConnectionString_HasNoApplicationName()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var context = new DatabaseContext("Server=a;Database=b;EmulatedProduct=SqlServer", factory);
        var method = typeof(DatabaseContext).GetMethod("ExtractApplicationName", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Null(method.Invoke(context, new object?[] { Malformed }));
    }
}
