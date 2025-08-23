namespace pengdows.crud.dialects;

public interface ISqlDialectProvider
{
    ISqlDialect Dialect { get; }
}
