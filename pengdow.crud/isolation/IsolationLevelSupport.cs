#region

using System.Data;
using pengdow.crud.enums;

#endregion

namespace pengdow.crud.isolation;

public class IsolationLevelSupport
{
    private readonly Dictionary<SupportedDatabase, HashSet<IsolationLevel>> SupportedIsolationLevels = new()
    {
        [SupportedDatabase.SqlServer] =
        [
            IsolationLevel.ReadUncommitted,
            IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable,
            IsolationLevel.Snapshot
        ],
        [SupportedDatabase.PostgreSql] =
        [
            IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable
        ],
        [SupportedDatabase.MySql] =
        [
            IsolationLevel.ReadUncommitted,
            IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable
        ],
        [SupportedDatabase.MariaDb] =
        [
            IsolationLevel.ReadUncommitted,
            IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable
        ],
        [SupportedDatabase.Oracle] =
        [
            IsolationLevel.ReadCommitted,
            IsolationLevel.Serializable
        ],
        [SupportedDatabase.Sqlite] =
        [
            IsolationLevel.ReadCommitted,
            IsolationLevel.Serializable
        ],
        [SupportedDatabase.Firebird] =
        [
            IsolationLevel.ReadCommitted,
            IsolationLevel.Snapshot,
            IsolationLevel.Serializable
        ],
        [SupportedDatabase.CockroachDb] = [IsolationLevel.Serializable]
    };

    public void Validate(SupportedDatabase db, IsolationLevel level)
    {
        if (!SupportedIsolationLevels.TryGetValue(db, out var levels))
            throw new NotSupportedException($"Isolation level support not defined for database: {db}");

        if (!levels.Contains(level))
            throw new InvalidOperationException(
                $"Isolation level {level} is not supported by {db}. Allowed: {string.Join(", ", levels)}");
    }
}