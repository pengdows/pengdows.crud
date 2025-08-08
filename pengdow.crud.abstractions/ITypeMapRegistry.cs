namespace pengdow.crud;

public interface ITypeMapRegistry
{
    ITableInfo GetTableInfo<T>();
}