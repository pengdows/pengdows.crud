namespace pengdows.crud;

internal interface ISqlDialectProvider
{
    SqlDialect Dialect { get; }
}
