using pengdows.crud.dialects;

namespace pengdows.crud;

internal interface ISqlDialectProvider
{
    SqlDialect Dialect { get; }
}
