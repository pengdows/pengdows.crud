namespace pengdows.crud;

public interface ITypeMapRegistry
{
    ITableInfo GetTableInfo<T>();
}