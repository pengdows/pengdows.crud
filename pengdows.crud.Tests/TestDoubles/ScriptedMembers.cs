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

    public string ConnectionString { get; set; } = "";

    public pengdows.crud.dialects.ISqlDialect Dialect { get; set; } = null!;
    public bool IsReadOnlyConnection { get; set; }
    public pengdows.crud.enums.SupportedDatabase Product { get; set; }
    public pengdows.crud.enums.ReadWriteMode ReadWriteMode { get; set; } = pengdows.crud.enums.ReadWriteMode.ReadWrite;
    public IDataSourceInformation DataSourceInfo { get; set; } = null!;
    public bool SupportsNamedParameters { get; set; }
    public int MaxParameterLimit { get; set; }
    public string DatabaseProductName { get; set; } = "";
    public pengdows.crud.enums.CommandPrepareMode PrepareMode { get; set; }

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
    public System.Reflection.PropertyInfo PropertyInfo { get; set; } = null!;
}

internal partial class ScriptedTableInfo
{
    public System.Collections.Generic.IReadOnlyDictionary<string, IColumnInfo> Columns { get; set; } =
        new System.Collections.Generic.Dictionary<string, IColumnInfo>();
}

internal partial class ScriptedLocker
{
    public int DisposeAsyncCount { get; private set; }

    /// <summary>Thrown by DisposeAsync when set (after it is counted).</summary>
    public Exception? DisposeAsyncException { get; set; }

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

/// <summary>
/// A record for the binary-read tests: it reports <see cref="BytesReported"/> bytes from <c>GetBytes(.., null, ..)</c> and
/// delivers <see cref="BytesDelivered"/> (fewer, when a provider's reported length is wrong); <c>GetValue</c> throws
/// <see cref="ValueException"/> when set.
/// </summary>
internal partial class ScriptedDataRecord
{
    public bool IsNull { get; set; }
    public long BytesReported { get; set; }
    public byte[] BytesDelivered { get; set; } = Array.Empty<byte>();
    public Exception? ValueException { get; set; }

    public bool IsDBNull(int i) => IsNull;

    public object GetValue(int i) => ValueException != null ? throw ValueException : DBNull.Value;

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
    {
        if (buffer == null)
        {
            return BytesReported;
        }

        var available = Math.Max(0, BytesDelivered.Length - (int)fieldOffset);
        var count = Math.Min(available, length);
        Array.Copy(BytesDelivered, fieldOffset, buffer, bufferoffset, count);
        return count;
    }
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

/// <summary>A scripted database context that also exposes a type map, as the library's own contexts do.</summary>
internal class ScriptedTypeMapDatabaseContext : ScriptedDatabaseContext, pengdows.crud.@internal.ITypeMapAccessor
{
    public ITypeMapRegistry TypeMapRegistry { get; set; } = null!;
}

/// <summary>A scripted context that is also a dialect provider, as the library's own contexts are.</summary>
internal sealed class ScriptedDialectProviderDatabaseContext : ScriptedDatabaseContext, pengdows.crud.dialects.ISqlDialectProvider
{
}

/// <summary>A scripted type-mapped context that also carries the identity the library's contexts have.</summary>
internal sealed class ScriptedIdentityDatabaseContext : ScriptedTypeMapDatabaseContext, IContextIdentity
{
    public Guid RootId { get; set; } = Guid.NewGuid();
}

/// <summary>An <see cref="IAuditValueResolver"/> that returns fixed values and counts how often it is asked.</summary>
internal sealed class CountingAuditValueResolver : IAuditValueResolver
{
    private readonly IAuditValues _values;

    public CountingAuditValueResolver(IAuditValues values) => _values = values;

    public int ResolveCount { get; private set; }

    public IAuditValues Resolve()
    {
        ResolveCount++;
        return _values;
    }
}
