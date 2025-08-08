namespace pengdow.crud.tenant;

public interface ITenantContextRegistry
{
    public IDatabaseContext GetContext(string tenant);
}