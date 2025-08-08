using System;
using System.Collections.Generic;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class FakeDbRegistrarTests
{
    [Fact]
    public void Register_RegistersFactory()
    {
        var registrar = new FakeDbRegistrar(FakeDbFactory.Instance);
        var name = Guid.NewGuid().ToString();
        registrar.Register(name, new FakeDbFactory("Unknown"));
        var factory = DbProviderFactories.GetFactory(name);
        Assert.IsType<FakeDbFactory>(factory);
    }

    [Fact]
    public void Register_NullFactory_Throws()
    {
        var registrar = new FakeDbRegistrar(FakeDbFactory.Instance);
        var name = Guid.NewGuid().ToString();
        Assert.Throws<ArgumentNullException>(() => registrar.Register(name, null!));
    }

    [Fact]
    public void RegisterAll_RegistersAllFactories()
    {
        var registrar = new FakeDbRegistrar(FakeDbFactory.Instance);
        var providers = new Dictionary<string, string>
        {
            { Guid.NewGuid().ToString(), SupportedDatabase.SqlServer.ToString() },
            { Guid.NewGuid().ToString(), SupportedDatabase.PostgreSql.ToString() } 
        };
        registrar.RegisterAll(providers);
        foreach (var key in providers.Keys)
        {
            var factory = DbProviderFactories.GetFactory(key);
            Assert.IsType<FakeDbFactory>(factory);
        }
    }

    [Fact]
    public void RegisterAll_NullDictionary_Throws()
    {
        var registrar = new FakeDbRegistrar(FakeDbFactory.Instance);
        Assert.Throws<ArgumentNullException>(() => registrar.RegisterAll(null!));
    }

    [Fact]
    public void RegisterAll_InvalidProvider_Throws()
    {
        var registrar = new FakeDbRegistrar(FakeDbFactory.Instance);
        var providers = new Dictionary<string, string>
        {
            { Guid.NewGuid().ToString(), "InvalidDb" }
        };

        Assert.Throws<ArgumentException>(() => registrar.RegisterAll(providers));
    }
}
