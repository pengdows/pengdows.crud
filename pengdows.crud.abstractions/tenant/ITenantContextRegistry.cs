namespace pengdows.crud.tenant;

public interface ITenantContextRegistry
{
    public IDatabaseContext GetContext(string tenant);
}