namespace pengdows.crud.dialects;

internal interface ISqlDialectProvider
{
    ISqlDialect Dialect { get; }
}
