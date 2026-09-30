using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Review 2026-09-29 (Schneier: fail loudly; Abrash: hidden cost). A pooled parameter is reset to
/// DbType.Object before reuse; Informix.Net.Core rejects that value ("No mapping exists from DbType
/// Object to a known IfxType"), and the failure was swallowed by an empty catch on every reuse. The
/// dialect now learns once that its provider rejects the reset, logs that at Debug, and stops
/// attempting it.
/// </summary>
public sealed class SqlDialectPooledParameterResetTests
{
    [Fact]
    public void PooledReset_ProviderRejectsDbTypeObject_IsLoggedOnceAndNotRetried()
    {
        var logs = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { logs });
        var factory = new RejectingFactory();
        var dialect = new TestDialect(factory, loggerFactory.CreateLogger<SqlDialect>());

        var parameter = dialect.CreateDbParameter("p0", DbType.Int32, 1);
        for (var i = 0; i < 3; i++)
        {
            dialect.ReturnParameterToPool(parameter);
            parameter = dialect.CreateDbParameter("p" + (i + 1), DbType.Int32, i);
            Assert.Equal(i, parameter.Value);
            Assert.Equal(DbType.Int32, parameter.DbType);
        }

        Assert.Equal(1, factory.Created.Sum(p => p.RejectedObjectResets));
        Assert.Single(logs.Entries, e => e.Level == LogLevel.Debug && e.Exception is ArgumentException);
    }

    private sealed class TestDialect(DbProviderFactory factory, ILogger logger) : SqlDialect(factory, logger)
    {
        public override SupportedDatabase DatabaseType => SupportedDatabase.Informix;
    }

    private sealed class RejectingFactory : DbProviderFactory
    {
        public readonly System.Collections.Generic.List<RejectingParameter> Created = new();

        public override DbParameter CreateParameter()
        {
            var parameter = new RejectingParameter();
            Created.Add(parameter);
            return parameter;
        }
    }

    // Mirrors Informix.Net.Core's IfxParameter: DbType.Object has no IfxType mapping.
    private sealed class RejectingParameter : fakeDbParameter
    {
        private DbType _dbType = DbType.String;

        public int RejectedObjectResets { get; private set; }

        // A provider's own ResetDbType clears its type state directly, not through the setter.
        public override void ResetDbType()
        {
            _dbType = DbType.String;
        }

        public override DbType DbType
        {
            get => _dbType;
            set
            {
                if (value == DbType.Object)
                {
                    RejectedObjectResets++;
                    throw new ArgumentException("No mapping exists from DbType Object to a known IfxType.");
                }

                _dbType = value;
            }
        }
    }
}
