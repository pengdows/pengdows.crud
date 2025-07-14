namespace pengdows.crud;

public static class SqlContainerExtensions
{
    public static ISqlContainer AppendQuery(this ISqlContainer container, string sql)
    {
        container.Query.Append(sql);
        return container;
    }
}