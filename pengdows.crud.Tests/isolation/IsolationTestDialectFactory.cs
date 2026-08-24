using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace pengdows.crud.Tests.isolation;

/// <summary>
/// IsolationResolver's constructor now takes a SqlDialect (the dialect owns which isolation
/// levels/profile mappings it supports) instead of a raw SupportedDatabase enum. This helper
/// gives isolation tests a one-line way to get a real dialect instance for a given product
/// without each test needing its own fakeDbFactory/logger boilerplate.
/// </summary>
internal static class IsolationTestDialectFactory
{
    internal static SqlDialect Create(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        return (SqlDialect)SqlDialectFactory.CreateDialectForType(product, factory, NullLogger.Instance);
    }
}
