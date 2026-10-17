using System;
using System.Threading.Tasks;
using pengdows.crud.tenant;

namespace pengdows.crud.Tests;

// The members a test scripts on the generated stubs (the generator leaves them out; see tools/test-stub-generator/README.md).

internal partial class ScriptedSqlDialect
{
    /// <summary>What <c>WrapObjectName</c> returns for a name; unset means unscripted.</summary>
    public Func<string, string>? WrapObjectNameFunc { get; set; }

    public bool SupportsOffsetFetch { get; set; }
    public bool SupportsLimitOffset { get; set; }
    public bool SupportsSavepoints { get; set; }

    public string WrapObjectName(string name) =>
        (WrapObjectNameFunc ?? throw Unscripted(nameof(WrapObjectName)))(name);
}

internal partial class ScriptedDataSourceInformation
{
    public string DatabaseProductVersion { get; set; } = "";
}

internal partial class ScriptedDatabaseContext
{
    public int DisposeCount { get; private set; }
    public int DisposeAsyncCount { get; private set; }

    public bool IsDisposed => DisposeCount + DisposeAsyncCount > 0;

    /// <summary>Thrown by Dispose when set (after it is counted).</summary>
    public Exception? DisposeException { get; set; }

    /// <summary>Thrown by DisposeAsync when set (after it is counted).</summary>
    public Exception? DisposeAsyncException { get; set; }

    public void Dispose()
    {
        DisposeCount++;
        if (DisposeException != null)
        {
            throw DisposeException;
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeAsyncCount++;
        if (DisposeAsyncException != null)
        {
            throw DisposeAsyncException;
        }

        return ValueTask.CompletedTask;
    }
}

internal partial class ScriptedTenantContextRegistry
{
    /// <summary>What <c>GetContext</c> returns for a tenant; unset means unscripted.</summary>
    public Func<string, IDatabaseContext>? ContextFactory { get; set; }

    public IDatabaseContext GetContext(string tenant) =>
        (ContextFactory ?? throw Unscripted(nameof(GetContext)))(tenant);
}

internal partial class ScriptedAuditValues
{
    public object UserId { get; init; } = null!;
}

internal partial class ScriptedColumnInfo
{
    public string Name { get; set; } = "";
    public bool IsJsonType { get; set; }
}

internal partial class ScriptedInternalSqlDialect
{
    public string QuotePrefix { get; set; } = "\"";
    public string QuoteSuffix { get; set; } = "\"";
    public string ParameterMarker { get; set; } = "@";

    /// <summary>What <c>MakeParameterName(string)</c> returns; unset means unscripted.</summary>
    public Func<string, string>? MakeParameterNameFunc { get; set; }

    public string MakeParameterName(string parameterName) =>
        (MakeParameterNameFunc ?? throw Unscripted(nameof(MakeParameterName)))(parameterName);

    public string MakeParameterName(System.Data.Common.DbParameter dbParameter) =>
        throw Unscripted(nameof(MakeParameterName));
}

/// <summary>One call to a scripted batch member: which one, and the arguments it received.</summary>
internal sealed record BatchCall(string Member, object Entities, object? Context, System.Threading.CancellationToken Token);

internal partial class ScriptedTableGateway<TEntity, TRowID>
{
    /// <summary>What each batch member returns, keyed by member: BatchCreate, BatchDeleteIds, BatchDeleteEntities, BatchUpdate, BatchUpsert.</summary>
    public System.Collections.Generic.Dictionary<string, int> Results { get; } = new();

    /// <summary>Every call to a batch member, in order.</summary>
    public System.Collections.Generic.List<BatchCall> Calls { get; } = new();

    private ValueTask<int> Batch(string member, object entities, IDatabaseContext? context,
        System.Threading.CancellationToken token)
    {
        Calls.Add(new BatchCall(member, entities, context, token));
        return new ValueTask<int>(Results.TryGetValue(member, out var result) ? result : throw Unscripted(member));
    }

    public ValueTask<int> BatchCreateAsync(System.Collections.Generic.IReadOnlyList<TEntity> entities,
        IDatabaseContext? context = null, System.Threading.CancellationToken cancellationToken = default) =>
        Batch("BatchCreate", entities, context, cancellationToken);

    public ValueTask<int> BatchDeleteAsync(System.Collections.Generic.IEnumerable<TRowID> ids,
        IDatabaseContext? context = null, System.Threading.CancellationToken cancellationToken = default) =>
        Batch("BatchDeleteIds", ids, context, cancellationToken);

    public ValueTask<int> BatchDeleteAsync(System.Collections.Generic.IReadOnlyCollection<TEntity> entities,
        IDatabaseContext? context = null, System.Threading.CancellationToken cancellationToken = default) =>
        Batch("BatchDeleteEntities", entities, context, cancellationToken);

    public ValueTask<int> BatchUpdateAsync(System.Collections.Generic.IReadOnlyList<TEntity> entities,
        IDatabaseContext? context = null, System.Threading.CancellationToken cancellationToken = default) =>
        Batch("BatchUpdate", entities, context, cancellationToken);

    public ValueTask<int> BatchUpsertAsync(System.Collections.Generic.IReadOnlyList<TEntity> entities,
        IDatabaseContext? context = null, System.Threading.CancellationToken cancellationToken = default) =>
        Batch("BatchUpsert", entities, context, cancellationToken);
}
