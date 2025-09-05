#region
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class DeterministicParameterNamingTests : IAsyncLifetime
{
    private readonly TypeMapRegistry _typeMap;
    private readonly IDatabaseContext _context;
    private readonly EntityHelper<IdentityTestEntity, int> _helper;

    public DeterministicParameterNamingTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<IdentityTestEntity>();
        _context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer), _typeMap);
        _helper = new EntityHelper<IdentityTestEntity, int>(_context);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_context is IAsyncDisposable disp)
        {
            await disp.DisposeAsync();
        }
    }

    [Fact]
    public async Task BuildUpdateAsync_ProducesStableSqlAcrossRuns()
    {
        var e = new IdentityTestEntity { Id = 1, Name = "A", Version = 1 };

        var sc1 = await _helper.BuildUpdateAsync(e);
        var sql1 = sc1.Query.ToString();

        var sc2 = await _helper.BuildUpdateAsync(e);
        var sql2 = sc2.Query.ToString();

        Assert.Equal(sql1, sql2);
    }

    [Fact]
    public async Task BuildUpdateAsync_UsesDistinctClauseParameterNames()
    {
        var e = new IdentityTestEntity { Id = 1, Name = "A", Version = 1 };

        var sc = await _helper.BuildUpdateAsync(e);
        var sql = sc.Query.ToString();

        var expectedS = sc.MakeParameterName("s0");
        var expectedK = sc.MakeParameterName("k0");
        Assert.Contains(expectedS, sql);
        Assert.Contains(expectedK, sql);
    }
}
