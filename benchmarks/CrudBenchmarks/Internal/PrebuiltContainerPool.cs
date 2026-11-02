using System.Collections.Concurrent;
using pengdows.crud;

namespace CrudBenchmarks;

// Prebuilt containers for one context. A concurrent task rents one, sets its parameter values, runs it and returns it,
// so no SQL is built inside a timed operation and no container is used by two tasks at once.
internal sealed class PrebuiltContainerPool : IDisposable
{
    private readonly ConcurrentBag<ISqlContainer> _free = new();
    private readonly Func<ISqlContainer> _build;

    public PrebuiltContainerPool(Func<ISqlContainer> build) => _build = build;

    public ISqlContainer Rent() => _free.TryTake(out var container) ? container : _build();

    public void Return(ISqlContainer container) => _free.Add(container);

    // Rents a container for the length of an await-using scope; disposing the lease returns it to the pool.
    public RentedContainer Lease() => new(this, Rent());

    public void Dispose()
    {
        while (_free.TryTake(out var container))
        {
            container.Dispose();
        }
    }

    public sealed class RentedContainer : IAsyncDisposable
    {
        private readonly PrebuiltContainerPool _pool;

        internal RentedContainer(PrebuiltContainerPool pool, ISqlContainer container)
        {
            _pool = pool;
            Container = container;
        }

        public ISqlContainer Container { get; }

        public ValueTask DisposeAsync()
        {
            _pool.Return(Container);
            return ValueTask.CompletedTask;
        }
    }
}
