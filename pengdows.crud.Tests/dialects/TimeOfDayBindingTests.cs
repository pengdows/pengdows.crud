using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-001, found live: a time of day (TimeSpan, or TimeOnly which binds as its TimeSpan) failed
/// to bind on three databases. Oracle has no TIME type and ODP.NET rejects DbType.Time
/// ("ORA-50028: Invalid parameter binding"), so it binds as INTERVAL DAY TO SECOND. FlatFile's driver
/// validates DATE/TIME columns against DateOnly/TimeOnly and rejects the DateTime/TimeSpan
/// equivalents ("contains parts which are not specific to the DateOnly", "Object must be of type
/// TimeOnly"), so it takes them natively. SingleStore rejects the typed TIME literal MySqlConnector
/// sends for a TimeSpan (MySQL, MariaDB and TiDB accept it), so it binds the time as text.
/// </summary>
public sealed class TimeOfDayBindingTests
{
    private static readonly TimeSpan SampleSpan = new(13, 45, 30);
    private static readonly TimeOnly SampleTime = new(13, 45, 30);

    public enum OdpLikeDbType
    {
        Varchar2 = 126,
        IntervalDS = 183,
        TimeStamp = 187
    }

    // Mirrors ODP.NET: assigning DbType also assigns OracleDbType (DbType.Time -> TimeStamp).
    private sealed class OdpLikeParameter : fakeDbParameter
    {
        public OdpLikeDbType OracleDbType { get; set; } = OdpLikeDbType.Varchar2;

        public override DbType DbType
        {
            get => base.DbType;
            set
            {
                base.DbType = value;
                OracleDbType = value == DbType.Time ? OdpLikeDbType.TimeStamp : OdpLikeDbType.Varchar2;
            }
        }
    }

    private sealed class OdpLikeFactory : DbProviderFactory
    {
        private readonly fakeDbFactory _inner = new(SupportedDatabase.Oracle);
        public override DbConnection CreateConnection() => _inner.CreateConnection()!;
        public override DbCommand CreateCommand() => _inner.CreateCommand()!;
        public override DbConnectionStringBuilder CreateConnectionStringBuilder() => _inner.CreateConnectionStringBuilder()!;
        public override DbParameter CreateParameter() => new OdpLikeParameter();
    }

    [Fact]
    public void Oracle_TimeSpanAndTimeOnlyForTime_BindAsIntervalDaySecond()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Oracle, new OdpLikeFactory(),
            NullLogger<SqlDialect>.Instance);

        foreach (var value in new object[] { SampleSpan, SampleTime })
        {
            var parameter = Assert.IsType<OdpLikeParameter>(dialect.CreateDbParameter("t", DbType.Time, value));

            Assert.Equal(OdpLikeDbType.IntervalDS, parameter.OracleDbType);
            Assert.NotEqual(DbType.Time, parameter.DbType);
            Assert.Equal(SampleSpan, parameter.Value);
        }
    }

    // Found live: a NULL time of day still bound as DbType.Time (OracleDbType.TimeStamp), and Oracle
    // type-checks a NULL bind against the INTERVAL column ("ORA-00932: expression is of data type
    // TIMESTAMP, which is incompatible with expected data type INTERVAL DAY TO SECOND").
    [Fact]
    public void Oracle_NullForTime_BindsAsIntervalDaySecond()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Oracle, new OdpLikeFactory(),
            NullLogger<SqlDialect>.Instance);

        foreach (var parameter in new[]
                 {
                     dialect.CreateDbParameter("a", DbType.Time, (TimeOnly?)null),
                     dialect.CreateDbParameter("b", DbType.Time, (TimeSpan?)null),
                     dialect.CreateDbParameter("c", DbType.Time, (object?)null)
                 })
        {
            var odp = Assert.IsType<OdpLikeParameter>(parameter);
            Assert.Equal(OdpLikeDbType.IntervalDS, odp.OracleDbType);
            Assert.Equal(DBNull.Value, odp.Value);
        }
    }

    [Fact]
    public void FlatFile_DateOnlyAndTimeOnly_BindNatively()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.FlatFile,
            new fakeDbFactory(SupportedDatabase.FlatFile), NullLogger<SqlDialect>.Instance);

        Assert.Equal(new DateOnly(2026, 9, 29), dialect.CreateDbParameter("d", DbType.Date, new DateOnly(2026, 9, 29)).Value);
        Assert.Equal(SampleTime, dialect.CreateDbParameter("t", DbType.Time, SampleTime).Value);
        Assert.Equal(DBNull.Value, dialect.CreateDbParameter("n", DbType.Date, (DateOnly?)null).Value);
    }

    // The same FlatFile validation rejects a DateTime property on a DATE column and a TimeSpan property
    // on a TIME column, so those bind as DateOnly/TimeOnly when the column's DbType says DATE/TIME.
    [Fact]
    public void FlatFile_DateTimeForDate_AndTimeSpanForTime_BindAsDateOnlyAndTimeOnly()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.FlatFile,
            new fakeDbFactory(SupportedDatabase.FlatFile), NullLogger<SqlDialect>.Instance);

        Assert.Equal(new DateOnly(2026, 9, 29),
            dialect.CreateDbParameter("d", DbType.Date, new DateTime(2026, 9, 29)).Value);
        Assert.Equal(SampleTime, dialect.CreateDbParameter("t", DbType.Time, SampleSpan).Value);
        // A timestamp column keeps the full DateTime.
        Assert.Equal(new DateTime(2026, 9, 29, 13, 45, 30),
            dialect.CreateDbParameter("ts", DbType.DateTime, new DateTime(2026, 9, 29, 13, 45, 30)).Value);
    }

    [Theory]
    [InlineData(SupportedDatabase.SingleStore, true)]
    [InlineData(SupportedDatabase.MySql, false)]
    [InlineData(SupportedDatabase.MariaDb, false)]
    [InlineData(SupportedDatabase.TiDb, false)]
    public void MySqlFamily_TimeForTime_BindsAsTextOnlyOnSingleStore(SupportedDatabase provider, bool asText)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(provider, new fakeDbFactory(provider),
            NullLogger<SqlDialect>.Instance);

        foreach (var value in new object[] { SampleSpan, SampleTime })
        {
            var parameter = dialect.CreateDbParameter("t", DbType.Time, value);

            if (asText)
            {
                Assert.Equal("13:45:30", parameter.Value);
                Assert.Equal(DbType.String, parameter.DbType);
            }
            else
            {
                Assert.Equal(SampleSpan, parameter.Value);
            }
        }
    }
}
