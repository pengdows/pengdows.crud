using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// Fallback SQL-92 dialect for unsupported databases.
/// </summary>
internal class Sql92Dialect : SqlDialect
{
    internal Sql92Dialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
    public override string ParameterMarker => "@";
}
