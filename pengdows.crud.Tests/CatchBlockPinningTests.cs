using System;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
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
            pengdows.crud.types.coercion.EnumCoercion.LogEnumFailure(typeof(Color));
            return 0;
        });
    }

    private sealed class CapturingTraceListener : TraceListener
    {
        public StringBuilder Text { get; } = new();

        public override void Write(string? message) => Text.Append(message);

        public override void WriteLine(string? message) => Text.AppendLine(message);

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id,
            string? message) => Text.AppendLine(message);
    }

    [Fact]
    public void BrokenLoggingSink_FailureIsTraced_NotSwallowedSilently()
    {
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            WithThrowingLogger(() =>
            {
                pengdows.crud.types.coercion.EnumCoercion.LogEnumFailure(typeof(Color));
                return 0;
            });
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.Contains("the logging sink is broken", listener.Text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BadJsonPayload_BrokenLoggingSink_StillThrowsTheJsonException()
    {
        var method = typeof(pengdows.crud.types.coercion.JsonCoercion).GetMethod("CoerceJsonValue", BindingFlags.NonPublic | BindingFlags.Static)!;

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

    // ---- the four catches no earlier test reached -------------------------------------------------------------------------

    [Fact]
    public void FailedInitialization_ADisposeThatThrows_DoesNotMaskTheOriginalFailure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.PostgreSql,
            ConnectionString = "Host=x;Database=y;EmulatedProduct=PostgreSql"
        };
        connection.SetFailOnDispose(new InvalidOperationException("dispose of the init connection failed"));
        connection.SetFailOnOpen();
        factory.Connections.Add(connection);

        var ex = Record.Exception(() => new DatabaseContext("Host=x;Database=y;EmulatedProduct=PostgreSql", factory));

        Assert.NotNull(ex);
        Assert.DoesNotContain("dispose of the init connection failed", ex!.ToString(), StringComparison.Ordinal);
    }

    private sealed class ClearRefusingCommand : fakeDbCommand
    {
        protected override DbConnection? DbConnection
        {
            get => base.DbConnection;
            set
            {
                if (value == null)
                {
                    throw new InvalidOperationException("this provider refuses Connection = null on a used command");
                }

                base.DbConnection = value;
            }
        }
    }

    [Fact]
    public async Task ACommandThatRefusesToClearItsConnection_StillExecutesAndIsDisposed()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var commands = new System.Collections.Concurrent.ConcurrentQueue<ClearRefusingCommand>();
        factory.CommandFactory = connection =>
        {
            var created = new ClearRefusingCommand { Connection = connection };
            commands.Enqueue(created);
            return created;
        };
        await using var context = new DatabaseContext("Host=x;Database=y;EmulatedProduct=PostgreSql", factory);
        factory.SetDefaultNonQueryResult(3);

        await using var sc = context.CreateSqlContainer("UPDATE t SET a = 1");
        var rows = await sc.ExecuteNonQueryAsync();

        Assert.Equal(3, rows);
        Assert.NotEmpty(commands);
        Assert.All(commands, c => Assert.True(c.WasDisposed));
    }

    private sealed class RefusingProviderParameter : fakeDbParameter
    {
        public string? DataTypeName { get; set; }

        public int SqlDbType
        {
            get => 0;
            set => throw new InvalidOperationException("this provider refuses SqlDbType in this state");
        }
    }

    [Fact]
    public void CopyingProviderProperties_OnePropertyThatRefusesTheValue_DoesNotStopTheOthers()
    {
        var copy = typeof(SqlContainer).GetMethod("CopyProviderSpecificProperties", BindingFlags.NonPublic | BindingFlags.Static)!;
        var source = new RefusingProviderParameter { DataTypeName = "int4" };
        var target = new RefusingProviderParameter();

        copy.Invoke(null, new object[] { source, target });

        Assert.Equal("int4", target.DataTypeName);
    }

    [Fact]
    public void ProcessNameThatCannotBeInspected_FallsBackToTheLibraryName()
    {
        var read = typeof(DatabaseContext).GetMethod("ReadProcessName", BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (Func<string?> refusing in new Func<string?>[]
                 {
                     () => throw new InvalidOperationException("process has exited"),
                     () => throw new NotSupportedException("remote process"),
                     () => throw new System.ComponentModel.Win32Exception("access denied")
                 })
        {
            Assert.Null(read.Invoke(null, new object[] { refusing }));
        }

        Assert.Equal("worker", read.Invoke(null, new object[] { new Func<string?>(() => " worker ") }));
    }
}
