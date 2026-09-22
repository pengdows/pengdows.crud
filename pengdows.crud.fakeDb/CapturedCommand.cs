namespace pengdows.crud.fakeDb;

/// <summary>
/// A single bound parameter, captured by value at command-execution time.
/// </summary>
public sealed record CapturedParameter(string Name, object? Value);

/// <summary>
/// Command text paired with the parameters bound to it at the moment it executed — see
/// <see cref="fakeDbConnection.ExecutedNonQueryCommands"/> and
/// <see cref="fakeDbConnection.ExecutedReaderCommands"/>.
/// </summary>
public sealed record CapturedCommand(string CommandText, IReadOnlyList<CapturedParameter> Parameters);
