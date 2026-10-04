using System;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-022: per-execution bookkeeping. Every reader got a new read-failure translator (a closure over the container) and a
/// new unreadable-value predicate (a method-group delegate over the dialect). Both depend only on
/// the dialect and the operation kind, so they are made once per dialect. Every container kept a
/// dictionary from each bound parameter to the command collection holding it, though every
/// parameter of one execution goes into the same collection.
/// </summary>
public class ReaderDelegateAllocationTests
{
    private static object? Field(ITrackedReader reader, string name) =>
        typeof(TrackedReader).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reader);

    [Fact]
    public async Task ReadersFromOneDialect_ShareTheirFailureDelegates()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory);

        async Task<(object? Translator, object? Unreadable)> Open()
        {
            await using var sc = context.CreateSqlContainer("SELECT 1");
            await using var reader = await sc.ExecuteReaderAsync();
            return (Field(reader, "_readFailureTranslator"), Field(reader, "_isUnreadableStoredValue"));
        }

        var first = await Open();
        var second = await Open();

        Assert.NotNull(first.Translator);
        Assert.NotNull(first.Unreadable);
        Assert.Same(first.Translator, second.Translator);
        Assert.Same(first.Unreadable, second.Unreadable);
    }

    [Fact]
    public void BindingParametersIntoACommand_AllocatesNoBookkeeping()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var bind = typeof(SqlContainer).GetMethod("AddParametersToCommand", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<Action<SqlContainer, System.Data.Common.DbCommand>>();
        var command = factory.CreateCommand()!;

        SqlContainer Container()
        {
            var sc = (SqlContainer)context.CreateSqlContainer("INSERT INTO t (a, b, c, d, e) VALUES (@a, @b, @c, @d, @e)");
            foreach (var name in new[] { "a", "b", "c", "d", "e" })
            {
                sc.AddParameterWithValue(name, System.Data.DbType.Int32, 1);
            }

            return sc;
        }

        var warm = Container();
        bind(warm, command);
        command.Parameters.Clear();
        var containers = new SqlContainer[50];
        for (var i = 0; i < containers.Length; i++) containers[i] = Container();

        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var sc in containers)
        {
            bind(sc, command);
            command.Parameters.Clear();
        }

        var perBind = (GC.GetAllocatedBytesForCurrentThread() - before) / containers.Length;
        Assert.Equal(0, perBind);
    }
}
