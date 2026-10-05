using System.Data;

namespace pengdows.crud.fakeDb;


/// <summary>
/// A single bound parameter, captured by value at command-execution time.
/// </summary>
public sealed record CapturedParameter(string Name, object? Value)
{
    /// <summary>
    /// The parameter's DbType when the command executed (what a real provider would bind it as).
    /// </summary>
    public DbType DbType { get; init; }

    /// <summary>
    /// The provider-specific type a dialect stamped on the parameter (for example an emulated
    /// <c>IfxType</c>, see <see cref="fakeDbInformixParameter"/>), by name; null when none was set.
    /// </summary>
    public string? ProviderType { get; init; }
}

/// <summary>
/// Command text paired with the parameters bound to it at the moment it executed — see
/// <see cref="fakeDbConnection.ExecutedNonQueryCommands"/> and
/// <see cref="fakeDbConnection.ExecutedReaderCommands"/>.
/// </summary>
public sealed record CapturedCommand(string CommandText, IReadOnlyList<CapturedParameter> Parameters);
