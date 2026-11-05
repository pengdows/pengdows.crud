using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// Oracle binds a spatial value as its EWKT text inside a scalar subquery that builds SDO_GEOMETRY(wkt, srid); SRID 0 is
/// stored as NULL. It reads back through SDO_UTIL.TO_WKTGEOMETRY with the SRID attached, which is the EWKT stored.
/// </summary>
public class FakeDataStoreOracleSpatialTests
{
    private const string Write =
        "(SELECT CASE WHEN x IS NULL THEN NULL ELSE SDO_GEOMETRY(SUBSTR(x, INSTR(x, ';') + 1), " +
        "NULLIF(TO_NUMBER(SUBSTR(x, 6, INSTR(x, ';') - 6)), 0)) END FROM (SELECT TO_CLOB(:p0) x FROM DUAL))";

    private const string Read =
        "CASE WHEN g IS NULL THEN NULL ELSE 'SRID=' || NVL(TO_CHAR((SELECT v.g.SDO_SRID FROM (SELECT g g FROM DUAL) v)), '0') " +
        "|| ';' || SDO_UTIL.TO_WKTGEOMETRY(g) END AS g";

    private static async Task<object?> RoundTripAsync(string ewkt)
    {
        using var conn = new fakeDbConnection(new FakeDataStore()) { EnableDataPersistence = true };
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE t (id INTEGER, g CLOB)";
            await create.ExecuteNonQueryAsync();
        }

        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO t (id, g) VALUES (1, " + Write + ")";
            var p = insert.CreateParameter();
            p.ParameterName = ":p0";
            p.Value = ewkt;
            insert.Parameters.Add(p);
            await insert.ExecuteNonQueryAsync();
        }

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT " + Read + " FROM t WHERE id = 1";
        return await select.ExecuteScalarAsync();
    }

    [Fact]
    public async Task AnEwktValueReadsBackAsItsEwkt()
    {
        Assert.Equal("SRID=4326;POINT (1 2)", await RoundTripAsync("SRID=4326;POINT (1 2)"));
    }

    [Fact]
    public async Task ASridZeroIsStoredAsNull()
    {
        // ADO.NET's answer for a NULL scalar is DBNull.
        Assert.Equal(System.DBNull.Value, await RoundTripAsync("SRID=0;POINT (1 2)"));
    }
}
