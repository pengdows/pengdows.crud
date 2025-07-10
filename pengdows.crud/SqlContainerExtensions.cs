namespace pengdows.crud;

public static class SqlContainerExtensions
{
    public static SqlContainer AppendQuery(this SqlContainer container, string sql)
    {
        container.Query.Append(sql);
        return container;
    }
}
