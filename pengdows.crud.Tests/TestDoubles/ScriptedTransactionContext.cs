using System;

namespace pengdows.crud.Tests;

/// <summary>
/// An <see cref="ITransactionContext"/> that is not the library's <c>TransactionContext</c>. Every member throws until
/// scripted; <see cref="ContainerFactory"/> scripts <c>CreateSqlContainer</c>, recording the SQL it was asked for.
/// </summary>
internal sealed partial class ScriptedTransactionContext
{
    /// <summary>Builds the container <c>CreateSqlContainer</c> returns; unset means unscripted.</summary>
    public Func<string?, ISqlContainer>? ContainerFactory { get; set; }

    /// <summary>The SQL text of every <c>CreateSqlContainer</c> call.</summary>
    public System.Collections.Generic.List<string?> RequestedSql { get; } = new();

    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        RequestedSql.Add(query);
        return (ContainerFactory ?? throw Unscripted(nameof(CreateSqlContainer)))(query);
    }

    ISqlContainer IDatabaseContext.CreateSqlContainer(string? query) => CreateSqlContainer(query);
}
